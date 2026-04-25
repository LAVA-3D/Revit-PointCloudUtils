# 03 — CI/CD & Installer

> Automate builds, create installers, and ship releases.

---

## Pipeline Overview

```
  Developer pushes code
       │
       ├── Push to main/master ──► Build + Installer artifact
       │
       ├── Pull Request ─────────► Build + Installer artifact
       │                           + PR comment with download links
       │
       └── Tag v*.*.* ───────────► Build + Installer artifact
                                   + GitHub Release with installer .exe
```

Every push and PR produces:
1. A **raw build artifact** (`build/` folder — DLL + addin + config)
2. An **installer .exe** (via Inno Setup)

PR builds also post a comment on the PR with download links. Tag pushes additionally create a GitHub Release.

---

## GitHub Actions Workflow

Create `.github/workflows/build.yml`:

```yaml
name: Build

on:
  push:
    branches: [ main, master ]
    tags: [ 'v*' ]
  pull_request:
    branches: [ main, master ]

permissions:
  pull-requests: write          # allow the PR comment step

jobs:
  build:
    runs-on: windows-latest

    steps:
      - name: Checkout
        uses: actions/checkout@v4

      # dotnet restore pulls the Nice3point NuGet shims (RevitAPI / RevitAPIUI)
      # because Revit is not installed on the runner.
      - name: Restore NuGet packages
        run: dotnet restore {AddinName}.csproj

      - name: Build (Release)
        run: dotnet build {AddinName}.csproj -c Release --no-restore

      # ── Installer ──────────────────────────────────────────
      - name: Determine version
        id: version
        shell: bash
        run: |
          if [[ "$GITHUB_REF" == refs/tags/v* ]]; then
            echo "version=${GITHUB_REF#refs/tags/v}" >> "$GITHUB_OUTPUT"
          else
            echo "version=0.0.0-dev" >> "$GITHUB_OUTPUT"
          fi

      - name: Install Inno Setup
        run: choco install innosetup -y --no-progress

      - name: Build installer
        run: >
          & "C:\Program Files (x86)\Inno Setup 6\ISCC.exe"
          /DAppVersion="${{ steps.version.outputs.version }}"
          installer.iss

      # ── Artifacts ──────────────────────────────────────────
      - name: Upload build artifact
        uses: actions/upload-artifact@v4
        with:
          name: {AddinName}
          path: build/

      - name: Upload installer
        uses: actions/upload-artifact@v4
        with:
          name: {AddinName}-Installer
          path: installer/*.exe

      # ── PR comment with download link ──────────────────────
      - name: Comment on PR with build link
        if: github.event_name == 'pull_request'
        uses: actions/github-script@v7
        with:
          script: |
            const runUrl = `${context.serverUrl}/${context.repo.owner}/${context.repo.repo}/actions/runs/${context.runId}`;
            const body = [
              `### :white_check_mark: Test build ready`,
              ``,
              `Download the installer and raw build from the **Artifacts** section:`,
              `**[View build artifacts](${runUrl}#artifacts)**`,
              ``,
              `| Artifact | Contents |`,
              `|---|---|`,
              `| **{AddinName}-Installer** | Setup .exe — run to install |`,
              `| **{AddinName}** | Raw build/ folder (DLL + addin + config) |`,
            ].join('\n');
            // Delete previous bot comments so we don't spam on re-runs
            const { data: comments } = await github.rest.issues.listComments({
              owner: context.repo.owner,
              repo: context.repo.repo,
              issue_number: context.issue.number,
            });
            for (const c of comments) {
              if (c.user.type === 'Bot' && c.body.includes('Test build ready')) {
                await github.rest.issues.deleteComment({
                  owner: context.repo.owner,
                  repo: context.repo.repo,
                  comment_id: c.id,
                });
              }
            }
            await github.rest.issues.createComment({
              owner: context.repo.owner,
              repo: context.repo.repo,
              issue_number: context.issue.number,
              body,
            });

  # ── Release (only on v* tags) ────────────────────────────
  release:
    needs: build
    if: startsWith(github.ref, 'refs/tags/v')
    runs-on: ubuntu-latest
    permissions:
      contents: write

    steps:
      - name: Download installer
        uses: actions/download-artifact@v4
        with:
          name: {AddinName}-Installer

      - name: Create GitHub Release
        uses: softprops/action-gh-release@v2
        with:
          files: '*.exe'
          generate_release_notes: true
```

### Key decisions

| Choice | Why |
|---|---|
| `windows-latest` runner | .NET 8 Windows build + Inno Setup require Windows. |
| `dotnet restore` separate from build | Allows `--no-restore` on build step for speed. Nice3point shims are pulled here. |
| Version from git tag | `v1.2.3` tag → version `1.2.3`. Non-tag builds get `0.0.0-dev`. |
| Inno Setup via Chocolatey | Pre-installed package manager on GitHub-hosted Windows runners. |
| De-duplicate PR comments | Deletes previous bot comments before posting — avoids spam on re-runs. |
| Separate `release` job | Runs on `ubuntu-latest` (faster/cheaper) since it only downloads + uploads. |
| `softprops/action-gh-release` | Creates GitHub Release with auto-generated release notes from commits. |

---

## Nice3point NuGet Shims

CI runners don't have Revit installed, so the real `RevitAPI.dll` and `RevitAPIUI.dll` are unavailable.

**Solution**: [Nice3point.Revit.Api](https://github.com/Nice3point/RevitApi) NuGet packages provide **reference-only assemblies** — they have the correct type signatures for compilation but contain no implementation. They're never copied to the build output.

### How it works in the `.csproj`

```xml
<!-- Local dev: Revit installed → use real DLLs -->
<ItemGroup Condition="Exists('$(RevitDir)RevitAPI.dll')">
  <Reference Include="RevitAPI">
    <HintPath>$(RevitDir)RevitAPI.dll</HintPath>
    <Private>False</Private>
  </Reference>
  ...
</ItemGroup>

<!-- CI: Revit not installed → use NuGet shims -->
<ItemGroup Condition="!Exists('$(RevitDir)RevitAPI.dll')">
  <PackageReference Include="Nice3point.Revit.Api.RevitAPI" Version="2026.*" />
  <PackageReference Include="Nice3point.Revit.Api.RevitAPIUI" Version="2026.*" />
</ItemGroup>
```

The `Exists()` condition automatically selects the right approach — no manual switching.

### Version pinning

`Version="2026.*"` pins to the Revit 2026 API but accepts any patch version. For stricter control, pin to a specific version like `Version="2026.0.0"`.

---

## Inno Setup Installer

Create `installer.iss` in the project root:

```iss
; ============================================================
; {AddinName} — Inno Setup Installer Script
; ============================================================
; Compile:  iscc /DAppVersion=x.y.z installer.iss
;           (defaults to 1.0.0 when /D is omitted)
;
; User data preserved across reinstall AND uninstall:
;   - config.json          (user-edited settings)
;   - any per-document data files
; ============================================================

#ifndef AppVersion
  #define AppVersion "1.0.0"
#endif

#define MyAppName      "{AddinName}"
#define MyAppPublisher "{VendorDescription}"
#define RevitYear      "2026"
#define AddinRoot      "{commonappdata}\Autodesk\Revit\Addins\" + RevitYear

[Setup]
AppId={{{InstallerGuid}}
AppName={#MyAppName}
AppVersion={#AppVersion}
AppPublisher={#MyAppPublisher}
DefaultDirName={#AddinRoot}\{AddinName}
DisableDirPage=yes
DisableProgramGroupPage=yes
OutputDir=installer
OutputBaseFilename={AddinName}-Setup-{#AppVersion}
Compression=lzma
SolidCompression=yes
PrivilegesRequired=admin
UninstallDisplayName={#MyAppName} for Revit {#RevitYear}
CloseApplications=yes
RestartApplications=no

; ── Files ────────────────────────────────────────────────────
[Files]
; Core addon DLL + dependency manifest — always overwritten
Source: "build\{AddinName}.dll";       DestDir: "{app}"; Flags: ignoreversion
Source: "build\{AddinName}.deps.json"; DestDir: "{app}"; Flags: ignoreversion

; .addin manifest lives one level up (Revit addins root)
Source: "build\{AddinName}.addin"; DestDir: "{#AddinRoot}"; Flags: ignoreversion

; Default config — only placed when no config exists yet;
; never removed by the uninstaller so user edits survive.
Source: "build\config.json"; DestDir: "{app}"; Flags: onlyifdoesntexist uninsneveruninstall

; ── Uninstall extras ─────────────────────────────────────────
[UninstallDelete]
; The .addin file lives outside {app}, so Inno won't auto-remove it.
Type: files; Name: "{#AddinRoot}\{AddinName}.addin"

; ── Pascal helpers ───────────────────────────────────────────
[Code]

(* Return True when Revit.exe is running *)
function IsRevitRunning: Boolean;
var
  Locator, Service, ResultSet: Variant;
begin
  Result := False;
  try
    Locator   := CreateOleObject('WbemScripting.SWbemLocator');
    Service   := Locator.ConnectServer('localhost', 'root\CIMV2', '', '');
    ResultSet := Service.ExecQuery(
                   'SELECT Name FROM Win32_Process WHERE Name="Revit.exe"');
    Result := ResultSet.Count > 0;
  except
  end;
end;

(* ── Install: warn if Revit is open ── *)
function InitializeSetup: Boolean;
begin
  Result := True;
  if IsRevitRunning then
  begin
    if MsgBox('Revit appears to be running.'#13#10#13#10 +
              'Please close Revit before installing to avoid locked-file errors.'#13#10#13#10 +
              'Continue anyway?',
              mbConfirmation, MB_YESNO or MB_DEFBUTTON2) = IDNO then
      Result := False;
  end;
end;

(* ── Uninstall: warn if Revit is open ── *)
function InitializeUninstall: Boolean;
begin
  Result := True;
  if IsRevitRunning then
  begin
    if MsgBox('Revit appears to be running.'#13#10#13#10 +
              'Please close Revit before uninstalling.'#13#10#13#10 +
              'Continue anyway?',
              mbConfirmation, MB_YESNO or MB_DEFBUTTON2) = IDNO then
      Result := False;
  end;
end;

(* ── After uninstall: tell user about preserved files ── *)
procedure CurUninstallStepChanged(CurUninstallStep: TUninstallStep);
begin
  if CurUninstallStep = usPostUninstall then
  begin
    if DirExists(ExpandConstant('{app}')) then
      MsgBox('User data files (config.json and saved data) were preserved in:'#13#10#13#10 +
             ExpandConstant('{app}') + #13#10#13#10 +
             'You can delete this folder manually if you no longer need them.',
             mbInformation, MB_OK);
  end;
end;
```

### Install paths explained

```
C:\ProgramData\Autodesk\Revit\Addins\2026\
├── {AddinName}.addin                    ← manifest (Revit scans this folder)
└── {AddinName}\                         ← {app} in Inno Setup terms
    ├── {AddinName}.dll                  ← your compiled addin
    ├── {AddinName}.deps.json            ← .NET dependency manifest
    ├── config.json                      ← user settings (preserved)
    └── mydata_*.json                    ← per-document data (not in installer)
```

### File flags

| Flag | Meaning |
|---|---|
| `ignoreversion` | Always overwrite, regardless of version — ensures clean upgrades. |
| `onlyifdoesntexist` | Only copy if the file doesn't exist — preserves user edits on upgrade. |
| `uninsneveruninstall` | Don't delete this file on uninstall — user data survives. |

### WMI Revit detection

The `IsRevitRunning` function uses Windows Management Instrumentation (WMI) to query running processes. This is more reliable than `FindWindow` because:
- Works even if Revit's main window is hidden or minimized.
- Doesn't depend on window titles that change between Revit versions.
- Fails silently (returns `False`) if WMI is unavailable.

### `[UninstallDelete]`

The `.addin` manifest lives one folder up from `{app}`, so Inno Setup won't auto-remove it during uninstall. The `[UninstallDelete]` section explicitly deletes it.

---

## Release Workflow

### Creating a release

```bash
# 1. Tag the commit
git tag v1.0.0

# 2. Push the tag
git push origin v1.0.0
```

This triggers the CI pipeline which:
1. Builds the addin (`dotnet build -c Release`)
2. Compiles the installer with version `1.0.0` baked in
3. Uploads both as artifacts
4. Creates a GitHub Release with the installer `.exe` attached
5. Auto-generates release notes from commit messages since last tag

### Version numbering

| Scenario | Version |
|---|---|
| PR build | `0.0.0-dev` (shown in installer filename) |
| Push to main (no tag) | `0.0.0-dev` |
| Tag `v1.2.3` | `1.2.3` |

The version is extracted from the git tag in the CI workflow:

```bash
if [[ "$GITHUB_REF" == refs/tags/v* ]]; then
  echo "version=${GITHUB_REF#refs/tags/v}" >> "$GITHUB_OUTPUT"
else
  echo "version=0.0.0-dev" >> "$GITHUB_OUTPUT"
fi
```

---

## Adapting for Multiple Revit Versions

To support Revit 2025 or 2027 alongside 2026:

### 1. Change the `.addin` manifest

Update the assembly path:
```xml
<Assembly>C:\ProgramData\Autodesk\Revit\Addins\{RevitYear}\{AddinName}\{AddinName}.dll</Assembly>
```

### 2. Change the installer

Update the preprocessor defines:
```iss
#define RevitYear "2025"   ; or "2027"
```

### 3. Change the Nice3point NuGet shim version

```xml
<PackageReference Include="Nice3point.Revit.Api.RevitAPI" Version="{RevitYear}.*" />
```

### 4. (Advanced) Multi-targeting

For a single codebase supporting multiple Revit versions, you'd need conditional compilation (`#if REVIT2025`), multiple output folders, and separate installer configs. This is a significant complexity increase — consider whether it's worth it for your use case.

---

## Quick Reference: What Goes Where

| What | Where in the project | Where it's installed |
|---|---|---|
| Source code | `*.cs`, `Commands/*.cs` | Not installed (compiled into DLL) |
| Built DLL | `build/{AddinName}.dll` | `C:\ProgramData\...\Addins\2026\{AddinName}\` |
| Addin manifest | `{AddinName}.addin` → `build/` | `C:\ProgramData\...\Addins\2026\` |
| Config | `config.json` → `build/` | `C:\ProgramData\...\Addins\2026\{AddinName}\` |
| Installer script | `installer.iss` | Not installed (used to build installer) |
| Installer output | `installer/{AddinName}-Setup-x.y.z.exe` | Distributed to users |
| CI workflow | `.github/workflows/build.yml` | Not installed (runs on GitHub) |

---

**Previous**: [02 — Commands, Handlers & UI Patterns](02-commands-and-ui.md)
