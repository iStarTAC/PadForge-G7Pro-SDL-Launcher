param(
    [string]$OutputDirectory = ''
)

$ErrorActionPreference = 'Stop'
$projectRoot = (Resolve-Path (Join-Path $PSScriptRoot '..\..')).Path
$source = Join-Path $projectRoot 'service\DirectGipReader\Program.cpp'
if ([string]::IsNullOrWhiteSpace($OutputDirectory)) {
    $OutputDirectory = Join-Path $projectRoot 'artifacts\service-build'
}
$output = Join-Path $OutputDirectory 'GameSirG7ProTelemetry.exe'
$object = Join-Path $OutputDirectory 'GameSirG7ProTelemetry.obj'
$vswhere = Join-Path ${env:ProgramFiles(x86)} 'Microsoft Visual Studio\Installer\vswhere.exe'
if (!(Test-Path -LiteralPath $vswhere -PathType Leaf)) {
    throw 'Visual Studio Installer (vswhere.exe) was not found.'
}
$vsPath = & $vswhere -latest -products * -requires Microsoft.VisualStudio.Component.VC.Tools.x86.x64 -property installationPath
if (!$vsPath) { throw 'Visual C++ x64 build tools were not found.' }
$devCmd = Join-Path $vsPath 'Common7\Tools\VsDevCmd.bat'
New-Item -ItemType Directory -Path $OutputDirectory -Force | Out-Null
& cmd.exe /d /s /c "call `"$devCmd`" -arch=x64 -host_arch=x64 >nul && cl /nologo /std:c++20 /O2 /EHsc /W4 /DUNICODE /D_UNICODE /DG7PRO_TELEMETRY_SERVICE `"$source`" /Fe:`"$output`" /Fo:`"$object`""
if ($LASTEXITCODE -ne 0) { throw 'GameSir G7 Pro telemetry service build failed.' }
& $output --control-self-test
if ($LASTEXITCODE -ne 0) { throw 'Control protocol self-test failed.' }
& $output --frame-self-test
if ($LASTEXITCODE -ne 0) { throw 'Telemetry frame self-test failed.' }
Write-Host "Xbox GIP preparation and telemetry service built and verified: $output"
