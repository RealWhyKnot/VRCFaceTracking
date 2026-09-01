#!/usr/bin/env pwsh
[CmdletBinding()]
param()

$ErrorActionPreference = "Stop"

$scriptRoot = Split-Path -Parent $MyInvocation.MyCommand.Path
$generate = Join-Path $scriptRoot "Generate-ReleaseNotes.ps1"

function Invoke-TestGit {
  param([string] $RepoRoot, [string[]] $Arguments)
  Push-Location $RepoRoot
  try {
    $output = & git @Arguments
    if ($LASTEXITCODE -ne 0) { throw "git $($Arguments -join ' ') failed with exit code $LASTEXITCODE" }
    return @($output)
  }
  finally { Pop-Location }
}

function Add-Commit {
  param([string] $RepoRoot, [string] $Subject)
  $file = Join-Path $RepoRoot "sample.txt"
  Add-Content -LiteralPath $file -Value $Subject -Encoding ASCII
  Invoke-TestGit -RepoRoot $RepoRoot -Arguments @("add", ".") | Out-Null
  Invoke-TestGit -RepoRoot $RepoRoot -Arguments @("commit", "-q", "-m", $Subject) | Out-Null
}

function Assert-Contains {
  param([string] $Text, [string] $Expected, [string] $Message)
  if ($Text -notmatch [regex]::Escape($Expected)) { throw "$Message`nExpected: $Expected`nGot:`n$Text" }
}

function Assert-NotContains {
  param([string] $Text, [string] $Expected, [string] $Message)
  if ($Text -match [regex]::Escape($Expected)) { throw "$Message`nUnexpected: $Expected`nGot:`n$Text" }
}

$root = Join-Path ([System.IO.Path]::GetTempPath()) ("vrcft-release-notes-" + [System.Guid]::NewGuid().ToString("N"))
New-Item -ItemType Directory -Path $root | Out-Null
try {
  Invoke-TestGit -RepoRoot $root -Arguments @("init", "-q", ".") | Out-Null
  Invoke-TestGit -RepoRoot $root -Arguments @("config", "user.name", "VRCFaceTracking Tests") | Out-Null
  Invoke-TestGit -RepoRoot $root -Arguments @("config", "user.email", "vrcft-tests@example.invalid") | Out-Null
  Invoke-TestGit -RepoRoot $root -Arguments @("config", "core.hooksPath", "/dev/null") | Out-Null
  Add-Commit -RepoRoot $root -Subject "chore: scaffold (2026.9.1.0-AB12)"
  Invoke-TestGit -RepoRoot $root -Arguments @("tag", "v2026.9.1.0") | Out-Null
  Start-Sleep -Seconds 1
  Add-Commit -RepoRoot $root -Subject "feat(filter): eye gaze linearisation (2026.9.2.0-CD34)"
  Add-Commit -RepoRoot $root -Subject "fix: keepalive flap"
  Add-Commit -RepoRoot $root -Subject "ci: lint job [skip changelog]"
  Add-Commit -RepoRoot $root -Subject "loose subject without a type"
  Invoke-TestGit -RepoRoot $root -Arguments @("tag", "v2026.9.2.0-beta") | Out-Null

  $zip = Join-Path $root "VRCFaceTracking-2026.9.2.0-beta-win-x64.zip"
  [System.IO.File]::WriteAllBytes($zip, [byte[]](1..64))
  $out = Join-Path $root "notes.md"

  $text = (& $generate -Tag "v2026.9.2.0-beta" -RepoRoot $root -ZipPath $zip -OutFile $out) -join "`n"
  if ($LASTEXITCODE -ne 0) { throw "generator failed" }

  Assert-Contains -Text $text -Expected "## v2026.9.2.0-beta" -Message "Heading missing."
  Assert-Contains -Text $text -Expected "Changes since v2026.9.1.0." -Message "Previous tag missing."
  Assert-Contains -Text $text -Expected "### Features`n- filter: eye gaze linearisation" -Message "Feature entry wrong (scope or stamp)."
  Assert-Contains -Text $text -Expected "### Fixes`n- keepalive flap" -Message "Fix entry missing."
  Assert-Contains -Text $text -Expected "- loose subject without a type" -Message "Untyped subject should land in Changes."
  Assert-NotContains -Text $text -Expected "scaffold" -Message "Commits before the previous tag leaked in."
  Assert-NotContains -Text $text -Expected "lint job" -Message "[skip changelog] commit leaked in."
  Assert-NotContains -Text $text -Expected "CD34" -Message "Build stamp not stripped."
  $hash = (Get-FileHash -LiteralPath $zip -Algorithm SHA256).Hash.ToLowerInvariant()
  Assert-Contains -Text $text -Expected "SHA256 ``$hash``" -Message "Zip hash missing."
  if (-not (Test-Path -LiteralPath $out)) { throw "OutFile not written." }

  $first = (& $generate -Tag "v2026.9.1.0" -RepoRoot $root) -join "`n"
  Assert-Contains -Text $first -Expected "### Maintenance`n- scaffold" -Message "First tag should include all history."
  Assert-NotContains -Text $first -Expected "Changes since" -Message "First tag has no previous tag."

  Write-Host "Release notes tests passed."
}
finally {
  if (Test-Path -LiteralPath $root) { Remove-Item -LiteralPath $root -Recurse -Force }
}
