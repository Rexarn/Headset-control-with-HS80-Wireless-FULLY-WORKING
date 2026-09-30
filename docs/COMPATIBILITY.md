# GUI and tray compatibility

## HeadsetControl-GUI

https://github.com/HeadsetControl-GUI/HeadsetControl-GUI

Frontend only: it runs `headsetcontrol` with official flags.

1. Build `HS80Control.exe`
2. Copy/rename to `headsetcontrol.exe` where the GUI expects it
3. Supported in GUI: battery, sidetone, inactivity timer, lights (if the GUI shows them)
4. Microphone **tab** may stay empty: boom mute is status-only (`0xA6`), not full mic controls

JSON includes `CAP_SIDETONE`, `CAP_BATTERY_STATUS`, `CAP_INACTIVE_TIME`, `CAP_LIGHTS`, `CAP_MICROPHONE_MUTE`.

## HeadsetControl-SystemTray (upstream)

https://github.com/zampierilucas/HeadsetControl-SystemTray

Stock app runs:

```text
headsetcontrol -bc
```

and expects a **bare integer** (e.g. `89`). Values `< 0` are treated as charging.

Upstream menu is only About / Reload (no LED toggle). Use **HS80Tray.exe** from this repo for:

- Battery % icon
- Red slash when mic boom is muted
- Right-click **LED Toggle**

## HS80Tray.exe (this repo)

| Action | Backend |
|--------|---------|
| Battery icon | `headsetcontrol -bc` |
| Mic slash | `headsetcontrol -o json` (parses muted fields) |
| LED Toggle | `headsetcontrol -l toggle` |

Place `headsetcontrol.exe` (or `HS80Control.exe`) beside `HS80Tray.exe`.
