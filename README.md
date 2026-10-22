# StrataSmartToggle (Windows 10/11 Taskbar Launcher)

A lightweight, zero-dependency native Windows taskbar controller for [Strata](https://github.com/Niko1221/Strata).

When running large MoE models locally on laptops or workstations (e.g., Gyro-M @ 64k with custom --data-dir), keeping terminal windows open or typing CLI flags repeatedly gets in the way. **StrataSmartToggle** pins directly to the Windows 11 taskbar with a dedicated AppUserModelID and provides a 1-click glassmorphic widget to launch and gracefully unload models from VRAM/RAM.

## Features

- **First-Run Setup -> Compact Quick Toggle:** Configure your `--data-dir`, model/preset (`Gyro-M`, `Gyro-S`, `Q2_0`, `IQ3_S`, etc. or local `models/` folder), and context window (`16k` / `32k` / `64k` / `128k`) once. On subsequent launches, it opens in a compact single-button mode.
- **Graceful VRAM/RAM Unload:** Calls `strata launch --stop --data-dir "..."`, waits for port `8080` to release, and cleanly closes the monitor terminal window.
- **Zero External Dependencies:** Compiles in 1 second using the built-in `.NET Framework` compiler (`csc.exe`) pre-installed on every Windows 10/11 machine. No Visual Studio or third-party NuGet packages required.
- **Native Windows 11 Look:** Follows system Light/Dark mode and Windows accent color, with a dedicated taskbar icon (never groups with PowerShell or `cmd.exe`).
- **Compact Tray Badge Mode:** Remembers your configuration on subsequent launches, collapsing into a sleek single-line status badge on your taskbar.

## Quick Build & Run

Open PowerShell in this directory and run:

    .\build.ps1
    .\StrataSmartToggle.exe

Right-click the running icon on your Windows Taskbar and select **Pin to taskbar**.

## License

MIT
