# GameSir G7 Pro SDL telemetry service

This is a telemetry-only LocalSystem service for the non-8K GameSir G7 Pro in
Xbox GIP App Mode (`3537:109B` wired or `3537:109C` dongle). It contains no
virtual-controller, HIDMaestro, HidHide, profile-management, or App Mode entry
code.

## Runtime prerequisites

- 64-bit Windows 10 version 1903 or later, or Windows 11. Windows 11 is the
  hardware-tested platform.
- A connected GameSir G7 Pro already enumerated in Xbox GIP App Mode. The tested
  identity is the 2.4 GHz dongle at `3537:109C`.
- Microsoft's Xbox GIP device driver (`xboxgip.sys`) installed and active.
  Windows normally supplies it automatically through Plug and Play/Windows
  Update when the Xbox-compatible controller is connected; users do not need a
  separate XboxGIP SDK or API download.
- Administrator access to install the service as LocalSystem.

The service directly opens the `\\.\XboxGIP_Admin` device endpoint exposed by
`xboxgip.sys`; it does not use the GameInput API. The companion SDL build does
use GameInput v3 for standard controls, so the complete PadForge launcher
package also requires the official
[Microsoft GameInput Redistributable](https://learn.microsoft.com/en-us/gaming/gdk/docs/features/common/input/overviews/input-nuget).
Microsoft makes it available through `winget install Microsoft.GameInput` from
an elevated terminal.

The service starts automatically with Windows but remains passive: it creates
only its authenticated control pipe. It opens `XboxGIP_Admin`, subscribes, and
sends the verified `0F/F2 00` heartbeat only while one authorized SDL client is
connected. The reader stops when that client closes or telemetry stalls.

## Build and hardware-free validation

```powershell
.\tools\G7ProTelemetryService\build.ps1
.\tools\G7ProTelemetryService\install-service.ps1 -ValidateOnly
```

## Installation

Installation requires one explicitly approved elevated PowerShell invocation.
It creates the automatic LocalSystem service `GameSirG7ProTelemetry`, protects
its files under Program Files, and authorizes only the installing Windows user
plus administrators and SYSTEM. Building and validation do not install or start
anything.

```powershell
.\tools\G7ProTelemetryService\install-service.ps1
```

The service is registered but not started by the installer. Start it once or
reboot. SDL then connects automatically when it opens a matching App Mode
controller. Removal deletes only the service registration and retains protected
files for audit:

```powershell
.\tools\G7ProTelemetryService\install-service.ps1 -Action Remove
```

The initial protocol intentionally permits only one telemetry-owning process.
Additional SDL processes retain ordinary GameInput controls but do not receive
paddles or motion until the owner disconnects.

## Live validation

The dongle identity (`3537:109C`) was tested end to end on 24 October 2026 with
the service installed as automatic LocalSystem and the modified SDL GameInput
backend. SDL reported the standard six axes, one hat, rumble, trigger rumble,
Share at button 11, the four independent paddles at buttons 12 through 15, and
live gyroscope and accelerometer data. After SDL closed, the data pipe and GIP
reader stopped while the service's control pipe remained available, confirming
the intended passive-idle behavior.

That validation used an elevated probe because the Codex test shell itself runs
at Low Mandatory Integrity. A normal medium-integrity desktop process is the
intended client and should still be verified separately. The wired `3537:109B`
identity also remains untested on hardware.
