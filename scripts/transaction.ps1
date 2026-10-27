[CmdletBinding()]
param(
    [Parameter(Mandatory=$true)][ValidateSet('stop','begin','rollback','commit')][string]$Action,
    [Parameter(Mandatory=$true)][string]$InstallDirectory,
    [Parameter(Mandatory=$true)][string]$DataDirectory,
    [string]$Journal,
    [string]$PlayerDirectory,
    [switch]$BundledFFmpeg,
    [string]$UninstallRegistryKey = 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Uninstall\{7C39C686-1D2E-4D46-B303-C7F3173B9D42}_is1',
    [switch]$TestMode
)
$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'maintenance.ps1')
$install = [IO.Path]::GetFullPath($InstallDirectory).TrimEnd('\')
$data = [IO.Path]::GetFullPath($DataDirectory).TrimEnd('\')
Assert-FrameClipSafeDirectory $install
Assert-FrameClipSafeDirectory $data
if ($Action -eq 'stop') { if (-not $TestMode) { Stop-FrameClipForMaintenance $install $data }; return }
if (-not $Journal) { throw 'A transaction journal is required.' }
$journalRoot = [IO.Path]::GetFullPath($Journal).TrimEnd('\')
Assert-FrameClipSafeDirectory $journalRoot
foreach ($root in @($install,$data)) {
    if ($journalRoot.Equals($root,[StringComparison]::OrdinalIgnoreCase) -or $journalRoot.StartsWith($root + '\',[StringComparison]::OrdinalIgnoreCase)) { throw 'Backup must be outside installation and data directories.' }
}
$manifest = Join-Path $journalRoot 'transaction.json'
$productFiles = @('PotPlayerFrameClip.exe','PotPlayerFrameClip.exe.config','PotPlayerFrameClip.pdb',
    'FrameClipMaintenance.exe','FrameClipBridge64.dll','FrameClipBridge32.dll','FrameClipBridgeHost32.exe',
    'FrameClipMenu.xml','install.ps1','uninstall.ps1','maintenance.ps1','transaction.ps1','ffmpeg.ps1','ffmpeg-manifest.json',
    'README.md','README.en.md','docs\guide.zh.md','docs\guide.en.md','docs\gpu-processing.md','docs\readme-artwork.md','docs\releases\v0.4.0.md','docs\releases\v0.4.1.md','docs\releases\v0.4.2.md',
    'assets\readme\banner-dark.png','assets\readme\banner-light.png','assets\readme\banner-zh-dark.png','assets\readme\banner-zh-light.png',
    'LICENSE','THIRD_PARTY_NOTICES.md','LUCIDE-LICENSE','CHANGELOG.md','unins000.exe','unins000.dat','unins000.msg')
$dataFiles = @('FrameClip.ini','install-state.json','menu-selection.pending','range.state','library-aliases.tsv','jobs.xml')
$uninstallKey = $UninstallRegistryKey
if ($uninstallKey -notin @('HKCU:\Software\Microsoft\Windows\CurrentVersion\Uninstall\{7C39C686-1D2E-4D46-B303-C7F3173B9D42}_is1','HKCU:\Software\Microsoft\Windows\CurrentVersion\Uninstall\{99CF512C-173D-4126-A610-41E04F5A51DB}_is1')) { throw 'Unexpected product registry key.' }
$players = @('PotPlayerMini64','PotPlayer64','PotPlayerMini','PotPlayer')
if ($Action -eq 'begin') {
    if (Test-Path -LiteralPath $journalRoot) { throw 'Transaction directory already exists.' }
    New-Item -ItemType Directory -Path $journalRoot | Out-Null
    $paths = [Collections.Generic.List[string]]::new()
    foreach ($name in $productFiles) { $paths.Add((Join-Path $install $name)) }
    if ($BundledFFmpeg) {
        . (Join-Path $PSScriptRoot 'ffmpeg.ps1')
        $bundle = Get-FrameClipFFmpegManifest
        foreach ($name in $bundle.files) { $paths.Add((Join-Path $install ('tools\ffmpeg\' + $bundle.version + '\' + $name))) }
    }
    foreach ($name in $dataFiles) { $paths.Add((Join-Path $data $name)) }
    $group = if ($uninstallKey.Contains('99CF512C')) { 'FrameClip Isolated Installer Test' } else { 'PotPlayer FrameClip' }
    $groupDirectory = Join-Path ([Environment]::GetFolderPath('Programs')) $group
    $guideName = [string][char]0x4f7f + [char]0x7528 + [char]0x8bf4 + [char]0x660e + '.lnk'
    $uninstallName = [string][char]0x5378 + [char]0x8f7d + ' PotPlayer FrameClip.lnk'
    foreach ($name in @($guideName,$uninstallName)) { $paths.Add((Join-Path $groupDirectory $name)) }
    $candidates = [Collections.Generic.List[string]]::new()
    if ($PlayerDirectory) { $candidates.Add($PlayerDirectory) }
    $oldState = Join-Path $data 'install-state.json'
    if (Test-Path -LiteralPath $oldState) {
        $state = Get-Content -LiteralPath $oldState -Raw -Encoding UTF8 | ConvertFrom-Json
        if ($state.PlayerDirectory) { $candidates.Add([string]$state.PlayerDirectory) }
    }
    if (-not $TestMode) {
        foreach ($name in $players) {
            $value = Get-ItemProperty -LiteralPath ('HKCU:\Software\DAUM\' + $name) -ErrorAction SilentlyContinue
            if ($value.ProgramPath) { $candidates.Add([string]$value.ProgramPath) }
        }
        foreach ($root in @($env:ProgramFiles,[Environment]::GetEnvironmentVariable('ProgramFiles(x86)'))) { if ($root) { $candidates.Add((Join-Path $root 'DAUM\PotPlayer')) } }
        $candidates.Add((Join-Path $env:LOCALAPPDATA 'Programs\PotPlayer'))
    }
    $verified = @($candidates | ForEach-Object { [IO.Path]::GetFullPath($_).TrimEnd('\') } | Select-Object -Unique | Where-Object {
        $candidate = $_; @($players | Where-Object { Test-Path -LiteralPath (Join-Path $candidate ($_ + '.exe')) -PathType Leaf }).Count -gt 0
    })
    foreach ($root in $verified) {
        Assert-FrameClipSafeDirectory $root
        foreach ($name in $players) { $paths.Add((Join-Path $root ($name + '.ini'))) }
        $paths.Add((Join-Path $root 'Menus\FrameClipMenu.xml'))
        $paths.Add((Join-Path $root 'Menus\ResolveCaptureMenu.xml'))
    }
    $entries = @(); $index = 0
    foreach ($path in @($paths | Select-Object -Unique)) {
        Assert-FrameClipSafeDirectory ([IO.Path]::GetDirectoryName($path))
        $exists = Test-Path -LiteralPath $path -PathType Leaf
        if ((Test-Path -LiteralPath $path) -and -not $exists) { throw "Expected a file: $path" }
        if ($exists -and ((Get-Item -LiteralPath $path -Force).Attributes -band [IO.FileAttributes]::ReparsePoint)) { throw "Linked file is not supported: $path" }
        $backup = '{0:D3}.bin' -f $index++
        if ($exists) { Copy-Item -LiteralPath $path -Destination (Join-Path $journalRoot $backup) }
        $entries += [ordered]@{ Path=$path; Existed=$exists; Backup=$backup }
    }
    $registry = @()
    $requests = @()
    if (-not $TestMode) {
        $requests += @{Path='HKCU:\Software\Microsoft\Windows\CurrentVersion\Run'; Names=@('PotPlayerFrameClip','PotPlayerResolveCapture')}
        foreach ($name in $players) { $requests += @{Path=('HKCU:\Software\DAUM\' + $name + '\Settings'); Names=@('LastMenuName')} }
    }
    if (-not $TestMode -or $uninstallKey.Contains('99CF512C')) { $requests += @{Path=$uninstallKey; Names=$null} }
        foreach ($request in $requests) {
            $key = Get-Item -LiteralPath $request.Path -ErrorAction SilentlyContinue
            $names = if ($null -ne $request.Names) { $request.Names } elseif ($key) { $key.GetValueNames() } else { @() }
            $values = @()
            foreach ($name in $names) {
                $exists = $key -and $key.GetValueNames() -contains $name
                $values += @{Name=$name; Existed=[bool]$exists; Value=$(if($exists){$key.GetValue($name,$null,[Microsoft.Win32.RegistryValueOptions]::DoNotExpandEnvironmentNames)}else{$null}); Kind=$(if($exists){$key.GetValueKind($name).ToString()}else{'String'})}
            }
            $registry += @{Path=$request.Path; Existed=[bool]$key; Whole=($null -eq $request.Names); Values=$values}
        }
    [ordered]@{Version=1; Install=$install; Data=$data; Status='pending'; Files=$entries; Registry=$registry} |
        ConvertTo-Json -Depth 8 | Set-Content -LiteralPath $manifest -Encoding UTF8
    Write-Host "Recovery backup: $journalRoot"
    return
}
$receipt = Get-Content -LiteralPath $manifest -Raw -Encoding UTF8 | ConvertFrom-Json
if ($receipt.Version -ne 1 -or $receipt.Install -ne $install -or $receipt.Data -ne $data) { throw 'Transaction identity mismatch.' }
if ($Action -eq 'commit') {
    $receipt.Status = 'committed'
} elseif ($receipt.Status -eq 'pending') {
    if (-not $TestMode) { Stop-FrameClipForMaintenance $install $data }
    foreach ($entry in $receipt.Files) {
        $path = [string]$entry.Path
        Assert-FrameClipSafeDirectory ([IO.Path]::GetDirectoryName($path))
        if ((Test-Path -LiteralPath $path) -and ((Get-Item -LiteralPath $path -Force).Attributes -band [IO.FileAttributes]::ReparsePoint)) { throw "Linked restore target: $path" }
        if ($entry.Existed) {
            New-Item -ItemType Directory -Path ([IO.Path]::GetDirectoryName($path)) -Force | Out-Null
            Copy-Item -LiteralPath (Join-Path $journalRoot $entry.Backup) -Destination $path -Force
        } elseif (Test-Path -LiteralPath $path -PathType Leaf) { Remove-Item -LiteralPath $path -Force }
    }
    foreach ($entry in $receipt.Registry) {
        if ($entry.Whole -and -not $entry.Existed) { Remove-Item -LiteralPath $entry.Path -Recurse -Force -ErrorAction SilentlyContinue; continue }
        if ($entry.Whole -and $entry.Existed -and (Test-Path -LiteralPath $entry.Path)) {
            $restoredNames = @($entry.Values | ForEach-Object { $_.Name })
            foreach ($name in (Get-Item -LiteralPath $entry.Path).GetValueNames()) {
                if ($name -notin $restoredNames) { Remove-ItemProperty -LiteralPath $entry.Path -Name $name }
            }
        }
        foreach ($value in $entry.Values) {
            if ($value.Existed) {
                if (-not (Test-Path -LiteralPath $entry.Path)) { New-Item -Path $entry.Path -Force | Out-Null }
                New-ItemProperty -LiteralPath $entry.Path -Name $value.Name -Value $value.Value -PropertyType $value.Kind -Force | Out-Null
            } else { Remove-ItemProperty -LiteralPath $entry.Path -Name $value.Name -ErrorAction SilentlyContinue }
        }
    }
    $receipt.Status = 'rolled-back'
}
$receipt | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath $manifest -Encoding UTF8
Write-Host "Transaction $($receipt.Status): $journalRoot"
