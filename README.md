# VitalsDash

A high-performance Windows hardware monitor with a sleek overlay mode.

## Features

- **Real-time Tracking**: Monitor usage and temperatures for:
  - CPU (Usage & Temperature)
  - RAM (Used vs Total)
  - GPU (Usage & Temperature)
  - NPU (Usage)
  - System Uptime
- **Overlay Mode**: A compact, semi-transparent window that stays on top, perfect for monitoring performance while gaming or working.
- **Multi-Monitor Support**: Choose which monitor the overlay should appear on.
- **Draggable Overlay**: Move the overlay anywhere on your screen (hold Alt + Left Click).
- **Tray Icon**: Minimize to tray to keep your taskbar clean.

## Installation

```bash
npm install -g https://github.com/ElfredSeow/PC-Monitoring-System
```

## How to use

Run the following command in your terminal:

```bash
vitals-dash
```

## Building from Source

### Prerequisites
- .NET 10.0 SDK or later
- Windows OS (uses WinForms and WMI)

### Build Instructions
1. Clone the repository.
2. Open a terminal in the project directory.
3. Run `dotnet build -c Release`.
4. The executable will be located in `MonitorApp/bin/Release/net10.0-windows/`.

## License

This project is licensed under the MIT License - see the [LICENSE](LICENSE) file for details.
