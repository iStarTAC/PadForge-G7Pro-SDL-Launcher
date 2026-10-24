param(
    [string]$SdlPath = '',
    [string]$PadForgePath = '',
    [switch]$SkipTelemetryBuild,
    [switch]$VerifyOfficialBuild
)

$ErrorActionPreference = 'Stop'
$projectRoot = $PSScriptRoot
$configuration = 'Release'
$rid = 'win-x64'
$artifactDirectory = Join-Path $projectRoot 'artifacts\win-x64'
$publishDirectory = Join-Path $projectRoot 'artifacts\launcher-publish'
$serviceBuildScript = Join-Path $projectRoot 'service\G7ProTelemetryService\build.ps1'
$serviceBuildDirectory = Join-Path $projectRoot 'artifacts\service-build'
$hookProject = Join-Path $projectRoot 'src\PadForge.G7Pro.StartupHook\PadForge.G7Pro.StartupHook.csproj'
$launcherProject = Join-Path $projectRoot 'src\PadForge.G7Pro.Launcher\PadForge.G7Pro.Launcher.csproj'
$telemetry = Join-Path $serviceBuildDirectory 'GameSirG7ProTelemetry.exe'

if ([string]::IsNullOrWhiteSpace($SdlPath)) {
    $sdlCandidates = @(
        (Join-Path $projectRoot '..\SDL-GameSirG7Pro\build-padforge-g7\Release\SDL3.dll'),
        (Join-Path $projectRoot '..\PadForge-SDL\build-padforge-g7\Release\SDL3.dll')
    )
    $SdlPath = $sdlCandidates | Where-Object { Test-Path -LiteralPath $_ -PathType Leaf } | Select-Object -First 1
}
if ([string]::IsNullOrWhiteSpace($SdlPath)) {
    throw 'Custom SDL3.dll was not found. Build the public SDL fork, then pass -SdlPath <path>.'
}
$customSdl = (Resolve-Path -LiteralPath $SdlPath).Path

if (!$SkipTelemetryBuild) {
    & $serviceBuildScript -OutputDirectory $serviceBuildDirectory
    if ($LASTEXITCODE -ne 0) { throw 'Telemetry service build failed.' }
}

foreach ($required in @($customSdl, $telemetry)) {
    if (!(Test-Path -LiteralPath $required -PathType Leaf)) {
        throw "Required file is missing: $required"
    }
}
if ($VerifyOfficialBuild -and !(Test-Path -LiteralPath $PadForgePath -PathType Leaf)) {
    throw "Official PadForge executable is missing: $PadForgePath"
}

dotnet build $hookProject -c $configuration
if ($LASTEXITCODE -ne 0) { throw 'Startup hook build failed.' }
dotnet publish $launcherProject -c $configuration -r $rid -o $publishDirectory
if ($LASTEXITCODE -ne 0) { throw 'Launcher publish failed.' }

New-Item -ItemType Directory -Path $artifactDirectory -Force | Out-Null
Copy-Item -LiteralPath (Join-Path $publishDirectory 'LaunchPadForge.exe') -Destination $artifactDirectory -Force
Copy-Item -LiteralPath (Join-Path $projectRoot 'src\PadForge.G7Pro.StartupHook\bin\Release\net10.0\PadForge.G7Pro.StartupHook.dll') -Destination $artifactDirectory -Force
Copy-Item -LiteralPath $customSdl -Destination (Join-Path $artifactDirectory 'SDL3.dll') -Force
Copy-Item -LiteralPath $telemetry -Destination $artifactDirectory -Force
Copy-Item -LiteralPath (Join-Path $projectRoot 'packaging\install-service.ps1') -Destination $artifactDirectory -Force
Copy-Item -LiteralPath (Join-Path $projectRoot 'packaging\install-autostart.ps1') -Destination $artifactDirectory -Force
Copy-Item -LiteralPath (Join-Path $projectRoot 'README.md') -Destination $artifactDirectory -Force
Copy-Item -LiteralPath (Join-Path $projectRoot 'LICENSE.txt') -Destination $artifactDirectory -Force
Copy-Item -LiteralPath (Join-Path $projectRoot 'THIRD_PARTY_NOTICES.md') -Destination $artifactDirectory -Force

Write-Host "Launcher package: $artifactDirectory"
Write-Host "Custom SDL: $customSdl"
if (![string]::IsNullOrWhiteSpace($PadForgePath)) {
    Write-Host "Official PadForge: $PadForgePath"
}

if ($VerifyOfficialBuild) {
    if ([string]::IsNullOrWhiteSpace($PadForgePath)) {
        throw '-PadForgePath is required with -VerifyOfficialBuild.'
    }
    $verification = Start-Process -FilePath (Join-Path $artifactDirectory 'LaunchPadForge.exe') `
        -ArgumentList @('--padforge', $PadForgePath, '--verify-hook', '--quiet') -Wait -PassThru
    if ($verification.ExitCode -ne 0) { throw 'Official PadForge hook verification failed.' }
}
