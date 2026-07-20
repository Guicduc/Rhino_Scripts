param(
    [ValidateSet(7, 8)]
    [int]$RhinoVersion = 7,
    [switch]$RunTests
)

$ErrorActionPreference = "Stop"

$root = Split-Path -Parent $MyInvocation.MyCommand.Path
$pluginName = "ThreadRhino"
$rhinoSystem = "C:\Program Files\Rhino $RhinoVersion\System"
$rhinoCommon = Join-Path $rhinoSystem "RhinoCommon.dll"
$rhinoUi = Join-Path $rhinoSystem "Rhino.UI.dll"
$eto = Join-Path $rhinoSystem "Eto.dll"
$compiler = "C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe"
$netstandard = "C:\Windows\Microsoft.NET\Framework64\v4.0.30319\netstandard.dll"
$dist = Join-Path $root "dist"
$versionedRhp = Join-Path $dist "$pluginName-Rhino$RhinoVersion.rhp"
$buildOutput = Join-Path ([System.IO.Path]::GetTempPath()) "$pluginName-Rhino$RhinoVersion-$PID.rhp"

foreach ($required in @($compiler, $rhinoCommon, $rhinoUi, $eto, $netstandard)) {
    if (!(Test-Path $required)) {
        throw "Dependência não encontrada: $required"
    }
}

New-Item -ItemType Directory -Force $dist | Out-Null

$sources = @(
    "AssemblyInfo.cs",
    "ThreadRhinoPlugin.cs",
    "ThreadMath.cs",
    "ThreadCatalog.cs",
    "ThreadDefinitions.cs",
    "ThreadFeatureUserData.cs",
    "ThreadAnalysis.cs",
    "ThreadGeometry.cs",
    "ThreadPreviewConduit.cs",
    "ThreadDialog.cs",
    "ThreadCommands.cs",
    "ThreadSelfTestCommand.cs"
) | ForEach-Object { Join-Path $root "plugin\$_" }

& $compiler `
    /nologo `
    /langversion:5 `
    /target:library `
    /out:$buildOutput `
    /r:$rhinoCommon `
    /r:$rhinoUi `
    /r:$eto `
    /r:$netstandard `
    /r:System.dll `
    /r:System.Core.dll `
    /r:System.Drawing.dll `
    $sources

if ($LASTEXITCODE -ne 0) {
    Remove-Item -LiteralPath $buildOutput -Force -ErrorAction SilentlyContinue
    throw "Falha na compilação para Rhino $RhinoVersion (código $LASTEXITCODE)."
}

try {
    Copy-Item $buildOutput $versionedRhp -Force
}
catch [System.IO.IOException] {
    throw "Não foi possível atualizar $versionedRhp porque o plugin está carregado. Salve o documento, feche o Rhino $RhinoVersion e execute o build novamente."
}
finally {
    Remove-Item -LiteralPath $buildOutput -Force -ErrorAction SilentlyContinue
}

if ($RunTests) {
    & (Join-Path $root "test.ps1")
}

Write-Host "Criado: $versionedRhp"
