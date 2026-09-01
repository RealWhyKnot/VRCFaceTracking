#!/usr/bin/env pwsh
[CmdletBinding()]
param(
  [Parameter(Mandatory = $true)][string] $Tag,
  [string] $RepoRoot = (Get-Location).Path,
  [string] $ZipPath = "",
  [string] $OutFile = ""
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

Push-Location (Resolve-Path -LiteralPath $RepoRoot).Path
try {
  $previous = Get-PreviousTag -Tag $Tag
  $range = if ($previous) { "$previous..$Tag" } else { $Tag }
  $subjects = @(Invoke-Git -Arguments @("log", "--format=%s", "--no-merges", $range))

  $sections = [ordered]@{
    "Features" = [System.Collections.Generic.List[string]]::new()
    "Fixes" = [System.Collections.Generic.List[string]]::new()
    "Changes" = [System.Collections.Generic.List[string]]::new()
    "Maintenance" = [System.Collections.Generic.List[string]]::new()
  }
  $typeToSection = @{
    feat = "Features"; fix = "Fixes"; perf = "Changes"; refactor = "Changes"; diag = "Changes"
    chore = "Maintenance"; ci = "Maintenance"; docs = "Maintenance"; test = "Maintenance"; style = "Maintenance"
  }

  foreach ($raw in $subjects) {
    $subject = ($raw -replace '\s*\([0-9]{4}\.[0-9]+\.[0-9]+\.[0-9]+(-[A-Fa-f0-9]{4})?\)\s*$', '').Trim()
    if (-not $subject -or $subject -match '\[skip changelog\]') { continue }
    if ($subject -match '^(?<type>[a-z]+)(\((?<scope>[a-z0-9-]+)\))?!?:\s*(?<summary>.+)$') {
      $section = $typeToSection[$Matches.type]
      if (-not $section) { $section = "Changes" }
      $summary = $Matches.summary.Trim()
      if ($Matches.scope) { $summary = "$($Matches.scope): $summary" }
      $sections[$section].Add($summary) | Out-Null
    } else {
      $sections["Changes"].Add($subject) | Out-Null
    }
  }

  $lines = [System.Collections.Generic.List[string]]::new()
  $lines.Add("## $Tag") | Out-Null
  if ($previous) { $lines.Add("Changes since $previous.") | Out-Null }
  $lines.Add("") | Out-Null
  $any = $false
  foreach ($name in $sections.Keys) {
    $items = $sections[$name]
    if ($items.Count -eq 0) { continue }
    $any = $true
    $lines.Add("### $name") | Out-Null
    foreach ($item in $items) { $lines.Add("- $item") | Out-Null }
    $lines.Add("") | Out-Null
  }
  if (-not $any) {
    $lines.Add("No user-facing changes recorded.") | Out-Null
    $lines.Add("") | Out-Null
  }

  if ($ZipPath) {
    $zip = Get-Item -LiteralPath $ZipPath
    $hash = (Get-FileHash -LiteralPath $zip.FullName -Algorithm SHA256).Hash.ToLowerInvariant()
    $sizeMiB = [math]::Round($zip.Length / 1MB, 2)
    $lines.Add("### Download") | Out-Null
    $lines.Add("- $($zip.Name) ($sizeMiB MiB)") | Out-Null
    $lines.Add("- SHA256 ``$hash``") | Out-Null
    $lines.Add("") | Out-Null
  }

  $text = ($lines -join "`n")
  if ($OutFile) {
    [System.IO.File]::WriteAllText($OutFile, $text, (New-Object System.Text.UTF8Encoding($false)))
  }
  Write-Output $text
}
finally {
  Pop-Location
}
