[CmdletBinding()]
param([Parameter(Mandatory=$true)][string]$SetupPath, [switch]$BundledFFmpeg, [switch]$MaintenanceUninstall)
$ErrorActionPreference = 'Stop'
$root = Join-Path $env:TEMP ('FrameClipSetupTests-' + [Guid]::NewGuid().ToString('N'))
$app = Join-Path $root 'application'
$player = Join-Path $root 'portable-player'
$evidence = Join-Path (Split-Path -Parent $PSScriptRoot) 'dist\validation\maintenance-engine'
New-Item -ItemType Directory -Path $player,$evidence -Force | Out-Null
function Assert([bool]$value,[string]$message) { if (-not $value) { throw $message } }
function Run([string]$exe,[string]$arguments,[bool]$success=$true) {
    $process = Start-Process -FilePath $exe -ArgumentList $arguments -WindowStyle Hidden -PassThru
    if (-not $process.WaitForExit(90000)) { throw "Installer timed out: $arguments" }
    if ($success -and $process.ExitCode -ne 0) { throw "Installer failed: $($process.ExitCode), $arguments" }
    if (-not $success -and $process.ExitCode -eq 0) { throw 'Failure injection unexpectedly succeeded.' }
}
$installed = $false
try {
    [IO.File]::WriteAllText((Join-Path $player 'PotPlayerMini64.exe'),'fake-player')
    [IO.File]::WriteAllLines((Join-Path $player 'PotPlayerMini64.ini'),@('[Settings]','LastMenuName=Custom.xml'))
    New-Item -ItemType Directory -Path (Join-Path $player 'Menus') | Out-Null
    [IO.File]::WriteAllText((Join-Path $player 'Menus\Custom.xml'),'<Menu><MenuItem CmdID="ID_PLAY_PAUSE"/></Menu>')
    $arguments = '/VERYSILENT /SUPPRESSMSGBOXES /NORESTART /DIR="' + $app + '" /POTPLAYERDIR="' + $player + '" /NOSTARTUP=1'
    $initialMode = if ($BundledFFmpeg) { ' /FFMPEGMODE=bundled' } else { ' /FFMPEGMODE=later' }
    Run $SetupPath ($arguments + $initialMode + ' /LOG="' + (Join-Path $evidence 'install.log') + '"')
    $installed = $true
    Assert (Test-Path -LiteralPath (Join-Path $app 'FrameClipMaintenance.exe')) 'Maintenance UI not installed.'
    foreach ($name in @('README.md','README.en.md','docs\gpu-processing.md','docs\readme-artwork.md','docs\releases\v0.4.0.md','docs\releases\v0.4.1.md',
            'assets\readme\banner-dark.png','assets\readme\banner-light.png','assets\readme\banner-zh-dark.png','assets\readme\banner-zh-light.png')) {
        Assert (Test-Path -LiteralPath (Join-Path $app $name) -PathType Leaf) ("Installed documentation missing: " + $name)
    }
    $settings = Join-Path $app 'FrameClip.ini'
    [IO.File]::AppendAllLines($settings,[string[]]@('Theme=dark','NotificationPosition=custom','NotificationX=0.37','NotificationMonitor=display-fixture'))
    [IO.File]::WriteAllText((Join-Path $app 'jobs.xml'),'<jobs version="1"><job state="Succeeded"/></jobs>')
    [IO.File]::WriteAllText((Join-Path $app 'media.mov'),'keep media')
    $oldConfig = [IO.File]::ReadAllText($settings)
    $oldExe = (Get-FileHash -LiteralPath (Join-Path $app 'PotPlayerFrameClip.exe')).Hash
    $oldIni = [IO.File]::ReadAllText((Join-Path $player 'PotPlayerMini64.ini'))
    # Invalid dependency must fail before replacement; test-only injection checks post-copy rollback.
    Run $SetupPath ($arguments + ' /FFMPEGPATH="' + (Join-Path $root 'missing-ffmpeg.exe') + '" /LOG="' + (Join-Path $evidence 'rollback.log') + '"') $false
    Assert ((Get-FileHash -LiteralPath (Join-Path $app 'PotPlayerFrameClip.exe')).Hash -eq $oldExe) 'Failed upgrade changed executable.'
    Assert ([IO.File]::ReadAllText($settings) -eq $oldConfig) 'Failed upgrade lost settings.'
    Assert ([IO.File]::ReadAllText((Join-Path $player 'PotPlayerMini64.ini')) -eq $oldIni) 'Failed upgrade lost player configuration.'
    Run $SetupPath ($arguments + ' /TESTFAILCONFIG=1 /LOG="' + (Join-Path $evidence 'post-copy-rollback.log') + '"') $false
    Assert ((Get-FileHash -LiteralPath (Join-Path $app 'PotPlayerFrameClip.exe')).Hash -eq $oldExe) 'Post-copy rollback lost executable.'
    Assert ([IO.File]::ReadAllText($settings) -eq $oldConfig) 'Post-copy rollback lost settings.'
    Run $SetupPath ($arguments + ' /LOG="' + (Join-Path $evidence 'upgrade.log') + '"')
    Assert ([IO.File]::ReadAllText($settings).Contains('NotificationX=0.37')) 'Upgrade lost custom notification coordinates.'
    Assert ([IO.File]::ReadAllText((Join-Path $app 'jobs.xml')).Contains('Succeeded')) 'Upgrade lost history.'
    if ($BundledFFmpeg) {
        $manifest = Get-Content -LiteralPath (Join-Path (Split-Path -Parent $PSScriptRoot) 'installer\ffmpeg-manifest.json') -Raw -Encoding UTF8 | ConvertFrom-Json
        $private = Join-Path $app ('tools\ffmpeg\' + $manifest.version)
        $pathBefore = [Environment]::GetEnvironmentVariable('Path','User')
        Run $SetupPath ($arguments + ' /FFMPEGMODE=bundled /LOG="' + (Join-Path $evidence 'bundle.log') + '"')
        Assert ([IO.File]::ReadAllText($settings).Contains('FFmpeg=' + (Join-Path $private 'ffmpeg.exe'))) 'Bundled path not configured.'
        Assert ([IO.File]::ReadAllText($settings).Contains('FFprobe=' + (Join-Path $private 'ffprobe.exe'))) 'Bundled FFprobe not configured.'
        foreach ($name in $manifest.files) { Assert (Test-Path -LiteralPath (Join-Path $private $name)) "Bundle file absent: $name" }
        & (Join-Path $private 'ffmpeg.exe') -version | Out-Null
        Assert ($LASTEXITCODE -eq 0) 'Installed FFmpeg failed to run.'
        Assert ([Environment]::GetEnvironmentVariable('Path','User') -eq $pathBefore) 'Bundled install modified user PATH.'
        $bundleConfig = [IO.File]::ReadAllText($settings)
        Run $SetupPath ($arguments + ' /FFMPEGMODE=later')
        Assert ([IO.File]::ReadAllText($settings) -eq $bundleConfig) 'Configure-later discarded existing FFmpeg/settings.'
        $external = Join-Path $root 'external-ffmpeg'
        New-Item -ItemType Directory -Path $external | Out-Null
        [IO.File]::WriteAllText((Join-Path $external 'ffmpeg.exe'),'external ffmpeg')
        Run $SetupPath ($arguments + ' /FFMPEGMODE=existing /FFMPEGPATH="' + (Join-Path $external 'ffmpeg.exe') + '"') $false
        Assert ([IO.File]::ReadAllText($settings) -eq $bundleConfig) 'Incomplete external pair altered settings.'
        [IO.File]::WriteAllText((Join-Path $external 'ffprobe.exe'),'external ffprobe')
        Run $SetupPath ($arguments + ' /FFMPEGMODE=existing /FFMPEGPATH="' + (Join-Path $external 'ffmpeg.exe') + '"')
        Assert ([IO.File]::ReadAllText($settings).Contains('FFmpeg=' + (Join-Path $external 'ffmpeg.exe'))) 'Existing FFmpeg not selected.'
    }
    $state = Join-Path $app 'install-state.json'
    $saved = [IO.File]::ReadAllText($state)
    $invalid = Get-Content -LiteralPath $state -Encoding UTF8 -Raw | ConvertFrom-Json
    $invalid.MenuPath = Join-Path $root 'unrelated.xml'
    $invalid | ConvertTo-Json | Set-Content -LiteralPath $state -Encoding UTF8
    $uninstallExe = Join-Path $app $(if ($MaintenanceUninstall) { 'FrameClipMaintenance.exe' } else { 'unins000.exe' })
    $uninstallPrefix = if ($MaintenanceUninstall) { '--uninstall ' } else { '' }
    Run $uninstallExe ($uninstallPrefix + '/VERYSILENT /SUPPRESSMSGBOXES /NORESTART /LOG="' + (Join-Path $evidence 'uninstall-refusal.log') + '"') $false
    Assert (Test-Path -LiteralPath (Join-Path $app 'PotPlayerFrameClip.exe')) 'Failed cleanup still deleted application.'
    [IO.File]::WriteAllText($state,$saved)
    Run $uninstallExe ($uninstallPrefix + '/VERYSILENT /SUPPRESSMSGBOXES /NORESTART /LOG="' + (Join-Path $evidence 'uninstall.log') + '"')
    $installed = $false
    Assert (-not (Test-Path -LiteralPath (Join-Path $app 'PotPlayerFrameClip.exe'))) 'Application not uninstalled.'
    $cleanupWait = [Diagnostics.Stopwatch]::StartNew()
    while ((Test-Path -LiteralPath (Join-Path $app 'FrameClipMaintenance.exe')) -and $cleanupWait.ElapsedMilliseconds -lt 10000) { Start-Sleep -Milliseconds 100 }
    Assert (-not (Test-Path -LiteralPath (Join-Path $app 'FrameClipMaintenance.exe'))) 'Maintenance executable not uninstalled.'
    Assert (Test-Path -LiteralPath $settings) 'Default uninstall deleted settings.'
    Assert (Test-Path -LiteralPath (Join-Path $app 'media.mov')) 'Uninstall deleted media.'
    if ($BundledFFmpeg) {
        Assert (-not (Test-Path -LiteralPath (Join-Path $private 'ffmpeg.exe'))) 'Private FFmpeg not uninstalled.'
        Assert ([IO.File]::ReadAllText((Join-Path $external 'ffmpeg.exe')) -eq 'external ffmpeg') 'Uninstall touched external FFmpeg.'
    }
    Assert ([IO.File]::ReadAllText((Join-Path $player 'PotPlayerMini64.ini')).Contains('LastMenuName=Custom.xml')) 'Uninstall did not restore original menu.'
    Write-Host "Actual Inno install, failed-upgrade recovery, upgrade and failed/successful uninstall passed. Logs: $evidence"
} finally {
    if ($installed -and (Test-Path -LiteralPath (Join-Path $app 'unins000.exe'))) {
        Write-Warning "Isolated installation retained for diagnosis: $root"
    } else { Remove-Item -LiteralPath $root -Recurse -Force -ErrorAction SilentlyContinue }
}
