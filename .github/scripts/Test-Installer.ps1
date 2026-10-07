#!/usr/bin/env pwsh
[CmdletBinding()]
param(
  [Parameter(Mandatory = $true)][string] $Setup,
  [Parameter(Mandatory = $true)][string] $PublishDir,
  [Parameter(Mandatory = $true)][string] $Version
)

$ErrorActionPreference = 'Stop'
$script:failures = @()

function Assert([bool] $Condition, [string] $Message) {
  if ($Condition) {
    Write-Host "ok   $Message"
  }
  else {
    $script:failures += $Message
    Write-Host "FAIL $Message"
  }
}

function Invoke-Exe([string] $Path, [string] $Arguments, [int] $TimeoutSeconds = 180) {
  $process = Start-Process -FilePath $Path -ArgumentList $Arguments -PassThru
  $null = $process.Handle
  if (-not $process.WaitForExit($TimeoutSeconds * 1000)) {
    Stop-Process -Id $process.Id -Force
    return 'timeout'
  }
  return $process.ExitCode
}

function Get-RegisteredManifests {
  if (-not (Test-Path -LiteralPath $vrPaths)) { return @() }
  $registered = @()
  foreach ($config in @((Get-Content -LiteralPath $vrPaths -Raw | ConvertFrom-Json).config)) {
    $appConfig = Join-Path $config 'appconfig.json'
    if (Test-Path -LiteralPath $appConfig) {
      $registered += @((Get-Content -LiteralPath $appConfig -Raw | ConvertFrom-Json).manifest_paths)
    }
  }
  return $registered
}

function Test-Registered([string] $Manifest) {
  return @(Get-RegisteredManifests | Where-Object { $_ -ieq $Manifest }).Count -gt 0
}

function Get-RelativeFiles([string] $Root) {
  $full = (Resolve-Path -LiteralPath $Root).Path.TrimEnd('\', '/')
  return @(Get-ChildItem -LiteralPath $full -File -Recurse -Force | ForEach-Object { $_.FullName.Substring($full.Length + 1) } | Sort-Object)
}

function Assert-Installed([string] $Dir) {
  $expected = Get-RelativeFiles $payload
  $missing = @()
  if (Test-Path -LiteralPath $Dir) {
    $actual = Get-RelativeFiles $Dir
    $missing = @($expected | Where-Object { $actual -notcontains $_ })
  }
  else {
    $missing = $expected
  }
  Assert ($missing.Count -eq 0) "all $($expected.Count) payload files installed in $Dir (missing: $($missing -join ', '))"
  Assert (Test-Path -LiteralPath (Join-Path $Dir 'Uninstall.exe')) "uninstaller written to $Dir"

  $arp = Get-ItemProperty -Path $arpPath
  Assert ($arp.DisplayName -eq 'VRCFaceTracking') "DisplayName (got $($arp.DisplayName))"
  Assert ($arp.DisplayVersion -eq $Version) "DisplayVersion $Version (got $($arp.DisplayVersion))"
  Assert ($arp.Publisher -eq 'RealWhyKnot') "Publisher (got $($arp.Publisher))"
  Assert ($arp.InstallLocation -eq $Dir) "InstallLocation $Dir (got $($arp.InstallLocation))"
  Assert ($arp.DisplayIcon -eq (Join-Path $Dir 'VRCFaceTracking.exe')) "DisplayIcon (got $($arp.DisplayIcon))"
  Assert ($arp.UninstallString -eq ('"' + (Join-Path $Dir 'Uninstall.exe') + '"')) "UninstallString (got $($arp.UninstallString))"
  Assert ($arp.QuietUninstallString -eq ('"' + (Join-Path $Dir 'Uninstall.exe') + '" /S')) "QuietUninstallString (got $($arp.QuietUninstallString))"
  Assert ($arp.NoModify -eq 1 -and $arp.NoRepair -eq 1) "NoModify and NoRepair"
  Assert ($arp.EstimatedSize -gt 0) "EstimatedSize (got $($arp.EstimatedSize))"

  $target = ''
  if (Test-Path -LiteralPath $shortcut) { $target = (New-Object -ComObject WScript.Shell).CreateShortcut($shortcut).TargetPath }
  Assert ($target -eq (Join-Path $Dir 'VRCFaceTracking.exe')) "start menu shortcut targets $Dir (got '$target')"

  $manifest = Join-Path $Dir 'app.vrmanifest'
  if ($steamVr) {
    Assert (Test-Registered $manifest) "SteamVR has $manifest registered"
  }
  else {
    $code = Invoke-Exe (Join-Path $Dir 'VRCFaceTracking.exe') '--register-steamvr' 60
    Assert ("$code" -eq '3') "--register-steamvr exits 3 without SteamVR instead of opening the app (got $code)"
  }
}

$setupPath = (Resolve-Path -LiteralPath $Setup).Path
$payload = (Resolve-Path -LiteralPath $PublishDir).Path.TrimEnd('\', '/')
$arpPath = 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Uninstall\VRCFaceTracking'
$shortcut = Join-Path $env:APPDATA 'Microsoft\Windows\Start Menu\Programs\VRCFaceTracking.lnk'
$dataDir = Join-Path $env:APPDATA 'VRCFaceTracking'
$localDir = Join-Path $env:LOCALAPPDATA 'VRCFaceTracking'
$vrPaths = Join-Path $env:LOCALAPPDATA 'openvr\openvrpaths.vrpath'
$root = Join-Path $env:TEMP 'VRCFT Setup Smoke'
$first = Join-Path $root 'first'
$second = Join-Path $root 'second'
$rival = Join-Path $root 'rival'

$appKey = @((Get-Content -LiteralPath (Join-Path $payload 'app.vrmanifest') -Raw | ConvertFrom-Json).applications)[0].app_key
$steamVr = Test-Path -LiteralPath $vrPaths
if (Test-Path -Path $arpPath) { throw "refusing to run: VRCFaceTracking is installed on this machine and the test would remove it" }
if (Test-Path -LiteralPath $shortcut) { throw "refusing to run: $shortcut exists and the test would remove it" }
if ($steamVr -and $appKey -eq 'benaclejames.vrcft') {
  throw "refusing to run: SteamVR is set up on this machine and the payload registers the real benaclejames.vrcft app key. Build the setup from a payload whose app.vrmanifest uses a test app key."
}
if (Test-Path -LiteralPath $root) { Remove-Item -LiteralPath $root -Recurse -Force }

$keptSeeds = @((Join-Path $dataDir 'setup-smoke.txt'), (Join-Path $localDir 'logs\setup-smoke.log'))
$stagingSeed = Join-Path $localDir 'update\setup-smoke.bin'
$createdDirs = @($dataDir, $localDir, (Join-Path $localDir 'logs')) | Where-Object { -not (Test-Path -LiteralPath $_) }
foreach ($seed in $keptSeeds + $stagingSeed) {
  New-Item -ItemType File -Force -Path $seed | Out-Null
}

try {
  if ($steamVr) {
    New-Item -ItemType Directory -Force -Path $rival | Out-Null
    Copy-Item -LiteralPath (Join-Path $payload 'app.vrmanifest') -Destination $rival
    $rivalManifest = Join-Path $rival 'app.vrmanifest'
    $code = Invoke-Exe (Join-Path $payload 'VRCFaceTracking.exe') "--register-steamvr `"$rivalManifest`""
    Assert ("$code" -eq '0' -and (Test-Registered $rivalManifest)) "rival copy registered with SteamVR first (exit $code)"
  }

  $code = Invoke-Exe $setupPath "/S /D=$first"
  Assert ("$code" -eq '0') "fresh install exit code 0 (got $code)"
  Assert-Installed $first
  if ($steamVr) {
    Assert (-not (Test-Registered $rivalManifest)) "install removed the rival registration"
  }

  $code = Invoke-Exe $setupPath "/S /D=$second"
  Assert ("$code" -eq '0') "install into a new folder exit code 0 (got $code)"
  Assert (-not (Test-Path -LiteralPath $first)) "moving the install removed the old folder"
  Assert-Installed $second
  if ($steamVr) {
    Assert (-not (Test-Registered (Join-Path $first 'app.vrmanifest'))) "moving the install removed the old SteamVR registration"
  }

  $code = Invoke-Exe $setupPath "/S /D=$second"
  Assert ("$code" -eq '0') "reinstall over the same folder exit code 0 (got $code)"
  Assert-Installed $second

  $code = Invoke-Exe (Join-Path $second 'Uninstall.exe') "/S _?=$second"
  Assert ("$code" -eq '0') "uninstaller exit code 0 (got $code)"
  Assert (-not (Test-Path -Path $arpPath)) "uninstall entry removed"
  Assert (-not (Test-Path -LiteralPath $shortcut)) "start menu shortcut removed"
  $left = @()
  if (Test-Path -LiteralPath $second) {
    $left = @(Get-ChildItem -LiteralPath $second -Recurse -Force | Where-Object { $_.Name -ne 'Uninstall.exe' })
  }
  Assert ($left.Count -eq 0) "install folder emptied (left: $(($left | ForEach-Object Name) -join ', '))"
  if ($steamVr) {
    Assert (-not (Test-Registered (Join-Path $second 'app.vrmanifest'))) "uninstall removed the SteamVR registration"
  }
  foreach ($seed in $keptSeeds) {
    Assert (Test-Path -LiteralPath $seed) "silent uninstall kept $seed"
  }
  Assert (-not (Test-Path -LiteralPath $stagingSeed)) "uninstall removed the update staging folder"
}
finally {
  if (Test-Path -LiteralPath $root) { Remove-Item -LiteralPath $root -Recurse -Force }
  foreach ($seed in $keptSeeds + $stagingSeed) {
    if (Test-Path -LiteralPath $seed) { Remove-Item -LiteralPath $seed -Force }
  }
  foreach ($dir in @($createdDirs | Sort-Object { $_.Length } -Descending)) {
    if ((Test-Path -LiteralPath $dir) -and @(Get-ChildItem -LiteralPath $dir -Force).Count -eq 0) { Remove-Item -LiteralPath $dir -Force }
  }
}

if ($script:failures.Count -gt 0) {
  throw "installer test failed: $($script:failures -join '; ')"
}
Write-Host "installer test passed"
