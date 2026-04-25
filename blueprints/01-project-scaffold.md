# 01 — Project Scaffold

> From zero to a compiling Revit 2026 addin with a ribbon button.

---

## Prerequisites

| Requirement | Notes |
|---|---|
| **.NET 8 SDK** | [Download](https://dotnet.microsoft.com/download/dotnet/8.0). Verify: `dotnet --version` |
| **Revit 2026** | Installed at `C:\Program Files\Autodesk\Revit 2026\` (local dev only — CI uses NuGet shims) |
| **GUIDs** | Generate unique GUIDs for your addin + each command. Use `[guid]::NewGuid()` in PowerShell or any online tool. You need one per `<AddIn>` entry in the manifest. |
| **IDE** | Visual Studio 2022+ or JetBrains Rider |

---

## Folder Structure

```
{AddinName}/
├── .github/
│   └── workflows/
│       └── build.yml                  → CI/CD pipeline (see 03-ci-and-installer.md)
├── Commands/
│   ├── {Feature}Command.cs            → IExternalCommand implementations
│   ├── {Feature}Handlers.cs           → IExternalEventHandler implementations
│   ├── {Feature}Entry.cs              → Data model + JSON persistence (if needed)
│   └── ToggleWindowCommand.cs         → Window show/hide command
├── App.cs                             → IExternalApplication entry point
├── {AddinName}Window.cs               → Modeless WPF window
├── {AddinName}.csproj                 → Build configuration
├── {AddinName}.addin                  → Revit manifest (loaded at startup)
├── config.json                        → User-editable settings
├── installer.iss                      → Inno Setup installer script
├── NuGet.config                       → NuGet sources (ensures nuget.org is available)
├── .gitignore
└── CLAUDE.md / README.md
```

**Convention**: All `IExternalCommand` and `IExternalEventHandler` classes live in the `Commands/` subfolder under the `{Namespace}.Commands` namespace.

---

## `.csproj` Template

```xml
<Project Sdk="Microsoft.NET.Sdk">

  <PropertyGroup>
    <TargetFramework>net8.0-windows</TargetFramework>
    <AssemblyName>{AddinName}</AssemblyName>
    <RootNamespace>{Namespace}</RootNamespace>
    <LangVersion>8.0</LangVersion>
    <Nullable>enable</Nullable>
    <PlatformTarget>x64</PlatformTarget>
    <UseWPF>true</UseWPF>
    <NoWarn>MSB3277</NoWarn>
    <RevitDir>C:\Program Files\Autodesk\Revit 2026\</RevitDir>
    <OutputPath>build\</OutputPath>
    <AppendTargetFrameworkToOutputPath>false</AppendTargetFrameworkToOutputPath>
    <AppendRuntimeIdentifierToOutputPath>false</AppendRuntimeIdentifierToOutputPath>
  </PropertyGroup>

  <!-- Local dev: Revit is installed -> use the real DLLs from the Revit folder -->
  <ItemGroup Condition="Exists('$(RevitDir)RevitAPI.dll')">
    <Reference Include="RevitAPI">
      <HintPath>$(RevitDir)RevitAPI.dll</HintPath>
      <Private>False</Private>
    </Reference>
    <Reference Include="RevitAPIUI">
      <HintPath>$(RevitDir)RevitAPIUI.dll</HintPath>
      <Private>False</Private>
    </Reference>
  </ItemGroup>

  <!-- CI: Revit not installed -> use Nice3point NuGet shims (reference-only) -->
  <ItemGroup Condition="!Exists('$(RevitDir)RevitAPI.dll')">
    <PackageReference Include="Nice3point.Revit.Api.RevitAPI" Version="2026.*" />
    <PackageReference Include="Nice3point.Revit.Api.RevitAPIUI" Version="2026.*" />
  </ItemGroup>

  <!-- System.Drawing (for GDI+ icon generation) — not included by default on .NET 8 -->
  <ItemGroup>
    <PackageReference Include="System.Drawing.Common" Version="8.*" />
  </ItemGroup>

  <!-- Copy config.json to output -->
  <ItemGroup>
    <None Update="config.json">
      <CopyToOutputDirectory>PreserveNewest</CopyToOutputDirectory>
    </None>
  </ItemGroup>

  <!-- Copy .addin manifest into build/ so everything needed for install is in one place -->
  <Target Name="CopyAddinManifest" AfterTargets="Build">
    <Copy SourceFiles="$(ProjectDir){AddinName}.addin" DestinationFolder="$(OutputPath)" />
  </Target>

</Project>
```

### Key properties explained

| Property | Why |
|---|---|
| `net8.0-windows` | Revit 2026 runs on .NET 8. The `-windows` suffix enables WPF. |
| `PlatformTarget: x64` | Revit is 64-bit only. |
| `UseWPF: true` | Required for WPF windows, even if you build UI in code-behind. |
| `LangVersion: 8.0` | Enables nullable reference types, switch expressions, `using` declarations. |
| `NoWarn: MSB3277` | Suppresses "indirect reference" warnings common with Revit API refs. |
| `OutputPath: build\` | Custom flat output folder — keeps artifacts separate from source. |
| `AppendTargetFramework...` | Prevents `build/net8.0-windows/` nesting — outputs go directly to `build/`. |
| `Private: False` | Revit DLLs already live in Revit's folder — don't copy them to output. |

### Conditional Revit API references

The csproj automatically picks the right reference strategy:

- **Local dev** (Revit installed): Uses the real DLLs from `C:\Program Files\Autodesk\Revit 2026\`. Full intellisense, no NuGet needed.
- **CI** (Revit not installed): Falls back to [Nice3point NuGet shims](https://github.com/Nice3point/RevitApi). These are reference-only assemblies — they provide type signatures for compilation but are never copied to output.

---

## `.addin` Manifest Template

The `.addin` file tells Revit what to load. Place it at `C:\ProgramData\Autodesk\Revit\Addins\2026\{AddinName}.addin`.

```xml
<?xml version="1.0" encoding="utf-8"?>
<RevitAddIns>

  <!-- Application entry point — loads at Revit startup, creates ribbon -->
  <AddIn Type="Application">
    <Name>{AddinName}</Name>
    <Assembly>C:\ProgramData\Autodesk\Revit\Addins\2026\{AddinName}\{AddinName}.dll</Assembly>
    <FullClassName>{Namespace}.App</FullClassName>
    <ClientId>{GUID-0000}</ClientId>
    <VendorId>{VendorId}</VendorId>
    <VendorDescription>{VendorDescription}</VendorDescription>
  </AddIn>

  <!-- Example command — can be assigned a keyboard shortcut in Revit -->
  <AddIn Type="Command">
    <Name>{Command Display Name}</Name>
    <Description>{What this command does}</Description>
    <Assembly>C:\ProgramData\Autodesk\Revit\Addins\2026\{AddinName}\{AddinName}.dll</Assembly>
    <FullClassName>{Namespace}.Commands.{CommandClassName}</FullClassName>
    <ClientId>{GUID-0001}</ClientId>
    <VendorId>{VendorId}</VendorId>
    <VendorDescription>{VendorDescription}</VendorDescription>
  </AddIn>

  <!-- Toggle window command -->
  <AddIn Type="Command">
    <Name>Toggle {AddinName} Window</Name>
    <Description>Show or hide the {AddinName} floating panel</Description>
    <Assembly>C:\ProgramData\Autodesk\Revit\Addins\2026\{AddinName}\{AddinName}.dll</Assembly>
    <FullClassName>{Namespace}.Commands.ToggleWindowCommand</FullClassName>
    <ClientId>{GUID-0002}</ClientId>
    <VendorId>{VendorId}</VendorId>
    <VendorDescription>{VendorDescription}</VendorDescription>
  </AddIn>

</RevitAddIns>
```

### Rules

- **One `Type="Application"` entry** that implements `IExternalApplication`. This runs `OnStartup()` once when Revit loads.
- **One `Type="Command"` entry per** `IExternalCommand` class. Each command can be individually assigned a keyboard shortcut by the user.
- **ClientId must be unique** across all addins loaded by Revit. Use proper GUIDs.
- **Assembly path** is absolute — points to the install location.

---

## `App.cs` — IExternalApplication Skeleton

This is the entry point. It creates the ribbon, wires up ExternalEvents, and manages the modeless window.

```csharp
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Events;
using Autodesk.Revit.UI;
using Autodesk.Revit.UI.Events;
using {Namespace}.Commands;
using System;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Reflection;
using System.Windows.Interop;
using System.Windows.Media.Imaging;
using Color = System.Drawing.Color;       // alias to avoid Autodesk.Revit.DB.Color ambiguity

namespace {Namespace}
{
    public class App : IExternalApplication
    {
        // ── Modeless window singleton ─────────────────────────────────────
        public static {AddinName}Window? Window;

        // ── ExternalEvent handlers (set properties before Raise()) ────────
        //    Create one pair (handler + event) per operation your window triggers.
        public static readonly MyHandler        MyHandler        = new MyHandler();
        // ... add more handlers as needed ...

        public static ExternalEvent MyEvent        = null!;
        // ... add more events as needed ...

        // ─────────────────────────────────────────────────────────────────

        public Result OnStartup(UIControlledApplication app)
        {
            try
            {
                // ── Create ExternalEvents ────────────────────────────────
                // MUST be created here or in IExternalCommand.Execute.
                // Never create from a background thread.
                MyEvent = ExternalEvent.Create(MyHandler);

                // ── Subscribe to Revit events (optional) ─────────────────
                app.ControlledApplication.DocumentOpened += (_, e) =>
                {
                    Window?.LoadFor(e.Document.Title);
                };

                // ── Build ribbon UI ──────────────────────────────────────
                RibbonPanel panel = app.CreateRibbonPanel("{Panel Name}");
                string dll = Assembly.GetExecutingAssembly().Location;

                var toggleData = new PushButtonData(
                    "{InternalName}", "{Button\nLabel}", dll,
                    "{Namespace}.Commands.ToggleWindowCommand")
                {
                    ToolTip    = "Show or hide the {AddinName} panel",
                    Image      = MakePanelIcon(16),
                    LargeImage = MakePanelIcon(32),
                };
                panel.AddItem(toggleData);

                return Result.Succeeded;
            }
            catch
            {
                return Result.Failed;
            }
        }

        public Result OnShutdown(UIControlledApplication app) => Result.Succeeded;

        // ── Window management ─────────────────────────────────────────────

        public static void ToggleWindow(UIApplication uiapp)
        {
            if (Window == null)
            {
                Window = new {AddinName}Window();

                // X button hides instead of destroying — preserves position and state.
                Window.Closing += (_, e) => { e.Cancel = true; Window.Hide(); };

                Window.Show();

                // Pin above Revit's main window (set after Show so the HWND exists).
                new WindowInteropHelper(Window).Owner =
                    Process.GetCurrentProcess().MainWindowHandle;

                Window.LoadFor(uiapp.ActiveUIDocument?.Document?.Title);
            }
            else if (!Window.IsVisible)
            {
                Window.LoadFor(uiapp.ActiveUIDocument?.Document?.Title);
                Window.Show();
            }
            else
            {
                Window.Hide();
            }
        }

        // ── Icon generation (no external image files needed) ──────────────

        /// <summary>
        /// Generates a simple icon programmatically using GDI+.
        /// Replace the drawing logic with your own design.
        /// </summary>
        private static BitmapSource MakePanelIcon(int size)
        {
            int m = Math.Max(2, size / 7);
            var bmp = new Bitmap(size, size);
            using (var g = Graphics.FromImage(bmp))
            {
                g.Clear(Color.FromArgb(70, 100, 140));    // background colour
                g.SmoothingMode = SmoothingMode.AntiAlias;
                float lw = Math.Max(1f, size / 10f);
                using (var pen = new System.Drawing.Pen(Color.White, lw))
                {
                    // Draw three horizontal lines (hamburger icon)
                    int span = size - 2 * m;
                    for (int i = 1; i <= 3; i++)
                    {
                        int y = m + i * span / 4;
                        g.DrawLine(pen, m, y, size - m, y);
                    }
                }
            }
            using (var ms = new MemoryStream())
            {
                bmp.Save(ms, System.Drawing.Imaging.ImageFormat.Png);
                bmp.Dispose();
                ms.Position = 0;
                return new PngBitmapDecoder(ms, BitmapCreateOptions.PreservePixelFormat,
                    BitmapCacheOption.OnLoad).Frames[0];
            }
        }
    }
}
```

### Why procedural icons?

- No image files to manage, embed, or lose.
- Scales to any size (16px for small, 32px for large ribbon icons).
- The GDI+ `Bitmap` is rendered to PNG in memory, then decoded into a WPF `BitmapSource`.
- Replace the drawing logic (lines, shapes, colours) with whatever suits your addin.

### Window lifecycle rules

1. **First toggle**: Create, hook `Closing` to hide, `Show()`, pin to Revit HWND.
2. **Subsequent toggle (hidden)**: Reload data for current document, `Show()`.
3. **Subsequent toggle (visible)**: `Hide()`.
4. **Never destroy** the window — hiding preserves its position, scroll state, and internal data.

---

## `Config` Facade Template

A static class that reads settings from `config.json` next to the DLL. Uses regex-based parsing to avoid JSON library dependencies.

```csharp
using System;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Text.RegularExpressions;

namespace {Namespace}.Commands
{
    /// <summary>
    /// Reads config.json from the same folder as the DLL.
    /// Falls back to sensible defaults if the file is missing or malformed.
    /// </summary>
    internal static class Config
    {
        private static readonly string ConfigPath = Path.Combine(
            Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location),
            "config.json");

        /// <summary>Read a numeric setting. Returns fallback on any error.</summary>
        public static double GetDoubleSetting(string key, double fallback)
        {
            try
            {
                if (File.Exists(ConfigPath))
                {
                    var json     = File.ReadAllText(ConfigPath);
                    var stripped = Regex.Replace(json, @"//[^\n]*", "");  // strip comments
                    var match    = Regex.Match(stripped,
                        $@"""{key}""\s*:\s*([0-9.]+)");
                    if (match.Success && double.TryParse(match.Groups[1].Value,
                            NumberStyles.Any, CultureInfo.InvariantCulture, out double val))
                        return val;
                }
            }
            catch { }
            return fallback;
        }

        /// <summary>Read an integer setting. Returns fallback on any error.</summary>
        public static int GetIntSetting(string key, int fallback)
        {
            try
            {
                if (File.Exists(ConfigPath))
                {
                    var json     = File.ReadAllText(ConfigPath);
                    var stripped = Regex.Replace(json, @"//[^\n]*", "");
                    var match    = Regex.Match(stripped,
                        $@"""{key}""\s*:\s*([0-9]+)");
                    if (match.Success && int.TryParse(match.Groups[1].Value, out int val) && val > 0)
                        return val;
                }
            }
            catch { }
            return fallback;
        }

        /// <summary>Write a single key-value pair back to config.json.</summary>
        public static void WriteSetting(string key, double value)
        {
            File.WriteAllText(ConfigPath,
                $"{{\n  \"{key}\": {value.ToString(CultureInfo.InvariantCulture)}\n}}\n");
        }
    }
}
```

### Why regex instead of `System.Text.Json`?

- Revit's .NET hosting can cause assembly version conflicts with `System.Text.Json`.
- A regex parser is zero-dependency and handles the simple `"key": value` structures that config files need.
- The `//` comment stripping lets users annotate their config files.
- Silent `catch {}` blocks ensure the addin always starts — it just uses defaults.

---

## `config.json` Template

```json
{
  // Description of setting 1. No restart needed.
  "SettingName": 100,

  // Description of setting 2. Restart Revit to apply.
  "AnotherSetting": 20
}
```

**Rules**:
- Keep it simple — flat key/value pairs only.
- Use `//` comments to document each setting.
- Ship a default copy with the build (via `CopyToOutputDirectory: PreserveNewest` in the csproj).
- The installer uses `onlyifdoesntexist` so user edits are never overwritten.

---

## `.gitignore` Template

```gitignore
# Build output
bin/
obj/
build/

# IDE
.vs/
*.user
*.suo
*.csproj.user

# Installer output
installer/*.exe

# NuGet
packages/

# OS
Thumbs.db
Desktop.ini
.DS_Store
```

---

## `NuGet.config` Template

```xml
<?xml version="1.0" encoding="utf-8"?>
<configuration>
  <packageSources>
    <add key="nuget.org" value="https://api.nuget.org/v3/index.json" />
  </packageSources>
</configuration>
```

This ensures the Nice3point NuGet shims are accessible on CI runners.

---

## Verification Checklist

After creating the scaffold, verify everything works:

1. **Build**:
   ```
   dotnet build -c Release
   ```
   Confirm `build/` contains: `{AddinName}.dll`, `{AddinName}.addin`, `config.json`.

2. **Install** (manual):
   - Copy `build/{AddinName}.dll` and `build/config.json` to:
     `C:\ProgramData\Autodesk\Revit\Addins\2026\{AddinName}\`
   - Copy `build/{AddinName}.addin` to:
     `C:\ProgramData\Autodesk\Revit\Addins\2026\`

3. **Launch Revit 2026** and verify:
   - Your ribbon panel appears in the Add-Ins tab.
   - Clicking the button toggles the modeless window open/closed.
   - No errors in Revit's journal file.

4. **Clean up**: Delete the copied files after testing. The installer (see `03-ci-and-installer.md`) handles deployment for real installs.

---

**Next**: [02 — Commands, Handlers & UI Patterns](02-commands-and-ui.md)
