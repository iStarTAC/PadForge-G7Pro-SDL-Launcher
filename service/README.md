# GameSir G7 Pro SDL mode and telemetry service

This is a narrow LocalSystem broker for the non-8K GameSir G7 Pro. On an
explicit authenticated launcher request, it accepts an existing healthy Xbox
GIP mode device (`3537:109B` wired or `3537:109C` dongle), or makes one guarded
attempt to transition a healthy `3537:100A` dongle to `3537:109C`. While SDL is
connected, it supplies the private GIP telemetry stream. It contains no virtual
controller, HIDMaestro, HidHide, profile-management, firmware-writing, reset,
or automatic mode-entry retry code.

## Runtime prerequisites

- 64-bit Windows 10 version 1903 or later, or Windows 11. Windows 11 is the
  hardware-tested platform.
- A connected GameSir G7 Pro at `3537:100A`, or already enumerated in Xbox GIP
  mode. The tested Xbox GIP identity is the 2.4 GHz dongle at `3537:109C`.
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
only its authenticated control pipe. It inspects or changes controller mode
only when the launcher sends the fixed `prepare` request. If Xbox GIP mode is
already healthy, that request performs no output. It sends the fixed
`gamesirapp` XInput sequence only for exactly one healthy `3537:100A` dongle and
exactly one connected XInput controller, waits for three consecutive healthy
`3537:109C` checks, and never retries automatically. It opens `XboxGIP_Admin`,
subscribes, and sends the verified `0F/F2 00` heartbeat only while one authorized
SDL client is connected. The reader stops when that client closes or telemetry
stalls.

## Build and hardware-free validation

```powershell
.\service\G7ProTelemetryService\build.ps1
```

The launcher runs the service's control-protocol and frame self-tests before
installation.

## Installation

Open `LaunchPadForge.exe` and use **Install** under **Xbox GIP service**. The
launcher creates the automatic LocalSystem service `GameSirG7ProTelemetry`,
protects its files under Program Files, authorizes the installing Windows user
plus administrators and SYSTEM, and starts it. No PowerShell script is needed.

The same launcher section can uninstall, start, stop, or restart the service.
Uninstall removes the service registration and the files installed by the
launcher.

The protocol intentionally permits only one telemetry-owning process.
Additional SDL processes retain ordinary GameInput controls but do not receive
paddles or motion until the owner disconnects.

## Live validation

The dongle identity (`3537:109C`) was tested end to end on 24 October 2026 with
the service installed as automatic LocalSystem and the modified SDL GameInput
backend. SDL reported the standard six axes, one hat, rumble, trigger rumble,
Guide and Share, the four independent paddles at raw joystick buttons 12
through 15, live gyroscope and accelerometer data, and GIP battery status. A focused follow-up on
25 October 2026 distinguished Guide (`button[5]`) from physical RB
(`button[10]`) in SDL's internal GameInput layout. After SDL closed, the data pipe and GIP
reader stopped while the service's control pipe remained available, confirming
the intended passive-idle behavior.

That validation used an elevated probe because the Codex test shell itself runs
at Low Mandatory Integrity. A normal medium-integrity desktop process is the
intended client and should still be verified separately. The wired `3537:109B`
identity also remains untested on hardware.
