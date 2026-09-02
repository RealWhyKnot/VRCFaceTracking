# VRCFaceTracking

Fork of [benaclejames/VRCFaceTracking](https://github.com/benaclejames/VRCFaceTracking), the
bridge between face and eye tracking hardware and VRChat's OSC input. Same modules, same
parameters, same registry. What this fork adds is a way to see what happened when it breaks.

- One dated log file per run under `%LocalAppData%\VRCFaceTracking\logs`, with the version,
  channel, OS and process in the header. The newest 20 runs are kept (configurable).
- The module host writes its own `vrcft_module_<name>_<time>.log`, so a module that dies before it
  can talk to the app still leaves a trail. When a module process stops, the app logs the exit
  code and the last stderr lines and marks the module as crashed instead of spinning.
- Unhandled exceptions on any thread are written and flushed before the process goes down.
- No Sentry. Nothing leaves your machine.
- Optionally exits when SteamVR shuts down (Settings, off by default). The module host also
  shuts itself down within ten seconds of losing the app.

Avatar setup, parameter lists and module docs are unchanged: [docs.vrcft.io](https://docs.vrcft.io).

## Install

Grab the zip from the latest release, unzip it anywhere, run `VRCFaceTracking.exe`. It is the
same unpackaged layout the Steam build uses; settings and installed modules in
`%AppData%\VRCFaceTracking` carry over.

Releases tagged `-beta` are prereleases built from the latest main. They log verbosely by
default. Stable releases log warnings and errors only unless you turn on verbose logging in
Settings, under Diagnostics.

## Logs

Settings has a Diagnostics section with the verbose toggle, the number of log files to keep and
an "Open folder" button. Dev and beta builds keep verbose logging on and grey the toggle out.

Everything the Output page shows is also in the file, plus Debug lines when verbose is on.

## Build from source

Needs the .NET 10 SDK and the Windows 10 SDK 22621 (both come with the Visual Studio 2022
".NET desktop" and "Windows application development" workloads).

```
powershell -ExecutionPolicy Bypass -File build.ps1
```

`build.ps1` stamps a dev version like `2026.9.1.0-A1B2` into `version.txt`, builds Debug x64,
runs the unit tests and turns on the repo's git hooks. `-Channel beta` or `-Channel release`
picks the other channels, `-Configuration Release` builds Release, `-Publish` produces the
self-contained folder under `build/publish` that releases ship, `-Format` runs `dotnet format`
first. Plain `dotnet build VRCFaceTracking.sln -p:Platform=x64` works too.

The version is `YYYY.M.D.N` where N counts builds or releases on that day. Local builds append a
four-character stamp; the hooks append the same stamp to commit subjects so a log line can be
matched to the commit it came from.

## Releases

Push a tag `vYYYY.M.D.N` for a stable release or `vYYYY.M.D.N-beta` for a prerelease. The
release workflow checks that N is the next free number for that day, builds, tests, publishes,
zips, attaches a `.integrity.tsv` with the SHA-256 of the zip and every file in it, and creates the
GitHub release with grouped notes from the conventional commit subjects since the previous tag plus
the install notes from `.github/release-template`. The nightly workflow tags a beta when main moved
since the last tag and dispatches the release workflow for it; no extra secret is needed.

CI on every push formats (`dotnet format --verify-no-changes`), builds both the dev and release
channels, runs the tests and checks commit subjects against `type(scope): summary`.

## Licence

Apache-2.0, same as upstream. See [LICENSE](LICENSE).
