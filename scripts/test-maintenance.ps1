[CmdletBinding()]
param()
$ErrorActionPreference = 'Stop'
$root = Join-Path $env:TEMP ('FrameClipMaintenanceTests-' + [Guid]::NewGuid().ToString('N'))
$install = Join-Path $root 'app'
$data = Join-Path $root 'data'
$player = Join-Path $root 'player'
$journal = Join-Path $root 'backup'
$script = Join-Path $PSScriptRoot 'transaction.ps1'
function Assert([bool]$value,[string]$message) { if (-not $value) { throw $message } }
try {
    New-Item -ItemType Directory -Path $install,$data,(Join-Path $player 'Menus') -Force | Out-Null
    [IO.File]::WriteAllText((Join-Path $install 'PotPlayerFrameClip.exe'),'old-exe')
    New-Item -ItemType Directory -Path (Join-Path $install 'assets\readme'),(Join-Path $install 'docs\releases') -Force | Out-Null
    [IO.File]::WriteAllText((Join-Path $install 'assets\readme\banner-zh-dark.png'),'old-artwork')
    [IO.File]::WriteAllLines((Join-Path $data 'FrameClip.ini'),@('Theme=dark','NotificationPosition=custom','NotificationX=0.42'))
    [IO.File]::WriteAllText((Join-Path $data 'jobs.xml'),'<jobs version="1"/>')
    [IO.File]::WriteAllText((Join-Path $data 'user-export.mov'),'export')
    [IO.File]::WriteAllText((Join-Path $install 'user-export.png'),'image')
    [IO.File]::WriteAllText((Join-Path $player 'PotPlayerMini64.exe'),'fixture')
    [IO.File]::WriteAllText((Join-Path $player 'PotPlayerMini64.ini'),'original-menu')
    $config = [IO.File]::ReadAllText((Join-Path $data 'FrameClip.ini'))
    . (Join-Path $PSScriptRoot 'ffmpeg.ps1')
    $bundle = Get-FrameClipFFmpegManifest
    $private = Join-Path $install ('tools\ffmpeg\' + $bundle.version)
    New-Item -ItemType Directory -Path $private -Force | Out-Null
    [IO.File]::WriteAllText((Join-Path $private 'ffmpeg.exe'),'previous-private-ffmpeg')
    & $script -Action begin -InstallDirectory $install -DataDirectory $data -Journal $journal -PlayerDirectory $player -BundledFFmpeg -TestMode
    [IO.File]::WriteAllText((Join-Path $private 'ffmpeg.exe'),'new-private-ffmpeg')
    [IO.File]::WriteAllText((Join-Path $private 'ffprobe.exe'),'new-private-ffprobe')
    [IO.File]::WriteAllText((Join-Path $install 'PotPlayerFrameClip.exe'),'new-exe')
    [IO.File]::WriteAllText((Join-Path $install 'assets\readme\banner-zh-dark.png'),'new-artwork')
    [IO.File]::WriteAllText((Join-Path $install 'docs\releases\v0.4.0.md'),'new-release-notes')
    [IO.File]::WriteAllText((Join-Path $install 'docs\readme-artwork.md'),'new-artwork-documentation')
    [IO.File]::WriteAllText((Join-Path $install 'FrameClipMaintenance.exe'),'new-file')
    [IO.File]::WriteAllText((Join-Path $data 'FrameClip.ini'),'changed')
    [IO.File]::WriteAllText((Join-Path $player 'PotPlayerMini64.ini'),'changed-menu')
    [IO.File]::WriteAllText((Join-Path $player 'Menus\FrameClipMenu.xml'),'new-menu')
    & $script -Action rollback -InstallDirectory $install -DataDirectory $data -Journal $journal -TestMode
    & $script -Action rollback -InstallDirectory $install -DataDirectory $data -Journal $journal -TestMode
    Assert ([IO.File]::ReadAllText((Join-Path $install 'PotPlayerFrameClip.exe')) -eq 'old-exe') 'Old executable not restored.'
    Assert ([IO.File]::ReadAllText((Join-Path $install 'assets\readme\banner-zh-dark.png')) -eq 'old-artwork') 'README artwork was not restored.'
    Assert (-not (Test-Path -LiteralPath (Join-Path $install 'docs\releases\v0.4.0.md'))) 'New release documentation was not rolled back.'
    Assert (-not (Test-Path -LiteralPath (Join-Path $install 'docs\readme-artwork.md'))) 'New artwork documentation was not rolled back.'
    Assert ([IO.File]::ReadAllText((Join-Path $private 'ffmpeg.exe')) -eq 'previous-private-ffmpeg') 'Previous private FFmpeg not restored.'
    Assert (-not (Test-Path -LiteralPath (Join-Path $private 'ffprobe.exe'))) 'New private FFprobe not rolled back.'
    Assert ([IO.File]::ReadAllText((Join-Path $data 'FrameClip.ini')) -eq $config) 'Settings not restored exactly.'
    Assert ([IO.File]::ReadAllText((Join-Path $player 'PotPlayerMini64.ini')) -eq 'original-menu') 'Player INI not restored.'
    Assert (-not (Test-Path -LiteralPath (Join-Path $player 'Menus\FrameClipMenu.xml'))) 'New menu not rolled back.'
    Assert (-not (Test-Path -LiteralPath (Join-Path $install 'FrameClipMaintenance.exe'))) 'New product file not removed.'
    Assert ([IO.File]::ReadAllText((Join-Path $data 'user-export.mov')) -eq 'export') 'Export changed during rollback.'
    & $script -Action begin -InstallDirectory $install -DataDirectory $data -Journal ($journal+'2') -PlayerDirectory $player -TestMode
    & $script -Action commit -InstallDirectory $install -DataDirectory $data -Journal ($journal+'2') -TestMode
    [IO.File]::WriteAllText((Join-Path $install 'PotPlayerFrameClip.exe'),'committed')
    & $script -Action rollback -InstallDirectory $install -DataDirectory $data -Journal ($journal+'2') -TestMode
    Assert ([IO.File]::ReadAllText((Join-Path $install 'PotPlayerFrameClip.exe')) -eq 'committed') 'Committed transaction rolled back.'
    & (Join-Path $PSScriptRoot 'uninstall.ps1') -InstallDirectory $install -DataDirectory $data -RemoveUserData -TestMode
    Assert (-not (Test-Path -LiteralPath (Join-Path $data 'FrameClip.ini'))) 'Remove-data did not remove settings.'
    Assert (-not (Test-Path -LiteralPath (Join-Path $data 'jobs.xml'))) 'Remove-data did not remove history.'
    Assert (Test-Path -LiteralPath (Join-Path $data 'user-export.mov')) 'Data-root export deleted.'
    Assert (Test-Path -LiteralPath (Join-Path $install 'user-export.png')) 'App-root export deleted.'
    . (Join-Path $PSScriptRoot 'maintenance.ps1')
    $refused = $false
    try { Assert-FrameClipSafeDirectory ([IO.Path]::GetPathRoot($root)) } catch { $refused = $true }
    Assert $refused 'Drive-root maintenance was accepted.'
    Write-Host 'Maintenance rollback, commit, repeatability and media-preserving uninstall passed.'
} finally { Remove-Item -LiteralPath $root -Recurse -Force -ErrorAction SilentlyContinue }
