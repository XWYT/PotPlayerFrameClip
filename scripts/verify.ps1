[CmdletBinding()]
param(
    [string]$ExecutablePath,
    [string]$ExpectedVersion
)

$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path -Parent $PSScriptRoot
$source = Join-Path $projectRoot 'src\PotPlayerFrameClip.cs'
$menuPath = Join-Path $projectRoot 'menu\FrameClipMenu.xml'
if (-not $ExecutablePath) { $ExecutablePath = Join-Path $projectRoot 'dist\PotPlayerFrameClip.exe' }

function Assert-True([bool]$Condition, [string]$Message) {
    if (-not $Condition) { throw $Message }
}

$sourceText = [IO.File]::ReadAllText($source, [Text.Encoding]::UTF8)
$forbiddenBrand = 'Re' + 'solve'
Assert-True (-not $sourceText.Contains($forbiddenBrand)) 'A legacy product-specific brand remains in source.'
Assert-True (-not ($sourceText -match '[A-Za-z]:\\')) 'A fixed Windows drive path remains in source.'
Assert-True ($sourceText.Contains('PotPlayerMini64') -and $sourceText.Contains('PotPlayerMini')) 'Both PotPlayer bitness variants must be detected.'
Assert-True ($sourceText.Contains('Environment.SpecialFolder.LocalApplicationData')) 'Writable per-user state path is missing.'
Assert-True ($sourceText.Contains('TryOpenSkinActionMenuFromRoot(data.Point)') -and $sourceText.Contains('TryOpenSkinActionMenuFromSubMenu(data.Point)')) `
    'The independent skin-menu entry paths are not connected to the mouse callback.'
Assert-True (-not $sourceText.Contains('TryMapSkinSubMenuRow') -and -not $sourceText.Contains('TryHandleCachedSkinMenuClick')) `
    'Legacy per-row skin coordinate dispatch is still present.'
Assert-True ($sourceText.Contains('[RememberFiles]') -and $sourceText.Contains('ExtractPathFromIniValue')) 'PotPlayer media-history parsing is incomplete.'
Assert-True ($sourceText.Contains('FindExistingWorkDirectory(root, derivedTitle, episodicSource)')) 'Derived and unclassified titles are not reused across captures.'
Assert-True ($sourceText.Contains('ToastForm previousToast = activeToast;') -and $sourceText.Contains('previousToast.Dispose();')) 'Toast replacement still risks clearing the active field during FormClosed.'
Assert-True ($sourceText.Contains('ExportRec709ForHdr') -and $sourceText.Contains('tonemap=mobius') -and $sourceText.Contains('format=gbrpf32le')) `
    'Optional HDR to Rec.709 companion output is incomplete.'
Assert-True ($sourceText.Contains('UiText.TryMapActionLabel') -and $sourceText.Contains('--apply-menu-language')) `
    'Bilingual settings and PotPlayer menu synchronization are incomplete.'
$installText = [IO.File]::ReadAllText((Join-Path $PSScriptRoot 'install.ps1'), [Text.Encoding]::UTF8)
Assert-True ($installText.Contains('ShellExecute($installExe') -and -not $installText.Contains('Start-Process -FilePath $installExe')) `
    'The helper must be launched through Windows Shell so silent installation can return.'
Assert-True ($installText.Contains("Contains('ExportRec709ForHdr')") -and $installText.Contains("ExportRec709ForHdr = 'False'") -and `
    $installText.Contains("Language = 'zh-CN'") -and $installText.Contains('PotPlayerMenuPath')) `
    'Installer defaults for the new capture and localization settings are incomplete.'

[xml]$menu = Get-Content -LiteralPath $menuPath -Raw -Encoding UTF8
$submenu = $menu.Menu.SubMenu | Select-Object -First 1
Assert-True ($submenu.Name -eq ([char]0x53C2 + [char]0x7167 + [char]0x5E27 + [char]0x4E0E + [char]0x7247 + [char]0x6BB5 + [char]0x622A + [char]0x53D6)) 'Unexpected menu title.'
$commandRows = @($submenu.MenuItem | Where-Object { $_.CmdID })
Assert-True ($commandRows.Count -eq 9) 'The localized skin entry must expose exactly nine command labels.'
Assert-True ($commandRows[0].Name.StartsWith([char]0x622A + [char]0x53D6 + [char]0x5F53 + [char]0x524D + [char]0x5E27)) 'Frame capture must remain the first submenu command.'
Assert-True (@($commandRows | Where-Object { $_.CmdID -eq 'ID_PLAY_PAUSE' }).Count -eq 0) `
    'FrameClip actions must never fall back to PotPlayer play/pause.'
Assert-True (@($commandRows | Where-Object { $_.CmdID -ne 'CMD_POPUPMENU_ETC' }).Count -eq 0) `
    'Skin-menu placeholders must use the fail-closed non-playback command.'
Assert-True ($sourceText.Contains('FrameClipActionMenuForm') -and $sourceText.Contains('QueueSkinActionMenu')) `
    'The independent skin action menu is not connected.'
Assert-True ($sourceText.Contains('Interlocked.CompareExchange(ref skinActionMenuQueued')) `
    'Concurrent skin events can still open duplicate action menus.'

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
