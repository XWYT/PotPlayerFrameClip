function Invoke-FrameClipCompiler([string]$Compiler, [string[]]$CompilerArguments) {
    $response = Join-Path $env:TEMP ('FrameClipCompile-' + [Guid]::NewGuid().ToString('N') + '.rsp')
    try {
        $lines = @($CompilerArguments | ForEach-Object { '"' + $_.Replace('"', '\"') + '"' })
        [IO.File]::WriteAllLines($response, $lines, [Text.UTF8Encoding]::new($true))
        & $Compiler ('@' + $response)
        if ($LASTEXITCODE -ne 0) { throw "C# compilation failed with exit code $LASTEXITCODE." }
    } finally {
        Remove-Item -LiteralPath $response -Force -ErrorAction SilentlyContinue
    }
}
