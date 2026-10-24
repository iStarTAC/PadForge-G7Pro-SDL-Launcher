param(
    [ValidateSet('Install', 'Remove', 'Status')][string]$Action = 'Install',
    [string]$PadForgePath
)

$ErrorActionPreference = 'Stop'
$taskName = 'PadForge G7 Pro Launcher'
$launcher = Join-Path $PSScriptRoot 'LaunchPadForge.exe'
$identity = [Security.Principal.WindowsIdentity]::GetCurrent()
$principal = [Security.Principal.WindowsPrincipal]::new($identity)

if ($Action -eq 'Status') {
    $task = Get-ScheduledTask -TaskName $taskName -ErrorAction SilentlyContinue
    if ($task) {
        $task | Select-Object TaskName, State
    } else {
        Write-Host 'PadForge G7 Pro autostart is not installed.'
    }
    exit 0
}

if (!$principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) {
    throw 'Run this script from an elevated PowerShell window.'
}

if ($Action -eq 'Remove') {
    if (Get-ScheduledTask -TaskName $taskName -ErrorAction SilentlyContinue) {
        Unregister-ScheduledTask -TaskName $taskName -Confirm:$false
        Write-Host 'PadForge G7 Pro autostart removed.'
    } else {
        Write-Host 'PadForge G7 Pro autostart was not installed.'
    }
    exit 0
}

if (!(Test-Path -LiteralPath $launcher -PathType Leaf)) {
    throw "Launcher executable is missing: $launcher"
}

$arguments = $null
if ($PadForgePath) {
    $resolvedPadForge = [IO.Path]::GetFullPath($PadForgePath)
    if (!(Test-Path -LiteralPath $resolvedPadForge -PathType Leaf)) {
        throw "PadForge executable is missing: $resolvedPadForge"
    }
    $arguments = '--padforge "' + $resolvedPadForge.Replace('"', '\"') + '"'
} elseif (!(Test-Path -LiteralPath (Join-Path $PSScriptRoot 'PadForge.exe') -PathType Leaf)) {
    throw 'PadForge.exe is not beside the launcher. Supply -PadForgePath explicitly.'
}

$actionParameters = @{
    Execute = $launcher
    WorkingDirectory = $PSScriptRoot
}
if ($arguments) { $actionParameters.Argument = $arguments }
$taskAction = New-ScheduledTaskAction @actionParameters
$trigger = New-ScheduledTaskTrigger -AtLogOn -User $identity.Name
$settings = New-ScheduledTaskSettingsSet -StartWhenAvailable -AllowStartIfOnBatteries -DontStopIfGoingOnBatteries -MultipleInstances IgnoreNew
$taskPrincipal = New-ScheduledTaskPrincipal -UserId $identity.Name -LogonType Interactive -RunLevel Highest
$task = New-ScheduledTask -Action $taskAction -Trigger $trigger -Settings $settings -Principal $taskPrincipal -Description 'Starts PadForge through the G7 Pro SDL override and telemetry launcher.'
Register-ScheduledTask -TaskName $taskName -InputObject $task -Force | Out-Null
Write-Host 'PadForge G7 Pro autostart installed for the current user.'
