# Reproducing the Windows build

This is a Windows x64 product repository. One checkout contains the custom SDL
source, GameSir G7 Pro telemetry service, PadForge startup hook, and launcher.
It intentionally does not support Linux or macOS builds because the telemetry
transport depends on Windows GameInput, `xboxgip.sys`, Windows services, and
named pipes.

## Toolchain

- 64-bit Windows 10 1903 or later, or Windows 11
- Git for Windows
- .NET SDK 10.0.402 (pinned by `global.json`; later 10.0 patches are accepted)
- Visual Studio Build Tools 2022 or newer with **Desktop development with C++**

CMake does not need to be installed separately when it is included with the
Visual Studio C++ workload. The build locates Visual Studio with `vswhere` and
uses its bundled CMake when `cmake.exe` is not on `PATH`.

## One-command build

From an ordinary PowerShell window in the repository root:

```powershell
.\build.ps1
```

The script performs the whole build:

1. Validates Windows x64 and locates the Visual C++ toolchain and CMake.
2. Downloads the official libusb 1.0.29 Windows SDK when absent and refuses it
   unless its SHA-256 is
   `964A38152CA9A104CD00EC8D2F0617B89CD814F9B635E29763C68563D951521D`.
3. Builds the vendored `sdl/` source as a Windows x64 shared library.
4. Builds and runs the vendored SDL controller-protocol regression suite.
5. Builds and self-tests `GameSirG7ProTelemetry.exe`.
6. Builds the startup hook and self-contained launcher.
7. Writes the runnable package, file hashes, licenses, custom SDL warning, and
   local toolchain provenance to `artifacts/win-x64/`.

No second repository, Git submodule, manually extracted SDK, Developer Command
Prompt, or PowerShell installation script is required.

The default package directory is `artifacts/win-x64`. If that package is
currently running and Windows has one of its DLLs locked, build into a fresh
directory without stopping the running PadForge instance:

```powershell
.\build.ps1 -PackageDirectory '.\artifacts\win-x64-next'
```

To additionally validate the startup hook against an official PadForge build:

```powershell
.\build.ps1 -PadForgePath 'C:\Apps\PadForge\PadForge.exe' -VerifyOfficialBuild
```

The official proprietary/application release of `PadForge.exe` is deliberately
not vendored or redistributed. At runtime, the launcher lets each user browse
to their own official PadForge installation and remembers that location.

## Continuous integration and releases

`.github/workflows/windows-build-release.yml` reproduces the same build on a
clean GitHub-hosted Windows runner. Pushes to `main` and manual runs produce a
downloadable workflow artifact. A pushed `v*` tag additionally publishes that
ZIP as a GitHub Release. CI covers compilation and hardware-free self-tests;
the physical controller checks documented in the main README still have to be
performed on a local Windows machine.

## Source layout

```text
sdl/       Customized SDL 3 source and its provenance/license
service/   Windows LocalSystem telemetry service
src/       Launcher and managed startup hook
artifacts/ Generated dependencies, intermediate builds, and package (ignored)
```

The SDL changes were created with generative-AI assistance for this private
integration. They must not be submitted to upstream SDL or used as the basis
of an upstream SDL pull request; see `sdl/GAMESIR_G7_PRO_AI_NOTICE.md`.
