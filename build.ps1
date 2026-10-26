param(
    [string]$PadForgePath = '',
    [string]$PackageDirectory = '',
    [switch]$SkipSdlBuild,
    [switch]$SkipProtocolTests,
    [switch]$SkipTelemetryBuild,
    [switch]$VerifyOfficialBuild
)

$ErrorActionPreference = 'Stop'

if ([System.Environment]::OSVersion.Platform -ne [System.PlatformID]::Win32NT) {
    throw 'This repository and its GameSir G7 Pro telemetry integration build only on Windows.'
}
if (![System.Environment]::Is64BitOperatingSystem) {
    throw 'A 64-bit Windows installation is required.'
}

$projectRoot = $PSScriptRoot
$configuration = 'Release'
$rid = 'win-x64'
$artifactRoot = Join-Path $projectRoot 'artifacts'
if ([string]::IsNullOrWhiteSpace($PackageDirectory)) {
    $artifactDirectory = Join-Path $artifactRoot 'win-x64'
} else {
    $artifactDirectory = [IO.Path]::GetFullPath($PackageDirectory, $projectRoot)
}
$runtimeDirectory = Join-Path $artifactDirectory 'runtime'
$publishDirectory = Join-Path $artifactRoot 'launcher-publish'
$serviceBuildScript = Join-Path $projectRoot 'service\G7ProTelemetryService\build.ps1'
$serviceBuildDirectory = Join-Path $artifactRoot 'service-build'
$hookProject = Join-Path $projectRoot 'src\PadForge.G7Pro.StartupHook\PadForge.G7Pro.StartupHook.csproj'
$launcherProject = Join-Path $projectRoot 'src\PadForge.G7Pro.Launcher\PadForge.G7Pro.Launcher.csproj'
$launcherProtocolTests = Join-Path $projectRoot 'src\PadForge.G7Pro.Launcher.ProtocolTests\PadForge.G7Pro.Launcher.ProtocolTests.csproj'
$telemetry = Join-Path $serviceBuildDirectory 'GameSirG7ProTelemetry.exe'

# SDL and its only build-time SDK dependency are deliberately owned by this
# repository. A clean checkout therefore does not need a sibling SDL clone.
$sdlSource = Join-Path $projectRoot 'sdl'
$sdlBuildDirectory = Join-Path $artifactRoot 'sdl-build'
$customSdl = Join-Path $sdlBuildDirectory 'Release\SDL3.dll'
$libusbVersion = '1.0.29'
$libusbSha256 = '964A38152CA9A104CD00EC8D2F0617B89CD814F9B635E29763C68563D951521D'
$dependencyDirectory = Join-Path $artifactRoot "dependencies\libusb-$libusbVersion"
$libusbArchive = Join-Path $dependencyDirectory "libusb-$libusbVersion.7z"
$libusbSdk = Join-Path $dependencyDirectory 'sdk'
$libusbHeader = Join-Path $libusbSdk 'include\libusb.h'
$libusbLibrary = Join-Path $libusbSdk 'VS2022\MS64\dll\libusb-1.0.lib'

$vswhere = Join-Path ${env:ProgramFiles(x86)} 'Microsoft Visual Studio\Installer\vswhere.exe'
if (!(Test-Path -LiteralPath $vswhere -PathType Leaf)) {
    throw 'Visual Studio Build Tools were not found. Install the Desktop development with C++ workload.'
}
$vsPath = & $vswhere -latest -products * -requires Microsoft.VisualStudio.Component.VC.Tools.x86.x64 -property installationPath
if (!$vsPath) {
    throw 'Visual C++ x64 build tools were not found. Install the Desktop development with C++ workload.'
}

$cmakeCommand = Get-Command cmake.exe -ErrorAction SilentlyContinue
if ($cmakeCommand) {
    $cmake = $cmakeCommand.Source
} else {
    $cmake = Join-Path $vsPath 'Common7\IDE\CommonExtensions\Microsoft\CMake\CMake\bin\cmake.exe'
}
if (!(Test-Path -LiteralPath $cmake -PathType Leaf)) {
    throw 'CMake was not found on PATH or in the selected Visual Studio installation.'
}

if (!$SkipSdlBuild) {
    if (!(Test-Path -LiteralPath $libusbHeader -PathType Leaf) -or
        !(Test-Path -LiteralPath $libusbLibrary -PathType Leaf)) {
        New-Item -ItemType Directory -Path $dependencyDirectory -Force | Out-Null
        if (!(Test-Path -LiteralPath $libusbArchive -PathType Leaf)) {
            $libusbUrl = "https://github.com/libusb/libusb/releases/download/v$libusbVersion/libusb-$libusbVersion.7z"
            Write-Host "Downloading pinned libusb $libusbVersion SDK..."
            Invoke-WebRequest -Uri $libusbUrl -OutFile $libusbArchive
        }

        $actualHash = (Get-FileHash -LiteralPath $libusbArchive -Algorithm SHA256).Hash
        if ($actualHash -ne $libusbSha256) {
            throw "libusb archive hash mismatch. Expected $libusbSha256, received $actualHash."
        }

        New-Item -ItemType Directory -Path $libusbSdk -Force | Out-Null
        & tar.exe -xf $libusbArchive -C $libusbSdk
        if ($LASTEXITCODE -ne 0) { throw 'Could not extract the pinned libusb SDK.' }
    }

    Write-Host 'Configuring the vendored Windows x64 SDL build...'
    & $cmake -S $sdlSource -B $sdlBuildDirectory -A x64 `
        -DLibUSB_INCLUDE_PATH="$libusbSdk\include" `
        -DLibUSB_LIBRARY="$libusbLibrary" `
        -DSDL_SHARED=ON `
        -DSDL_STATIC=OFF `
        -DSDL_HIDAPI_LIBUSB=ON `
        -DSDL_HIDAPI_LIBUSB_SHARED=ON `
        -DSDL_VENDOR_INFO=PadForge-G7Pro-private-55c6604a `
        -DSDL_TEST_LIBRARY=OFF `
        -DSDL_TESTS=OFF `
        -DSDL_EXAMPLES=OFF `
        -DSDL_INSTALL=OFF
    if ($LASTEXITCODE -ne 0) { throw 'Custom SDL configuration failed.' }

    & $cmake --build $sdlBuildDirectory --config $configuration --target SDL3-shared --parallel
    if ($LASTEXITCODE -ne 0) { throw 'Custom SDL build failed.' }
}

if (!(Test-Path -LiteralPath $customSdl -PathType Leaf)) {
    throw "Custom SDL3.dll is missing: $customSdl. Run without -SkipSdlBuild first."
}

if (!$SkipProtocolTests) {
    $protocolSource = Join-Path $sdlSource 'test\controller-protocols'
    $protocolBuild = Join-Path $artifactRoot 'protocol-tests'
    $ctest = Join-Path (Split-Path -Parent $cmake) 'ctest.exe'
    if (!(Test-Path -LiteralPath $ctest -PathType Leaf)) {
        throw "CTest was not found beside CMake: $ctest"
    }

    & $cmake -S $protocolSource -B $protocolBuild -A x64
    if ($LASTEXITCODE -ne 0) { throw 'SDL controller protocol test configuration failed.' }
    & $cmake --build $protocolBuild --config $configuration --parallel
    if ($LASTEXITCODE -ne 0) { throw 'SDL controller protocol test build failed.' }
    & $ctest --test-dir $protocolBuild -C $configuration --output-on-failure
    if ($LASTEXITCODE -ne 0) { throw 'SDL controller protocol tests failed.' }
}

if (!$SkipTelemetryBuild) {
    & $serviceBuildScript -OutputDirectory $serviceBuildDirectory
    if ($LASTEXITCODE -ne 0) { throw 'Telemetry service build failed.' }
}

if (!(Test-Path -LiteralPath $telemetry -PathType Leaf)) {
    throw "Telemetry service is missing: $telemetry"
}
if ($VerifyOfficialBuild -and !(Test-Path -LiteralPath $PadForgePath -PathType Leaf)) {
    throw "Official PadForge executable is missing: $PadForgePath"
}

dotnet build $hookProject -c $configuration
if ($LASTEXITCODE -ne 0) { throw 'Startup hook build failed.' }
dotnet run --project $launcherProtocolTests -c $configuration
if ($LASTEXITCODE -ne 0) { throw 'Launcher telemetry control protocol tests failed.' }
dotnet publish $launcherProject -c $configuration -r $rid -o $publishDirectory
if ($LASTEXITCODE -ne 0) { throw 'Launcher publish failed.' }

New-Item -ItemType Directory -Path $artifactDirectory -Force | Out-Null
New-Item -ItemType Directory -Path $runtimeDirectory -Force | Out-Null
New-Item -ItemType Directory -Path (Join-Path $artifactDirectory 'licenses') -Force | Out-Null
Copy-Item -LiteralPath (Join-Path $publishDirectory 'LaunchPadForge.exe') -Destination $artifactDirectory -Force
Copy-Item -LiteralPath (Join-Path $projectRoot 'src\PadForge.G7Pro.StartupHook\bin\Release\net10.0\PadForge.G7Pro.StartupHook.dll') -Destination $runtimeDirectory -Force
Copy-Item -LiteralPath $customSdl -Destination (Join-Path $runtimeDirectory 'SDL3.dll') -Force
Copy-Item -LiteralPath $telemetry -Destination $runtimeDirectory -Force
Copy-Item -LiteralPath (Join-Path $projectRoot 'README.md') -Destination $artifactDirectory -Force
Copy-Item -LiteralPath (Join-Path $projectRoot 'LICENSE.txt') -Destination $artifactDirectory -Force
Copy-Item -LiteralPath (Join-Path $projectRoot 'THIRD_PARTY_NOTICES.md') -Destination $artifactDirectory -Force
Copy-Item -LiteralPath (Join-Path $sdlSource 'LICENSE.txt') -Destination (Join-Path $artifactDirectory 'licenses\SDL-LICENSE.txt') -Force
Copy-Item -LiteralPath (Join-Path $sdlSource 'GAMESIR_G7_PRO_AI_NOTICE.md') -Destination (Join-Path $artifactDirectory 'CUSTOM_SDL_NOTICE.md') -Force

# Remove files left by package layouts predating the runtime subdirectory.
Remove-Item -LiteralPath (Join-Path $artifactDirectory 'PadForge.G7Pro.StartupHook.dll') -Force -ErrorAction SilentlyContinue
Remove-Item -LiteralPath (Join-Path $artifactDirectory 'SDL3.dll') -Force -ErrorAction SilentlyContinue
Remove-Item -LiteralPath (Join-Path $artifactDirectory 'GameSirG7ProTelemetry.exe') -Force -ErrorAction SilentlyContinue
Remove-Item -LiteralPath (Join-Path $artifactDirectory 'install-service.ps1') -Force -ErrorAction SilentlyContinue
Remove-Item -LiteralPath (Join-Path $artifactDirectory 'install-autostart.ps1') -Force -ErrorAction SilentlyContinue

$hashTargets = @(
    (Join-Path $artifactDirectory 'LaunchPadForge.exe'),
    (Join-Path $runtimeDirectory 'PadForge.G7Pro.StartupHook.dll'),
    (Join-Path $runtimeDirectory 'SDL3.dll'),
    (Join-Path $runtimeDirectory 'GameSirG7ProTelemetry.exe')
)
$hashLines = foreach ($file in $hashTargets) {
    $relative = [IO.Path]::GetRelativePath($artifactDirectory, $file).Replace('\', '/')
    '{0}  {1}' -f (Get-FileHash -LiteralPath $file -Algorithm SHA256).Hash, $relative
}
Set-Content -LiteralPath (Join-Path $artifactDirectory 'SHA256SUMS.txt') -Value $hashLines -Encoding ascii

$notice = Get-Content -LiteralPath (Join-Path $sdlSource 'GAMESIR_G7_PRO_AI_NOTICE.md') -Raw
$baseMatch = [regex]::Match($notice, 'later private maintenance changes built on commit\s+`([0-9a-f]{8,40})`')
$sdlBaseCommit = if ($baseMatch.Success) { $baseMatch.Groups[1].Value } else { 'unknown' }
$provenance = @(
    'PadForge G7 Pro Windows build provenance',
    "Built: $([DateTimeOffset]::Now.ToString('O'))",
    "OS: $([System.Environment]::OSVersion.VersionString)",
    "Architecture: $([System.Runtime.InteropServices.RuntimeInformation]::OSArchitecture)",
    "Dotnet SDK: $(dotnet --version)",
    "CMake: $((& $cmake --version | Select-Object -First 1))",
    "Visual Studio: $vsPath",
    "SDL base commit: $sdlBaseCommit",
    "libusb SDK: $libusbVersion ($libusbSha256)"
)
Set-Content -LiteralPath (Join-Path $artifactDirectory 'BUILD-PROVENANCE.txt') -Value $provenance -Encoding utf8

Write-Host "Complete Windows x64 package: $artifactDirectory"
Write-Host "Vendored custom SDL: $customSdl"
if (![string]::IsNullOrWhiteSpace($PadForgePath)) {
    Write-Host "Official PadForge: $PadForgePath"
}

if ($VerifyOfficialBuild) {
    $verification = Start-Process -FilePath (Join-Path $artifactDirectory 'LaunchPadForge.exe') `
        -ArgumentList @('--padforge', $PadForgePath, '--verify-hook', '--skip-service', '--quiet') -Wait -PassThru
    if ($verification.ExitCode -ne 0) { throw 'Official PadForge hook verification failed.' }
}
