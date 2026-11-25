; Assistant d'installation de BT Battery Tray (Inno Setup 6).
; Compilé par tools\build-installer.ps1, qui fournit AppVersion et SourceDir.

#ifndef AppVersion
  #define AppVersion "1.0.0"
#endif
#ifndef SourceDir
  #define SourceDir "..\artifacts\publish"
#endif

#define AppName "BT Battery Tray"
#define AppExe "BtBatteryTray.exe"
#define AppUrl "https://github.com/BechirZalila/bt_speaker"

[Setup]
; Ne jamais changer AppId : il identifie l'application pour les mises à jour et la désinstallation.
AppId={{EF2B34AB-95CE-4C01-A66F-E4D1058FA9C5}
AppName={#AppName}
AppVersion={#AppVersion}
AppVerName={#AppName} {#AppVersion}
AppPublisher=Bechir Zalila
AppPublisherURL={#AppUrl}
AppSupportURL={#AppUrl}/issues
AppUpdatesURL={#AppUrl}/releases
; Installation pour l'utilisateur courant, sans droits administrateur.
PrivilegesRequired=lowest
DefaultDirName={autopf}\{#AppName}
DisableProgramGroupPage=yes
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
MinVersion=10.0.19041
LicenseFile=..\LICENSE
SetupIconFile=..\src\BtBatteryTray\app.ico
UninstallDisplayIcon={app}\{#AppExe}
UninstallDisplayName={#AppName}
OutputDir=..\artifacts
OutputBaseFilename=BtBatteryTray-Setup-{#AppVersion}
Compression=lzma2/max
SolidCompression=yes
WizardStyle=modern
; L'application est fermée par le code ci-dessous avant l'installation.
CloseApplications=no
VersionInfoVersion={#AppVersion}
VersionInfoProductName={#AppName}
VersionInfoDescription=Installation de {#AppName}

[Languages]
Name: "french"; MessagesFile: "compiler:Languages\French.isl"

[Tasks]
Name: "startup"; Description: "Lancer {#AppName} au démarrage de Windows"; GroupDescription: "Options :"

[Files]
Source: "{#SourceDir}\{#AppExe}"; DestDir: "{app}"; Flags: ignoreversion

[Icons]
Name: "{autoprograms}\{#AppName}"; Filename: "{app}\{#AppExe}"

[Registry]
; Même valeur que celle écrite par l'option « Lancer au démarrage » du menu de l'application.
Root: HKCU; Subkey: "Software\Microsoft\Windows\CurrentVersion\Run"; ValueType: string; ValueName: "BtBatteryTray"; ValueData: """{app}\{#AppExe}"""; Tasks: startup; Check: not IsAppUpdate

[Run]
Filename: "{app}\{#AppExe}"; Description: "Lancer {#AppName} maintenant"; Flags: nowait postinstall skipifsilent
; Mise à jour lancée depuis l'application (/SILENT /UPDATE=1) : on la relance une fois l'exe remplacé.
Filename: "{app}\{#AppExe}"; Flags: nowait; Check: IsAppUpdate

[Code]
const
  RunKey = 'Software\Microsoft\Windows\CurrentVersion\Run';
  RunValue = 'BtBatteryTray';
  DotNetUrl = 'https://aka.ms/dotnet/8.0/windowsdesktop-runtime-win-x64.exe';
  DotNetFile = 'windowsdesktop-runtime-8-win-x64.exe';

var
  DownloadPage: TDownloadWizardPage;

// Vrai quand l'assistant est lancé par la mise à jour intégrée de l'application (paramètre /UPDATE=1) :
// le lancement au démarrage est alors laissé tel quel (il a pu être changé depuis le menu de l'application).
function IsAppUpdate: Boolean;
begin
  Result := ExpandConstant('{param:UPDATE|0}') = '1';
end;

function IsDesktopRuntime8Installed: Boolean;
var
  FindRec: TFindRec;
begin
  Result := False;
  if FindFirst(ExpandConstant('{commonpf64}\dotnet\shared\Microsoft.WindowsDesktop.App\8.*'), FindRec) then
  begin
    try
      repeat
        if (FindRec.Attributes and FILE_ATTRIBUTE_DIRECTORY) <> 0 then
          Result := True;
      until Result or not FindNext(FindRec);
    finally
      FindClose(FindRec);
    end;
  end;
end;

procedure CloseRunningApp;
var
  ResultCode: Integer;
begin
  Exec(ExpandConstant('{sys}\taskkill.exe'), '/f /im {#AppExe}', '', SW_HIDE, ewWaitUntilTerminated, ResultCode);
end;

procedure InitializeWizard;
begin
  DownloadPage := CreateDownloadPage(SetupMessage(msgWizardPreparing), SetupMessage(msgPreparingDesc), nil);
end;

function UpdateReadyMemo(Space, NewLine, MemoUserInfoInfo, MemoDirInfo, MemoTypeInfo,
  MemoComponentsInfo, MemoGroupInfo, MemoTasksInfo: String): String;
begin
  Result := MemoDirInfo;
  if MemoTasksInfo <> '' then
    Result := Result + NewLine + NewLine + MemoTasksInfo;
  if not IsDesktopRuntime8Installed then
    Result := Result + NewLine + NewLine + 'Prérequis :' + NewLine + Space +
      'Le runtime .NET 8 Desktop (Microsoft) sera téléchargé et installé.' + NewLine + Space +
      'Windows demandera une autorisation administrateur pour cette étape.';
end;

// Télécharge et installe le runtime .NET 8 Desktop s'il est absent.
function InstallDesktopRuntime: Boolean;
var
  ResultCode: Integer;
begin
  Result := False;
  DownloadPage.Clear;
  DownloadPage.Add(DotNetUrl, DotNetFile, '');
  DownloadPage.Show;
  try
    try
      DownloadPage.Download;
    except
      SuppressibleMsgBox('Le téléchargement du runtime .NET 8 a échoué :' + #13#10 + GetExceptionMessage,
        mbCriticalError, MB_OK, IDOK);
      Exit;
    end;
  finally
    DownloadPage.Hide;
  end;

  // ShellExec permet à l'installeur du runtime de demander l'élévation (UAC).
  if not ShellExec('', ExpandConstant('{tmp}\' + DotNetFile), '/install /passive /norestart', '',
    SW_SHOW, ewWaitUntilTerminated, ResultCode) then
  begin
    SuppressibleMsgBox('Impossible de lancer l''installation du runtime .NET 8 : ' +
      SysErrorMessage(ResultCode), mbCriticalError, MB_OK, IDOK);
    Exit;
  end;

  Result := IsDesktopRuntime8Installed;
  if not Result then
    SuppressibleMsgBox('Le runtime .NET 8 Desktop n''a pas été installé (code ' + IntToStr(ResultCode) + ').' + #13#10 +
      'Tu peux l''installer manuellement : winget install --id Microsoft.DotNet.DesktopRuntime.8 -e',
      mbCriticalError, MB_OK, IDOK);
end;

function NextButtonClick(CurPageID: Integer): Boolean;
begin
  Result := True;
  if (CurPageID = wpReady) and not IsDesktopRuntime8Installed then
    Result := InstallDesktopRuntime;
end;

function PrepareToInstall(var NeedsRestart: Boolean): String;
begin
  CloseRunningApp;
  Result := '';
end;

procedure CurStepChanged(CurStep: TSetupStep);
begin
  // Réinstallation sans l'option : on retire un éventuel lancement au démarrage existant.
  if (CurStep = ssPostInstall) and not IsAppUpdate and not WizardIsTaskSelected('startup') then
    RegDeleteValue(HKEY_CURRENT_USER, RunKey, RunValue);
end;

procedure CurUninstallStepChanged(CurUninstallStep: TUninstallStep);
var
  SettingsDir: String;
begin
  if CurUninstallStep = usUninstall then
  begin
    CloseRunningApp;
    // Retire aussi la valeur si elle a été activée depuis le menu de l'application.
    RegDeleteValue(HKEY_CURRENT_USER, RunKey, RunValue);
  end
  else if CurUninstallStep = usPostUninstall then
  begin
    SettingsDir := ExpandConstant('{userappdata}\BtBatteryTray');
    if DirExists(SettingsDir) and not UninstallSilent and
       (MsgBox('Supprimer aussi tes préférences (appareils masqués, alerte) ?', mbConfirmation, MB_YESNO) = IDYES) then
      DelTree(SettingsDir, True, True, True);
  end;
end;
