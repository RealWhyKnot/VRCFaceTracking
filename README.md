# VRCFaceTracking

My fork of [benaclejames/VRCFaceTracking](https://github.com/benaclejames/VRCFaceTracking). Docs for avatars, parameters and modules are at [docs.vrcft.io](https://docs.vrcft.io).

It runs on Windows and Linux, and the macOS builds are experimental. You can run several tracking modules at once and limit each one to the data it should provide (eyes, brows, mouth, tongue or head). For debugging I added a live view of the module camera feeds, under the developer section of the settings page.

## Install

Builds for all three are on the [Releases](https://github.com/RealWhyKnot/VRCFaceTracking/releases) page.

### Windows

Run `VRCFaceTracking-Setup-<version>.exe` from the latest release. It installs for your account only, without an admin prompt. You get a Start menu entry, a SteamVR registration and an entry under Settings, Apps for uninstalling.

To skip the installer, extract the `win-x64` zip anywhere and run `VRCFaceTracking.exe`. Settings and installed modules in `%AppData%\VRCFaceTracking` carry over from the upstream build.

### Linux

Extract the archive and run `./VRCFaceTracking`. Minimal distros also need `libice6` and `libsm6`. Most tracking modules only come with Windows binaries. Check that yours runs on Linux before you count on it.

### macOS

Extract the archive and run `./VRCFaceTracking`. If Gatekeeper blocks it, run `xattr -dr com.apple.quarantine <extracted folder>`. SteamVR features don't work on macOS.

## Build

You need the .NET 10 SDK.

```
powershell -ExecutionPolicy Bypass -File build.ps1
```

Add `-Installer` to also publish win-x64 and build the setup into `build\installer`. That needs NSIS 3.

## License

Apache-2.0, like upstream. See [LICENSE](LICENSE).
