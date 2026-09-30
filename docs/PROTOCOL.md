# Corsair HS80 MAX – Bragi HID protocol notes

Research notes that made this tool work on Windows. Upstream [HeadsetControl](https://github.com/Sapd/HeadsetControl) 4.x
ships a Corsair Void V2 Wireless class that **does not** match the HS80 MAX dongle layout on Windows.

## Hardware IDs

| Role | VID | PID |
|------|-----|-----|
| USB dongle (receiver) | `0x1B1C` | `0x0A97` |
| Headset (over RF, behind dongle) | `0x1B1C` | `0x0A96` |

## Windows HID topology

Interface **MI_03** exposes multiple top-level collections:

| Collection | Usage Page | Usage | Role |
|------------|------------|-------|------|
| Col01 | `0x000C` | `0x0001` | Consumer control |
| Col02 | `0xFF13` | `0x0001` | Vendor |
| Col03 | `0x000B` | `0x0005` | Telephony |
| **Col04** | **`0xFF42`** | **`0x0001`** | **Bragi control (use this)** |
| Col05 | `0xFF42` | `0x0002` | Bragi secondary (no useful replies in testing) |

**Upstream bug:** hard-coded USB interface `4` and wrong collection selection.
On this dongle the control path is **interface 3**, collection **Col04**, usage page **`0xFF42` / usage `0x0001`**.

## Report IDs

| Direction | Report ID | Notes |
|-----------|-----------|--------|
| Host → device (OUT) | `0x02` | Required on Windows; report ID `0x00` produces no reply |
| Device → host (IN) | `0x01` | Command responses |
| Notify | `0x03` | Unsolicited property changes (e.g. mic) |

## Packet layout (host → device)

```
Byte 0: Report ID = 0x02
Byte 1: Magic = 0x08 | child_id     (dongle child_id=0 → 0x08, headset child_id=1 → 0x09)
Byte 2: Command
Byte 3+: Command payload (zero-pad to 64 data bytes; total buffer often 65 with report ID)
```

## Commands used

| ID | Name | Use |
|----|------|-----|
| `0x01` | SET | Set property |
| `0x02` | GET | Get property |
| `0x05` | CLOSE_HANDLE | Close lighting handle |
| `0x06` | WRITE_DATA | Write LED frame |
| `0x0D` | OPEN_HANDLE | Open lighting resource |

## Properties (headset magic `0x09`)

| Prop | Meaning | Scale / values |
|------|---------|----------------|
| `0x02` | Brightness | 0–1000 |
| `0x03` | Render mode | `1` = HW/self-operated, `2` = software |
| `0x0F` | Battery level | firmware may report 0–100 or 0–1000 (÷10) |
| `0x10` | Battery status | `0` discharge, `1` charge, `2` full, `3` error |
| `0x12` | Product ID (heartbeat) | dongle `0x0A97`, headset `0x0A96` |
| `0x13` | Firmware / app version | |
| `0x0D` / `0x0E` | Inactive timer enable / ms | |
| `0x46` / `0x47` | Sidetone toggle / level | level mapped 0–128 → ~0–1000 |
| `0xA6` | Mic mute (boom) | `0` open, `1` muted |
| `0xD1` | Related to ANC/sidetone path | used when setting sidetone |

GET example (battery):

```
OUT: 02 09 02 0F 00 ...
IN:  01 01 02 00 [lo] [hi] ...   → little-endian value at bytes 4–5
```

SET example (brightness 1000 = `0x03E8`):

```
OUT: 02 09 01 02 00 E8 03 ...
```

## LED control sequence (working)

1. SET render mode software: prop `0x03` = `0x02`
2. SET brightness: prop `0x02` = `0` (off) or `1000` (on)
3. For ON with explicit color:
   - OPEN_HANDLE resource `0x22` (ALT_LIGHTING)
   - WRITE_DATA: `[handle][size=8][0x12][0x00][R0 G0 B0][R1 G1 B1]` (logo + mic LEDs)
   - CLOSE_HANDLE

**Important:** WRITE_DATA must include **handle** and **data_size**. Omitting them made “LED on” a no-op while “off” still worked via brightness 0.

## Discovery / wake

1. GET prop `0x12` with magic `0x08` (dongle)
2. GET prop `0x12` with magic `0x09` (headset) → expect `0x0A96` if RF-connected

Audio can work while Bragi control fails if the wrong collection or report ID is used.

## Windows I/O notes

- Use **overlapped** `ReadFile` with timeout; synchronous HID reads block forever with no data.
- `HidP_GetCaps` success is **`0x00110000`**, not `0`.
- iCUE was **not** required; exclusive access was not the issue in testing.

## Compatibility outputs

| Consumer | Expectation |
|----------|-------------|
| HeadsetControl-GUI | `-b`, `-s`, `-i`, `-l`, `-o json` |
| HeadsetControl-SystemTray | `-bc` → bare integer on stdout; charging may be negative |

## References

- Bragi protocol write-ups for HS80 MAX (community reverse engineering)
- Upstream HeadsetControl Corsair Void V2 Wireless driver (partial match; wrong interface/report ID on this hardware)
