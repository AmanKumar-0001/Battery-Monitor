# Battery Monitor for Windows 11

A low-overhead desktop application for tracking battery level, charging status, and low-battery alerts across wireless peripherals.

## Features
- Multi-device monitoring for mice, keyboards, headsets, and controllers.
- Read-only protocol support: Logitech HID++ 2.0 (feature discovery), Generic HID, and Windows Bluetooth.
- Fluent Windows 11 companion taskbar widget and system tray icon.
- Automated alert suppression with cooldown timer and hysteresis recovery.
- Batched SQLite history with 30-day retention and CSV export.
- Headless CLI interface (`--list`, `--dump`, `--quit`).

## Download & Packaging Flavors
- **Portable Edition (`win-x64-portable`)**: Self-contained single executable. Requires no installed .NET runtime.
- **Lightweight Edition (`win-x64-lightweight`)**: Compact single executable (~0.2 MB). Requires .NET 8 Desktop Runtime.

## Verifying Download Checksums
After downloading, verify the executable's SHA256 checksum in PowerShell:
```powershell
Get-FileHash -Algorithm SHA256 .\BatteryMonitor-1.0.0-win-x64-portable.exe
```
Compare the output against `SHA256SUMS.txt` on the release page. If the hashes do not match, do not run the executable.

## Windows SmartScreen Notice
Because this executable is unsigned by default, Windows Defender SmartScreen may display an "Unrecognized app" message on first launch.
To proceed after verifying the hash:
1. Click **More info**.
2. Click **Run anyway**.

## Command Line Usage
- `BatteryMonitor.exe --list`: Print detected devices and battery levels to stdout.
- `BatteryMonitor.exe --dump [path]`: Export hardware diagnostics to JSON.
- `BatteryMonitor.exe --quit`: Gracefully terminate a running background instance.
