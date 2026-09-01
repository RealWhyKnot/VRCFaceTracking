param(
    [ValidateSet('Debug', 'Release')][string]$Configuration = 'Debug',
    [ValidateSet('x64', 'x86', 'arm64')][string]$Platform = 'x64',
    [ValidateSet('dev', 'beta', 'release')][string]$Channel = 'dev',
    [switch]$NoTest,
    [switch]$Format
)

$ErrorActionPreference = 'Stop'
Set-Location $PSScriptRoot

git config core.hooksPath .githooks | Out-Null

if ($Format) {
    & dotnet format VRCFaceTracking.sln
    if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
}

& dotnet build VRCFaceTracking.sln -c $Configuration -p:Platform=$Platform -p:VftBuildChannel=$Channel -nologo -v m
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

if (-not $NoTest) {
    & dotnet test VRCFaceTracking.Core.Tests/VRCFaceTracking.Core.Tests.csproj -c $Configuration -p:Platform=$Platform --no-build -nologo
    if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
}

$exe = Join-Path $PSScriptRoot "VRCFaceTracking\bin\$Platform\$Configuration\net10.0-windows10.0.22621.0\win-$Platform\VRCFaceTracking.exe"
Write-Host "Built $Channel $Configuration $Platform -> $exe"
