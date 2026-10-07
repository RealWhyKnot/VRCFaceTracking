# VRCFaceTracking

Fork of [benaclejames/VRCFaceTracking](https://github.com/benaclejames/VRCFaceTracking) with some
tweaks. Docs for avatars, parameters and modules: [docs.vrcft.io](https://docs.vrcft.io).

Runs on Windows and Linux; macOS builds are experimental. You can run several tracking modules at
once: each module can be limited to the data it should provide (eyes, brows, mouth, tongue, head).
I also added a live view of the module camera feeds for debugging, under the developer section of the settings page.

## Install

- Windows: run `VRCFaceTracking-Setup-<version>.exe` from the latest release. It installs for your
  account only (no admin prompt), adds a Start menu entry, registers with SteamVR and shows up under
  Settings, Apps for uninstalling. To skip the installer, extract the `win-x64` zip anywhere and run
  `VRCFaceTracking.exe`. Settings and installed modules in `%AppData%\VRCFaceTracking` carry over
  from the upstream build.
- Linux: extract the archive and run `./VRCFaceTracking`. Minimal distros also need `libice6` and
  `libsm6`. Most tracking modules only ship Windows binaries, so check your module works on Linux
  before relying on it.
- macOS: extract the archive and run `./VRCFaceTracking`. If Gatekeeper complains, run
  `xattr -dr com.apple.quarantine <extracted folder>`. SteamVR features are unavailable on macOS.

## Build

Needs the .NET 10 SDK.

```
powershell -ExecutionPolicy Bypass -File build.ps1
```

Add `-Installer` to also publish win-x64 and build the setup into `build\installer` (needs NSIS 3).

Apache-2.0, same as upstream. See [LICENSE](LICENSE).
