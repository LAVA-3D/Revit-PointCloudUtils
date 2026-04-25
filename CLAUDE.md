# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Project Overview

Revit-PointCloudUtils is a **Revit 2026 addin** (.NET 8, C#, WPF) for point cloud utilities. The project is in early stages — design blueprints are in `blueprints/` and define the full architecture, patterns, and CI/CD pipeline to follow when implementing.

## Build & Run

```bash
# Build
dotnet build -c Release

# Restore NuGet packages (CI or first-time)
dotnet restore {AddinName}.csproj
```

Output goes to `build/` (flat, no framework subfolder). The build should produce: `{AddinName}.dll`, `{AddinName}.addin`, `config.json`.

**Manual install for testing**: copy DLL + config.json to `C:\ProgramData\Autodesk\Revit\Addins\2026\{AddinName}\`, and the `.addin` manifest to `C:\ProgramData\Autodesk\Revit\Addins\2026\`.

## Architecture

Detailed design lives in `blueprints/01-project-scaffold.md`, `blueprints/02-commands-and-ui.md`, and `blueprints/03-ci-and-installer.md`. Key architectural decisions:

### Target Framework & References

- **.NET 8.0-windows** (`net8.0-windows`), x64 only, WPF enabled, `LangVersion 8.0`.
- Revit API references are **conditional** in the `.csproj`: local dev uses real DLLs from `C:\Program Files\Autodesk\Revit 2026\`; CI uses **Nice3point NuGet shims** (`Nice3point.Revit.Api.RevitAPI`, `Nice3point.Revit.Api.RevitAPIUI`, version `2026.*`) which are reference-only assemblies.
- Revit DLLs are set `Private=False` — never copy them to output.

### Threading Model (Critical)

Revit API is **single-threaded** and can only be called on Revit's main thread. The modeless WPF window runs on its own thread. All Revit API calls from the window **must** go through the `ExternalEvent` + `IExternalEventHandler` bridge:

1. Window sets properties on a handler instance, then calls `ExternalEvent.Raise()`.
2. Revit invokes `Handler.Execute(UIApplication)` on its thread, where transactions are safe.
3. Handler calls an `OnDone` callback (set by window) to pass results back; handler nulls `OnDone` in `finally`.
4. UI updates from Revit's thread require `Dispatcher.InvokeAsync()`.

### Code Organization

```
Commands/               → All IExternalCommand and IExternalEventHandler classes
  {Feature}Command.cs   → Ribbon commands (IExternalCommand)
  {Feature}Handlers.cs  → Modeless event handlers (IExternalEventHandler)
  {Feature}Entry.cs     → Data models + JSON persistence
  ToggleWindowCommand.cs
App.cs                  → IExternalApplication entry point (ribbon, ExternalEvent creation, window management)
{AddinName}Window.cs    → Modeless WPF window (code-behind, no XAML)
config.json             → User-editable settings (flat key/value, supports // comments)
```

### Key Patterns

- **Template Method for commands**: Abstract base `IExternalCommand` with concrete subclasses that only declare parameters (axis, direction, etc.). Each gets its own `.addin` entry so users can assign keyboard shortcuts.
- **Transaction modes**: `TransactionMode.Manual` for model-modifying commands; `TransactionMode.ReadOnly` for window toggles / info dialogs.
- **Window lifecycle**: Create once, hide on close (`Closing += cancel + Hide`), pin to Revit HWND via `WindowInteropHelper`. Never destroy — hiding preserves position and state.
- **Topmost management**: Window is `Topmost=true` by default. Lower temporarily for interactive pick operations (`PickObject`), restore in `finally`.
- **SuppressHistoryRecord flag**: When your own handlers modify the model, set `App.SuppressHistoryRecord = true` to prevent the `DocumentChanged` listener from double-recording into the undo stack.
- **ComboBox suppression**: Use a `_suppress` bool to ignore `SelectionChanged` events during programmatic `ItemsSource` refreshes.

### Data Persistence (No JSON Library)

- **Avoid `System.Text.Json`** — Revit's .NET hosting causes assembly version conflicts. Use regex-based parsing instead.
- Config reading: `Config.GetDoubleSetting(key, fallback)` / `Config.GetIntSetting(key, fallback)` with regex extraction from `config.json`.
- Data files: per-document JSON files (`mydata_{sanitisedDocTitle}.json`) stored next to the DLL. Always use `CultureInfo.InvariantCulture` for numbers and `"R"` format for roundtrip double precision.
- All loading is fault-tolerant — parse errors return empty defaults so the addin always starts.

### Programmatic Icons

Ribbon icons are generated at runtime using GDI+ (`System.Drawing.Common` NuGet) — no external image files. `Bitmap` rendered to PNG in memory, decoded to WPF `BitmapSource`.

## CI/CD

GitHub Actions workflow (`.github/workflows/build.yml`) on `windows-latest`:
- **Every push/PR**: `dotnet restore` → `dotnet build -c Release` → Inno Setup installer → upload artifacts.
- **PR builds**: Post a comment with download links (de-duplicates previous bot comments).
- **Tag `v*.*.*`**: Everything above + creates a GitHub Release with the installer `.exe` via `softprops/action-gh-release`.
- Version derived from git tag (`v1.2.3` → `1.2.3`); non-tag builds use `0.0.0-dev`.

## Installer

Inno Setup script (`installer.iss`). Key behaviors:
- Installs to `C:\ProgramData\Autodesk\Revit\Addins\2026\{AddinName}\` (requires admin).
- `config.json` uses `onlyifdoesntexist` + `uninsneveruninstall` — user edits survive upgrades and uninstalls.
- WMI-based Revit process detection warns user if Revit is running.
- `.addin` manifest installed one level up; explicitly cleaned in `[UninstallDelete]`.

## Adding a New Feature

1. Create handler class in `Commands/{Feature}Handler.cs` implementing `IExternalEventHandler`.
2. Add static handler + `ExternalEvent` fields in `App.cs`.
3. Create the `ExternalEvent` in `App.OnStartup()`.
4. Add UI controls in the window that set handler properties then call `Raise()`.
5. If it's a ribbon command: add `IExternalCommand` class + `.addin` manifest entry with unique GUID.
