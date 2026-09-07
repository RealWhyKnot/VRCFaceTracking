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

  $changelog = Join-Path $root "changelog.md"
  $changelogText = "# VRCFaceTracking v2026.9.2.0-beta`n`n## What's Changed`n`n### Features`n- feat(filter): eye gaze linearisation by @RealWhyKnot in abc1234`n`n**Full Changelog**: https://github.com/RealWhyKnot/VRCFaceTracking/compare/v2026.9.1.0...v2026.9.2.0-beta`n"
  [System.IO.File]::WriteAllText($changelog, $changelogText, (New-Object System.Text.UTF8Encoding($false)))

  $text = (& $generate -Tag "v2026.9.2.0-beta" -RepoRoot $root -ChangelogPath $changelog -ZipPath $zip -OutFile $out) -join "`n"
  if ($LASTEXITCODE -ne 0) { throw "generator failed" }

  Assert-Contains -Text $text -Expected "# VRCFaceTracking v2026.9.2.0-beta" -Message "The changelog heading must survive."
  Assert-Contains -Text $text -Expected "- feat(filter): eye gaze linearisation by @RealWhyKnot in abc1234" -Message "The changelog body must survive."
  Assert-Contains -Text $text -Expected "compare/v2026.9.1.0...v2026.9.2.0-beta" -Message "Full changelog link missing."
  if ($text.IndexOf("# VRCFaceTracking v2026.9.2.0-beta") -ne 0) { throw "The changelog must lead the body." }
  $hash = (Get-FileHash -LiteralPath $zip -Algorithm SHA256).Hash.ToLowerInvariant()
  Assert-Contains -Text $text -Expected "SHA256 ``$hash``" -Message "Zip hash missing."
  if (-not (Test-Path -LiteralPath $out)) { throw "OutFile not written." }

  $missing = Join-Path $root "nope.md"
  $threw = $false
  try { & $generate -Tag "v2026.9.2.0-beta" -RepoRoot $root -ChangelogPath $missing | Out-Null } catch { $threw = $true }
  if (-not $threw) { throw "A missing changelog must throw." }

  $unicode = Join-Path $root "unicode.md"
  [System.IO.File]::WriteAllText($unicode, ("## What's Changed`n`n- feat: caf" + [char]0x00E9 + " by @RealWhyKnot in abc1234`n"), (New-Object System.Text.UTF8Encoding($false)))
  $threw = $false
  try { & $generate -Tag "v2026.9.2.0-beta" -RepoRoot $root -ChangelogPath $unicode | Out-Null } catch { $threw = $true }
  if (-not $threw) { throw "Non-ASCII in the changelog must throw." }

  Write-Host "Release notes tests passed."
}
finally {
  if (Test-Path -LiteralPath $root) { Remove-Item -LiteralPath $root -Recurse -Force }
}
