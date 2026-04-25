# Point Cloud Visibility Manager for Revit 2026

A Revit addin that provides quick access to point cloud visibility controls through a floating panel.

## The Problem

Toggling point cloud visibility in Revit requires multiple steps:
1. Type `VV` to open Visibility/Graphics dialog
2. Navigate to the Point Clouds tab
3. Find and toggle the checkbox
4. Click OK

## The Solution

A floating window that lets you toggle point cloud visibility with a single click.

![Point Cloud Visibility Window](docs/screenshot.png)

## Features

- **Always-on-top floating panel** — stays visible while you work
- **Individual toggles** — checkbox for each point cloud in the project
- **Toggle All** — show/hide all point clouds at once
- **Auto-refresh** — updates when you switch views or documents
- **Keyboard shortcut ready** — assign a hotkey in Revit for quick access

## Installation

### Option 1: Installer (Recommended)

1. Download the latest `PointCloudUtils-Setup-x.x.x.exe` from [Releases](https://github.com/LAVA-3D/Revit-PointCloudUtils/releases)
2. Close Revit
3. Run the installer
4. Start Revit 2026

### Option 2: Manual Install

1. Download the latest build artifact from [Actions](https://github.com/LAVA-3D/Revit-PointCloudUtils/actions)
2. Copy `PointCloudUtils.dll` and `config.json` to:
   ```
   C:\ProgramData\Autodesk\Revit\Addins\2026\PointCloudUtils\
   ```
3. Copy `PointCloudUtils.addin` to:
   ```
   C:\ProgramData\Autodesk\Revit\Addins\2026\
   ```
4. Start Revit 2026

## Usage

1. Go to **Add-Ins** tab in Revit
2. Click **Point Cloud Visibility** in the Point Cloud panel
3. Use checkboxes to toggle individual point clouds
4. Use **Toggle All** to show/hide all at once

### Keyboard Shortcut

To assign a keyboard shortcut:
1. Go to **View** → **User Interface** → **Keyboard Shortcuts**
2. Search for "Toggle Point Cloud Visibility Window"
3. Assign your preferred key combination

## Requirements

- Revit 2026
- Windows 10/11 (64-bit)

## Building from Source

```bash
# Clone the repository
git clone https://github.com/LAVA-3D/Revit-PointCloudUtils.git
cd Revit-PointCloudUtils

# Build
dotnet build -c Release

# Output is in build/
```

## License

MIT License — see [LICENSE](LICENSE) for details.

## Contributing

Contributions welcome! Please open an issue or pull request.
