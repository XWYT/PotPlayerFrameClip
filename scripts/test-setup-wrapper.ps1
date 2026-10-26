[CmdletBinding()]
param([Parameter(Mandatory=$true)][string]$EnginePath, [switch]$BundledFFmpeg)
$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'compiler.ps1')
$root = Split-Path -Parent $PSScriptRoot
$directory = Join-Path $root 'dist-test'
New-Item -ItemType Directory -Path $directory -Force | Out-Null
$output = Join-Path $directory 'FrameClipWrapperTest.exe'
$compiler = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
$arguments = @('/nologo','/target:winexe','/platform:anycpu','/optimize+','/codepage:65001',
    '/main:PotPlayerFrameClip.MaintenanceProgram',('/out:' + $output),
    ('/win32manifest:' + (Join-Path $root 'app.manifest')),
    ('/resource:' + [IO.Path]::GetFullPath($EnginePath) + ',FrameClip.SetupEngine'),
    '/reference:System.dll','/reference:System.Core.dll','/reference:System.Drawing.dll',
    '/reference:System.Windows.Forms.dll','/reference:System.Management.dll','/reference:System.Xml.dll','/reference:Microsoft.CSharp.dll')
if ($BundledFFmpeg) { $arguments += '/define:FRAMECLIP_BUNDLED_FFMPEG' }
$arguments += @(Get-ChildItem -LiteralPath (Join-Path $root 'src') -Filter '*.cs' -Recurse | ForEach-Object FullName)
$arguments += @(Get-ChildItem -LiteralPath (Join-Path $root 'assets\icons') -Filter '*.png' | ForEach-Object { '/resource:' + $_.FullName + ',FrameClip.Icons.' + $_.Name })
Invoke-FrameClipCompiler $compiler $arguments
& (Join-Path $PSScriptRoot 'test-setup-engine.ps1') -SetupPath $output -BundledFFmpeg:$BundledFFmpeg -MaintenanceUninstall
