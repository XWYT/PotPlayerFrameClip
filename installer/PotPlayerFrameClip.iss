#ifndef AppVersion
  #define AppVersion "0.3.0"
#endif

#define AppName "PotPlayer FrameClip"
#define AppExeName "PotPlayerFrameClip.exe"
#define AppId "{{7C39C686-1D2E-4D46-B303-C7F3173B9D42}"

[Setup]
AppId={#AppId}
AppName={#AppName}
AppVersion={#AppVersion}
AppVerName={#AppName} {#AppVersion}
AppPublisher=PotPlayer FrameClip contributors
DefaultDirName={localappdata}\PotPlayerFrameClip
DefaultGroupName={#AppName}
DisableProgramGroupPage=yes
PrivilegesRequired=lowest
OutputDir=..\dist
OutputBaseFilename=PotPlayerFrameClip-v{#AppVersion}-Setup
Compression=lzma2/ultra64
SolidCompression=yes
WizardStyle=modern
SetupLogging=yes
RestartApplications=no
RestartIfNeededByRun=no
CloseApplications=no
UninstallDisplayIcon={app}\{#AppExeName}
LicenseFile=..\LICENSE
InfoBeforeFile=..\installer\before-install.txt
VersionInfoVersion={#AppVersion}.0
VersionInfoCompany=PotPlayer FrameClip contributors
VersionInfoDescription=PotPlayer FrameClip installer
VersionInfoProductName={#AppName}
VersionInfoProductVersion={#AppVersion}
VersionInfoCopyright=Copyright (c) 2026 PotPlayer FrameClip contributors

[Languages]
Name: "chinesesimplified"; MessagesFile: "languages\ChineseSimplified.isl"
Name: "english"; MessagesFile: "compiler:Default.isl"

[Files]
Source: "..\dist\release\PotPlayerFrameClip.exe"; DestDir: "{app}"; Flags: ignoreversion
Source: "..\dist\release\PotPlayerFrameClip.exe.config"; DestDir: "{app}"; Flags: ignoreversion
Source: "..\dist\release\FrameClipMenu.xml"; DestDir: "{app}"; Flags: ignoreversion
Source: "..\dist\release\install.ps1"; DestDir: "{app}"; Flags: ignoreversion
Source: "..\dist\release\uninstall.ps1"; DestDir: "{app}"; Flags: ignoreversion
Source: "..\README.md"; DestDir: "{app}"; Flags: ignoreversion
Source: "..\LICENSE"; DestDir: "{app}"; Flags: ignoreversion
Source: "..\THIRD_PARTY_NOTICES.md"; DestDir: "{app}"; Flags: ignoreversion
Source: "..\CHANGELOG.md"; DestDir: "{app}"; Flags: ignoreversion

[Icons]
Name: "{group}\使用说明"; Filename: "{app}\README.md"
Name: "{group}\卸载 {#AppName}"; Filename: "{uninstallexe}"

[Run]
Filename: "{sys}\WindowsPowerShell\v1.0\powershell.exe"; Parameters: "-NoProfile -ExecutionPolicy Bypass -File ""{app}\install.ps1"" -InstallDirectory ""{app}"" {code:GetInstallArguments}"; WorkingDir: "{app}"; StatusMsg: "正在配置 PotPlayer 菜单与启动项..."; Flags: runhidden waituntilterminated

[UninstallRun]
Filename: "{sys}\WindowsPowerShell\v1.0\powershell.exe"; Parameters: "-NoProfile -ExecutionPolicy Bypass -File ""{app}\uninstall.ps1"" -InstallDirectory ""{app}"" {code:GetUninstallArguments}"; WorkingDir: "{app}"; Flags: runhidden waituntilterminated skipifdoesntexist; RunOnceId: "FrameClipCleanup"

[Code]
var
  PotPlayerPage: TInputDirWizardPage;
  FFmpegPage: TInputFileWizardPage;

function QuotePowerShellArgument(const Value: String): String;
begin
  Result := '"' + Value + '"';
end;

function DetectPotPlayerDirectory(): String;
var
  Candidate: String;
begin
  Result := '';
  if RegQueryStringValue(HKCU, 'Software\DAUM\PotPlayer64', 'ProgramPath', Candidate) and DirExists(Candidate) then
    Result := Candidate
  else if RegQueryStringValue(HKCU, 'Software\DAUM\PotPlayer', 'ProgramPath', Candidate) and DirExists(Candidate) then
    Result := Candidate
  else if DirExists(ExpandConstant('{localappdata}\Programs\PotPlayer')) then
    Result := ExpandConstant('{localappdata}\Programs\PotPlayer')
  else if DirExists(ExpandConstant('{pf}\DAUM\PotPlayer')) then
    Result := ExpandConstant('{pf}\DAUM\PotPlayer')
  else if DirExists(ExpandConstant('{pf32}\DAUM\PotPlayer')) then
    Result := ExpandConstant('{pf32}\DAUM\PotPlayer');
end;

function DetectFFmpeg(): String;
var
  Candidate: String;
begin
  Result := '';
  Candidate := ExpandConstant('{localappdata}\Microsoft\WinGet\Links\ffmpeg.exe');
  if FileExists(Candidate) then
    Result := Candidate
  else begin
    Candidate := ExpandConstant('{userprofile}\scoop\apps\ffmpeg\current\bin\ffmpeg.exe');
    if FileExists(Candidate) then Result := Candidate;
  end;
end;

procedure InitializeWizard();
var
  RequestedPotPlayer: String;
  RequestedFFmpeg: String;
begin
  PotPlayerPage := CreateInputDirPage(wpSelectDir,
    'PotPlayer 位置', '确认 PotPlayer 安装目录',
    '通常可以保持为空并继续，安装器会自动查找。使用便携版或自动识别失败时，请选择包含 PotPlayerMini64.exe、PotPlayer64.exe、PotPlayerMini.exe 或 PotPlayer.exe 的文件夹。',
    False, '');
  PotPlayerPage.Add('');
  RequestedPotPlayer := ExpandConstant('{param:POTPLAYERDIR|}');
  if Trim(RequestedPotPlayer) <> '' then
    PotPlayerPage.Values[0] := RequestedPotPlayer
  else
    PotPlayerPage.Values[0] := DetectPotPlayerDirectory();

  FFmpegPage := CreateInputFilePage(PotPlayerPage.ID,
    'FFmpeg 位置', '确认 ffmpeg.exe',
    'FFmpeg 用于读取源画面和导出片段。已经加入 PATH，或准备稍后在 FrameClip 设置中选择时，可以保持为空。');
  FFmpegPage.Add('ffmpeg.exe：', '可执行文件|*.exe|所有文件|*.*', '.exe');
  RequestedFFmpeg := ExpandConstant('{param:FFMPEGPATH|}');
  if Trim(RequestedFFmpeg) <> '' then
    FFmpegPage.Values[0] := RequestedFFmpeg
  else
    FFmpegPage.Values[0] := DetectFFmpeg();
end;

function GetInstallArguments(Param: String): String;
begin
  Result := '';
  if Trim(PotPlayerPage.Values[0]) <> '' then
    Result := Result + ' -PotPlayerDirectory ' + QuotePowerShellArgument(PotPlayerPage.Values[0]);
  if Trim(FFmpegPage.Values[0]) <> '' then
    Result := Result + ' -FFmpegPath ' + QuotePowerShellArgument(FFmpegPage.Values[0]);
  if CompareText(ExpandConstant('{param:NOSTARTUP|0}'), '1') = 0 then
    Result := Result + ' -NoStartup';
end;

function GetUninstallArguments(Param: String): String;
begin
  if CompareText(ExpandConstant('{param:REMOVEUSERDATA|0}'), '1') = 0 then
    Result := '-RemoveUserData'
  else
    Result := '';
end;

function StopFrameClipHelpers(): Boolean;
var
  ResultCode: Integer;
begin
  Result := Exec(ExpandConstant('{sys}\WindowsPowerShell\v1.0\powershell.exe'),
    '-NoProfile -WindowStyle Hidden -Command "Get-Process PotPlayerFrameClip,PotPlayerResolveCapture -ErrorAction SilentlyContinue | Stop-Process -Force"',
    '', SW_HIDE, ewWaitUntilTerminated, ResultCode);
end;

function PrepareToInstall(var NeedsRestart: Boolean): String;
begin
  StopFrameClipHelpers();
  Result := '';
end;

function IsPotPlayerRunning(): Boolean;
var
  ResultCode: Integer;
begin
  Exec(ExpandConstant('{sys}\WindowsPowerShell\v1.0\powershell.exe'),
    '-NoProfile -WindowStyle Hidden -Command "if (Get-Process PotPlayerMini64,PotPlayer64,PotPlayerMini,PotPlayer -ErrorAction SilentlyContinue) { exit 1 } else { exit 0 }"',
    '', SW_HIDE, ewWaitUntilTerminated, ResultCode);
  Result := ResultCode = 1;
end;

function InitializeUninstall(): Boolean;
begin
  Result := True;
  if IsPotPlayerRunning() then begin
    SuppressibleMsgBox('请先关闭 PotPlayer，再重新运行卸载程序。这样才能恢复安装前的菜单设置。',
      mbError, MB_OK, IDOK);
    Result := False;
  end;
end;
