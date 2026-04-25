; ============================================================
; PointCloudUtils — Inno Setup Installer Script
; ============================================================
; Compile:  iscc /DAppVersion=x.y.z installer.iss
;           (defaults to 1.0.0 when /D is omitted)
;
; User data preserved across reinstall AND uninstall:
;   - config.json          (user-edited settings)
; ============================================================

#ifndef AppVersion
  #define AppVersion "1.0.0"
#endif

#define MyAppName      "PointCloudUtils"
#define MyAppPublisher "Studio V2"
#define RevitYear      "2026"
#define AddinRoot      "{commonappdata}\Autodesk\Revit\Addins\" + RevitYear

[Setup]
AppId={{E7F3A2B1-C4D5-4E6F-8A9B-0C1D2E3F4A5B}
AppName={#MyAppName}
AppVersion={#AppVersion}
AppPublisher={#MyAppPublisher}
DefaultDirName={#AddinRoot}\PointCloudUtils
DisableDirPage=yes
DisableProgramGroupPage=yes
OutputDir=installer
OutputBaseFilename=PointCloudUtils-Setup-{#AppVersion}
Compression=lzma
SolidCompression=yes
PrivilegesRequired=admin
UninstallDisplayName={#MyAppName} for Revit {#RevitYear}
CloseApplications=yes
RestartApplications=no

; ── Files ────────────────────────────────────────────────────
[Files]
; Core addon DLL + dependency manifest — always overwritten
Source: "build\PointCloudUtils.dll";       DestDir: "{app}"; Flags: ignoreversion
Source: "build\PointCloudUtils.deps.json"; DestDir: "{app}"; Flags: ignoreversion

; .addin manifest lives one level up (Revit addins root)
Source: "build\PointCloudUtils.addin"; DestDir: "{#AddinRoot}"; Flags: ignoreversion

; Default config — only placed when no config exists yet;
; never removed by the uninstaller so user edits survive.
Source: "build\config.json"; DestDir: "{app}"; Flags: onlyifdoesntexist uninsneveruninstall

; ── Uninstall extras ─────────────────────────────────────────
[UninstallDelete]
; The .addin file lives outside {app}, so Inno won't auto-remove it.
Type: files; Name: "{#AddinRoot}\PointCloudUtils.addin"

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
      MsgBox('User data files (config.json) were preserved in:'#13#10#13#10 +
             ExpandConstant('{app}') + #13#10#13#10 +
             'You can delete this folder manually if you no longer need them.',
             mbInformation, MB_OK);
  end;
end;
