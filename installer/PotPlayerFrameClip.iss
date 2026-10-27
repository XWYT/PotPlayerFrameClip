#ifndef AppVersion
  #define AppVersion "0.4.2"
#endif

#define AppName "PotPlayer FrameClip"
#define AppExeName "PotPlayerFrameClip.exe"
#define AppId "{{7C39C686-1D2E-4D46-B303-C7F3173B9D42}"
#ifdef BundledFFmpeg
  #ifndef FFmpegVersion
    #error FFmpegVersion must be passed by the pinned bundle build
  #endif
#endif
#ifdef FrameClipTestBuild
  #undef AppId
  #define AppId "{{99CF512C-173D-4126-A610-41E04F5A51DB}"
#endif

[Setup]
AppId={#AppId}
AppName={#AppName}
AppVersion={#AppVersion}
AppVerName={#AppName} {#AppVersion}
AppPublisher=PotPlayer FrameClip contributors
DefaultDirName={localappdata}\PotPlayerFrameClip
#ifdef FrameClipTestBuild
DefaultGroupName=FrameClip Isolated Installer Test
#else
DefaultGroupName={#AppName}
#endif
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
Source: "..\dist\release\FrameClipBridge64.dll"; DestDir: "{app}"; Flags: ignoreversion
Source: "..\dist\release\FrameClipBridge32.dll"; DestDir: "{app}"; Flags: ignoreversion
Source: "..\dist\release\FrameClipBridgeHost32.exe"; DestDir: "{app}"; Flags: ignoreversion
Source: "..\dist\release\FrameClipMenu.xml"; DestDir: "{app}"; Flags: ignoreversion
Source: "..\dist\release\install.ps1"; DestDir: "{app}"; Flags: ignoreversion
Source: "..\dist\release\uninstall.ps1"; DestDir: "{app}"; Flags: ignoreversion
Source: "..\dist\release\maintenance.ps1"; DestDir: "{app}"; Flags: ignoreversion
Source: "..\dist\release\transaction.ps1"; DestDir: "{app}"; Flags: ignoreversion
Source: "..\dist\release\maintenance.ps1"; Flags: dontcopy
Source: "..\dist\release\transaction.ps1"; Flags: dontcopy
Source: "..\dist\release\ffmpeg.ps1"; DestDir: "{app}"; Flags: ignoreversion
Source: "..\dist\release\ffmpeg-manifest.json"; DestDir: "{app}"; Flags: ignoreversion
Source: "..\dist\release\ffmpeg.ps1"; Flags: dontcopy
Source: "..\dist\release\ffmpeg-manifest.json"; Flags: dontcopy
#ifdef BundledFFmpeg
Source: "..\dist\vendor\{#FFmpegVersion}\*"; DestDir: "{app}\tools\ffmpeg\{#FFmpegVersion}"; Flags: ignoreversion; Check: UseBundledFFmpeg
#endif
Source: "..\dist\release\FrameClipMaintenance.exe"; DestDir: "{app}"; Flags: ignoreversion
Source: "..\README.md"; DestDir: "{app}"; Flags: ignoreversion
Source: "..\README.en.md"; DestDir: "{app}"; Flags: ignoreversion
Source: "..\docs\*.md"; DestDir: "{app}\docs"; Flags: ignoreversion
Source: "..\docs\releases\*.md"; DestDir: "{app}\docs\releases"; Flags: ignoreversion
Source: "..\assets\readme\*.png"; DestDir: "{app}\assets\readme"; Flags: ignoreversion
Source: "..\LICENSE"; DestDir: "{app}"; Flags: ignoreversion
Source: "..\THIRD_PARTY_NOTICES.md"; DestDir: "{app}"; Flags: ignoreversion
Source: "..\assets\LUCIDE-LICENSE"; DestDir: "{app}"; Flags: ignoreversion
Source: "..\CHANGELOG.md"; DestDir: "{app}"; Flags: ignoreversion

[Icons]
Name: "{group}\使用说明"; Filename: "{app}\README.md"
Name: "{group}\卸载 {#AppName}"; Filename: "{app}\FrameClipMaintenance.exe"; Parameters: "--uninstall"

[Code]
var
  PotPlayerPage: TInputDirWizardPage;
  FFmpegPage: TInputFileWizardPage;
  FFmpegModePage: TInputOptionWizardPage;
  TransactionStarted, TransactionCommitted, ConfigurationFailed: Boolean;
  JournalDirectory: String;

function DataDirectory(): String;
begin
#ifdef FrameClipTestBuild
  Result := ExpandConstant('{param:DATADIR|{app}}');
#else
  Result := ExpandConstant('{localappdata}\PotPlayerFrameClip');
#endif
end;

function TestArguments(): String;
begin
  Result := '';
#ifdef FrameClipTestBuild
  Result := ' -TestMode';
#endif
end;

function QuotePowerShellArgument(const Value: String): String;
begin
  Result := '"' + Value + '"';
end;

function DetectPotPlayerDirectory(): String;
var
  Candidate: String;
  ExistingConfig: String;
  ConfigLines: TArrayOfString;
  Index: Integer;
begin
  Result := '';
  ExistingConfig := ExpandConstant('{localappdata}\PotPlayerFrameClip\FrameClip.ini');
  if LoadStringsFromFile(ExistingConfig, ConfigLines) then begin
    for Index := 0 to GetArrayLength(ConfigLines) - 1 do begin
      if Pos('PotPlayerMenuPath=', ConfigLines[Index]) = 1 then begin
        Candidate := ExtractFileDir(ExtractFileDir(Copy(ConfigLines[Index], Length('PotPlayerMenuPath=') + 1, MaxInt)));
        if DirExists(Candidate) then begin
          Result := Candidate;
          Exit;
        end;
      end;
    end;
  end;
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
  ConfigLines: TArrayOfString;
  Index: Integer;
begin
  Result := '';
#ifndef FrameClipTestBuild
  if LoadStringsFromFile(ExpandConstant('{localappdata}\PotPlayerFrameClip\FrameClip.ini'), ConfigLines) then begin
    for Index := 0 to GetArrayLength(ConfigLines) - 1 do begin
      if Pos('FFmpeg=', ConfigLines[Index]) = 1 then begin
        Candidate := Copy(ConfigLines[Index], Length('FFmpeg=') + 1, MaxInt);
        if FileExists(Candidate) and FileExists(AddBackslash(ExtractFileDir(Candidate)) + 'ffprobe.exe') then begin Result := Candidate; Exit; end;
      end;
    end;
  end;
#endif
  Candidate := ExpandConstant('{pf}\FFmpeg\ffmpeg.exe');
  if FileExists(Candidate) then begin Result := Candidate; Exit; end;
  Candidate := ExpandConstant('{pf}\FFmpeg\bin\ffmpeg.exe');
  if FileExists(Candidate) then begin Result := Candidate; Exit; end;
  Candidate := ExpandConstant('{localappdata}\Microsoft\WinGet\Links\ffmpeg.exe');
  if FileExists(Candidate) then
    Result := Candidate
  else begin
    Candidate := ExpandConstant('{%USERPROFILE}\scoop\apps\ffmpeg\current\bin\ffmpeg.exe');
    if FileExists(Candidate) then Result := Candidate;
  end;
end;

function FFmpegMode(): String;
begin
  Result := ExpandConstant('{param:FFMPEGMODE|}');
  if Result <> '' then Exit;
  Result := 'existing';
  if ExpandConstant('{param:FFMPEGPATH|}') <> '' then Exit;
#ifdef BundledFFmpeg
  if FFmpegModePage.SelectedValueIndex = 1 then Result := 'bundled';
  if FFmpegModePage.SelectedValueIndex = 2 then Result := 'later';
#else
  if FFmpegModePage.SelectedValueIndex = 1 then Result := 'later';
#endif
end;

function UseBundledFFmpeg(): Boolean;
begin
  Result := CompareText(FFmpegMode(), 'bundled') = 0;
end;

function ShouldSkipPage(PageID: Integer): Boolean;
begin
  Result := (PageID = FFmpegPage.ID) and (CompareText(FFmpegMode(), 'existing') <> 0);
end;

procedure InitializeWizard();
var
  RequestedPotPlayer: String;
  RequestedFFmpeg: String;
begin
  JournalDirectory := ExpandConstant('{localappdata}\FrameClipMaintenance\backups\') + GetDateTimeString('yyyymmdd-hhnnss', '-', ':') + '-' + ExtractFileName(ExpandConstant('{tmp}'));
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

  FFmpegModePage := CreateInputOptionPage(PotPlayerPage.ID, 'FFmpeg', '选择依赖配置方式',
    '内置版本仅供 FrameClip 使用，不修改系统 PATH，也不覆盖已有安装。稍后配置时，截图和导出需要先在设置中选择 FFmpeg。', True, False);
  FFmpegModePage.Add('使用已有 FFmpeg');
#ifdef BundledFFmpeg
  FFmpegModePage.Add('未安装，配置内置 FFmpeg');
#endif
  FFmpegModePage.Add('稍后配置');
  FFmpegModePage.SelectedValueIndex := 0;
#ifdef BundledFFmpeg
  if DetectFFmpeg() = '' then FFmpegModePage.SelectedValueIndex := 1;
#endif
  FFmpegPage := CreateInputFilePage(FFmpegModePage.ID,
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
  Result := Result + ' -FFmpegMode ' + QuotePowerShellArgument(FFmpegMode());
  if (CompareText(FFmpegMode(), 'existing') = 0) and (Trim(FFmpegPage.Values[0]) <> '') then
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

function IsPotPlayerRunning(): Boolean;
var
  ResultCode: Integer;
begin
#ifdef FrameClipTestBuild
  Result := False;
  Exit;
#endif
  if not Exec(ExpandConstant('{sys}\WindowsPowerShell\v1.0\powershell.exe'),
    '-NoProfile -WindowStyle Hidden -Command "if (Get-Process PotPlayerMini64,PotPlayer64,PotPlayerMini,PotPlayer -ErrorAction SilentlyContinue) { exit 1 } else { exit 0 }"',
    '', SW_HIDE, ewWaitUntilTerminated, ResultCode) then begin Result := True; Exit; end;
  Result := ResultCode <> 0;
end;

function RunScript(const Script, Arguments: String): Boolean;
var Code: Integer;
begin
  Result := Exec(ExpandConstant('{sys}\WindowsPowerShell\v1.0\powershell.exe'),
    '-NoProfile -ExecutionPolicy Bypass -File "' + Script + '" ' + Arguments,
    '', SW_HIDE, ewWaitUntilTerminated, Code);
  Result := Result and (Code = 0);
end;

function Transaction(const Action: String): Boolean;
var RegistryKey, BundleArgument: String;
begin
  BundleArgument := '';
  if UseBundledFFmpeg() then BundleArgument := ' -BundledFFmpeg';
  RegistryKey := 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Uninstall\{7C39C686-1D2E-4D46-B303-C7F3173B9D42}_is1';
#ifdef FrameClipTestBuild
  RegistryKey := 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Uninstall\{99CF512C-173D-4126-A610-41E04F5A51DB}_is1';
#endif
  Result := RunScript(ExpandConstant('{tmp}\transaction.ps1'), '-Action ' + Action +
    ' -InstallDirectory "' + ExpandConstant('{app}') + '" -DataDirectory "' + DataDirectory() +
    '" -Journal "' + JournalDirectory + '" -UninstallRegistryKey "' + RegistryKey + '" -PlayerDirectory "' + PotPlayerPage.Values[0] + '"' + BundleArgument + TestArguments());
end;

function PrepareToInstall(var NeedsRestart: Boolean): String;
var Player: String;
begin
  Result := '';
  Player := Trim(PotPlayerPage.Values[0]);
  if Player = '' then Player := DetectPotPlayerDirectory();
  if not (FileExists(AddBackslash(Player) + 'PotPlayerMini64.exe') or FileExists(AddBackslash(Player) + 'PotPlayer64.exe') or FileExists(AddBackslash(Player) + 'PotPlayerMini.exe') or FileExists(AddBackslash(Player) + 'PotPlayer.exe')) then begin
    Result := 'Select the folder containing the PotPlayer executable. No files have been replaced.'; Exit;
  end;
  PotPlayerPage.Values[0] := Player;
  if (FFmpegMode() <> 'existing') and (FFmpegMode() <> 'bundled') and (FFmpegMode() <> 'later') then begin Result := 'Invalid FFmpeg mode.'; Exit; end;
#ifndef BundledFFmpeg
  if UseBundledFFmpeg() then begin Result := 'This installer does not contain FFmpeg. Use the Full offline installer.'; Exit; end;
#else
  if UseBundledFFmpeg() and not IsWin64 then begin Result := 'Bundled FFmpeg requires 64-bit Windows. Select an existing compatible FFmpeg instead.'; Exit; end;
#endif
  if (FFmpegMode() = 'existing') and (Trim(FFmpegPage.Values[0]) <> '') then begin
    if not FileExists(FFmpegPage.Values[0]) or not FileExists(AddBackslash(ExtractFileDir(FFmpegPage.Values[0])) + 'ffprobe.exe') then begin
      Result := 'Select ffmpeg.exe with ffprobe.exe in the same folder.'; Exit;
    end;
  end;
  if IsPotPlayerRunning() then begin
    Result := '请先关闭 PotPlayer，再继续安装或升级。FrameClip 原生菜单桥接会在播放器进程内保持加载，不能在播放期间安全替换。';
    Exit;
  end;
  ExtractTemporaryFile('maintenance.ps1');
  ExtractTemporaryFile('transaction.ps1');
  ExtractTemporaryFile('ffmpeg.ps1');
  ExtractTemporaryFile('ffmpeg-manifest.json');
  if not RunScript(ExpandConstant('{tmp}\transaction.ps1'), '-Action stop -InstallDirectory "' + ExpandConstant('{app}') + '" -DataDirectory "' + DataDirectory() + '"' + TestArguments()) then begin
    Result := '请先完成或取消 FrameClip 任务，并从托盘退出后重试。安装器不会强制终止任务。'; Exit;
  end;
  if not TransactionStarted then begin
    if not Transaction('begin') then begin Result := '无法创建升级回退备份，未开始替换文件。'; Exit; end;
    TransactionStarted := True;
  end;
  Result := '';
end;

procedure CurStepChanged(CurStep: TSetupStep);
begin
  if CurStep = ssPostInstall then begin
#ifdef FrameClipTestBuild
    if ExpandConstant('{param:TESTFAILCONFIG|0}') = '1' then begin ConfigurationFailed := True; Exit; end;
#endif
    if not RunScript(ExpandConstant('{app}\install.ps1'), '-NoLaunch -InstallDirectory "' + ExpandConstant('{app}') + '" -DataDirectory "' + DataDirectory() + '" ' + GetInstallArguments('') + TestArguments()) then begin
      ConfigurationFailed := True;
      Log('FrameClip configuration failed. Transaction will roll back and setup will return a failure code.');
    end;
  end;
  if (CurStep = ssDone) and not ConfigurationFailed then begin
    if not Transaction('commit') then begin ConfigurationFailed := True; Exit; end;
    TransactionCommitted := True;
#ifndef FrameClipTestBuild
    RegWriteStringValue(HKCU, 'Software\Microsoft\Windows\CurrentVersion\Uninstall\{7C39C686-1D2E-4D46-B303-C7F3173B9D42}_is1',
      'UninstallString', '"' + ExpandConstant('{app}\FrameClipMaintenance.exe') + '" --uninstall');
#endif
  end;
end;

function GetCustomSetupExitCode(): Integer;
begin
  if ConfigurationFailed then Result := 1 else Result := 0;
end;

procedure DeinitializeSetup();
begin
  if TransactionStarted and not TransactionCommitted then begin
    if not Transaction('rollback') then Log('FrameClip rollback failed; preserve the setup log and transaction directory.');
  end;
end;

function InitializeUninstall(): Boolean;
begin
  Result := True;
  if IsPotPlayerRunning() then begin
    SuppressibleMsgBox('请先关闭 PotPlayer，再重新运行卸载程序。这样才能恢复安装前的菜单设置。',
      mbError, MB_OK, IDOK);
    Result := False;
    Exit;
  end;
  Result := RunScript(ExpandConstant('{app}\uninstall.ps1'), '-KeepProgramFiles -InstallDirectory "' + ExpandConstant('{app}') + '" -DataDirectory "' + DataDirectory() + '" ' + GetUninstallArguments('') + TestArguments());
  if not Result then SuppressibleMsgBox('卸载前清理未完成，程序文件已保留。请先完成任务并检查日志后重试。', mbError, MB_OK, IDOK);
end;
