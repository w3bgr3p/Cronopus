[Setup]
AppName=Cronopus
AppVersion=1.1.3.1
; По умолчанию предлагаем локальную папку, но даем ВЫБОР
DefaultDirName={localappdata}\Cronopus
DefaultGroupName=Cronopus
; Установка только для текущего пользователя (не нужен админ)
PrivilegesRequired=lowest

; --- ВКЛЮЧАЕМ ДИАЛОГОВЫЕ ОКНА ---
DisableDirPage=no
DisableWelcomePage=no
DisableProgramGroupPage=no
; Позволяет пользователю видеть процесс распаковки
AlwaysShowDirOnReadyPage=yes

OutputDir=installer_output
OutputBaseFilename=Cronopus_Setup
Compression=lzma2
SolidCompression=yes
SetupIconFile=icon.ico
UninstallDisplayIcon={app}\Cronopus.exe
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible


[Files]
; Publish is a folder now, not a single file: Roslyn needs assemblies on disk,
; otherwise Assembly.Location is empty and no csx or xml OwnCode compiles.
; publish-new must be cleaned before publishing - stale files would ship as is.
Source: "publish-new\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs; Excludes: "appsettings.secrets.json,*.pdb"
Source: "icon.ico"; DestDir: "{app}"; Flags: ignoreversion
Source: "zpServer.zp"; DestDir: "{app}"; Flags: ignoreversion



[Icons]
Name: "{group}\Cronopus"; Filename: "{app}\Cronopus.exe"
Name: "{autodesktop}\Cronopus"; Filename: "{app}\Cronopus.exe"

[Run]
Filename: "{app}\Cronopus.exe"; Description: "Запустить Cronopus"; WorkingDir: "{app}"; Flags: postinstall shellexec nowait skipifsilent

[Code]
procedure CurStepChanged(CurStep: TSetupStep);
var
  ResultCode: Integer;
begin
  if CurStep = ssPostInstall then
  begin
    if not Exec(ExpandConstant('{app}\Cronopus.exe'), '--import-legacy',
      ExpandConstant('{app}'), SW_HIDE, ewWaitUntilTerminated, ResultCode) then
      RaiseException('Could not start z3nDash data import.');
    if ResultCode <> 0 then
      RaiseException('z3nDash data import failed. Original data was retained.');
  end;
end;
