function Get-FrameClipFFmpegManifest {
    $path = Join-Path $PSScriptRoot 'ffmpeg-manifest.json'
    if (-not (Test-Path -LiteralPath $path)) { $path = Join-Path (Split-Path -Parent $PSScriptRoot) 'installer\ffmpeg-manifest.json' }
    $manifest = Get-Content -LiteralPath $path -Raw -Encoding UTF8 | ConvertFrom-Json
    if ($manifest.version -notmatch '^\d+\.\d+\.\d+-\d{8}$') { throw 'Invalid bundled FFmpeg version.' }
    foreach ($name in $manifest.files) { if ($name -notmatch '^[A-Za-z0-9_.-]+$') { throw 'Invalid FFmpeg manifest path.' } }
    return $manifest
}

function Assert-FrameClipFFmpegPair([string]$Path) {
    if (-not $Path -or [IO.Path]::GetFileName($Path) -ine 'ffmpeg.exe' -or -not (Test-Path -LiteralPath $Path -PathType Leaf)) { throw "Select an existing ffmpeg.exe: $Path" }
    $probe = Join-Path (Split-Path -Parent $Path) 'ffprobe.exe'
    if (-not (Test-Path -LiteralPath $probe -PathType Leaf)) { throw "FFprobe must be next to FFmpeg: $probe" }
}
