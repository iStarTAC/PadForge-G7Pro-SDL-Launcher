# PadForge G7 Pro launcher

> [!IMPORTANT]
> This is an unofficial community project. It is not affiliated with or
> endorsed by PadForge, GameSir, Microsoft, or the SDL project. The downloadable
> executables are not code-signed, so Windows SmartScreen may show an unknown-
> publisher warning. Verify the release hashes before running them.

This Windows-only, all-in-one source repository launches an unchanged official
PadForge release with its included custom GameSir G7 Pro SDL build. It contains
the customized SDL source, telemetry service, startup hook, launcher, and
reproducible packaging build. It uses the supported .NET startup hook and
native import resolver mechanisms instead of rewriting or injecting code into
`PadForge.exe`.

## Download

Download the compiled Windows x64 package from the
[latest GitHub release](https://github.com/iStarTAC/PadForge-G7Pro-SDL-Launcher/releases/latest).
The official `PadForge.exe` is intentionally not included; obtain it separately
from the official PadForge releases page.

The launcher is a control panel for starting PadForge, managing the
`GameSirG7ProTelemetry` service, and enabling or disabling Windows auto-start.
Starting PadForge starts an installed, stopped telemetry service. Service
installation and removal remain explicit actions in the launcher.

## Required PadForge build

Use an **official x64 `PadForge.exe`**. Download the official package from the
[PadForge releases page](https://github.com/hifihedgehog/PadForge/releases),
and extract it normally. PadForge can remain in its own folder; the launcher
asks for that folder on first use and remembers the selected executable. Do not
use the separately rebuilt PadForge executable from this workspace and do not
replace or patch the official file.

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
  and exposes explicit install, uninstall, start, stop, and restart controls.

The telemetry service and GameInput are separate parts of the data path. SDL
uses GameInput for the controller's standard buttons, sticks, triggers, POV and
rumble. The service directly opens the `\\.\XboxGIP_Admin` endpoint exposed by
Microsoft's `xboxgip.sys` driver to receive the private paddle and motion
telemetry in the background. GameSir Nexus is useful for saving the required
onboard profile, but it does not supply either Windows API and does not need to
remain running while PadForge is used.

## Why the telemetry service is necessary

A replacement `SDL3.dll` is sufficient for code and protocol changes, but it
cannot grant itself a different Windows security identity. A DLL always runs
inside its host process—in this case PadForge—with PadForge's user token,
desktop session, integrity level, and background-access restrictions. Loading
more native code into that process therefore does not make the protected
`XboxGIP_Admin` device endpoint reliably available.

That distinction matters for the non-8K G7 Pro in App Mode:

- GameInput supplies the ordinary controller state and rumble without special
  privileges, but it does not expose this firmware's four independent paddles,
  IMU stream, and battery-status message through the normal gamepad reading.
- A direct desktop XboxGIP client was able to enumerate the `3537:109C` device
  and send heartbeats, but received no telemetry during an eight-second
  background test. Earlier foreground delivery stopped after focus was lost.
- Running the same reader as LocalSystem in session 0 continued receiving the
  telemetry at approximately 62.5 Hz while PadForge or a game was in the
  foreground. Ordinary administrator elevation alone was not sufficient on the
  tested Windows configuration with Developer Mode disabled.

The service is consequently a deliberately small privilege boundary, not a
second controller driver and not a virtual controller. It performs only the
operations that require the working security context: open `XboxGIP_Admin`,
subscribe to the target GIP device, maintain its verified `0x0F / F2 00`
heartbeat, and read the GIP telemetry/status messages. The custom SDL DLL keeps
all ordinary controls and rumble on GameInput and receives only the normalized
extra data through an authenticated local named pipe. The service remains
passive until an authorized SDL client connects and stops the privileged reader
when that client disconnects or telemetry stalls.

```text
G7 Pro -> xboxgip.sys -> GameInput -----------------> SDL -> PadForge
                    \-> XboxGIP_Admin -> service --/     standard controls
                                               paddles, IMU, battery/status
```

Putting the privileged reader directly in SDL would require running the whole
interactive PadForge process as LocalSystem or finding a different supported
capability model. The former is unsafe and unsuitable for a desktop UI; the
latter has not been demonstrated for this device. A separate service keeps the
high-privilege component narrow, auditable, and independently installable.

### Why runtime USB capture was not used

USBPcap was essential during research. It captured the controller's USB
traffic and proved that the `0x10`/`0xE0` IMU and paddle reports were still
arriving at about 62.47 Hz even in a test where the corresponding Windows
Gaming Input callback count remained zero. Marked captures were also used to
identify the independent L4, R4, L5, and R5 bits. In other words, packet capture
showed that the signals existed and helped determine their wire layout.

USBPcap is a diagnostic capture layer, however, rather than an appropriate
runtime API for PadForge. Depending on it would require users to install and
operate an additional capture driver with broad visibility into USB traffic,
identify the correct host controller/device/interface after every topology or
reconnect change, reconstruct transfers, and then reproduce Xbox GIP framing
and lifecycle behavior above that capture. It would also leave output-side
requirements such as the heartbeat to a separate mechanism.

The selected runtime instead reads the logical GIP messages from Microsoft's
already-installed `xboxgip.sys` endpoint. That preserves device IDs, command,
sequence, and payload boundaries; follows the same Windows driver stack that
owns the Xbox-compatible device; avoids monitoring unrelated USB traffic; and
works for both the dongle and wired GIP identities without binding the solution
to a particular USB hub path. USBPcap remains valuable for protocol discovery
and regression investigation, while raw GIP is the smaller and more direct
production transport.

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
  LocalSystem service for the private 65-byte GIP telemetry stream and the
  controller's GIP battery-status message.
- Preserves GameInput for ordinary controls and rumble, while using the
  telemetry stream as a firmware-compatible fallback for Guide and Share.
- Exposes L4, R4, L5 and R5 as four independent SDL paddle buttons, the
  three-axis gyroscope and accelerometer, and battery level/charging state. The
  tested `109C` therefore has 16 buttons and two motion sensors rather than the
  12-button fail-closed view.
- Adds the controller IDs and paddle mappings to SDL's joystick/device tables.
  The normal `3537:100A` XInput path is deliberately unchanged, and the
  `3537:1022` native-HID path exposes only controls verified for that transport.

The hardware-tested path is the 2.4 GHz dongle in App Mode (`3537:109C`). The
wired App Mode identity (`3537:109B`) is implemented from captured evidence but
has not been hardware-validated by this project. This is not the G7 Pro 8K
vendor-HID implementation: the non-8K App Mode transport and report layout are
Xbox GIP and must not be treated as the 8K protocol.

The controller integration was developed with generative-AI assistance and is
maintained in a separate experimental fork. It must not be submitted to
upstream SDL or used as the basis of an upstream pull request. The tested SDL
source revision for the first public launcher
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
5. Close Nexus and other SDL controller test programs, open
   `LaunchPadForge.exe`, and select **Start PadForge**. Only one SDL client
   should consume this telemetry path at a time.

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

The repository builds only on 64-bit Windows. Install the .NET 10 SDK and
Visual Studio Build Tools with **Desktop development with C++**, then use one
ordinary PowerShell command from a single clone:

```powershell
git clone https://github.com/iStarTAC/PadForge-G7Pro-SDL-Launcher.git
Set-Location .\PadForge-G7Pro-SDL-Launcher
.\build.ps1
```

The build uses the custom SDL source in `sdl/`; there is no second clone or Git
submodule. It finds Visual Studio and its bundled CMake, downloads and verifies
the pinned libusb 1.0.29 SDK, compiles SDL, runs its offline controller-protocol
suite, builds and self-tests the telemetry service, builds the startup hook and
launcher, and writes the complete package to `artifacts\win-x64`.

The package also contains `SHA256SUMS.txt`, `BUILD-PROVENANCE.txt`, SDL's
license, and the private-fork/AI notice. See [BUILDING.md](BUILDING.md) for the
exact requirements, source layout, dependency hash, and optional official
PadForge hook verification command.

### GitHub builds and releases

The `Windows build and release` GitHub Actions workflow runs the same
`build.ps1` on a clean Windows x64 runner for every push to `main` and for
manual workflow runs. Successful builds are available from the workflow run as
a downloadable ZIP artifact for 30 days.

Pushing a version tag whose name begins with `v`, for example `v1.1.0`, runs
the complete build and test suite and then creates a GitHub Release containing
the Windows x64 ZIP. GitHub generates the source archives automatically. A
local/offline build remains useful for hardware testing, because hosted GitHub
runners cannot attach the physical G7 Pro or validate its live GIP telemetry.

## Install and select PadForge

Keep the complete contents of `artifacts\win-x64` together and run
`LaunchPadForge.exe`. The launcher-managed components are stored in its
`runtime` subfolder and do not need to be copied into the official PadForge
folder.

If `PadForge.exe` is not beside the launcher and no remembered location is
valid, its dedicated location row shows **Not found**. Use **Browse…** in that
row to select the folder containing it; a valid selection changes the state to
**Found** and is remembered. Then use **Install** in the telemetry service
section once and **Start PadForge**. Do not replace the official executable.

If a previous uninstall or interrupted installation left
`%ProgramFiles%\GameSirG7ProTelemetry` behind without a registered service, the
launcher removes only its known telemetry files and rebuilds the directory. It
refuses recovery when unrelated files or subdirectories are present.

The launcher sets `DOTNET_STARTUP_HOOKS` and `PADFORGE_SDL3_OVERRIDE` only in the
PadForge child process. Launching `PadForge.exe` normally still uses its bundled
SDL library.

The launcher follows the Windows app theme by default. Its theme button cycles
between **System**, **Light**, and **Dark**. The selection is stored in:

```text
%LOCALAPPDATA%\PadForgeG7ProLauncher\settings.json
```

This file stores both the theme preference and selected PadForge path.

The launcher and PadForge request administrator access, so its telemetry
service controls do not need to create a second elevation prompt.

## Start automatically with Windows

Turn off PadForge's own **Start with Windows** option. That option launches
`PadForge.exe` directly and bypasses the SDL startup hook.

Use **Install auto-start** in the launcher. The resulting scheduled task invokes
the launcher with `--autostart`, which starts PadForge without showing the
launcher window.
It registers `LaunchPadForge.exe --autostart` as an interactive logon task with
highest privileges, so PadForge starts with the SDL override without a UAC
prompt or a visible launcher window at every sign-in. Use **Uninstall
auto-start** to remove the task.

## Diagnostics

Logs are written under:

```text
%LOCALAPPDATA%\PadForgeG7ProLauncher\logs
```

Useful options:

```text
--launch            Start PadForge directly instead of opening the launcher window.
--autostart         Start PadForge without opening the launcher window or showing errors.
--padforge <path>   Select a PadForge executable outside this folder.
--dry-run           Validate files, architecture, and service without launching.
--verify-hook       Start PadForge only long enough to prove its SDL P/Invoke was redirected.
--skip-service      Do not inspect or start the telemetry service.
--wait              Keep the launcher alive until PadForge exits.
--quiet             Suppress successful validation dialogs.
--                  Forward all remaining arguments to PadForge.
```
