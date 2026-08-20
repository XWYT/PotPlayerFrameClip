[CmdletBinding()]
param(
    [string]$PotPlayerDirectory,
    [string]$FFmpegPath,
    [switch]$NoStartup,
    [string]$InstallDirectory,
    [string]$DataDirectory,
    [switch]$TestMode,
    [switch]$Elevated
)

$ErrorActionPreference = 'Stop'
$scriptPath = $MyInvocation.MyCommand.Path
$productName = 'PotPlayer FrameClip'
$appName = 'PotPlayerFrameClip'
$releaseRoot = Split-Path -Parent $MyInvocation.MyCommand.Path
$sourceExe = Join-Path $releaseRoot 'PotPlayerFrameClip.exe'
$sourceMenu = Join-Path $releaseRoot 'FrameClipMenu.xml'
if (-not $InstallDirectory) { $InstallDirectory = Join-Path $env:LOCALAPPDATA $appName }
$installDirectory = [IO.Path]::GetFullPath($InstallDirectory)
$installExe = Join-Path $installDirectory 'PotPlayerFrameClip.exe'
if (-not $DataDirectory) { $DataDirectory = Join-Path $env:LOCALAPPDATA $appName }
$dataDirectory = [IO.Path]::GetFullPath($DataDirectory)
$statePath = Join-Path $dataDirectory 'install-state.json'
$pendingMenuPath = Join-Path $dataDirectory 'menu-selection.pending'
$menuName = 'FrameClipMenu.xml'
$legacyRunName = 'PotPlayer' + ([char]0x52) + 'esolveCapture'
$legacyMenuName = ([char]0x52) + 'esolveCaptureMenu.xml'
$frameClipMenuTitle = [char]0x53C2 + [char]0x7167 + [char]0x5E27 + [char]0x4E0E + [char]0x7247 + [char]0x6BB5 + [char]0x622A + [char]0x53D6
$previousFrameClipMenuTitle = [char]0x5E27 + [char]0x4E0E + [char]0x7247 + [char]0x6BB5

function Get-PotPlayerDirectory {
    param([string]$RequestedDirectory)

    $candidates = [Collections.Generic.List[string]]::new()
    if ($RequestedDirectory) { $candidates.Add($RequestedDirectory) }

    foreach ($name in 'PotPlayerMini64','PotPlayer64','PotPlayerMini','PotPlayer') {
        Get-Process -Name $name -ErrorAction SilentlyContinue | ForEach-Object {
            try { $candidates.Add((Split-Path -Parent $_.MainModule.FileName)) } catch { }
        }
    }

    foreach ($keyPath in @(
        'HKCU:\Software\DAUM\PotPlayer64', 'HKCU:\Software\DAUM\PotPlayer',
        'HKLM:\Software\DAUM\PotPlayer64', 'HKLM:\Software\DAUM\PotPlayer',
        'HKLM:\Software\WOW6432Node\DAUM\PotPlayer64', 'HKLM:\Software\WOW6432Node\DAUM\PotPlayer'
    )) {
        try {
            $programPath = (Get-ItemProperty -LiteralPath $keyPath -ErrorAction Stop).ProgramPath
            if ($programPath) {
                if (Test-Path -LiteralPath $programPath -PathType Leaf) {
                    $candidates.Add((Split-Path -Parent $programPath))
                } else {
                    $candidates.Add([string]$programPath)
                }
            }
        } catch { }
    }

    foreach ($uninstallRoot in @(
        'HKCU:\Software\Microsoft\Windows\CurrentVersion\Uninstall\*',
        'HKLM:\Software\Microsoft\Windows\CurrentVersion\Uninstall\*',
        'HKLM:\Software\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall\*'
    )) {
        Get-ItemProperty $uninstallRoot -ErrorAction SilentlyContinue |
            Where-Object { $_.DisplayName -like 'PotPlayer*' } |
            ForEach-Object {
                if ($_.InstallLocation) { $candidates.Add($_.InstallLocation) }
                if ($_.DisplayIcon) {
                    $iconPath = ([string]$_.DisplayIcon).Trim('"').Split(',')[0]
                    if (Test-Path -LiteralPath $iconPath) { $candidates.Add((Split-Path -Parent $iconPath)) }
                }
            }
    }

    foreach ($root in @($env:ProgramFiles, ${env:ProgramFiles(x86)}, $env:LOCALAPPDATA)) {
        if (-not $root) { continue }
        $candidates.Add((Join-Path $root 'DAUM\PotPlayer'))
        $candidates.Add((Join-Path $root 'PotPlayer'))
    }

    foreach ($candidate in $candidates | Select-Object -Unique) {
        if (-not $candidate) { continue }
        foreach ($exeName in 'PotPlayerMini64.exe','PotPlayerMini.exe','PotPlayer64.exe','PotPlayer.exe') {
            if (Test-Path -LiteralPath (Join-Path $candidate $exeName)) { return [IO.Path]::GetFullPath($candidate) }
        }
    }
    throw 'PotPlayer was not found. Run install.ps1 with -PotPlayerDirectory "C:\path\to\PotPlayer".'
}

function Test-DirectoryWritable {
    param([string]$Directory)
    try {
        New-Item -ItemType Directory -Force -Path $Directory | Out-Null
        $probe = Join-Path $Directory ('.frameclip-write-' + [Guid]::NewGuid().ToString('N') + '.tmp')
        [IO.File]::WriteAllText($probe, 'test')
        Remove-Item -LiteralPath $probe -Force
        return $true
    } catch {
        return $false
    }
}

function Quote-PowerShellLiteral {
    param([string]$Value)
    return "'" + $Value.Replace("'", "''") + "'"
}

function Test-PotPlayerRunning {
    foreach ($name in 'PotPlayerMini64','PotPlayer64','PotPlayerMini','PotPlayer') {
        if (Get-Process -Name $name -ErrorAction SilentlyContinue) { return $true }
    }
    return $false
}

function Copy-IfDifferent {
    param([string]$Source, [string]$Destination)
    if ([IO.Path]::GetFullPath($Source) -eq [IO.Path]::GetFullPath($Destination)) { return }
    Copy-Item -LiteralPath $Source -Destination $Destination -Force
}

function Write-PendingMenuRepair {
    param([string]$Mode, [string]$Path, [string]$Value)
    $encodedPath = [Convert]::ToBase64String([Text.Encoding]::UTF8.GetBytes($Path))
    [IO.File]::WriteAllLines($pendingMenuPath, @(
        "Mode=$Mode",
        "Path=$encodedPath",
        "Value=$Value"
    ), [Text.UTF8Encoding]::new($false))
}

function Start-FrameClipHelper {
    # 通过 Windows Shell 代理启动，使常驻程序进入当前用户桌面会话，并避免继承
    # 安装脚本的隐藏窗口和控制台状态。
    $shell = $null
    try {
        $shell = New-Object -ComObject Shell.Application
        $shell.ShellExecute($installExe, '', $installDirectory, 'open', 0)
    } finally {
        if ($shell) { [void][Runtime.InteropServices.Marshal]::ReleaseComObject($shell) }
    }
}

function Get-TextFileEncoding {
    param([string]$Path)

    $bytes = [IO.File]::ReadAllBytes($Path)
    if ($bytes.Length -ge 2 -and $bytes[0] -eq 0xFF -and $bytes[1] -eq 0xFE) { return [Text.Encoding]::Unicode }
    if ($bytes.Length -ge 2 -and $bytes[0] -eq 0xFE -and $bytes[1] -eq 0xFF) { return [Text.Encoding]::BigEndianUnicode }
    if ($bytes.Length -ge 3 -and $bytes[0] -eq 0xEF -and $bytes[1] -eq 0xBB -and $bytes[2] -eq 0xBF) {
        return [Text.UTF8Encoding]::new($true)
    }

    # PotPlayer 便携版通常使用 UTF-16 LE；部分第三方整合版使用 UTF-8 或系统 ANSI。
    # 先识别无 BOM 的 UTF-16，再验证 UTF-8，最后才回退到当前系统代码页。
    $sampleLength = [Math]::Min($bytes.Length, 512)
    $oddNulls = 0
    for ($index = 1; $index -lt $sampleLength; $index += 2) {
        if ($bytes[$index] -eq 0) { $oddNulls++ }
    }
    if ($sampleLength -ge 4 -and $oddNulls -ge [Math]::Max(2, [int]($sampleLength / 8))) {
        return [Text.UnicodeEncoding]::new($false, $false)
    }

    try {
        $utf8 = [Text.UTF8Encoding]::new($false, $true)
        [void]$utf8.GetString($bytes)
        return [Text.UTF8Encoding]::new($false)
    } catch {
        return [Text.Encoding]::Default
    }
}

function Read-TextFileLines {
    param([string]$Path)
    return [IO.File]::ReadAllLines($Path, (Get-TextFileEncoding $Path))
}

function Restart-Elevated {
    param([string]$PlayerDirectory)
    $parts = @(
        '& ' + (Quote-PowerShellLiteral $scriptPath),
        '-PotPlayerDirectory ' + (Quote-PowerShellLiteral $PlayerDirectory),
        '-InstallDirectory ' + (Quote-PowerShellLiteral $installDirectory),
        '-DataDirectory ' + (Quote-PowerShellLiteral $dataDirectory),
        '-Elevated'
    )
    if ($FFmpegPath) { $parts += '-FFmpegPath ' + (Quote-PowerShellLiteral $FFmpegPath) }
    if ($NoStartup) { $parts += '-NoStartup' }
    $command = $parts -join ' '
    $encoded = [Convert]::ToBase64String([Text.Encoding]::Unicode.GetBytes($command))
    $process = Start-Process -FilePath 'powershell.exe' -Verb RunAs -Wait -PassThru `
        -ArgumentList @('-NoProfile', '-ExecutionPolicy', 'Bypass', '-EncodedCommand', $encoded)
    exit $process.ExitCode
}

function Get-IniValue {
    param([string]$Path, [string]$Section, [string]$Key)
    if (-not (Test-Path -LiteralPath $Path)) { return $null }
    $inSection = $false
    foreach ($line in Read-TextFileLines $Path) {
        if ($line -match '^\[(.+)\]$') { $inSection = $Matches[1] -eq $Section; continue }
        if ($inSection -and $line.StartsWith($Key + '=', [StringComparison]::OrdinalIgnoreCase)) {
            return $line.Substring($Key.Length + 1)
        }
    }
    return $null
}

function Test-IniKey {
    param([string]$Path, [string]$Section, [string]$Key)
    if (-not (Test-Path -LiteralPath $Path)) { return $false }
    $inSection = $false
    foreach ($line in Read-TextFileLines $Path) {
        if ($line -match '^\[(.+)\]$') { $inSection = $Matches[1] -eq $Section; continue }
        if ($inSection -and $line.StartsWith($Key + '=', [StringComparison]::OrdinalIgnoreCase)) { return $true }
    }
    return $false
}

function Set-IniValue {
    param([string]$Path, [string]$Section, [string]$Key, [string]$Value)
    $lines = [Collections.Generic.List[string]]::new()
    $encoding = if (Test-Path -LiteralPath $Path) { Get-TextFileEncoding $Path } else { [Text.Encoding]::Unicode }
    if (Test-Path -LiteralPath $Path) {
        foreach ($line in [IO.File]::ReadAllLines($Path, $encoding)) { $lines.Add($line) }
    }
    $sectionIndex = -1
    $nextSectionIndex = $lines.Count
    for ($index = 0; $index -lt $lines.Count; $index++) {
        if ($lines[$index] -eq "[$Section]") { $sectionIndex = $index; continue }
        if ($sectionIndex -ge 0 -and $index -gt $sectionIndex -and $lines[$index] -match '^\[.+\]$') {
            $nextSectionIndex = $index
            break
        }
    }
    if ($sectionIndex -lt 0) {
        if ($lines.Count -gt 0) { $lines.Add('') }
        $lines.Add("[$Section]")
        $lines.Add("$Key=$Value")
    } else {
        $replaced = $false
        for ($index = $sectionIndex + 1; $index -lt $nextSectionIndex; $index++) {
            if ($lines[$index].StartsWith($Key + '=', [StringComparison]::OrdinalIgnoreCase)) {
                $lines[$index] = "$Key=$Value"
                $replaced = $true
                break
            }
        }
        if (-not $replaced) { $lines.Insert($nextSectionIndex, "$Key=$Value") }
    }
    [IO.File]::WriteAllLines($Path, $lines, $encoding)
}

function Get-MenuConfiguration {
    param([string]$PlayerDirectory)
    foreach ($iniName in 'PotPlayerMini64.ini','PotPlayerMini.ini','PotPlayer64.ini','PotPlayer.ini') {
        $iniPath = Join-Path $PlayerDirectory $iniName
        if (Test-Path -LiteralPath $iniPath) {
            return [pscustomobject]@{
                Mode = 'Ini'
                Path = $iniPath
                Previous = (Get-IniValue $iniPath 'Settings' 'LastMenuName')
                PreviousExists = (Test-IniKey $iniPath 'Settings' 'LastMenuName')
            }
        }
    }
    $registryCandidates = @(
        'HKCU:\Software\DAUM\PotPlayerMini64\Settings',
        'HKCU:\Software\DAUM\PotPlayer64\Settings',
        'HKCU:\Software\DAUM\PotPlayerMini\Settings',
        'HKCU:\Software\DAUM\PotPlayer\Settings'
    )
    $existing = $registryCandidates | Where-Object { Test-Path -LiteralPath $_ } | Select-Object -First 1
    if (-not $existing) {
        $is64 = Test-Path -LiteralPath (Join-Path $PlayerDirectory 'PotPlayerMini64.exe')
        $existing = if ($is64) { $registryCandidates[0] } else { $registryCandidates[2] }
        New-Item -Path $existing -Force | Out-Null
    }
    $registryItem = Get-ItemProperty -LiteralPath $existing -ErrorAction SilentlyContinue
    $previous = $registryItem.LastMenuName
    $previousExists = $registryItem -and ($registryItem.PSObject.Properties.Name -contains 'LastMenuName')
    return [pscustomobject]@{ Mode='Registry'; Path=$existing; Previous=$previous; PreviousExists=$previousExists }
}

function Install-Menu {
    param([string]$PlayerDirectory, [string]$PreviousMenuName)
    $menusDirectory = Join-Path $PlayerDirectory 'Menus'
    New-Item -ItemType Directory -Force -Path $menusDirectory | Out-Null
    $destination = Join-Path $menusDirectory $menuName
    $baseMenu = if ($PreviousMenuName) { Join-Path $menusDirectory $PreviousMenuName } else { $null }

    if ($baseMenu -and (Test-Path -LiteralPath $baseMenu) -and $PreviousMenuName -ne $legacyMenuName) {
        $targetText = [IO.File]::ReadAllText($baseMenu)
        if ($targetText.StartsWith('<?XML', [StringComparison]::Ordinal)) {
            $targetText = '<?xml' + $targetText.Substring(5)
        }
        [xml]$target = $targetText
        [xml]$template = [IO.File]::ReadAllText($sourceMenu)
        foreach ($node in @($target.Menu.SubMenu | Where-Object { $_.Name -eq $frameClipMenuTitle -or $_.Name -eq $previousFrameClipMenuTitle })) {
            [void]$target.Menu.RemoveChild($node)
        }
        $imported = $target.ImportNode($template.Menu.SubMenu, $true)
        [void]$target.Menu.InsertBefore($imported, $target.Menu.FirstChild)
        $settings = [Xml.XmlWriterSettings]::new()
        $settings.Encoding = [Text.UTF8Encoding]::new($false)
        $settings.Indent = $true
        $writer = [Xml.XmlWriter]::Create($destination, $settings)
        try { $target.Save($writer) } finally { $writer.Dispose() }
    } else {
        Copy-Item -LiteralPath $sourceMenu -Destination $destination -Force
    }
    return $destination
}

if (-not (Test-Path -LiteralPath $sourceExe) -or -not (Test-Path -LiteralPath $sourceMenu)) {
    throw 'Run install.ps1 from the extracted release folder.'
}

$playerDirectory = Get-PotPlayerDirectory $PotPlayerDirectory
$menusDirectory = Join-Path $playerDirectory 'Menus'
$menuConfiguration = Get-MenuConfiguration $playerDirectory
$menuConfigDirectory = if ($menuConfiguration.Mode -eq 'Ini') { Split-Path -Parent $menuConfiguration.Path } else { $null }
$needsElevation = -not (Test-DirectoryWritable $menusDirectory) -or
    ($menuConfigDirectory -and -not (Test-DirectoryWritable $menuConfigDirectory))
if (-not $TestMode -and $needsElevation) {
    if ($Elevated) { throw "The PotPlayer menu directory is not writable: $menusDirectory" }
    Restart-Elevated $playerDirectory
}
$selectedMenuName = $menuConfiguration.Previous
$originalPreviousMenuName = $selectedMenuName
if ($selectedMenuName -eq $menuName -and (Test-Path -LiteralPath $statePath)) {
    try {
        $previousState = Get-Content -LiteralPath $statePath -Raw -Encoding UTF8 | ConvertFrom-Json
        $originalPreviousMenuName = [string]$previousState.PreviousMenuName
        if ($previousState.PSObject.Properties.Name -contains 'PreviousMenuValueExists') {
            $menuConfiguration.PreviousExists = [bool]$previousState.PreviousMenuValueExists
        }
    } catch { }
}
if ($originalPreviousMenuName -eq $legacyMenuName) { $originalPreviousMenuName = '' }
$menuBaseName = if ($selectedMenuName -eq $menuName -and $originalPreviousMenuName) { $originalPreviousMenuName } else { $selectedMenuName }
$menuPath = Install-Menu $playerDirectory $menuBaseName

New-Item -ItemType Directory -Force -Path $installDirectory | Out-Null
New-Item -ItemType Directory -Force -Path $dataDirectory | Out-Null
if (-not $TestMode) {
    Get-Process -Name 'PotPlayerFrameClip' -ErrorAction SilentlyContinue | Stop-Process -Force
    Get-Process -Name $legacyRunName -ErrorAction SilentlyContinue | Stop-Process -Force
}
Copy-IfDifferent $sourceExe $installExe
if (Test-Path -LiteralPath (Join-Path $releaseRoot 'PotPlayerFrameClip.exe.config')) {
    Copy-IfDifferent (Join-Path $releaseRoot 'PotPlayerFrameClip.exe.config') ($installExe + '.config')
}
Copy-IfDifferent (Join-Path $releaseRoot 'uninstall.ps1') (Join-Path $installDirectory 'uninstall.ps1')

$legacyMenuPath = Join-Path $menusDirectory $legacyMenuName
if (Test-Path -LiteralPath $legacyMenuPath) {
    $backupDirectory = Join-Path $dataDirectory 'legacy-backups'
    New-Item -ItemType Directory -Force -Path $backupDirectory | Out-Null
    Copy-Item -LiteralPath $legacyMenuPath -Destination (Join-Path $backupDirectory ($legacyMenuName + '.' + (Get-Date -Format 'yyyyMMdd-HHmmss') + '.bak')) -Force
    Remove-Item -LiteralPath $legacyMenuPath -Force
}

if ($FFmpegPath) {
    if (-not (Test-Path -LiteralPath $FFmpegPath)) { throw "FFmpeg was not found at $FFmpegPath" }
    $probe = Join-Path (Split-Path -Parent $FFmpegPath) 'ffprobe.exe'
    $configPath = Join-Path $dataDirectory 'FrameClip.ini'
    $existingLines = if (Test-Path -LiteralPath $configPath) { Get-Content -LiteralPath $configPath -Encoding UTF8 } else { @() }
    $values = [ordered]@{}
    foreach ($line in $existingLines) {
        $split = $line.IndexOf('=')
        if ($split -gt 0) { $values[$line.Substring(0,$split)] = $line.Substring($split+1) }
    }
    if (-not $values.Contains('LibraryRootDirectory')) { $values.LibraryRootDirectory = Join-Path ([Environment]::GetFolderPath('MyVideos')) 'FrameClip' }
    if (-not $values.Contains('ImageFormat')) { $values.ImageFormat = 'png16' }
    if (-not $values.Contains('VideoPreset')) { $values.VideoPreset = 'prores422hq' }
    $values.FFmpeg = [IO.Path]::GetFullPath($FFmpegPath)
    $values.FFprobe = if (Test-Path -LiteralPath $probe) { [IO.Path]::GetFullPath($probe) } else { $probe }
    [IO.File]::WriteAllLines($configPath, @($values.GetEnumerator() | ForEach-Object { "$($_.Key)=$($_.Value)" }), [Text.UTF8Encoding]::new($false))
}

if ($menuConfiguration.Mode -eq 'Ini') {
    Copy-Item -LiteralPath $menuConfiguration.Path -Destination ($menuConfiguration.Path + '.frameclip-backup-' + (Get-Date -Format 'yyyyMMdd-HHmmss'))
    Set-IniValue $menuConfiguration.Path 'Settings' 'LastMenuName' $menuName
} else {
    New-ItemProperty -LiteralPath $menuConfiguration.Path -Name LastMenuName -Value $menuName -PropertyType String -Force | Out-Null
}
if (-not $TestMode -and (Test-PotPlayerRunning)) {
    # PotPlayer 会在退出时把内存中的旧菜单名写回。常驻程序在播放器退出后
    # 再应用一次此状态，安装过程无需强制中断当前播放。
    Write-PendingMenuRepair $menuConfiguration.Mode $menuConfiguration.Path $menuName
} else {
    Remove-Item -LiteralPath $pendingMenuPath -Force -ErrorAction SilentlyContinue
}

$runKey = 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Run'
$legacyCommand = if ($TestMode) { $null } else { (Get-ItemProperty -LiteralPath $runKey -ErrorAction SilentlyContinue).$legacyRunName }
if ($legacyCommand -and -not (Test-Path -LiteralPath (Join-Path $dataDirectory 'FrameClip.ini'))) {
    $legacyExecutable = ([string]$legacyCommand).Trim()
    if ($legacyExecutable.StartsWith('"')) {
        $closingQuote = $legacyExecutable.IndexOf('"', 1)
        if ($closingQuote -gt 1) { $legacyExecutable = $legacyExecutable.Substring(1, $closingQuote - 1) }
    } else {
        $space = $legacyExecutable.IndexOf(' ')
        if ($space -gt 0) { $legacyExecutable = $legacyExecutable.Substring(0, $space) }
    }
    $legacyConfig = Join-Path (Split-Path -Parent $legacyExecutable) ($legacyRunName + '.ini')
    if (Test-Path -LiteralPath $legacyConfig) {
        Copy-Item -LiteralPath $legacyConfig -Destination (Join-Path $dataDirectory 'FrameClip.ini') -Force
    }
}
if (-not $TestMode) {
    New-Item -Path $runKey -Force | Out-Null
    Remove-ItemProperty -LiteralPath $runKey -Name $legacyRunName -ErrorAction SilentlyContinue
    if (-not $NoStartup) {
        New-ItemProperty -LiteralPath $runKey -Name $appName -Value ('"' + $installExe + '"') -PropertyType String -Force | Out-Null
    } else {
        Remove-ItemProperty -LiteralPath $runKey -Name $appName -ErrorAction SilentlyContinue
    }
}

[ordered]@{
    InstalledAt = (Get-Date).ToString('o')
    InstallDirectory = $installDirectory
    DataDirectory = $dataDirectory
    PlayerDirectory = $playerDirectory
    MenuPath = $menuPath
    MenuConfigMode = $menuConfiguration.Mode
    MenuConfigPath = $menuConfiguration.Path
    PreviousMenuName = $originalPreviousMenuName
    PreviousMenuValueExists = [bool]$menuConfiguration.PreviousExists
} | ConvertTo-Json | Set-Content -LiteralPath $statePath -Encoding UTF8

if (-not $TestMode) { Start-FrameClipHelper }
Write-Host "$productName installed. Restart PotPlayer once if the menu is already open."
