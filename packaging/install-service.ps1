param(
    [ValidateSet('Install', 'Remove', 'Repair')][string]$Action = 'Install'
)

$ErrorActionPreference = 'Stop'
$serviceName = 'GameSirG7ProTelemetry'
$source = Join-Path $PSScriptRoot 'GameSirG7ProTelemetry.exe'
$installDirectory = Join-Path $env:ProgramFiles 'GameSirG7ProTelemetry'
$installed = Join-Path $installDirectory 'GameSirG7ProTelemetry.exe'
$sidFile = Join-Path $installDirectory 'client.sid'
$identity = [Security.Principal.WindowsIdentity]::GetCurrent()
$ownerSid = $identity.User.Value
$principal = [Security.Principal.WindowsPrincipal]::new($identity)
$expectedCommand = '"' + $installed + '" --service'

if (!$principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) {
    throw 'Telemetry service installation and removal require an elevated launcher.'
}

if ($Action -eq 'Remove') {
    $configuration = Get-CimInstance Win32_Service -Filter "Name='$serviceName'"
    if (!$configuration) {
        Write-Host 'Telemetry service is not installed.'
        exit 0
    }
    if ($configuration.PathName -ne $expectedCommand -or $configuration.StartName -ne 'LocalSystem') {
        throw 'Unexpected service configuration. Refusing removal.'
    }
    Stop-Service -Name $serviceName -ErrorAction SilentlyContinue
    & sc.exe delete $serviceName
    if ($LASTEXITCODE -ne 0) { throw 'Service removal failed.' }
    Write-Host "Service registration removed. Installed files remain at $installDirectory."
    exit 0
}

if ($Action -eq 'Repair') {
    if (!(Test-Path -LiteralPath $source -PathType Leaf)) {
        throw "Telemetry executable is missing: $source"
    }
    & $source --control-self-test
    if ($LASTEXITCODE -ne 0) { throw 'Control protocol self-test failed.' }
    & $source --frame-self-test
    if ($LASTEXITCODE -ne 0) { throw 'Telemetry frame self-test failed.' }

    $configuration = Get-CimInstance Win32_Service -Filter "Name='$serviceName'"
    if (!$configuration -or $configuration.PathName -ne $expectedCommand -or $configuration.StartName -ne 'LocalSystem') {
        throw 'Unexpected service configuration. Refusing repair.'
    }

    Stop-Service -Name $serviceName -ErrorAction Stop
    $previousHash = (Get-FileHash -LiteralPath $installed -Algorithm SHA256).Hash.Substring(0, 12)
    $backup = Join-Path $installDirectory "GameSirG7ProTelemetry.exe.previous-$previousHash"
    if (!(Test-Path -LiteralPath $backup)) {
        Copy-Item -LiteralPath $installed -Destination $backup
    }
    Copy-Item -LiteralPath $source -Destination $installed -Force
    if ((Get-FileHash -LiteralPath $source).Hash -ne (Get-FileHash -LiteralPath $installed).Hash) {
        throw 'Repaired executable hash mismatch.'
    }
    Start-Service -Name $serviceName
    Write-Host 'Telemetry service binary repaired and restarted.'
    exit 0
}

if (Get-Service -Name $serviceName -ErrorAction SilentlyContinue) {
    Write-Host 'Telemetry service is already installed.'
    exit 0
}
if (!(Test-Path -LiteralPath $source -PathType Leaf)) {
    throw "Telemetry executable is missing: $source"
}
& $source --control-self-test
if ($LASTEXITCODE -ne 0) { throw 'Control protocol self-test failed.' }
& $source --frame-self-test
if ($LASTEXITCODE -ne 0) { throw 'Telemetry frame self-test failed.' }
if (Test-Path -LiteralPath $installDirectory) {
    throw "Installation directory already exists without a registered service: $installDirectory"
}

$security = [Security.AccessControl.DirectorySecurity]::new()
$security.SetSecurityDescriptorSddlForm("O:BAG:BAD:P(A;OICI;FA;;;SY)(A;OICI;FA;;;BA)(A;OICI;GRGX;;;$ownerSid)")
[IO.Directory]::CreateDirectory($installDirectory, $security) | Out-Null
try {
    Copy-Item -LiteralPath $source -Destination $installed
    if ((Get-FileHash -LiteralPath $source).Hash -ne (Get-FileHash -LiteralPath $installed).Hash) {
        throw 'Installed executable hash mismatch.'
    }
    [IO.File]::WriteAllText($sidFile, $ownerSid, [Text.Encoding]::ASCII)
    New-Service -Name $serviceName -BinaryPathName $expectedCommand -DisplayName 'GameSir G7 Pro SDL telemetry' -StartupType Automatic | Out-Null
    & sc.exe sdset $serviceName "D:(A;;GA;;;SY)(A;;GA;;;BA)(A;;LCRPWP;;;$ownerSid)"
    if ($LASTEXITCODE -ne 0) { throw 'Service-management ACL configuration failed.' }
    Start-Service -Name $serviceName
    Write-Host 'Telemetry service installed and started.'
} catch {
    if (Get-Service -Name $serviceName -ErrorAction SilentlyContinue) {
        & sc.exe delete $serviceName | Out-Null
    }
    throw
}
