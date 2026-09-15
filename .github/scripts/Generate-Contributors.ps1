#!/usr/bin/env pwsh
[CmdletBinding()]
param(
  [string[]] $Repos = @('RealWhyKnot/VRCFaceTracking', 'benaclejames/VRCFaceTracking'),
  [string] $OutFile = 'VRCFaceTracking.Avalonia/Assets/contributors.json',
  [string[]] $FromJson
)

$ErrorActionPreference = "Stop"

$sets = @()
if ($FromJson) {
  foreach ($path in $FromJson) {
    $parsed = ConvertFrom-Json -InputObject (Get-Content -LiteralPath $path -Raw -Encoding UTF8)
    $sets += , @($parsed)
  }
} else {
  foreach ($repo in $Repos) {
    $raw = & gh api "repos/$repo/contributors?per_page=100" --paginate
    if ($LASTEXITCODE -ne 0) {
      Write-Warning "Skipping ${repo}: gh api failed with exit code $LASTEXITCODE."
      $global:LASTEXITCODE = 0
      continue
    }
    $parsed = ConvertFrom-Json -InputObject (($raw -join "`n") -replace '\]\s*\[', ',')
    $sets += , @($parsed)
  }
}

$merged = @{}
foreach ($set in $sets) {
  foreach ($entry in $set) {
    if (-not $entry.login) { continue }
    if ($entry.type -eq 'Bot') { continue }
    if ($entry.login.EndsWith('[bot]')) { continue }
    if ($merged.ContainsKey($entry.login)) {
      $merged[$entry.login].contributions += [int]$entry.contributions
    } else {
      $merged[$entry.login] = [pscustomobject]@{
        login = [string]$entry.login
        html_url = [string]$entry.html_url
        contributions = [int]$entry.contributions
      }
    }
  }
}

if ($merged.Count -eq 0) {
  Write-Warning "No contributors collected; leaving $OutFile untouched."
  exit 0
}

$sorted = @($merged.Values)
[Array]::Sort($sorted, [System.Comparison[object]]{
  param($a, $b)
  $c = $b.contributions.CompareTo($a.contributions)
  if ($c -ne 0) { return $c }
  return [System.StringComparer]::OrdinalIgnoreCase.Compare($a.login, $b.login)
})

if (-not [System.IO.Path]::IsPathRooted($OutFile)) { $OutFile = Join-Path (Get-Location).Path $OutFile }
$json = ConvertTo-Json -InputObject @($sorted) -Depth 3
[System.IO.File]::WriteAllText($OutFile, $json + "`n", (New-Object System.Text.UTF8Encoding($false)))
Write-Host "Wrote $($sorted.Count) contributors to $OutFile."
