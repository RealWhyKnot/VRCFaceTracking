#!/usr/bin/env pwsh
[CmdletBinding()]
param(
  [ValidateSet('dev', 'beta', 'release')][string] $Channel = 'dev',
  [Parameter(Mandatory = $true)][string] $Version,
  [string] $OutDir = 'build/publish',
  [string] $RepoRoot = (Get-Location).Path
)

$ErrorActionPreference = 'Stop'
Push-Location (Resolve-Path -LiteralPath $RepoRoot).Path
try {
  if (Test-Path -LiteralPath $OutDir) { Remove-Item -LiteralPath $OutDir -Recurse -Force }

  & dotnet publish VRCFaceTracking/VRCFaceTracking.csproj -c Release -p:Platform=x64 -r win-x64 --self-contained true `
    -p:WindowsPackageType=None -p:GenerateAppInstallerFile=false -p:AppxPackageDir= `
    "-p:VftBuildChannel=$Channel" "-p:VftVersion=$Version" -o $OutDir
  if ($LASTEXITCODE -ne 0) { throw "dotnet publish failed ($LASTEXITCODE)" }

  foreach ($required in @('VRCFaceTracking.exe', 'VRCFaceTracking.ModuleProcess.exe', 'fti_osc.dll', 'app.vrmanifest')) {
    if (-not (Test-Path -LiteralPath (Join-Path $OutDir $required))) { throw "$required missing from $OutDir" }
  }

  $exe = Get-Item -LiteralPath (Join-Path $OutDir 'VRCFaceTracking.exe')
  Write-Host "Published $Channel $Version to $OutDir (VRCFaceTracking.exe $($exe.VersionInfo.ProductVersion))"
}
finally {
  Pop-Location
}
