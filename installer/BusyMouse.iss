; BusyMouse installer (Inno Setup 6.1 or later). Build both flavours with tools\package.ps1, or by hand:
;   ISCC /DFlavor=WithRuntime /DPlatform=x64 /DSource=<publish folder> /DAppVersion=1.0.0 installer\BusyMouse.iss
;
;   WithRuntime  everything, .NET included: nothing else to install.
;   NoRuntime    smaller; installs the .NET Runtime from Microsoft during setup if the PC doesn't have it.
;                The launcher (BusyMouse.exe) also offers it later if .NET is removed.

#ifndef Flavor
  #define Flavor "WithRuntime"
#endif
#ifndef Platform
  #define Platform "x64"
#endif
#ifndef AppVersion
  #define AppVersion "1.0.0"
#endif
#ifndef Source
  #error Pass /DSource=<publish folder>
#endif
#ifndef OutputDir
  #define OutputDir "..\dist"
#endif

#define RuntimeMajor "10"
#if Platform == "ARM64"
  #define Arch "arm64"
#else
  #define Arch "x64"
#endif

[Setup]
AppId={{6E1F4C2B-7A0D-4B8E-9C35-B05F1A9D2E71}
AppName=BusyMouse
AppVersion={#AppVersion}
AppVerName=BusyMouse {#AppVersion}
AppPublisher=Azharul Islam
AppCopyright=© 2026 Azharul Islam
DefaultDirName={autopf}\BusyMouse
DefaultGroupName=BusyMouse
DisableProgramGroupPage=yes
; Per-user install by default (no admin needed); the user may choose "all users" instead.
PrivilegesRequired=lowest
PrivilegesRequiredOverridesAllowed=dialog
#if Platform == "ARM64"
ArchitecturesAllowed=arm64
ArchitecturesInstallIn64BitMode=arm64
#else
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
#endif
MinVersion=10.0.17763
; Held by the running app: setup and uninstall ask to close it first.
AppMutex=single instance: BusyMouse
CloseApplications=yes
SetupIconFile=..\BusyMouse.WinUI\Assets\BusyMouse.ico
UninstallDisplayIcon={app}\BusyMouse.exe
UninstallDisplayName=BusyMouse
WizardStyle=modern
Compression=lzma2/max
SolidCompression=yes
OutputDir={#OutputDir}
#if Flavor == "NoRuntime"
OutputBaseFilename=BusyMouse-{#AppVersion}-{#Arch}-setup-no-runtime
#else
OutputBaseFilename=BusyMouse-{#AppVersion}-{#Arch}-setup
#endif

[Tasks]
Name: desktopicon; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"; Flags: unchecked

[InstallDelete]
; A different flavour may be installed already; clear it so the two layouts don't mix.
Type: filesandordirs; Name: "{app}\app"
Type: filesandordirs; Name: "{app}\*.dll"

[Files]
Source: "{#Source}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{autoprograms}\BusyMouse"; Filename: "{app}\BusyMouse.exe"
Name: "{autodesktop}\BusyMouse"; Filename: "{app}\BusyMouse.exe"; Tasks: desktopicon

[Registry]
; "Start with Windows" is switched on inside the app; just make sure uninstall removes it.
Root: HKCU; Subkey: "Software\Microsoft\Windows\CurrentVersion\Run"; ValueType: none; ValueName: "BusyMouse"; Flags: uninsdeletevalue

[Run]
Filename: "{app}\BusyMouse.exe"; Parameters: "--settings"; Description: "{cm:LaunchProgram,BusyMouse}"; Flags: nowait postinstall skipifsilent

#if Flavor == "NoRuntime"
[Code]
var
  DownloadPage: TDownloadWizardPage;

// True when Root\shared\Microsoft.NETCore.App holds a release of .NET {#RuntimeMajor} or later.
function HasRuntimeIn(Root: String): Boolean;
var
  Find: TFindRec;
  Name: String;
  Dot: Integer;
begin
  Result := False;
  if (Root = '') or not FindFirst(AddBackslash(Root) + 'shared\Microsoft.NETCore.App\*', Find) then
    Exit;
  try
    repeat
      Name := Find.Name;
      Dot := Pos('.', Name);
      if (Find.Attributes and FILE_ATTRIBUTE_DIRECTORY <> 0) and (Dot > 1) and (Pos('-', Name) = 0) and
         (StrToIntDef(Copy(Name, 1, Dot - 1), 0) >= {#RuntimeMajor}) then
        Result := True;
    until Result or not FindNext(Find);
  finally
    FindClose(Find);
  end;
end;

function HasRuntime(): Boolean;
var
  Location: String;
begin
  Result := HasRuntimeIn(GetEnv('DOTNET_ROOT'));
  if not Result and RegQueryStringValue(HKLM32, 'SOFTWARE\dotnet\Setup\InstalledVersions\{#Arch}', 'InstallLocation', Location) then
    Result := HasRuntimeIn(Location);
  if not Result then
    Result := HasRuntimeIn(ExpandConstant('{commonpf64}\dotnet'));
end;

function OnDownloadProgress(const Url, FileName: String; const Progress, ProgressMax: Int64): Boolean;
begin
  Result := True;
end;

procedure InitializeWizard;
begin
  DownloadPage := CreateDownloadPage(SetupMessage(msgWizardPreparing), 'Downloading the .NET {#RuntimeMajor} Runtime from Microsoft...', @OnDownloadProgress);
end;

function UpdateReadyMemo(Space, NewLine, MemoUserInfoInfo, MemoDirInfo, MemoTypeInfo, MemoComponentsInfo, MemoGroupInfo, MemoTasksInfo: String): String;
begin
  Result := MemoDirInfo + NewLine + NewLine + MemoGroupInfo;
  if MemoTasksInfo <> '' then
    Result := Result + NewLine + NewLine + MemoTasksInfo;
  if not HasRuntime() then
    Result := Result + NewLine + NewLine + 'Also download and install:' + NewLine + Space + '.NET {#RuntimeMajor} Runtime ({#Arch}), about 30 MB, from Microsoft';
end;

// Before copying files: fetch and install the runtime if it's missing.
function NextButtonClick(CurPageID: Integer): Boolean;
var
  Installer: String;
  Code: Integer;
begin
  Result := True;
  if (CurPageID <> wpReady) or HasRuntime() then
    Exit;

  DownloadPage.Clear;
  DownloadPage.Add('https://aka.ms/dotnet/{#RuntimeMajor}.0/dotnet-runtime-win-{#Arch}.exe', 'dotnet-runtime.exe', '');
  DownloadPage.Show;
  try
    try
      DownloadPage.Download;
    except
      if DownloadPage.AbortedByUser then
        Result := False
      else
        Result := SuppressibleMsgBox('The .NET Runtime couldn''t be downloaded: ' + GetExceptionMessage + #13#10#13#10 +
          'Install BusyMouse anyway? It will offer to install .NET again when you start it.', mbError, MB_YESNO, IDYES) = IDYES;
      Exit;
    end;

    DownloadPage.SetText('Installing the .NET {#RuntimeMajor} Runtime...', 'Windows may ask for permission.');
    DownloadPage.ProgressBar.Style := npbstMarquee;
    Installer := ExpandConstant('{tmp}\dotnet-runtime.exe');
    // The Microsoft installer asks Windows for admin rights itself.
    if not ShellExec('', Installer, '/install /passive /norestart', '', SW_SHOW, ewWaitUntilTerminated, Code) or
       ((Code <> 0) and (Code <> 3010) and not HasRuntime()) then
      Result := SuppressibleMsgBox('The .NET Runtime wasn''t installed (code ' + IntToStr(Code) + ').' + #13#10#13#10 +
        'Install BusyMouse anyway? It will offer to install .NET again when you start it.', mbError, MB_YESNO, IDYES) = IDYES;
  finally
    DownloadPage.ProgressBar.Style := npbstNormal;
    DownloadPage.Hide;
  end;
end;
#endif
