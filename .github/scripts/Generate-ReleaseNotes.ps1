#!/usr/bin/env pwsh
[CmdletBinding()]
param(
  [Parameter(Mandatory = $true)][string] $Tag,
  [string] $Repo = $(if ($env:GITHUB_REPOSITORY) { $env:GITHUB_REPOSITORY } else { 'RealWhyKnot/VRCFaceTracking' }),
  [string] $RepoRoot = (Get-Location).Path,
  [string] $ZipPath = "",
  [string] $IntegrityName = "",
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

$authorHandles = @{ 'WhyKnot' = 'RealWhyKnot' }

$categories = @(
  @{ Prefix = 'feat'; Name = 'Features' }
  @{ Prefix = 'fix'; Name = 'Bug Fixes' }
  @{ Prefix = 'perf'; Name = 'Performance' }
  @{ Prefix = 'refactor'; Name = 'Refactors' }
  @{ Prefix = 'revert'; Name = 'Reverts' }
  @{ Prefix = 'docs'; Name = 'Documentation' }
  @{ Prefix = 'style'; Name = 'Style' }
  @{ Prefix = 'test'; Name = 'Tests' }
  @{ Prefix = 'ci'; Name = 'CI' }
  @{ Prefix = 'chore'; Name = 'Chores' }
  @{ Prefix = 'diag'; Name = 'Diagnostics' }
)

Push-Location (Resolve-Path -LiteralPath $RepoRoot).Path
try {
  $previous = Get-PreviousTag -Tag $Tag
  $baseFile = Join-Path (Get-Location).Path ".github/release-base"
  $base = if (Test-Path -LiteralPath $baseFile) { (Get-Content -LiteralPath $baseFile -Raw).Trim() } else { "" }
  $range = if ($previous) { "$previous..$Tag" } elseif ($base) { "$base..$Tag" } else { $Tag }
  $compareFrom = if ($previous) { $previous } elseif ($base) { $base.Substring(0, 12) } else { "" }

  $raw = @(Invoke-Git -Arguments @("log", "--no-merges", "--format=%h%x09%an%x09%s", $range))
  $entries = foreach ($line in $raw) {
    if (-not $line -or $line -match '\[skip changelog\]') { continue }
    $parts = $line -split "`t", 3
    if ($parts.Count -lt 3) { continue }
    $author = $parts[1]
    $authorText = if ($authorHandles.ContainsKey($author)) { "@$($authorHandles[$author])" } else { $author }
    $subject = ($parts[2] -replace '\s*\([0-9]{4}\.[0-9]+\.[0-9]+\.[0-9]+(-[A-Fa-f0-9]{4})?\)\s*$', '').Trim()
    if (-not $subject) { continue }
    [pscustomobject]@{ Short = $parts[0]; Author = $authorText; Subject = $subject }
  }

  $repoShort = ($Repo -split '/')[-1]
  $tagSha = ([string](@(Invoke-Git -Arguments @("rev-list", "-n", "1", $Tag)) | Select-Object -First 1)).Trim()
  $zipName = if ($ZipPath) { Split-Path -Leaf $ZipPath } else { "VRCFaceTracking-$($Tag.Substring(1))-win-x64.zip" }
  if (-not $IntegrityName) { $IntegrityName = $zipName -replace '\.zip$', '.integrity.tsv' }
  $tokens = @{
    '{tag}' = $Tag
    '{version}' = $Tag.Substring(1)
    '{full-repo}' = $Repo
    '{commit-sha-short}' = $tagSha.Substring(0, 12)
    '{prior-tag}' = $previous
    '{zip-name}' = $zipName
    '{integrity-name}' = $IntegrityName
  }

  $lines = [System.Collections.Generic.List[string]]::new()
  $lines.Add("# $repoShort $Tag") | Out-Null
  $lines.Add("") | Out-Null
  $lines.Add("## What's Changed") | Out-Null
  $lines.Add("") | Out-Null

  $any = $false
  $used = @{}
  foreach ($category in $categories) {
    $pattern = "^$($category.Prefix)(\((?<scope>[^)]+)\))?!?:\s*(?<summary>.+)$"
    $items = @($entries | Where-Object { $_.Subject -match $pattern })
    if ($items.Count -eq 0) { continue }
    $any = $true
    $lines.Add("### $($category.Name)") | Out-Null
    foreach ($item in $items) {
      $used[$item.Short] = $true
      $lines.Add("- $($item.Subject) by $($item.Author) in $($item.Short)") | Out-Null
    }
    $lines.Add("") | Out-Null
  }
  $other = @($entries | Where-Object { -not $used.ContainsKey($_.Short) })
  if ($other.Count -gt 0) {
    $any = $true
    $lines.Add("### Other Changes") | Out-Null
    foreach ($item in $other) { $lines.Add("- $($item.Subject) by $($item.Author) in $($item.Short)") | Out-Null }
    $lines.Add("") | Out-Null
  }
  if (-not $any) {
    $lines.Add("_No user-facing changes recorded._") | Out-Null
    $lines.Add("") | Out-Null
  }
  if ($compareFrom) {
    $lines.Add("**Full Changelog**: https://github.com/$Repo/compare/$compareFrom...$Tag") | Out-Null
  }

  if ($ZipPath) {
    $zip = Get-Item -LiteralPath $ZipPath
    $hash = (Get-FileHash -LiteralPath $zip.FullName -Algorithm SHA256).Hash.ToLowerInvariant()
    $sizeMiB = [math]::Round($zip.Length / 1MB, 2)
    $lines.Add("") | Out-Null
    $lines.Add("## File integrity") | Out-Null
    $lines.Add("") | Out-Null
    $lines.Add("- ``$($zip.Name)`` ($sizeMiB MiB), SHA256 ``$hash``") | Out-Null
    $lines.Add("- Hashes for every file in the zip are attached as ``$IntegrityName``.") | Out-Null
  }

  $templateDir = Join-Path (Get-Location).Path ".github/release-template"
  foreach ($name in @('links', 'install', 'uninstall', 'what-you-need-to-do')) {
    $path = Join-Path $templateDir "$name.md"
    if (-not (Test-Path -LiteralPath $path)) { continue }
    $text = (Get-Content -LiteralPath $path -Raw -Encoding UTF8).Trim()
    if (-not $text) { continue }
    foreach ($key in $tokens.Keys) { $text = $text.Replace($key, [string]$tokens[$key]) }
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

  $body = ($lines -join "`n").TrimEnd()

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
