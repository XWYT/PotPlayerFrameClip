function Assert-FrameClipSafeDirectory {
    param([string]$Directory)
    $full = [IO.Path]::GetFullPath($Directory).TrimEnd('\')
    if ($full.Length -le [IO.Path]::GetPathRoot($full).Length) { throw 'A drive root is not a maintenance directory.' }
    $cursor = $full
    while ($cursor) {
        if ((Test-Path -LiteralPath $cursor) -and ((Get-Item -LiteralPath $cursor -Force).Attributes -band [IO.FileAttributes]::ReparsePoint)) { throw "Linked maintenance directory is not supported: $cursor" }
        $parent = [IO.Path]::GetDirectoryName($cursor)
        if ($parent -eq $cursor) { break }; $cursor = $parent
    }
}

function Get-FrameClipOwnedProcesses {
    param([string]$InstallDirectory)
    $root = [IO.Path]::GetFullPath($InstallDirectory).TrimEnd('\')
    @(Get-CimInstance Win32_Process -ErrorAction Stop | Where-Object {
        $_.ExecutablePath -and [IO.Path]::GetDirectoryName($_.ExecutablePath).Equals($root, [StringComparison]::OrdinalIgnoreCase) -and
        $_.Name -in @('PotPlayerFrameClip.exe', 'FrameClipBridgeHost32.exe', 'PotPlayerResolveCapture.exe')
    })
}

if ($env:FRAMECLIP_MAINTENANCE_LOG) {
    Start-Transcript -LiteralPath $env:FRAMECLIP_MAINTENANCE_LOG -Append -Force | Out-Null
}

function Stop-FrameClipForMaintenance {
    param([string]$InstallDirectory, [string]$DataDirectory)
    $player = @(Get-Process -Name PotPlayerMini64,PotPlayer64,PotPlayerMini,PotPlayer -ErrorAction SilentlyContinue)
    if ($player.Count) { throw 'Close PotPlayer before upgrading or uninstalling. No player process will be terminated.' }
    $owned = @(Get-FrameClipOwnedProcesses $InstallDirectory)
    $jobs = Join-Path $DataDirectory 'jobs.xml'
    if ($owned.Count -and (Test-Path -LiteralPath $jobs)) {
        $xml = New-Object Xml.XmlDocument; $xml.XmlResolver = $null; $xml.Load($jobs)
        if (@($xml.SelectNodes('/jobs/job') | Where-Object { $_.state -notin @('Succeeded','Failed','Canceled','Interrupted') }).Count) {
            throw 'FrameClip jobs are active. Finish or cancel them in the task center before continuing.'
        }
    }
    $exe = Join-Path $InstallDirectory 'PotPlayerFrameClip.exe'
    if (@($owned | Where-Object Name -eq 'PotPlayerFrameClip.exe').Count) {
        $previous = $env:FRAMECLIP_DATA_DIRECTORY
        try {
            $env:FRAMECLIP_DATA_DIRECTORY = [IO.Path]::GetFullPath($DataDirectory)
            $default = Join-Path $env:LOCALAPPDATA 'PotPlayerFrameClip'
            if ($env:FRAMECLIP_DATA_DIRECTORY.Equals($default, [StringComparison]::OrdinalIgnoreCase)) { $env:FRAMECLIP_DATA_DIRECTORY = $null }
            $quit = Start-Process -FilePath $exe -ArgumentList '--maintenance-stop' -WindowStyle Hidden -PassThru
            if (-not $quit.WaitForExit(12000)) { throw 'FrameClip maintenance request timed out. Nothing replaced.' }
            if ($quit.ExitCode -eq 2) {
                # Older builds lack the atomic maintenance command. Never force them.
                $quit = Start-Process -FilePath $exe -ArgumentList '--quit' -WindowStyle Hidden -PassThru
                if (-not $quit.WaitForExit(12000)) { throw 'FrameClip graceful shutdown timed out. Nothing replaced.' }
            }
            if ($quit.ExitCode -ne 0) { throw 'FrameClip is busy or unavailable. Finish tasks and exit its tray icon before retrying.' }
        } finally { $env:FRAMECLIP_DATA_DIRECTORY = $previous }
    }
    $deadline = (Get-Date).AddSeconds(12)
    do {
        $owned = @(Get-FrameClipOwnedProcesses $InstallDirectory)
        if (-not $owned.Count) { return }
        Start-Sleep -Milliseconds 200
    } while ((Get-Date) -lt $deadline)
    throw 'FrameClip is still running. Exit its tray icon and retry. No process was force-killed.'
}
