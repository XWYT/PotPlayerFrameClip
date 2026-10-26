[CmdletBinding()]
param([string]$FfmpegPath, [string]$FixtureFfmpegPath, [string]$RenderDirectory, [switch]$Benchmark, [switch]$GpuBenchmark)
$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'compiler.ps1')
$root = Split-Path -Parent $PSScriptRoot
$compiler = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
$output = Join-Path $env:TEMP ('FrameClipJobTests-' + [Guid]::NewGuid().ToString('N') + '.exe')
$oldMedia = $env:FRAMECLIP_TEST_FFMPEG
$oldRender = $env:FRAMECLIP_UI_PREVIEW
$oldFixture = $env:FRAMECLIP_TEST_FIXTURE_FFMPEG
try {
    if ($Benchmark -and $GpuBenchmark) { throw 'Choose one benchmark suite.' }
    $entry = if ($GpuBenchmark) { 'PotPlayerFrameClip.GpuBenchmarkProgram' } elseif ($Benchmark) { 'PotPlayerFrameClip.JobBenchmarkProgram' } else { 'PotPlayerFrameClip.JobTestProgram' }
    $arguments = @('/nologo', '/target:exe', '/platform:anycpu', '/optimize+', '/codepage:65001',
        ('/main:' + $entry), ('/out:' + $output),
        ('/win32manifest:' + (Join-Path $root 'app.manifest')),
        '/reference:System.dll', '/reference:System.Core.dll', '/reference:System.Drawing.dll',
        '/reference:System.Windows.Forms.dll', '/reference:System.Management.dll', '/reference:System.Xml.dll', '/reference:Microsoft.CSharp.dll')
    $arguments += @(Get-ChildItem -LiteralPath (Join-Path $root 'src') -Filter '*.cs' -Recurse | ForEach-Object FullName)
    $arguments += @(Get-ChildItem -LiteralPath (Join-Path $root 'tests') -Filter '*.cs' | ForEach-Object FullName)
    $arguments += @(Get-ChildItem -LiteralPath (Join-Path $root 'assets\icons') -Filter '*.png' | ForEach-Object { '/resource:' + $_.FullName + ',FrameClip.Icons.' + $_.Name })
    Invoke-FrameClipCompiler $compiler $arguments
    if ($FfmpegPath) { $env:FRAMECLIP_TEST_FFMPEG = [IO.Path]::GetFullPath($FfmpegPath) }
    if ($FixtureFfmpegPath) { $env:FRAMECLIP_TEST_FIXTURE_FFMPEG = [IO.Path]::GetFullPath($FixtureFfmpegPath) }
    if ($RenderDirectory) { $env:FRAMECLIP_UI_PREVIEW = [IO.Path]::GetFullPath($RenderDirectory) }
    if ($Benchmark -or $GpuBenchmark) {
        if (-not $FfmpegPath -or -not $RenderDirectory) { throw 'Benchmark requires FfmpegPath and RenderDirectory.' }
        & $output ([IO.Path]::GetFullPath($FfmpegPath)) ([IO.Path]::GetFullPath($RenderDirectory))
    } else { & $output }
    if ($LASTEXITCODE -ne 0) { throw 'Job tests failed.' }
} finally {
    $env:FRAMECLIP_TEST_FFMPEG = $oldMedia
    $env:FRAMECLIP_UI_PREVIEW = $oldRender
    $env:FRAMECLIP_TEST_FIXTURE_FFMPEG = $oldFixture
    Remove-Item -LiteralPath $output -Force -ErrorAction SilentlyContinue
}
