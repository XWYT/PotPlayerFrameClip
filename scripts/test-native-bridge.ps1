[CmdletBinding()]
param([string]$ReleaseDirectory)
$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'compiler.ps1')
$root = Split-Path -Parent $PSScriptRoot
if (-not $ReleaseDirectory) { $ReleaseDirectory = Join-Path $root 'dist\release' }
$ReleaseDirectory = [IO.Path]::GetFullPath($ReleaseDirectory)
$scratch = Join-Path $env:TEMP ('FrameClipNativeTests-' + [Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $scratch | Out-Null
$previous = $env:FRAMECLIP_DATA_DIRECTORY
try {
    $env:FRAMECLIP_DATA_DIRECTORY = $scratch
    $compiler = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
    foreach ($architecture in @('64','32')) {
        if ($architecture -eq '64' -and -not [Environment]::Is64BitOperatingSystem) { continue }
        $platform = if ($architecture -eq '64') { 'x64' } else { 'x86' }
        $runner = Join-Path $scratch ('BridgeTests' + $architecture + '.exe')
        Invoke-FrameClipCompiler $compiler @('/nologo','/target:exe',('/platform:' + $platform),
            '/reference:System.dll','/reference:System.Windows.Forms.dll',
            '/main:PotPlayerFrameClip.NativeBridgeTestProgram',('/out:' + $runner),
            (Join-Path $root 'tests\NativeBridgeTests.cs'), (Join-Path $root 'tests\TestDesktop.cs'))
        & $runner (Join-Path $ReleaseDirectory ('FrameClipBridge' + $architecture + '.dll')) $scratch
        if ($LASTEXITCODE -ne 0) { throw ('Native bridge regression failed: ' + $platform) }
    }
} finally {
    $env:FRAMECLIP_DATA_DIRECTORY = $previous
    # Only the unique test directory is removed, never application data or media.
    if ([IO.Path]::GetFileName($scratch) -like 'FrameClipNativeTests-*') { Remove-Item -LiteralPath $scratch -Recurse -Force }
}
