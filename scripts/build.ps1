[CmdletBinding()]
param(
    [string]$Version = '0.3.3',
    [switch]$SkipTests,
    [switch]$SkipInstaller
)

$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path -Parent $PSScriptRoot
$source = Join-Path $projectRoot 'src\PotPlayerFrameClip.cs'
$dist = Join-Path $projectRoot 'dist'
$release = Join-Path $dist 'release'
$obj = Join-Path $dist 'obj'
$installerScript = Join-Path $projectRoot 'installer\PotPlayerFrameClip.iss'

function Get-Compiler {
    foreach ($candidate in @(
        "$env:WINDIR\Microsoft.NET\Framework64\v4.0.30319\csc.exe",
        "$env:WINDIR\Microsoft.NET\Framework\v4.0.30319\csc.exe"
    )) {
        if (Test-Path -LiteralPath $candidate) { return $candidate }
    }
    throw '.NET Framework 4 C# compiler was not found.'
}

function Get-InnoCompiler {
    $candidates = [Collections.Generic.List[string]]::new()
    if ($env:ISCC_PATH) { $candidates.Add($env:ISCC_PATH) }
    $candidates.Add((Join-Path $env:LOCALAPPDATA 'Programs\Inno Setup 6\ISCC.exe'))
    $candidates.Add((Join-Path $env:LOCALAPPDATA 'Programs\Inno Setup 7\ISCC.exe'))
    foreach ($root in @(${env:ProgramFiles(x86)}, $env:ProgramFiles)) {
        if (-not $root) { continue }
        $candidates.Add((Join-Path $root 'Inno Setup 6\ISCC.exe'))
        $candidates.Add((Join-Path $root 'Inno Setup 7\ISCC.exe'))
    }
    $fromPath = Get-Command ISCC.exe -ErrorAction SilentlyContinue
    if ($fromPath) { $candidates.Add($fromPath.Source) }
    foreach ($candidate in $candidates) {
        if ($candidate -and (Test-Path -LiteralPath $candidate)) { return $candidate }
    }
    throw 'Inno Setup Compiler was not found. Install it with: winget install --id JRSoftware.InnoSetup --exact --source winget'
}

function Get-ZigCompiler {
    $fromPath = Get-Command zig.exe -ErrorAction SilentlyContinue
    if ($fromPath) { return $fromPath.Source }
    $packageRoot = Join-Path $env:LOCALAPPDATA 'Microsoft\WinGet\Packages'
    if (Test-Path -LiteralPath $packageRoot) {
        $candidate = Get-ChildItem -LiteralPath $packageRoot -Filter zig.exe -Recurse -ErrorAction SilentlyContinue |
            Sort-Object FullName -Descending | Select-Object -First 1
        if ($candidate) { return $candidate.FullName }
    }
    throw 'Zig compiler was not found. Install it with: winget install --id zig.zig --exact --source winget'
}

New-Item -ItemType Directory -Force -Path $dist | Out-Null
Remove-Item -LiteralPath $release -Recurse -Force -ErrorAction SilentlyContinue
Remove-Item -LiteralPath $obj -Recurse -Force -ErrorAction SilentlyContinue
Get-ChildItem -LiteralPath $dist -File -ErrorAction SilentlyContinue |
    Where-Object { $_.Name -match '^PotPlayerFrameClip-v.+-(windows\.zip|Setup\.exe|Setup\.exe\.sha256)$' } |
    Remove-Item -Force
New-Item -ItemType Directory -Force -Path $release | Out-Null
New-Item -ItemType Directory -Force -Path $obj | Out-Null

# Keep Zig caches inside the build tree so restricted shells and CI runners do not
# need write access to the interactive user's LocalAppData directory.
$env:ZIG_GLOBAL_CACHE_DIR = Join-Path $obj 'zig-global-cache'
$env:ZIG_LOCAL_CACHE_DIR = Join-Path $obj 'zig-local-cache'
New-Item -ItemType Directory -Force -Path $env:ZIG_GLOBAL_CACHE_DIR | Out-Null
New-Item -ItemType Directory -Force -Path $env:ZIG_LOCAL_CACHE_DIR | Out-Null

$zig = Get-ZigCompiler
$bridge64 = Join-Path $release 'FrameClipBridge64.dll'
$bridge32 = Join-Path $release 'FrameClipBridge32.dll'
$bridgeHost32 = Join-Path $release 'FrameClipBridgeHost32.exe'
& $zig cc -target x86_64-windows-gnu -O2 -shared `
    (Join-Path $projectRoot 'native\frameclip_bridge.c') (Join-Path $projectRoot 'native\frameclip_bridge.def') `
    -o $bridge64 -luser32 -lcomctl32
if ($LASTEXITCODE -ne 0) { throw '64-bit native bridge compilation failed.' }
& $zig cc -target x86-windows-gnu -O2 -shared `
    (Join-Path $projectRoot 'native\frameclip_bridge.c') (Join-Path $projectRoot 'native\frameclip_bridge.def') `
    -o $bridge32 -luser32 -lcomctl32
if ($LASTEXITCODE -ne 0) { throw '32-bit native bridge compilation failed.' }
& $zig cc -target x86-windows-gnu -O2 -municode '-Wl,/subsystem:windows' `
    (Join-Path $projectRoot 'native\bridge_host.c') -o $bridgeHost32 -luser32 -lshell32
if ($LASTEXITCODE -ne 0) { throw '32-bit bridge host compilation failed.' }

if ($Version -notmatch '^\d+\.\d+\.\d+$') {
    throw 'Version must use the form major.minor.patch, for example 0.3.3.'
}
$assemblyVersion = $Version + '.0'
$generatedSource = Join-Path $obj 'PotPlayerFrameClip.generated.cs'
$sourceText = [IO.File]::ReadAllText($source, [Text.Encoding]::UTF8)
$sourceText = [Text.RegularExpressions.Regex]::Replace(
    $sourceText,
    '\[assembly:\s*AssemblyVersion\("[^"]+"\)\]',
    '[assembly: AssemblyVersion("' + $assemblyVersion + '")]')
$sourceText = [Text.RegularExpressions.Regex]::Replace(
    $sourceText,
    '\[assembly:\s*AssemblyFileVersion\("[^"]+"\)\]',
    '[assembly: AssemblyFileVersion("' + $assemblyVersion + '")]')
[IO.File]::WriteAllText($generatedSource, $sourceText, [Text.UTF8Encoding]::new($false))

$compiler = Get-Compiler
$output = Join-Path $release 'PotPlayerFrameClip.exe'
$compilerArguments = @(
    '/nologo', '/target:winexe', '/platform:anycpu', '/optimize+', '/debug:pdbonly', '/codepage:65001',
    ('/win32manifest:' + (Join-Path $projectRoot 'app.manifest')),
    ('/out:' + $output),
    '/reference:System.dll', '/reference:System.Core.dll', '/reference:System.Drawing.dll',
    '/reference:System.Windows.Forms.dll', '/reference:System.Management.dll', '/reference:System.Xml.dll', '/reference:Microsoft.CSharp.dll',
    $generatedSource
)
& $compiler $compilerArguments
if ($LASTEXITCODE -ne 0) { throw "Compilation failed with exit code $LASTEXITCODE." }

if (-not $SkipTests) {
    & (Join-Path $PSScriptRoot 'verify.ps1') -ExecutablePath $output -ExpectedVersion $assemblyVersion
    if ($LASTEXITCODE -ne 0) { throw 'Verification failed.' }
}

Copy-Item -LiteralPath (Join-Path $projectRoot 'app.config') -Destination (Join-Path $release 'PotPlayerFrameClip.exe.config')
Copy-Item -LiteralPath (Join-Path $projectRoot 'menu\FrameClipMenu.xml') -Destination $release
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'install.ps1') -Destination $release
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'uninstall.ps1') -Destination $release

if ($SkipInstaller) {
    Get-FileHash -LiteralPath $output -Algorithm SHA256
    return
}

$iscc = Get-InnoCompiler
& $iscc ("/DAppVersion={0}" -f $Version) $installerScript
if ($LASTEXITCODE -ne 0) { throw "Inno Setup compilation failed with exit code $LASTEXITCODE." }

$setup = Join-Path $dist ("PotPlayerFrameClip-v{0}-Setup.exe" -f $Version)
if (-not (Test-Path -LiteralPath $setup)) { throw "Installer was not created: $setup" }
$hash = Get-FileHash -LiteralPath $setup -Algorithm SHA256
[IO.File]::WriteAllText($setup + '.sha256', ($hash.Hash + '  ' + [IO.Path]::GetFileName($setup) + [Environment]::NewLine), [Text.UTF8Encoding]::new($false))
$hash
