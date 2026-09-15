# VRCFaceTracking

Fork of [benaclejames/VRCFaceTracking](https://github.com/benaclejames/VRCFaceTracking) with some
tweaks. Docs for avatars, parameters and modules: [docs.vrcft.io](https://docs.vrcft.io).

## Install

Grab the zip from the latest release, unzip it anywhere, run `VRCFaceTracking.exe`. Settings and
installed modules in `%AppData%\VRCFaceTracking` carry over from the upstream build.

## Build

Needs the .NET 10 SDK and the Windows 10 SDK 22621.

```
powershell -ExecutionPolicy Bypass -File build.ps1
```

Apache-2.0, same as upstream. See [LICENSE](LICENSE).
