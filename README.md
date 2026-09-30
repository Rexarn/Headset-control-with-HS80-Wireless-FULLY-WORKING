# Corsair HS80 MAX Control (Windows)

Windows tools for the **Corsair HS80 MAX Wireless** only (dongle `1B1C:0A97`, headset `0x0A96`).

This is **not** a general multi-headset HeadsetControl build. It targets this model because that is what was tested and fixed. If someone ports pieces into the main [HeadsetControl](https://github.com/Sapd/HeadsetControl) project (or similar), they are welcome to use this work however they like — just give credit and a thanks to the people who reverse-engineered and implemented the HS80 MAX path (Me, Rexarn who finally took on the challenge to fix this and poke the clanker in the right direction, and said what was working and not and Grok who is a fucking wizard at whatever he does.).

## Why this exists

Stock HeadsetControl could see the USB receiver but often could not read battery or control the headset on the HS80 MAX. On real hardware the fixes were:

- Use the correct HID collection (Usage Page `0xFF42`, Col04 on interface 3)
- Use HID report ID `0x02` on Windows
- Send a complete LED write sequence (not brightness alone)
- Use non-blocking reads so the app does not hang

## System tray (`HS80Tray.exe`)

The tray app runs in the background **with no console window**.

| What you see / do | Meaning | [How it actually looks..]([url](https://imgur.com/a/tray-icon-demo-XqMOsx3))
|-------------------|---------|
| **Number on the icon** | Battery percent (colors: white normal, yellow low/charging, red critical) |
| **Red slash over the number** | Mic boom is muted (up) |
| **Hover tooltip** | Battery % and whether the mic is muted |
| **Right-click → LED Toggle** | Turns headset LEDs off or on |
| **Right-click → Reload** | Refresh battery/mic now (otherwise about every 60 seconds) |
| **Right-click → Exit** | Quit the tray app |

**Setup**

1. Build both programs (`build_all.bat`), or build CLI + tray separately.
2. Put `headsetcontrol.exe` (or `HS80Control.exe`) in the **same folder** as `HS80Tray.exe`.
3. Double-click `HS80Tray.exe`.

Optional:
If you want the program to start with windows.
1. Windows + R (Run window)
2. taskschd.msc (Enter)
3. Right click, new task and enter a name
4. Go to triggers and press New, change to "Begin the task: at logon" and press any user and use a delay of 1 minute (or longer if you want).
5. Go to actions, make sure it says "start a program" then point to your fully built "hs80tray.exe" (or the one from releases tab if you only care for the final windows program. (haven't tested anything else but Windows 10 LTSC Iot, Swedish/Nordic version.)
6. Press OK and close everything and it should work.
[[Picture tutorial]([url](https://imgur.com/a/HEg7rHj))]

The tray does not talk to the headset by itself; it calls the CLI (`-bc` for battery, `-o json` for mic, `-l toggle` for lights).

## Command-line features (`HS80Control.exe` / `headsetcontrol.exe`)

| Feature | How to use |
|---------|------------|
| Battery (plain number, for scripts/tray) | `headsetcontrol -bc` |
| Battery (readable text) | `headsetcontrol -b` |
| Sidetone | `headsetcontrol -s 64` (0–128) |
| Auto-off timer | `headsetcontrol -i 30` (minutes; `0` = off) |
| LEDs off / on / flip | `headsetcontrol -l 0` / `-l 1` / `-l toggle` |
| Mic boom status | `headsetcontrol mic` |
| JSON for GUIs | `headsetcontrol -o json` |
| List HID collections | `headsetcontrol list` |

Works as a drop-in `headsetcontrol.exe` for **HeadsetControl-GUI** on this headset (battery, sidetone, inactivity, lights). The GUI’s Microphone tab may stay empty: boom mute is status-only, not full mic controls.

## What each file is for

| File | Purpose |
|------|---------|
| `src/HS80Control.cs` | CLI source (HID protocol + all features above) |
| `src/HS80Tray.cs` | System tray source |
| `build_hs80.bat` / `build_hs80.ps1` | Build the CLI (no Visual Studio required) |
| `build_hs80_tray.bat` | Build the tray app |
| `build_all.bat` | Build both and copy CLI → `headsetcontrol.exe` |
| `docs/PROTOCOL.md` | Packet/HID notes for developers |
| `docs/COMPATIBILITY.md` | GUI and tray integration notes |
| `LICENSE` | MIT — reuse allowed; please credit/thanks if you upstream this |

## Build

Needs .NET Framework 4.x (`csc`). Visual Studio is optional.

```bat
build_all.bat
```

## Hardware

- Corsair **HS80 MAX Wireless** only  
- Dongle plugged in, headset on **RF/wireless** (not Bluetooth-only)

## Credit / reuse

MIT licensed. Upstream authors and anyone else may take this code, ideas, or protocol notes for the wider HeadsetControl ecosystem (or their own tools). Please credit the HS80 MAX work and say thanks — that is all that is asked.

Not affiliated with Corsair or the HeadsetControl project.

Sources used Thank you <3:

- Sapd / HeadsetControl
  https://github.com/Sapd/HeadsetControl

- HeadsetControl-GUI
  https://github.com/HeadsetControl-GUI/HeadsetControl-GUI

- zampierilucas / HeadsetControl-SystemTray
  https://github.com/zampierilucas/HeadsetControl-SystemTray

- ToastKiste21 / corsair-hs80-max-re (Bragi protocol, HS80 MAX RE)
  https://github.com/ToastKiste21/corsair-hs80-max-re
  https://github.com/ToastKiste21/corsair-hs80-max-re/blob/master/docs/bragi_protocol.md

- HeadsetControl wiki – API / building on HeadsetControl
  https://github.com/Sapd/HeadsetControl/wiki/API-%E2%80%90-Building-Applications-on-top-of-HeadsetControl

- HeadsetControl – adding a Corsair device
  https://github.com/Sapd/HeadsetControl/blob/master/docs/ADDING_A_CORSAIR_DEVICE.md

- Live testing on Corsair HS80 MAX Wireless
  Dongle USB ID 1B1C:0A97, headset 1B1C:0A96 (RF mode)
