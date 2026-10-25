# Third-party notices

The release package contains components from separate projects. Their names and
trademarks remain the property of their respective owners.

## SDL

`SDL3.dll` is built from the customized source included in this repository's
`sdl/` directory. That tree is based on the PadForge SDL fork and SDL and was
previously maintained as the SDL-GameSirG7Pro fork. SDL is distributed under
the zlib license; the complete corresponding source, license, provenance, and
private-fork warning are included here.

## libusb

The build downloads the official libusb 1.0.29 Windows SDK and links SDL to its
dynamic interface. The SDK is build-time material and is not copied into this
project's release package. PadForge supplies its own libusb runtime. libusb is
licensed under LGPL-2.1-or-later; see the
[libusb project](https://github.com/libusb/libusb).

## .NET

`LaunchPadForge.exe` is published as a self-contained .NET application and may
contain portions of the .NET runtime. .NET is licensed by Microsoft under the
MIT license. See the [.NET repository](https://github.com/dotnet/runtime) for
source and license information.

## PadForge and GameSir

PadForge is not redistributed by this project. Users must obtain the official
application separately from the
[PadForge releases page](https://github.com/hifihedgehog/PadForge/releases).

This project is unofficial and is not endorsed by or affiliated with PadForge,
GameSir, Microsoft, or the SDL project.
