$ErrorActionPreference = 'Stop'
$src = Join-Path $PSScriptRoot 'src\HS80Control.cs'
$out = Join-Path $PSScriptRoot 'HS80Control.exe'
if (-not (Test-Path $src)) { throw "src\HS80Control.cs not found" }
$code = Get-Content -Path $src -Raw -Encoding UTF8
Write-Host "Compiling with Add-Type..."
try {
    Add-Type -TypeDefinition $code -Language CSharp -OutputAssembly $out -OutputType ConsoleApplication -ReferencedAssemblies @('System.dll','System.Core.dll')
    if (Test-Path $out) { Write-Host "OK: $out"; exit 0 }
    throw "exe not created"
} catch {
    Write-Host "Add-Type failed: $_"
    $csc = @(
        "$env:WINDIR\Microsoft.NET\Framework64\v4.0.30319\csc.exe",
        "$env:WINDIR\Microsoft.NET\Framework\v4.0.30319\csc.exe"
    ) | Where-Object { Test-Path $_ } | Select-Object -First 1
    if ($csc) {
        & $csc /nologo /optimize+ /platform:anycpu /out:$out $src
        if ($LASTEXITCODE -eq 0 -and (Test-Path $out)) { exit 0 }
    }
    exit 1
}
