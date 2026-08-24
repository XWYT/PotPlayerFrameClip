[CmdletBinding()]
param(
    [string]$ExecutablePath,
    [string]$ExpectedVersion
)

$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path -Parent $PSScriptRoot
$source = Join-Path $projectRoot 'src\PotPlayerFrameClip.cs'
$menuPath = Join-Path $projectRoot 'menu\FrameClipMenu.xml'
$installerPath = Join-Path $projectRoot 'installer\PotPlayerFrameClip.iss'
if (-not $ExecutablePath) { $ExecutablePath = Join-Path $projectRoot 'dist\PotPlayerFrameClip.exe' }

function Assert-True([bool]$Condition, [string]$Message) {
    if (-not $Condition) { throw $Message }
}

function Get-PeMachine([string]$Path) {
    $bytes = [IO.File]::ReadAllBytes($Path)
    $offset = [BitConverter]::ToInt32($bytes, 0x3c)
    return [BitConverter]::ToUInt16($bytes, $offset + 4)
}

$sourceText = [IO.File]::ReadAllText($source, [Text.Encoding]::UTF8)
$nativeSource = [IO.File]::ReadAllText((Join-Path $projectRoot 'native\frameclip_bridge.c'), [Text.Encoding]::UTF8)
$installerText = [IO.File]::ReadAllText($installerPath, [Text.Encoding]::UTF8)
$uninstallText = [IO.File]::ReadAllText((Join-Path $PSScriptRoot 'uninstall.ps1'), [Text.Encoding]::UTF8)
$forbiddenBrand = 'Re' + 'solve'
Assert-True (-not $sourceText.Contains($forbiddenBrand)) 'A legacy product-specific brand remains in source.'
Assert-True (-not ($sourceText -match '[A-Za-z]:\\')) 'A fixed Windows drive path remains in source.'
Assert-True ($sourceText.Contains('PotPlayerMini64') -and $sourceText.Contains('PotPlayerMini')) 'Both PotPlayer bitness variants must be detected.'
Assert-True ($sourceText.Contains('Environment.SpecialFolder.LocalApplicationData')) 'Writable per-user state path is missing.'
Assert-True ($sourceText.Contains('NativeBridgeManager') -and $sourceText.Contains('SetWindowsHookExNative') -and `
    $sourceText.Contains('FrameClipMouseProc')) 'The process-local native menu bridge is not connected.'
Assert-True ($sourceText.Contains('NativeBridgeMessageWindow') -and $sourceText.Contains('PotPlayerFrameClip.NativeBridge') -and `
    $nativeSource.Contains('posted-action') -and $nativeSource.Contains('FindFrameClipReceiver') -and `
    $nativeSource.Contains('EnumWindows')) 'Native actions are not routed to the resident WinForms message loop.'
Assert-True (-not $sourceText.Contains('TryMapSkinSubMenuRow') -and -not $sourceText.Contains('CacheSkinClickGeometry') -and `
    -not $sourceText.Contains('GetPotPlayerMenuScale') -and -not $sourceText.Contains('root-row') -and `
    -not $sourceText.Contains('AutomationElement') -and -not $sourceText.Contains('TreeScope.Descendants')) `
    'Legacy geometry or UI Automation skin interception is still present.'
Assert-True ($sourceText.Contains('pass unmatched player-left') -and $sourceText.Contains('session native-bridge')) `
    'External mouse handling is still attempting to consume skinned menu actions.'
Assert-True ($sourceText.Contains('[RememberFiles]') -and $sourceText.Contains('ExtractPathFromIniValue')) 'PotPlayer media-history parsing is incomplete.'
Assert-True ($sourceText.Contains('FindExistingWorkDirectory(root, derivedTitle, episodicSource)')) 'Derived and unclassified titles are not reused across captures.'
Assert-True ($sourceText.Contains('ToastForm previousToast = activeToast;') -and $sourceText.Contains('previousToast.Dispose();')) 'Toast replacement still risks clearing the active field during FormClosed.'
Assert-True ($sourceText.Contains('ControlStyles.OptimizedDoubleBuffer') -and $sourceText.Contains('protected override void OnPaint(PaintEventArgs') -and `
    $sourceText.Contains('while (engine.IsBusy || engine.HasActiveToast)') -and -not $sourceText.Contains('Label titleLabel = new Label();')) `
    'Toast rendering still depends on child controls or a short-lived one-shot message loop.'
Assert-True ($sourceText.Contains('ExportRec709ForHdr') -and $sourceText.Contains('tonemap=mobius') -and $sourceText.Contains('format=gbrpf32le')) `
    'Optional HDR to Rec.709 companion output is incomplete.'
Assert-True ($sourceText.Contains('ReadToEndAsync()') -and $sourceText.Contains('WaitForExit(timeout)') -and `
    $sourceText.Contains('Task.WaitAll(new Task[] { stdoutTask, stderrTask }') -and $sourceText.Contains('process.Kill()')) `
    'External process output can still deadlock or run without a bounded timeout.'
Assert-True ($sourceText.Contains('UiText.TryMapActionLabel') -and $sourceText.Contains('--apply-menu-language')) `
    'Bilingual settings and PotPlayer menu synchronization are incomplete.'
$installText = [IO.File]::ReadAllText((Join-Path $PSScriptRoot 'install.ps1'), [Text.Encoding]::UTF8)
Assert-True ($installText.Contains('ShellExecute($installExe') -and -not $installText.Contains('Start-Process -FilePath $installExe')) `
    'The helper must be launched through Windows Shell so silent installation can return.'
Assert-True ($installText.Contains("Contains('ExportRec709ForHdr')") -and $installText.Contains("ExportRec709ForHdr = 'False'") -and `
    $installText.Contains("Language = 'zh-CN'") -and $installText.Contains('PotPlayerMenuPath')) `
    'Installer defaults for the new capture and localization settings are incomplete.'
Assert-True ($uninstallText.Contains('Get-ValidatedInstallState') -and $uninstallText.Contains('Test-FrameClipMenuFile') -and `
    $uninstallText.Contains("Join-Path `$playerDirectory 'Menus\FrameClipMenu.xml'") -and `
    $uninstallText.Contains('No PotPlayer files were changed.')) `
    'Uninstaller state paths are not validated before restoration, elevation, or deletion.'

[xml]$menu = Get-Content -LiteralPath $menuPath -Raw -Encoding UTF8
$submenu = $menu.Menu.SubMenu | Select-Object -First 1
Assert-True ($submenu.Name -eq ([char]0x53C2 + [char]0x7167 + [char]0x5E27 + [char]0x4E0E + [char]0x7247 + [char]0x6BB5 + [char]0x622A + [char]0x53D6)) 'Unexpected menu title.'
$commandRows = @($submenu.MenuItem | Where-Object { $_.CmdID })
Assert-True ($commandRows.Count -eq 9) 'The localized skin entry must expose exactly nine command labels.'
Assert-True ($commandRows[0].Name.StartsWith([char]0x622A + [char]0x53D6 + [char]0x5F53 + [char]0x524D + [char]0x5E27)) 'Frame capture must remain the first submenu command.'
Assert-True (@($commandRows | Where-Object { $_.CmdID -eq 'ID_PLAY_PAUSE' }).Count -eq 0) `
    'FrameClip actions must never fall back to PotPlayer play/pause.'
Assert-True (@($commandRows | Where-Object { $_.CmdID -ne 'ID_APP_ABOUT' }).Count -eq 0) `
    'Skin-menu actions must use a leaf, non-playback fallback command.'
Assert-True (@($commandRows | Where-Object { $_.CmdID -eq 'CMD_POPUPMENU_ETC' }).Count -eq 0) `
    'FrameClip actions must not open PotPlayer third-level popup menus.'
Assert-True (-not $sourceText.Contains('FrameClipActionMenuForm') -and -not $sourceText.Contains('QueueSkinActionMenu')) `
    'The removed independent action popup has been reintroduced.'
Assert-True (-not $sourceText.Contains('QueueSemanticPointProbe') -and -not $sourceText.Contains('TryGetActionAtPoint')) `
    'The abandoned point-local UI Automation path has been reintroduced.'
Assert-True (-not $installerText.Contains('{userprofile}')) `
    'The installer uses the unsupported Inno Setup {userprofile} constant.'
Assert-True ($installerText.Contains('{%USERPROFILE}')) `
    'The installer no longer checks the current user Scoop FFmpeg path.'
Assert-True ($installerText.Contains('PotPlayerMenuPath=') -and $installerText.Contains('LoadStringsFromFile')) `
    'Upgrade installation no longer recovers the existing PotPlayer directory from FrameClip settings.'
Assert-True ($installerText.Contains('FrameClipBridge64.dll') -and $installerText.Contains('FrameClipBridge32.dll') -and `
    $installerText.Contains('FrameClipBridgeHost32.exe')) 'The installer does not contain both native bridge architectures.'

$releaseDirectory = Split-Path -Parent $ExecutablePath
$bridge64 = Join-Path $releaseDirectory 'FrameClipBridge64.dll'
$bridge32 = Join-Path $releaseDirectory 'FrameClipBridge32.dll'
$bridgeHost32 = Join-Path $releaseDirectory 'FrameClipBridgeHost32.exe'
Assert-True (Test-Path -LiteralPath $bridge64) '64-bit native bridge is missing.'
Assert-True (Test-Path -LiteralPath $bridge32) '32-bit native bridge is missing.'
Assert-True (Test-Path -LiteralPath $bridgeHost32) '32-bit bridge host is missing.'
Assert-True ((Get-PeMachine $bridge64) -eq 0x8664) '64-bit native bridge has the wrong PE architecture.'
Assert-True ((Get-PeMachine $bridge32) -eq 0x014c) '32-bit native bridge has the wrong PE architecture.'
Assert-True ((Get-PeMachine $bridgeHost32) -eq 0x014c) '32-bit bridge host has the wrong PE architecture.'
Assert-True ($nativeSource.Contains('SetWindowSubclass') -and $nativeSource.Contains('WM_COMMAND') -and `
    $nativeSource.Contains('FrameClipMouseProc')) 'Native command interception is incomplete.'
Assert-True ($nativeSource.Contains('FRAMECLIP_PLACEHOLDER_COMMAND') -and $nativeSource.Contains('HIWORD(w_param) != 0') -and `
    $nativeSource.Contains('LOWORD(w_param) != FRAMECLIP_PLACEHOLDER_COMMAND') -and $nativeSource.Contains('l_param != 0') -and `
    $nativeSource.Contains('ignored-command')) `
    'Native menu interception can still consume commands from unrelated PotPlayer submenus.'
Assert-True ($nativeSource.Contains('GET_MODULE_HANDLE_EX_FLAG_PIN') -and $installerText.Contains('if IsPotPlayerRunning() then')) `
    'The injected bridge can still unload beneath a live PotPlayer window subclass.'
Assert-True (-not ($nativeSource -match '[A-Za-z]:\\')) 'A fixed Windows path remains in the native bridge.'

Assert-True (Test-Path -LiteralPath $ExecutablePath) 'Compiled executable is missing.'
$versionInfo = (Get-Item -LiteralPath $ExecutablePath).VersionInfo
Assert-True ($versionInfo.ProductName -eq 'PotPlayer FrameClip') 'Executable product metadata is incorrect.'
if ($ExpectedVersion) {
    Assert-True ($versionInfo.FileVersion -eq $ExpectedVersion) "Executable version is $($versionInfo.FileVersion), expected $ExpectedVersion."
}

$compiler = @(
    "$env:WINDIR\Microsoft.NET\Framework64\v4.0.30319\csc.exe",
    "$env:WINDIR\Microsoft.NET\Framework\v4.0.30319\csc.exe"
) | Where-Object { Test-Path -LiteralPath $_ } | Select-Object -First 1
Assert-True ([bool]$compiler) '.NET Framework compiler is unavailable for tests.'
$testExe = Join-Path $env:TEMP ("FrameClipTests-{0}.exe" -f [Guid]::NewGuid().ToString('N'))
try {
    & $compiler /nologo /target:exe /platform:anycpu /optimize+ /codepage:65001 `
        /main:PotPlayerFrameClip.Tests /out:$testExe `
        /reference:System.dll /reference:System.Core.dll /reference:System.Drawing.dll `
        /reference:System.Windows.Forms.dll /reference:System.Management.dll /reference:System.Xml.dll /reference:Microsoft.CSharp.dll `
        $source (Join-Path $projectRoot 'tests\Tests.cs')
    if ($LASTEXITCODE -ne 0) { throw 'Test compilation failed.' }
    & $testExe
    if ($LASTEXITCODE -ne 0) { throw "Tests failed with exit code $LASTEXITCODE." }
}
finally {
    Remove-Item -LiteralPath $testExe -Force -ErrorAction SilentlyContinue
}

& (Join-Path $PSScriptRoot 'test-installation.ps1') -ExecutablePath $ExecutablePath
if ($LASTEXITCODE -ne 0) { throw 'Installation regression tests failed.' }

Write-Host 'Verification passed.'
