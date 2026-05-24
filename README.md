# ClevoRGBControl

ClevoRGBControl is a Windows service and tray controller for Clevo-compatible laptop keyboard RGB lighting.

It uses `InsydeDCHU.dll` to write keyboard LED colors, so the DLL from the vendor keyboard LED software is required.

## Features

- Runs as a Windows service.
- Tray menu for quick control.
- Fixed color, RGB loop, breathing, color sequence, music mode and off mode.
- Global brightness and speed controls.
- Music mode based on system output audio level.
- Idle dimming and simple time schedule.
- Self-contained installer for Windows x64.

## Install

1. Download `ClevoRGBControlSetup.exe` from Releases.
2. Run it as Administrator.
3. Copy `InsydeDCHU.dll` to:

```text
C:\Program Files\ClevoRGBControl\Service
```

The service name is:

```text
ClevoRGBControlService
```

## Uninstall

Use Windows Settings > Apps, or run `ClevoRGBControlSetup.exe` again and choose uninstall.

Command-line uninstall:

```powershell
ClevoRGBControlSetup.exe /uninstall
```

## Build

Requirements:

- Windows 10/11 x64
- .NET SDK

Build:

```powershell
dotnet build .\ColorfulLedKeyboard.slnx -c Release
```

Create installer:

```powershell
.\scripts\publish.ps1
```

The installer is generated at:

```text
publish\ClevoRGBControlSetup.exe
```

## Acknowledgements

This project is based on the hardware control approach from [moshuiD/Colorful-Keyborad-Led-Color-Setting](https://github.com/moshuiD/Colorful-Keyborad-Led-Color-Setting).

The original project identified the use of `InsydeDCHU.dll` and `SetDCHU_Data` for controlling the keyboard LED colors. ClevoRGBControl extends that idea into a Windows service, tray app, installer and additional lighting effects.

## License

GPL-3.0. See [LICENSE](LICENSE).
