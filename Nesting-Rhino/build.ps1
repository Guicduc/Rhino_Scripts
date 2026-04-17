param(
    [ValidateSet(7, 8)]
    [int]$RhinoVersion = 8
)

$ErrorActionPreference = "Stop"

$root = Split-Path -Parent $MyInvocation.MyCommand.Path
$pluginName = "NestingRhino"
$rhinoSystem = "C:\Program Files\Rhino $RhinoVersion\System"
$rhinoCommon = Join-Path $rhinoSystem "RhinoCommon.dll"
$eto = Join-Path $rhinoSystem "Eto.dll"
$csc = "C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe"
$netstandard = "C:\Windows\Microsoft.NET\Framework64\v4.0.30319\netstandard.dll"
$dist = Join-Path $root "dist"
$outDllVersioned = Join-Path $dist "$pluginName-Rhino$RhinoVersion.dll"
$outRhpVersioned = Join-Path $dist "$pluginName-Rhino$RhinoVersion.rhp"
$outDll = Join-Path $dist "$pluginName.dll"
$outRhp = Join-Path $dist "$pluginName.rhp"

if (!(Test-Path $csc)) { throw "C# compiler not found at $csc" }
if (!(Test-Path $rhinoCommon)) { throw "RhinoCommon not found at $rhinoCommon" }
if (!(Test-Path $eto)) { throw "Eto not found at $eto" }
if (!(Test-Path $netstandard)) { throw "netstandard facade not found at $netstandard" }

New-Item -ItemType Directory -Force $dist | Out-Null

$sources = @(
    (Join-Path $root "plugin\AssemblyInfo.cs"),
    (Join-Path $root "plugin\NestingRhinoPlugin.cs"),
    (Join-Path $root "plugin\NestingRhinoCommand.cs"),
    (Join-Path $root "plugin\NestingRhinoCore.cs")
)

& $csc `
    /nologo `
    /target:library `
    /out:$outDllVersioned `
    /r:$rhinoCommon `
    /r:$eto `
    /r:$netstandard `
    /r:System.Core.dll `
    /r:System.Drawing.dll `
    $sources

if ($LASTEXITCODE -ne 0) {
    throw "Compilation failed with exit code $LASTEXITCODE"
}

Copy-Item $outDllVersioned $outRhpVersioned -Force
Copy-Item $outDllVersioned $outDll -Force
Copy-Item $outDllVersioned $outRhp -Force
Write-Host "Built $outRhp"
