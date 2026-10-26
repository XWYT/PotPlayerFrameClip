[CmdletBinding()]
param([switch]$Download)
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$manifest = Get-Content -LiteralPath (Join-Path $root 'installer\ffmpeg-manifest.json') -Raw -Encoding UTF8 | ConvertFrom-Json
$vendor = Join-Path $root 'dist\vendor'
New-Item -ItemType Directory -Path $vendor -Force | Out-Null
$archive = Join-Path $vendor $manifest.archive
if (-not (Test-Path -LiteralPath $archive)) {
    if (-not $Download) { throw 'Pinned FFmpeg archive missing. Run scripts\prepare-ffmpeg.ps1 -Download first.' }
    [Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12
    Invoke-WebRequest -UseBasicParsing -Uri $manifest.url -OutFile $archive
}
if ((Get-FileHash -LiteralPath $archive -Algorithm SHA256).Hash -ne $manifest.sha256) { throw 'FFmpeg archive SHA256 mismatch.' }
$destination = Join-Path $vendor $manifest.version
New-Item -ItemType Directory -Path $destination -Force | Out-Null
foreach ($item in Get-ChildItem -LiteralPath $destination -Force) {
    if ($item.PSIsContainer -or $item.Name -notin $manifest.files) { throw "Unexpected file in bundle staging directory: $($item.FullName)" }
}
Add-Type -AssemblyName System.IO.Compression.FileSystem
$zip = [IO.Compression.ZipFile]::OpenRead($archive)
try {
    $prefix = [IO.Path]::GetFileNameWithoutExtension($manifest.archive) + '/'
    foreach ($name in $manifest.files) {
        if ($name -eq 'provenance.json') { continue }
        $entryName = if ($name -eq 'LICENSE.txt') { $prefix + $name } else { $prefix + 'bin/' + $name }
        $entry = $zip.GetEntry($entryName)
        if (-not $entry) { throw "Required FFmpeg file missing: $entryName" }
        [IO.Compression.ZipFileExtensions]::ExtractToFile($entry,(Join-Path $destination $name),$true)
    }
} finally { $zip.Dispose() }
$manifest | ConvertTo-Json -Depth 4 | Set-Content -LiteralPath (Join-Path $destination 'provenance.json') -Encoding UTF8
$versionOutput = & (Join-Path $destination 'ffmpeg.exe') -version
if ($LASTEXITCODE -ne 0) { throw 'Bundled FFmpeg cannot run on this build host.' }
$versionOutput | Select-Object -First 2
$probeOutput = & (Join-Path $destination 'ffprobe.exe') -version
if ($LASTEXITCODE -ne 0) { throw 'Bundled FFprobe cannot run on this build host.' }
$probeOutput | Select-Object -First 1
Write-Host "Prepared pinned LGPL shared FFmpeg: $destination"
