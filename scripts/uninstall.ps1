[CmdletBinding()]
param(
    [switch]$RemoveUserData,
    [string]$InstallDirectory,
    [string]$DataDirectory,
    [switch]$TestMode,
    [switch]$Elevated
)

$ErrorActionPreference = 'Stop'
$scriptPath = $MyInvocation.MyCommand.Path
$appName = 'PotPlayerFrameClip'
if (-not $InstallDirectory) { $InstallDirectory = Join-Path $env:LOCALAPPDATA $appName }
$installDirectory = [IO.Path]::GetFullPath($InstallDirectory)
if (-not $DataDirectory) { $DataDirectory = Join-Path $env:LOCALAPPDATA $appName }
$dataDirectory = [IO.Path]::GetFullPath($DataDirectory)
$statePath = Join-Path $dataDirectory 'install-state.json'
$pendingMenuPath = Join-Path $dataDirectory 'menu-selection.pending'
$runKey = 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Run'
$legacyRunName = 'PotPlayer' + ([char]0x52) + 'esolveCapture'

function Test-PotPlayerRunning {
    foreach ($name in 'PotPlayerMini64','PotPlayer64','PotPlayerMini','PotPlayer') {
        if (Get-Process -Name $name -ErrorAction SilentlyContinue) { return $true }
    }
    return $false
}

function Test-DirectoryWritable {
    param([string]$Directory)
    try {
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

function Get-TextFileEncoding {
    param([string]$Path)

    $bytes = [IO.File]::ReadAllBytes($Path)
    if ($bytes.Length -ge 2 -and $bytes[0] -eq 0xFF -and $bytes[1] -eq 0xFE) { return [Text.Encoding]::Unicode }
    if ($bytes.Length -ge 2 -and $bytes[0] -eq 0xFE -and $bytes[1] -eq 0xFF) { return [Text.Encoding]::BigEndianUnicode }
    if ($bytes.Length -ge 3 -and $bytes[0] -eq 0xEF -and $bytes[1] -eq 0xBB -and $bytes[2] -eq 0xBF) {
        return [Text.UTF8Encoding]::new($true)
    }
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

function Get-ValidatedInstallState {
    param($RawState)

    if (-not $RawState) { return $null }

    try {
        $playerDirectory = [IO.Path]::GetFullPath([string]$RawState.PlayerDirectory)
        $menuPath = [IO.Path]::GetFullPath([string]$RawState.MenuPath)
    } catch {
        throw 'FrameClip install state contains an invalid path. No PotPlayer files were changed.'
    }

    $playerExecutables = @('PotPlayerMini64.exe','PotPlayer64.exe','PotPlayerMini.exe','PotPlayer.exe')
    $verifiedPlayerDirectory = [bool]($playerExecutables |
        Where-Object { Test-Path -LiteralPath (Join-Path $playerDirectory $_) -PathType Leaf })

    $expectedMenuPath = [IO.Path]::GetFullPath((Join-Path $playerDirectory 'Menus\FrameClipMenu.xml'))
    if (-not $menuPath.Equals($expectedMenuPath, [StringComparison]::OrdinalIgnoreCase)) {
        throw 'FrameClip install state contains an unexpected menu path. No PotPlayer files were changed.'
    }

    $mode = [string]$RawState.MenuConfigMode
    $configPath = [string]$RawState.MenuConfigPath
    if ($mode -eq 'Ini') {
        try { $configPath = [IO.Path]::GetFullPath($configPath) }
        catch { throw 'FrameClip install state contains an invalid PotPlayer INI path. No PotPlayer files were changed.' }

        $allowedIniPaths = @('PotPlayerMini64.ini','PotPlayer64.ini','PotPlayerMini.ini','PotPlayer.ini') |
            ForEach-Object { [IO.Path]::GetFullPath((Join-Path $playerDirectory $_)) }
        if (-not ($allowedIniPaths | Where-Object { $_.Equals($configPath, [StringComparison]::OrdinalIgnoreCase) })) {
            throw 'FrameClip install state contains an unexpected PotPlayer INI path. No PotPlayer files were changed.'
        }
    } elseif ($mode -eq 'Registry') {
        $allowedRegistryPaths = @(
            'HKCU:\Software\DAUM\PotPlayerMini64\Settings',
            'HKCU:\Software\DAUM\PotPlayer64\Settings',
            'HKCU:\Software\DAUM\PotPlayerMini\Settings',
            'HKCU:\Software\DAUM\PotPlayer\Settings'
        )
        if (-not ($allowedRegistryPaths | Where-Object { $_.Equals($configPath, [StringComparison]::OrdinalIgnoreCase) })) {
            throw 'FrameClip install state contains an unexpected PotPlayer registry path. No PotPlayer files were changed.'
        }
    } else {
        throw 'FrameClip install state contains an unsupported PotPlayer configuration mode. No PotPlayer files were changed.'
    }

    $recordedPlayerFileExists = (Test-Path -LiteralPath $menuPath -PathType Leaf) -or
        ($mode -eq 'Ini' -and (Test-Path -LiteralPath $configPath -PathType Leaf))
    if (-not $verifiedPlayerDirectory -and $recordedPlayerFileExists) {
        throw 'FrameClip install state does not point to a verified PotPlayer directory. No PotPlayer files were changed.'
    }

    $previousMenuName = [string]$RawState.PreviousMenuName
    if ($previousMenuName.Contains("`r") -or $previousMenuName.Contains("`n") -or
        ($previousMenuName -and [IO.Path]::GetFileName($previousMenuName) -ne $previousMenuName)) {
        throw 'FrameClip install state contains an invalid previous menu name. No PotPlayer files were changed.'
    }

    return [pscustomobject]@{
        PlayerDirectory = $playerDirectory
        MenuPath = $menuPath
        MenuConfigMode = $mode
        MenuConfigPath = $configPath
        PreviousMenuName = $previousMenuName
        PreviousMenuValueExists = if ($RawState.PSObject.Properties.Name -contains 'PreviousMenuValueExists') {
            [bool]$RawState.PreviousMenuValueExists
        } else {
            $true
        }
    }
}

function Test-FrameClipMenuFile {
    param([string]$Path)

    if (-not (Test-Path -LiteralPath $Path -PathType Leaf)) { return $false }
    try {
        [xml]$document = [IO.File]::ReadAllText($Path)
        $titles = @('参照帧与片段截取', 'Reference Frame & Clip Capture', '帧与片段')
        foreach ($submenu in @($document.Menu.SubMenu)) {
            if ($titles -notcontains [string]$submenu.Name) { continue }
            $commands = @($submenu.MenuItem | Where-Object { $_.CmdID })
            if ($commands.Count -eq 9 -and @($commands | Where-Object { $_.CmdID -ne 'ID_APP_ABOUT' }).Count -eq 0) {
                return $true
            }
        }
    } catch { }
    return $false
}

function Restart-Elevated {
    $parts = @(
        '& ' + (Quote-PowerShellLiteral $scriptPath),
        '-InstallDirectory ' + (Quote-PowerShellLiteral $installDirectory),
        '-DataDirectory ' + (Quote-PowerShellLiteral $dataDirectory),
        '-Elevated'
    )
    if ($RemoveUserData) { $parts += '-RemoveUserData' }
    $encoded = [Convert]::ToBase64String([Text.Encoding]::Unicode.GetBytes($parts -join ' '))
    $process = Start-Process -FilePath 'powershell.exe' -Verb RunAs -Wait -PassThru `
        -ArgumentList @('-NoProfile', '-ExecutionPolicy', 'Bypass', '-EncodedCommand', $encoded)
    exit $process.ExitCode
}

$state = $null
if (Test-Path -LiteralPath $statePath) {
    $state = Get-ValidatedInstallState (Get-Content -LiteralPath $statePath -Raw -Encoding UTF8 | ConvertFrom-Json)
    if ((Test-Path -LiteralPath $state.MenuPath) -and -not (Test-FrameClipMenuFile $state.MenuPath)) {
        throw 'The recorded menu file is not a verified FrameClip menu. No PotPlayer settings were changed.'
    }
    $menuDirectory = if ($state.MenuPath) { Split-Path -Parent $state.MenuPath } else { $null }
    $configDirectory = if ($state.MenuConfigMode -eq 'Ini' -and $state.MenuConfigPath) { Split-Path -Parent $state.MenuConfigPath } else { $null }
    $needsElevation = ($menuDirectory -and (Test-Path -LiteralPath $menuDirectory) -and -not (Test-DirectoryWritable $menuDirectory)) -or
        ($configDirectory -and (Test-Path -LiteralPath $configDirectory) -and -not (Test-DirectoryWritable $configDirectory))
    if (-not $TestMode -and $needsElevation) {
        if ($Elevated) { throw "The PotPlayer menu directory is not writable: $menuDirectory" }
        Restart-Elevated
    }
}

function Set-IniValue {
    param([string]$Path, [string]$Section, [string]$Key, [string]$Value)
    $encoding = Get-TextFileEncoding $Path
    $lines = [Collections.Generic.List[string]]::new()
    foreach ($line in [IO.File]::ReadAllLines($Path, $encoding)) { $lines.Add($line) }
    $inSection = $false
    for ($index = 0; $index -lt $lines.Count; $index++) {
        if ($lines[$index] -match '^\[(.+)\]$') { $inSection = $Matches[1] -eq $Section; continue }
        if ($inSection -and $lines[$index].StartsWith($Key + '=', [StringComparison]::OrdinalIgnoreCase)) {
            $lines[$index] = "$Key=$Value"
            [IO.File]::WriteAllLines($Path, $lines, $encoding)
            return
        }
    }
}

function Remove-IniValue {
    param([string]$Path, [string]$Section, [string]$Key)
    $encoding = Get-TextFileEncoding $Path
    $lines = [Collections.Generic.List[string]]::new()
    foreach ($line in [IO.File]::ReadAllLines($Path, $encoding)) { $lines.Add($line) }
    $inSection = $false
    for ($index = 0; $index -lt $lines.Count; $index++) {
        if ($lines[$index] -match '^\[(.+)\]$') { $inSection = $Matches[1] -eq $Section; continue }
        if ($inSection -and $lines[$index].StartsWith($Key + '=', [StringComparison]::OrdinalIgnoreCase)) {
            $lines.RemoveAt($index)
            [IO.File]::WriteAllLines($Path, $lines, $encoding)
            return
        }
    }
}

if (-not $TestMode -and (Test-PotPlayerRunning)) {
    throw '请先关闭 PotPlayer，再继续卸载。这样才能可靠恢复原菜单，并避免播放器退出时写回已经失效的菜单配置。'
}

if (-not $TestMode) {
    Get-Process -Name 'PotPlayerFrameClip','FrameClipBridgeHost32' -ErrorAction SilentlyContinue | Stop-Process -Force
    Remove-ItemProperty -LiteralPath $runKey -Name $appName -ErrorAction SilentlyContinue
    Remove-ItemProperty -LiteralPath $runKey -Name $legacyRunName -ErrorAction SilentlyContinue
}
Remove-Item -LiteralPath $pendingMenuPath -Force -ErrorAction SilentlyContinue

if ($state) {
    if ($state.MenuConfigMode -eq 'Ini' -and (Test-Path -LiteralPath $state.MenuConfigPath)) {
        $previousExists = -not ($state.PSObject.Properties.Name -contains 'PreviousMenuValueExists') -or [bool]$state.PreviousMenuValueExists
        if ($previousExists) {
            Set-IniValue $state.MenuConfigPath 'Settings' 'LastMenuName' ([string]$state.PreviousMenuName)
        } else {
            Remove-IniValue $state.MenuConfigPath 'Settings' 'LastMenuName'
        }
    } elseif ($state.MenuConfigMode -eq 'Registry' -and (Test-Path -LiteralPath $state.MenuConfigPath)) {
        $previousExists = -not ($state.PSObject.Properties.Name -contains 'PreviousMenuValueExists') -or [bool]$state.PreviousMenuValueExists
        if (-not $previousExists) {
            Remove-ItemProperty -LiteralPath $state.MenuConfigPath -Name LastMenuName -ErrorAction SilentlyContinue
        } else {
            New-ItemProperty -LiteralPath $state.MenuConfigPath -Name LastMenuName -Value ([string]$state.PreviousMenuName) -PropertyType String -Force | Out-Null
        }
    }
    if ($state.MenuPath -and (Test-Path -LiteralPath $state.MenuPath)) { Remove-Item -LiteralPath $state.MenuPath -Force }
}

if ($RemoveUserData) {
    Remove-Item -LiteralPath $dataDirectory -Recurse -Force -ErrorAction SilentlyContinue
    if (-not $installDirectory.Equals($dataDirectory, [StringComparison]::OrdinalIgnoreCase)) {
        Remove-Item -LiteralPath $installDirectory -Recurse -Force -ErrorAction SilentlyContinue
    }
} else {
    Remove-Item -LiteralPath (Join-Path $installDirectory 'PotPlayerFrameClip.exe') -Force -ErrorAction SilentlyContinue
    Remove-Item -LiteralPath (Join-Path $installDirectory 'PotPlayerFrameClip.exe.config') -Force -ErrorAction SilentlyContinue
    Remove-Item -LiteralPath (Join-Path $installDirectory 'FrameClipBridge64.dll') -Force -ErrorAction SilentlyContinue
    Remove-Item -LiteralPath (Join-Path $installDirectory 'FrameClipBridge32.dll') -Force -ErrorAction SilentlyContinue
    Remove-Item -LiteralPath (Join-Path $installDirectory 'FrameClipBridgeHost32.exe') -Force -ErrorAction SilentlyContinue
    Write-Host "Executable removed. Settings and classification data remain in $dataDirectory"
}

Write-Host 'PotPlayer FrameClip uninstalled. Restart PotPlayer once.'
