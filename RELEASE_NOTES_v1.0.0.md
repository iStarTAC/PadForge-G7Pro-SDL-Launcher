# PadForge G7 Pro SDL Launcher v1.0.0

Initial public Windows x64 release for the non-8K GameSir G7 Pro.

## Included

- `LaunchPadForge.exe`, which starts an unchanged official PadForge build with
  the custom SDL runtime through the .NET startup-hook/native resolver path.
- A self-contained `runtime` folder for the SDL override, startup hook, and
  telemetry-service source binary.
- The matching custom `SDL3.dll`, identifying the controller as
  `GameSir-G7 Pro (SDL)`.
- The GameSir G7 Pro telemetry service plus launcher controls for installation,
  removal, start, stop, and restart.
- Launcher controls that register or remove silent PadForge auto-start through
  Windows Task Scheduler without separate PowerShell scripts.

## Compatibility

- Tested end to end with official PadForge **5.1.0.0**.
- Hardware-tested with GameSir App Mode dongle PID `3537:109C`.
- The wired App Mode PID `3537:109B` is implemented but has not been
  hardware-validated by this project.
- Requires 64-bit Windows 10 version 1903 or later, or Windows 11, plus the
  Microsoft GameInput Redistributable.

The official `PadForge.exe` is deliberately not included. Download it from the
official PadForge releases page, then select its folder in the launcher. The
launcher and its `runtime` folder can remain separate from PadForge.

## Important notices

These community binaries are unsigned, so Windows SmartScreen may display an
unknown-publisher warning. Verify the separately supplied SHA-256 checksums
before running them. This project is unofficial and is not affiliated with or
endorsed by PadForge, GameSir, Microsoft, or the SDL project.

The corresponding SDL source is the `padforge-g7pro-v1.0.0` tag in
<https://github.com/iStarTAC/SDL-GameSirG7Pro>.
