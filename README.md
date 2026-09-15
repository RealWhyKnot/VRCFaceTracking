# VRCFaceTracking

Fork of [benaclejames/VRCFaceTracking](https://github.com/benaclejames/VRCFaceTracking), the
bridge between face and eye tracking hardware and VRChat's OSC input. Modules, parameters and
the module registry are unchanged. I made this fork because I wanted to see why it broke: every
run leaves a dated log file, a module that dies leaves its own, and none of it goes anywhere but
your disk. No Sentry.

It can also quit together with SteamVR (Settings, off by default).

Avatar setup, parameter lists and module docs: [docs.vrcft.io](https://docs.vrcft.io).

## Install

Grab the zip from the latest release, unzip it anywhere, run `VRCFaceTracking.exe`. Settings and
installed modules in `%AppData%\VRCFaceTracking` carry over from the upstream build. Releases
tagged `-beta` are prereleases built from the latest main.

## Logs

`%LocalAppData%\VRCFaceTracking\logs`, one file per run, newest 20 kept. The verbose toggle,
retention count and an "Open folder" button are in Settings under Diagnostics.

## Build from source

Needs the .NET 10 SDK and the Windows 10 SDK 22621.

```
powershell -ExecutionPolicy Bypass -File build.ps1
```

That builds Debug x64, runs the tests and turns on the repo's git hooks. `-Publish` produces the
self-contained folder under `build/publish` that releases ship. Plain
`dotnet build VRCFaceTracking.sln -p:Platform=x64` works too.

## Releases

Push a tag `vYYYY.M.D.N`, or `vYYYY.M.D.N-beta` for a prerelease; the workflow builds, tests and
publishes the GitHub release.

## Licence

Apache-2.0, same as upstream. See [LICENSE](LICENSE).
