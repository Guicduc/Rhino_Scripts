$ErrorActionPreference = "Stop"

$root = Split-Path -Parent $MyInvocation.MyCommand.Path
$compiler = "C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe"
$testOutput = Join-Path $env:TEMP "ThreadRhino-CoreTests.exe"

if (!(Test-Path $compiler)) {
    throw "Compilador C# não encontrado: $compiler"
}

& $compiler `
    /nologo `
    /langversion:5 `
    /target:exe `
    /out:$testOutput `
    /r:System.dll `
    /r:System.Core.dll `
    (Join-Path $root "plugin\ThreadMath.cs") `
    (Join-Path $root "plugin\ThreadCatalog.cs") `
    (Join-Path $root "tests\CoreTests.cs")

if ($LASTEXITCODE -ne 0) {
    throw "Falha ao compilar os testes do núcleo."
}

& $testOutput
if ($LASTEXITCODE -ne 0) {
    throw "Os testes do núcleo falharam (código $LASTEXITCODE)."
}

Write-Host "Testes do núcleo concluídos com sucesso."
