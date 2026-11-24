# Contributing

I maintain this fork on my own. I'm picky about the codebase and its style, so please open an issue before sending a PR. Unsolicited PRs will likely be closed, which wastes your time.

If you want to help regularly, open an issue first and we can agree on workflow and style before any code is written. I'm happy to work with people who stick around.

## Building and testing

You need the .NET 10 SDK. `global.json` pins 10.0.203 and rolls forward to newer feature bands.

To build and run the three test projects:

```
powershell -ExecutionPolicy Bypass -File build.ps1
```

`build.ps1` also takes `-Configuration Release`, `-SkipTests`, `-Format` and `-Installer`. To run the steps by hand:

```
dotnet restore VRCFaceTracking.slnx -p:Platform=x64
dotnet build VRCFaceTracking.slnx -c Release -p:Platform=x64
dotnet test tests/VRCFaceTracking.Core.Tests/VRCFaceTracking.Core.Tests.csproj -c Release -p:Platform=x64 --no-build
dotnet test tests/VRCFaceTracking.Tests/VRCFaceTracking.Tests.csproj -c Release -p:Platform=x64 --no-build
dotnet test tests/VRCFaceTracking.Perf.Tests/VRCFaceTracking.Perf.Tests.csproj -c Release -p:Platform=x64 --no-build
```

On Linux and macOS, leave out `-p:Platform=x64`.

CI also checks formatting with `dotnet format VRCFaceTracking.slnx --verify-no-changes`, so run `dotnet format VRCFaceTracking.slnx` before you commit. Warnings are treated as errors.

The repo has git hooks in `.githooks`. `build.ps1` turns them on for you, or run `git config core.hooksPath .githooks`. The pre-push hook runs the restore, format check, build, tests and style checks before every push.

Bugs and questions are welcome as issues. Security problems go through [the security policy](.github/SECURITY.md).

Please keep references to the original VRCFaceTracking as plain text. Don't link to its repository, mention its authors with `@`, or reference its issue and PR numbers here, since that cross-links and notifies people who have nothing to do with this fork.
