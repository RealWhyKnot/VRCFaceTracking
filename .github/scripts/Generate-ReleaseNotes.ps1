#!/usr/bin/env pwsh
[CmdletBinding()]
param(
  [Parameter(Mandatory = $true)][string] $Tag,
  [Parameter(Mandatory = $true)][string] $ChangelogPath,
  [string] $Repo = $(if ($env:GITHUB_REPOSITORY) { $env:GITHUB_REPOSITORY } else { 'RealWhyKnot/VRCFaceTracking' }),
  [string] $RepoRoot = (Get-Location).Path,
  [string] $AssetsDir = "",
  [string] $OutFile = "",
  [switch] $SkipScrub
)

$ErrorActionPreference = "Stop"

function Invoke-Git {
  param([string[]] $Arguments)
  $output = & git @Arguments
  if ($LASTEXITCODE -ne 0) { throw "git $($Arguments -join ' ') failed with exit code $LASTEXITCODE" }
  return @($output)
}

function Get-PreviousTag {
  param([string] $Tag)
  $tags = @(Invoke-Git -Arguments @("tag", "--list", "v*", "--sort=-creatordate"))
  $seen = $false
  foreach ($t in $tags) {
    if ($seen) { return $t }
    if ($t -eq $Tag) { $seen = $true }
  }
  return ""
}

if ($AssetsDir) { $AssetsDir = (Resolve-Path -LiteralPath $AssetsDir).Path }

Push-Location (Resolve-Path -LiteralPath $RepoRoot).Path
try {
  $previous = Get-PreviousTag -Tag $Tag

  if (-not (Test-Path -LiteralPath $ChangelogPath)) {
    throw "Changelog not found at $ChangelogPath. It comes from RealWhyKnot/workflows/release-notes."
  }
  $changelog = (Get-Content -LiteralPath $ChangelogPath -Raw -Encoding UTF8).Trim()
  if (-not $changelog) { throw "Changelog at $ChangelogPath is empty." }

  $tagSha = ([string](@(Invoke-Git -Arguments @("rev-list", "-n", "1", $Tag)) | Select-Object -First 1)).Trim()
  $tokens = @{
    '{tag}' = $Tag
    '{version}' = $Tag.Substring(1)
    '{full-repo}' = $Repo
    '{commit-sha-short}' = $tagSha.Substring(0, 12)
    '{prior-tag}' = $previous
  }

  $archives = @()
  if ($AssetsDir) {
    $archives = @(Get-ChildItem -LiteralPath $AssetsDir -File | Where-Object { $_.Name -like '*.zip' -or $_.Name -like '*.tar.gz' } | Sort-Object Name)
  }

  $lines = [System.Collections.Generic.List[string]]::new()
  $lines.Add($changelog) | Out-Null

  if ($archives.Count -gt 0) {
    $lines.Add("") | Out-Null
    $lines.Add("## File integrity") | Out-Null
    $lines.Add("") | Out-Null
    $lines.Add("| Asset | Size (MiB) | SHA-256 |") | Out-Null
    $lines.Add("| --- | --- | --- |") | Out-Null
    foreach ($archive in $archives) {
      $hash = (Get-FileHash -LiteralPath $archive.FullName -Algorithm SHA256).Hash.ToLowerInvariant()
      $sizeMiB = ($archive.Length / 1MB).ToString('0.00', [System.Globalization.CultureInfo]::InvariantCulture)
      $lines.Add("| ``$($archive.Name)`` | $sizeMiB | ``$hash`` |") | Out-Null
    }
    $lines.Add("") | Out-Null
    $lines.Add("Per-file hashes ship beside each archive as a matching ``.integrity.tsv`` asset.") | Out-Null
  }

  $installSections = [ordered]@{
    'install-windows' = '*-win-*.zip'
    'install-linux' = '*-linux-*.tar.gz'
    'install-macos' = '*-osx-*.tar.gz'
  }
  $templateDir = Join-Path (Get-Location).Path ".github/release-template"
  $names = @('links') + @($installSections.Keys) + @('uninstall', 'what-you-need-to-do')
  foreach ($name in $names) {
    $path = Join-Path $templateDir "$name.md"
    if (-not (Test-Path -LiteralPath $path)) { continue }
    $sectionTokens = @{} + $tokens
    if ($installSections.Contains($name)) {
      $matching = @($archives | Where-Object { $_.Name -like $installSections[$name] })
      if ($matching.Count -eq 0) { continue }
      $sectionTokens['{zip-name}'] = (($matching | ForEach-Object { '`' + $_.Name + '`' }) -join ' or ')
    }
    $text = (Get-Content -LiteralPath $path -Raw -Encoding UTF8).Trim()
    if (-not $text) { continue }
    foreach ($key in $sectionTokens.Keys) { $text = $text.Replace($key, [string]$sectionTokens[$key]) }
    $lines.Add("") | Out-Null
    $lines.Add($text) | Out-Null
  }

  $extras = Join-Path (Get-Location).Path ".github/release-extras/$Tag.md"
  if (Test-Path -LiteralPath $extras) {
    $text = (Get-Content -LiteralPath $extras -Raw -Encoding UTF8).Trim()
    if ($text) {
      $lines.Add("") | Out-Null
      $lines.Add("---") | Out-Null
      $lines.Add("") | Out-Null
      $lines.Add("## Additional notes") | Out-Null
      $lines.Add("") | Out-Null
      $lines.Add($text) | Out-Null
    }
  }

  $body = (($lines -join "`n") -replace "`r`n", "`n").TrimEnd()

  if (-not $SkipScrub) {
    $offenders = @()
    $lineNumber = 0
    foreach ($line in ($body -split "`n")) {
      $lineNumber++
      for ($i = 0; $i -lt $line.Length; $i++) {
        $code = [int][char]$line[$i]
        if (-not (($code -ge 0x20 -and $code -le 0x7E) -or $code -eq 9)) {
          $offenders += "  line $lineNumber col $($i + 1): U+$('{0:X4}' -f $code) in: $line"
        }
      }
    }
    if ($offenders.Count -gt 0) {
      throw "Non-ASCII characters in release body:`n$($offenders -join "`n")`nAmend the offending commit subject or template to use ASCII."
    }
    $forbidden = @('\bcomprehensive\b', '\bleveraging\b', '\bwhether\s+you''?re\b', '\bempowers?\b', '\bstreamline\b', '\belevate\b', '\bcutting-edge\b', '\bseamless(ly)?\b', '\belegant\b', '\brobust\b', '\bthoroughly\b', '\bpolished\b')
    foreach ($pattern in $forbidden) {
      $m = [regex]::Match($body, $pattern, 'IgnoreCase')
      if ($m.Success) { throw "Release body contains '$($m.Value)' (pattern $pattern). Reword the commit subject or template, or mark the commit [skip changelog]." }
    }
  }

  if ($OutFile) {
    [System.IO.File]::WriteAllText($OutFile, $body + "`n", (New-Object System.Text.UTF8Encoding($false)))
  }
  Write-Output $body
}
finally {
  Pop-Location
}
