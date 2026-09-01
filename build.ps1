#Requires -Version 5.1
[CmdletBinding()]
param(
	[ValidateSet("dev", "beta", "release")]
	[string]$Channel = "dev",
	[string]$Version = "",
	[ValidateSet("Debug", "Release")]
	[string]$Configuration = "Debug",
	[switch]$SkipTests,
	[switch]$Publish,
	[switch]$Format
)

$ErrorActionPreference = "Stop"
Set-Location $PSScriptRoot

$hooksPath = & git config --get core.hooksPath 2>$null
$global:LASTEXITCODE = 0
if ([string]::IsNullOrWhiteSpace($hooksPath) -and (Test-Path -LiteralPath (Join-Path $PSScriptRoot ".githooks"))) {
	& git config core.hooksPath .githooks
	Write-Host "git hooks enabled (.githooks)"
}

if ([string]::IsNullOrWhiteSpace($Version)) {
	$stateDir = Join-Path $PSScriptRoot "build"
	New-Item -ItemType Directory -Force -Path $stateDir | Out-Null
	$statePath = Join-Path $stateDir "local_build_state.json"
	$today = (Get-Date).ToString("yyyy.M.d")
	$counter = 0
	if (Test-Path -LiteralPath $statePath) {
		$state = Get-Content -LiteralPath $statePath -Raw | ConvertFrom-Json
		if ($state.date -eq $today) { $counter = [int]$state.counter + 1 }
	}
	$suffix = ([guid]::NewGuid().ToString("N").Substring(0, 4)).ToUpperInvariant()
	$Version = "$today.$counter-$suffix"
	$stateJson = @{ date = $today; counter = $counter } | ConvertTo-Json -Compress
	[System.IO.File]::WriteAllText($statePath, $stateJson, (New-Object System.Text.UTF8Encoding($false)))
}
[System.IO.File]::WriteAllText((Join-Path $PSScriptRoot "version.txt"), $Version, (New-Object System.Text.UTF8Encoding($false)))
Write-Host "build $Version ($Channel, $Configuration)"

if ($Format) {
	& dotnet format VRCFaceTracking.sln
	if ($LASTEXITCODE -ne 0) { throw "dotnet format failed (exit $LASTEXITCODE)" }
}

& dotnet build VRCFaceTracking.sln -c $Configuration -p:Platform=x64 "-p:VftBuildChannel=$Channel" "-p:VftVersion=$Version" -nologo -v m
if ($LASTEXITCODE -ne 0) { throw "build failed (exit $LASTEXITCODE)" }

if (-not $SkipTests) {
	& dotnet test VRCFaceTracking.Core.Tests/VRCFaceTracking.Core.Tests.csproj -c $Configuration -p:Platform=x64 --no-build -nologo
	if ($LASTEXITCODE -ne 0) { throw "tests failed (exit $LASTEXITCODE)" }
}

if ($Publish) {
	& (Join-Path $PSScriptRoot ".github/scripts/Publish-App.ps1") -Channel $Channel -Version $Version -OutDir "build/publish"
	if ($LASTEXITCODE -ne 0) { throw "publish failed (exit $LASTEXITCODE)" }
} else {
	$exe = Join-Path $PSScriptRoot "VRCFaceTracking\bin\x64\$Configuration\net10.0-windows10.0.22621.0\win-x64\VRCFaceTracking.exe"
	Write-Host "built $exe"
}
