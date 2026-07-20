param(
    [ValidateSet(7, 8)]
    [int]$RhinoVersion = 7
)

$ErrorActionPreference = "Stop"

$root = Split-Path -Parent $MyInvocation.MyCommand.Path
$rhinoExe = "C:\Program Files\Rhino $RhinoVersion\System\Rhino.exe"
$plugin = Join-Path $root "dist\ThreadRhino-Rhino$RhinoVersion.rhp"
$log = Join-Path $env:TEMP "ThreadRhino-SelfTest-Rhino$RhinoVersion.log"

if (!(Test-Path $rhinoExe)) { throw "Rhino não encontrado: $rhinoExe" }
if (!(Test-Path $plugin)) { throw "Compile o plugin antes do teste: $plugin" }

Remove-Item -LiteralPath $log -Force -ErrorAction SilentlyContinue
$env:THREADRHINO_SELFTEST_LOG = $log
$macro = "_-LoadPlugIn `"$plugin`" _Enter _RhinoThreadSelfTest _-Exit _Enter"
$existingRhinoIds = @(Get-Process Rhino -ErrorAction SilentlyContinue | Select-Object -ExpandProperty Id)

try {
    & $rhinoExe /nosplash /notemplate "/runscript=$macro"

    $deadline = [DateTime]::UtcNow.AddSeconds(90)
    while ([DateTime]::UtcNow -lt $deadline -and !(Test-Path -LiteralPath $log)) {
        Start-Sleep -Milliseconds 500
    }

    if (!(Test-Path -LiteralPath $log)) {
        throw "O Rhino não produziu o relatório. Feche sessões abertas, confirme a licença/inicialização normal do Rhino e tente novamente."
    }

    $lines = Get-Content -LiteralPath $log
    $lines | ForEach-Object { Write-Host $_ }
    if ($lines.Count -eq 0 -or $lines[0] -ne "PASS") {
        throw "Os testes geométricos falharam no Rhino $RhinoVersion."
    }
}
finally {
    $testProcesses = Get-Process Rhino -ErrorAction SilentlyContinue | Where-Object { $existingRhinoIds -notcontains $_.Id }
    foreach ($testProcess in $testProcesses) {
        Stop-Process -Id $testProcess.Id -Force -ErrorAction SilentlyContinue
    }
    Remove-Item Env:\THREADRHINO_SELFTEST_LOG -ErrorAction SilentlyContinue
}

Write-Host "Testes geométricos do Rhino $RhinoVersion concluídos com sucesso."
