# PadForge G7 Pro launcher

> [!IMPORTANT]
> This is an unofficial community project. It is not affiliated with or
> endorsed by PadForge, GameSir, Microsoft, or the SDL project. The downloadable
> executables are not code-signed, so Windows SmartScreen may show an unknown-
> publisher warning. Verify the release hashes before running them.

This project launches an unchanged official PadForge release with the existing
GameSir G7 Pro build of the PadForge SDL fork. It uses the supported .NET startup
hook and native import resolver mechanisms instead of rewriting or injecting
code into `PadForge.exe`.

## Download

Download the compiled Windows x64 package from the
[latest GitHub release](https://github.com/iStarTAC/PadForge-G7Pro-SDL-Launcher/releases/latest).
The official `PadForge.exe` is intentionally not included; obtain it separately
from the official PadForge releases page.

The launcher also checks the `GameSirG7ProTelemetry` service. If it is installed
but stopped, the launcher starts it. If it is absent, the packaged installer
validates and installs the telemetry executable once, then starts the service.
If an installed service has an older binary, its configuration is validated and
the binary is repaired before PadForge starts. The replaced executable is kept
in the service directory as `GameSirG7ProTelemetry.exe.previous-<hash>`.

## Required PadForge build

Use an **official x64 `PadForge.exe`**. Download the official package from the
[PadForge releases page](https://github.com/hifihedgehog/PadForge/releases),
extract it normally, and place this launcher's package files beside the official
executable. Do not use the separately rebuilt PadForge executable from this
workspace and do not replace or patch the official file.

The launcher was tested end to end on 24 October 2026 with the official
`PadForge.exe` whose Windows file and product version is **5.1.0.0**. That file
has SHA-256
`45749ADCE87969C76885B789B87BFE81E6E372BC3D7F85CB3CEF1F00BD4C689F`.
The injected SDL fork reports SDL **3.5.0**. A later official PadForge build may
still work, but run `LaunchPadForge.exe --verify-hook` after updating PadForge
to confirm that its SDL import is still redirected.

## Windows prerequisites

- 64-bit Windows 10 version 1903 (19H1) or later, or Windows 11. Windows 11 is
  the hardware-tested platform.
- The GameSir G7 Pro and its dongle must be recognized by Windows as an
  Xbox-compatible controller. Windows normally installs Microsoft's Xbox GIP
  device driver (`xboxgip.sys`) automatically through Plug and Play/Windows
  Update. There is no separate XboxGIP SDK or API package for users to install.
- Install or update the official
  [Microsoft GameInput Redistributable](https://learn.microsoft.com/en-us/gaming/gdk/docs/features/common/input/overviews/input-nuget)
  because this SDL build uses the GameInput v3 interface for standard controls.
  Modern Windows includes an inbox GameInput runtime, but Microsoft directs PC
  applications using GameInput v1 or later to install the redistributable. From
  an elevated terminal it can be provisioned with:

  ```powershell
  winget install Microsoft.GameInput
  ```

- Administrator access is required once to install the packaged
  `GameSirG7ProTelemetry` LocalSystem service. The launcher requests elevation
  and performs this installation automatically when the service is absent.

The telemetry service and GameInput are separate parts of the data path. SDL
uses GameInput for the controller's standard buttons, sticks, triggers, POV and
rumble. The service directly opens the `\\.\XboxGIP_Admin` endpoint exposed by
Microsoft's `xboxgip.sys` driver to receive the private paddle and motion
telemetry in the background. GameSir Nexus is useful for saving the required
onboard profile, but it does not supply either Windows API and does not need to
remain running while PadForge is used.

If the service reports that `XboxGIP_Admin` is unavailable, reconnect the
controller/dongle, confirm the `3537:109C` device is present, apply Windows and
optional driver updates, and reboot. Do not download an `xboxgip.sys` file from
a third-party driver site.

## What is forked in SDL

This package does not fork PadForge. It supplies a custom `SDL3.dll` built from
the PadForge SDL fork, with a focused non-8K GameSir G7 Pro integration:

- Adds the verified G7 Pro identities `3537:1022` (native HID), `3537:109B`
  (wired App Mode), and `3537:109C` (2.4 GHz dongle App Mode).
- Extends SDL's Windows **GameInput v3** joystick backend for the `109B`/`109C`
  Xbox GIP App Mode path. Normal sticks, triggers, buttons, POV and rumble remain
  GameInput data.
- Connects that backend to the authenticated `GameSirG7ProTelemetry`
  LocalSystem service for the private 65-byte GIP telemetry stream.
- Exposes L4, R4, L5 and R5 as four independent SDL paddle buttons and exposes
  the three-axis gyroscope and accelerometer. The tested `109C` therefore has
  16 buttons and two motion sensors rather than the 12-button fail-closed view.
- Adds the controller IDs and paddle mappings to SDL's joystick/device tables.
  The normal `3537:100A` XInput path is deliberately unchanged, and the
  `3537:1022` native-HID path exposes only controls verified for that transport.

The hardware-tested path is the 2.4 GHz dongle in App Mode (`3537:109C`). The
wired App Mode identity (`3537:109B`) is implemented from captured evidence but
has not been hardware-validated by this project. This is not the G7 Pro 8K
vendor-HID implementation: the non-8K App Mode transport and report layout are
Xbox GIP and must not be treated as the 8K protocol.

The controller integration was developed with generative-AI assistance and is
maintained in a separate experimental fork. It is not intended for submission
to upstream SDL. The tested SDL source revision for the first public launcher
release is `55c6604a1` on the fork's `gamesir-g7-pro-padforge` branch.

## GameSir G7 Pro setup

The preferred configuration is a profile-persisted App Mode setup:

1. In GameSir Nexus, select the **G7 PRO XBOX** controller and use an onboard
   profile.
2. Leave **Motion / X-Axis Output Mode** set to **Off**. SDL reads the raw IMU;
   Nexus must not convert gyro motion into stick input.
3. Keep L4, R4, L5 and R5 independent (`L4 -> L4`, `R4 -> R4`, `L5 -> L5`,
   `R5 -> R5`). Do not turn them into macros or remap them to ordinary buttons.
4. Save the profile to the controller, then power-cycle or reconnect the
   controller and dongle. The saved profile should enumerate directly as
   `3537:109C`; the launcher does not send the unreliable App Mode transition
   sequence automatically.
5. Close Nexus and other SDL controller test programs, then start PadForge with
   `LaunchPadForge.exe`. Only one SDL client should consume this telemetry path
   at a time.

The Nexus report-rate selection is not what enables the private SDL telemetry
path. The tested controller was shown at 250 Hz, while its App Mode telemetry
stream was independently observed at about 62.5 Hz.

## Which PadForge device to select

Select the row named **GameSir-G7 Pro (SDL)** with the tested identity
**`3537:109C`**. A fully working service-backed device should show:

```text
GameSir-G7 Pro (SDL)
GAMEPAD • 3537:109C • 6 AXES, 16 BUTTONS, 1 POV(S), RUMBLE, GYRO, ACCEL
```

That is the correct G7 Pro source in the supplied screenshot. Do not select the
`045E:02FF` **Xbox One Controller** or `045E:0B13` **Xbox Series X Controller**
rows for this integration. Those are separate Xbox-compatible device entries.

The `109B` and `109C` devices are owned by SDL's GameInput backend. Windows
reports the generic product name **Xbox Gaming Device**, so this controller-
specific SDL fork overrides only those two App Mode identities with
**GameSir-G7 Pro (SDL)**. Other GameInput devices retain their Windows names.
The native-HID `3537:1022` backend uses **GameSir-G7 Pro** without the `(SDL)`
suffix because it is a different transport. Windows `joy.cpl` may still show
**Controller (Xbox One For Windows)**: the override changes SDL's friendly name
inside PadForge, not the underlying Windows device or driver name.

Use the VID:PID and capabilities—not the friendly name—as the definitive check:

- `3537:109C` + 16 buttons + `GYRO, ACCEL`: custom SDL and telemetry are working.
- `3537:109C` + 12 buttons and no sensors: App Mode is present, but the telemetry
  service/client connection is not active.
- `3537:100A`: normal XInput mode; the four independent paddles and raw motion
  are unavailable through this launcher path.

## Build

Building from source requires the .NET 10 SDK, Visual Studio Build Tools with
the x64 C++ workload, CMake, Git, and the separately maintained
[SDL-GameSirG7Pro fork](https://github.com/iStarTAC/SDL-GameSirG7Pro).

Clone both repositories beside each other and build the SDL fork first:

```powershell
git clone --branch gamesir-g7-pro-padforge https://github.com/iStarTAC/SDL-GameSirG7Pro.git
git clone https://github.com/iStarTAC/PadForge-G7Pro-SDL-Launcher.git
cmake -S .\SDL-GameSirG7Pro -B .\SDL-GameSirG7Pro\build-padforge-g7 -A x64 -DSDL_SHARED=ON -DSDL_STATIC=OFF -DSDL_TESTS=OFF
cmake --build .\SDL-GameSirG7Pro\build-padforge-g7 --config Release --target SDL3-shared
```

Then build the telemetry service, startup hook, launcher, and package:

```powershell
.\PadForge-G7Pro-SDL-Launcher\build.ps1 -SdlPath '.\SDL-GameSirG7Pro\build-padforge-g7\Release\SDL3.dll'
```

The package is written to `artifacts\win-x64`. To verify the result against a
local official PadForge installation during the build:

```powershell
.\PadForge-G7Pro-SDL-Launcher\build.ps1 `
  -SdlPath '.\SDL-GameSirG7Pro\build-padforge-g7\Release\SDL3.dll' `
  -PadForgePath 'C:\Apps\PadForge\PadForge.exe' `
  -VerifyOfficialBuild
```

The SDL source remains in the separate PadForge SDL fork and is not changed by
this launcher project.

## Install beside PadForge

Copy the contents of `artifacts\win-x64` beside the official `PadForge.exe`, then
run `LaunchPadForge.exe`. Do not replace the official executable.

The launcher sets `DOTNET_STARTUP_HOOKS` and `PADFORGE_SDL3_OVERRIDE` only in the
PadForge child process. Launching `PadForge.exe` normally still uses its bundled
SDL library.

The launcher and PadForge request administrator access. The telemetry service
installer therefore does not need to create a second elevation prompt.

## Start automatically with Windows

Turn off PadForge's own **Start with Windows** option. That option launches
`PadForge.exe` directly and bypasses the SDL startup hook.

Instead, open an elevated PowerShell window in the package directory and run:

```powershell
.\install-autostart.ps1
```

This registers `LaunchPadForge.exe` as an interactive logon task with highest
privileges, so the normal launcher, SDL override, and telemetry checks all run
without a UAC prompt at every sign-in. If PadForge is not beside the launcher:

```powershell
.\install-autostart.ps1 -PadForgePath 'C:\Apps\PadForge\PadForge.exe'
```

Inspect or remove it with:

```powershell
.\install-autostart.ps1 -Action Status
.\install-autostart.ps1 -Action Remove
```

## Diagnostics

Logs are written under:

```text
%LOCALAPPDATA%\PadForgeG7ProLauncher\logs
```

Useful options:

```text
--padforge <path>  Test or launch a PadForge executable outside this folder.
--dry-run          Validate files, architecture, and service without launching.
--verify-hook      Start PadForge only long enough to prove its SDL P/Invoke was redirected.
--skip-service     Do not inspect, install, or start the telemetry service.
--wait             Keep the launcher alive until PadForge exits.
--quiet            Suppress successful validation dialogs.
--                 Forward all remaining arguments to PadForge.
```
