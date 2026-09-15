#!/usr/bin/env pwsh
[CmdletBinding()]
param(
  [ValidateSet('dev', 'beta', 'release')][string] $Channel = 'dev',
  [Parameter(Mandatory = $true)][string] $Version,
  [Parameter(Mandatory = $true)][string] $Rid,
  [string] $OutDir = 'build/publish',
  [string] $RepoRoot = (Get-Location).Path
)

$ErrorActionPreference = 'Stop'
Push-Location (Resolve-Path -LiteralPath $RepoRoot).Path
try {
  if (Test-Path -LiteralPath $OutDir) { Remove-Item -LiteralPath $OutDir -Recurse -Force }

  & dotnet publish VRCFaceTracking/VRCFaceTracking.csproj -c Release -r $Rid --self-contained true `
    "-p:VftBuildChannel=$Channel" "-p:VftVersion=$Version" -o $OutDir
  if ($LASTEXITCODE -ne 0) { throw "dotnet publish failed ($LASTEXITCODE)" }

  & dotnet publish VRCFaceTracking.ModuleProcess/VRCFaceTracking.ModuleProcess.csproj -c Release -r $Rid --self-contained true `
    "-p:VftBuildChannel=$Channel" "-p:VftVersion=$Version" -o $OutDir
  if ($LASTEXITCODE -ne 0) { throw "dotnet publish (ModuleProcess) failed ($LASTEXITCODE)" }

  $requiredFiles = switch -Wildcard ($Rid) {
    'win-*' { @('VRCFaceTracking.exe', 'VRCFaceTracking.ModuleProcess.exe', 'app.vrmanifest', 'openvr_api.dll') }
    'linux-*' { @('VRCFaceTracking', 'VRCFaceTracking.ModuleProcess', 'app.vrmanifest', 'libopenvr_api.so') }
    'osx-*' { @('VRCFaceTracking', 'VRCFaceTracking.ModuleProcess') }
    default { throw "Unknown RID '$Rid'" }
  }
  foreach ($required in $requiredFiles) {
    if (-not (Test-Path -LiteralPath (Join-Path $OutDir $required))) { throw "$required missing from $OutDir" }
  }

  if ($Rid -like 'win-*') {
    $exe = Get-Item -LiteralPath (Join-Path $OutDir 'VRCFaceTracking.exe')
    Write-Host "Published $Channel $Version ($Rid) to $OutDir (VRCFaceTracking.exe $($exe.VersionInfo.ProductVersion))"
  } else {
    Write-Host "Published $Channel $Version ($Rid) to $OutDir"
  }
}
finally {
  Pop-Location
}
