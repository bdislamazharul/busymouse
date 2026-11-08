# BusyMouse

Keeps your Windows PC awake by gently nudging the mouse pointer, so the screen doesn't lock and chat apps keep
showing you as available.

BusyMouse lives in the tray. Turn it on or off from the tray menu, the main window, or <kbd>Ctrl</kbd>+<kbd>Shift</kbd>+<kbd>J</kbd>.
When you're already using the mouse yourself, it stays out of the way and skips the nudge.

## Download

With [winget](https://learn.microsoft.com/windows/package-manager/winget/):

```
winget install azh.busymouse
```

Or get it from the Microsoft Store, or download the latest version from
[Releases](https://github.com/bdislamazharul/busymouse/releases/latest).

| File | What it is |
|---|---|
| `BusyMouse-<version>-x64-setup.exe` | Installer. Everything included; the usual choice. |
| `BusyMouse-<version>-x64-setup-no-runtime.exe` | Smaller installer. Downloads and installs the .NET Runtime from Microsoft if your PC doesn't have it. |
| `BusyMouse-<version>-x64-portable.zip` | No install: unzip anywhere and run `BusyMouse.exe`. |
| `BusyMouse-<version>-x64-portable-no-runtime.zip` | Smaller portable copy. Offers to install the .NET Runtime on first start if it's missing. |

Use the `arm64` files on Windows on ARM PCs. Needs Windows 10 version 1809 or later, or Windows 11.

## Features

- **Nudge styles:** Default, Diagonal, Quiet (moves the pointer by zero pixels) and Orbit.
- **Interval and distance:** nudge every 1 second to 3 hours, by 1 to 120 pixels, optionally at random intervals.
- **Time limit:** stay awake for a while, or until a set time, then stop on its own.
- **Pause while locked:** optionally stop nudging while the PC is locked.
- **Start with Windows**, straight into the tray.
- **Windows 11 look:** Mica window, acrylic tray menu, light and dark themes.

## Command line

```
BusyMouse [options]

  -n, --nudge                   Keep awake as soon as the app starts
  -m, --minimized               Start in the tray, without the window
  -g, --settings                Open the window on start
  -o, --mode MODE               Default, Diagonal, Quiet or Orbit
  -s, --seconds N               Seconds between nudges, 1 to 10800
  -d, --distance N              How far each nudge moves, 1 to 120
  -r, --random                  Wait a random time, up to the seconds, between nudges
  -l, --respect-locked-state    Pause while the PC is locked
      --stop-nudging            Ask the running copy to stop keeping awake, then exit
  -h, --help                    Show this help
```

Mode, seconds, distance, random and locked-state are remembered for next time.

## Building

Needs the [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0) on Windows.

```
dotnet build BusyMouse.sln -c Debug -p:Platform=x64
```

To build every download into `dist\`, also install Visual Studio Build Tools with "Desktop development with C++"
(for the launcher) and [Inno Setup 6](https://jrsoftware.org/isinfo.php) (for the installers), then run:

```
powershell -ExecutionPolicy Bypass -File tools\package.ps1 [-Platform ARM64] [-Trim]
```

Releases are built by GitHub Actions: pushing a tag such as `v1.0.0` builds everything for x64 and ARM64 and
publishes a release.

The Microsoft Store package (an unsigned `.msixbundle` for x64 and ARM64; the Store signs it) is built by:

```
powershell -ExecutionPolicy Bypass -File tools\package-store.ps1
```

The release workflow builds it too and keeps it as the `BusyMouse-store` artifact of the run. Before the first upload,
copy the package identity from Partner Center (Product management > Product identity) into
`BusyMouse.WinUI\Package.appxmanifest`.

| Folder | What's in it |
|---|---|
| `BusyMouse.Core` | The keep-awake engine: timing and pointer nudges. |
| `BusyMouse.WinUI` | The app: WinUI 3 window, tray icon and menu, settings. |
| `launcher` | Native launcher for the no-runtime bundle; installs .NET when it's missing. |
| `installer` | Inno Setup script for both installers. |
| `tools` | Packaging script and icon generator. |

## License

[MIT](LICENSE.txt) © 2026 Azharul Islam
