#!/usr/bin/env pwsh
[CmdletBinding()]
param(
  [Parameter(Mandatory = $true)][string] $PublishDir,
  [Parameter(Mandatory = $true)][string] $Version,
  [string] $OutDir = 'build/installer',
  [string] $RepoRoot = (Get-Location).Path,
  [string] $MakeNsis = (Join-Path ${env:ProgramFiles(x86)} 'NSIS\makensis.exe')
)

$ErrorActionPreference = 'Stop'
Push-Location (Resolve-Path -LiteralPath $RepoRoot).Path
try {
  $payload = (Resolve-Path -LiteralPath $PublishDir).Path.TrimEnd('\', '/')
  foreach ($required in @('VRCFaceTracking.exe', 'VRCFaceTracking.ModuleProcess.exe', 'app.vrmanifest', 'openvr_api.dll')) {
    if (-not (Test-Path -LiteralPath (Join-Path $payload $required))) { throw "$required missing from $payload" }
  }
  if (Test-Path -LiteralPath (Join-Path $payload 'Uninstall.exe')) { throw "$payload already contains Uninstall.exe" }
  if (-not (Test-Path -LiteralPath $MakeNsis)) { throw "makensis not found at $MakeNsis (install NSIS 3 from https://nsis.sourceforge.io)" }

  New-Item -ItemType Directory -Force -Path $OutDir | Out-Null
  $out = (Resolve-Path -LiteralPath $OutDir).Path

  $lines = [System.Collections.Generic.List[string]]::new()
  foreach ($file in Get-ChildItem -LiteralPath $payload -File -Recurse -Force | Sort-Object FullName) {
    $lines.Add('Delete "$INSTDIR\' + $file.FullName.Substring($payload.Length + 1).Replace('$', '$$') + '"')
  }
  foreach ($dir in Get-ChildItem -LiteralPath $payload -Directory -Recurse -Force | Sort-Object { $_.FullName.Length } -Descending) {
    $lines.Add('RMDir "$INSTDIR\' + $dir.FullName.Substring($payload.Length + 1).Replace('$', '$$') + '"')
  }
  $fileList = Join-Path $out 'uninstall-files.nsh'
  [System.IO.File]::WriteAllLines($fileList, $lines, (New-Object System.Text.UTF8Encoding($false)))

  $numbers = @($Version -split '[^0-9]+' | Where-Object { $_ -ne '' } | Select-Object -First 4)
  while ($numbers.Count -lt 4) { $numbers += '0' }
  $viVersion = $numbers -join '.'

  $setup = Join-Path $out "VRCFaceTracking-Setup-$Version.exe"
  & $MakeNsis /V2 /WX "/DVERSION=$Version" "/DVIVERSION=$viVersion" "/DPAYLOAD=$payload" "/DFILELIST=$fileList" "/DOUTFILE=$setup" (Join-Path (Get-Location).Path 'installer/installer.nsi')
  if ($LASTEXITCODE -ne 0) { throw "makensis failed ($LASTEXITCODE)" }
  Write-Host "Built $setup ($($lines.Count) uninstall entries)"
}
finally {
  Pop-Location
}
