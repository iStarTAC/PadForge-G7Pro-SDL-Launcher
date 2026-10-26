# PadForge G7 Pro SDL Launcher v1.2.0

This release makes controller detection and Xbox GIP mode preparation reliable
from the interactive launcher session, while retaining the narrowly scoped
LocalSystem service for protected telemetry only.

## Changes

- Detects the active GameSir G7 Pro through the packaged SDL runtime and shows
  its live connection/mode status in the launcher.
- Refreshes controller and PadForge process status every three seconds while
  the launcher is visible, without continuous high-frequency polling.
- Sends the one-shot `gamesirapp` Xbox GIP mode-entry sequence through SDL when
  the controller is in normal `3537:100A` mode.
- Accepts existing `3537:109B`/`3537:109C` Xbox GIP mode without changing it.
- Correctly accepts companion keyboard/mouse interfaces and temporary old/new
  USB identity overlap instead of treating multiple entries as ambiguous.
- Distinguishes a disconnected controller from a controller that failed to
  transition into Xbox GIP mode.
- Prevents duplicate PadForge launches while the application is starting or
  already running.
- Adds the launcher version, a GitHub project link, and an **Open logs** shortcut
  to the launcher window.
- Fixes control-pipe asynchronous timeouts and request serialization, with a
  local named-pipe protocol regression suite included in the build.
- Updates GitHub Actions dependencies to Node.js 24-compatible releases.

## Compatibility

- Tested with official PadForge **5.1.0.0**.
- Hardware-tested with the GameSir G7 Pro 2.4 GHz dongle.
- Requires 64-bit Windows 10 version 1903 or later, or Windows 11, plus the
  Microsoft GameInput Redistributable.

The official `PadForge.exe` is not included. Download it separately from the
official PadForge releases page and select its folder in the launcher.

## Important notice

These community binaries are unsigned, so Windows SmartScreen may display an
unknown-publisher warning. Verify the included SHA-256 checksums before running
them. This project is unofficial and is not affiliated with or endorsed by
PadForge, GameSir, Microsoft, or the SDL project.
