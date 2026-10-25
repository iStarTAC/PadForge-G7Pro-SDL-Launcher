# GameSir G7 Pro private integration

> [!WARNING]
> This integration was written with generative AI assistance for private
> research and testing. It is not an upstream SDL contribution and must not be
> submitted to libsdl-org/SDL, hifihedgehog/SDL, or used as the basis of an
> upstream pull request.

This branch starts from `hifihedgehog/SDL` branch
`feat/hidmaestro-filter` at commit `77cf0379` so that it remains binary
compatible with PadForge's custom SDL build. It adds the non-8K GameSir G7 Pro
identities and consumes privileged paddle and IMU telemetry from the companion
`GameSirG7ProTelemetry` Windows service.

The branch also contains later private maintenance changes built on commit
`55c6604a` (`padforge-g7pro-v1.0.0`). These changes may be redistributed under
SDL's zlib license as part of this private fork, but their commit history and
code must remain outside upstream SDL pull requests.

Supported identities:

- `3537:1022`: standard HID mode
- `3537:109B`: wired App identity, routed through GameInput
- `3537:109C`: 2.4 GHz App identity, routed through GameInput

The service remains a separate Windows process but its source is maintained in
this same all-in-one repository. SDL authenticates the named
pipe server as the installed LocalSystem service, keeps the ordinary GameInput
controls and rumble path, and adds Guide/Share fallback state, four SDL paddle
buttons, gyro and accelerometer sensors, plus the controller's GIP battery
status. The service source and launcher controls are under `service/` and
`src/` at the repository root.

This vendored tree intentionally has a Windows-only CMake guard. It exists to
build the SDL3 DLL used by the Windows-only PadForge integration, not to replace
or redistribute SDL as a general cross-platform development branch.

## Build SDL3.dll on Windows x64

Requirements:

- Visual Studio Build Tools 2022 or newer with Desktop development with C++
- CMake
- The official libusb 1.0.29 Windows SDK

The repository root `build.ps1` downloads and verifies the pinned libusb SDK,
then configures this directory and builds the DLL. The standalone commands
below are retained only for low-level SDL development:

```powershell
$cmake = "C:\Program Files (x86)\Microsoft Visual Studio\18\BuildTools\Common7\IDE\CommonExtensions\Microsoft\CMake\CMake\bin\cmake.exe"
$libusb = "C:\path\to\libusb-1.0.29"

& $cmake -S . -B build-padforge-g7 `
  -DLibUSB_INCLUDE_PATH="$libusb\include" `
  -DLibUSB_LIBRARY="$libusb\VS2022\MS64\dll\libusb-1.0.lib" `
  -DSDL_HIDAPI_LIBUSB=ON `
  -DSDL_HIDAPI_LIBUSB_SHARED=ON `
  -DSDL_TEST_LIBRARY=OFF `
  -DSDL_TESTS=OFF

& $cmake --build build-padforge-g7 --config Release --target SDL3-shared --parallel
```

The result is `build-padforge-g7\Release\SDL3.dll`. Keep
`libusb-1.0.dll` beside the consuming program; PadForge already bundles the
matching x64 DLL.

## Build the protocol regression test

```powershell
& $cmake -S test/controller-protocols -B build-controller-protocols
& $cmake --build build-controller-protocols --config Release
& .\build-controller-protocols\Release\testprotocolghl.exe
```

The expected result for this revision is `PASSED: 6690 checks, 0 failures`.

## Runtime requirements

Install and start `GameSirG7ProTelemetry` as an automatic LocalSystem service,
connect the controller, and enable SDL's GameInput backend before SDL joystick
initialization:

```c
SDL_SetHint(SDL_HINT_JOYSTICK_GAMEINPUT, "1");
```

If an application must receive controller events while unfocused, it should
also enable `SDL_HINT_JOYSTICK_ALLOW_BACKGROUND_EVENTS`.
