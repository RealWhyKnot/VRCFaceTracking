#!/usr/bin/env pwsh
[CmdletBinding()]
param()

$ErrorActionPreference = "Stop"

$scriptRoot = Split-Path -Parent $MyInvocation.MyCommand.Path
$generate = Join-Path $scriptRoot "Generate-Contributors.ps1"

$root = Join-Path ([System.IO.Path]::GetTempPath()) ("vrcft-contributors-" + [System.Guid]::NewGuid().ToString("N"))
New-Item -ItemType Directory -Path $root | Out-Null
try {
  $repoA = Join-Path $root "a.json"
  $repoB = Join-Path $root "b.json"
  $utf8 = New-Object System.Text.UTF8Encoding($false)
  [System.IO.File]::WriteAllText($repoA, '[{"login":"alice","html_url":"https://github.com/alice","contributions":50,"type":"User"},{"login":"bob","html_url":"https://github.com/bob","contributions":10,"type":"User"},{"login":"helper[bot]","html_url":"https://github.com/apps/helper","contributions":99,"type":"Bot"}]', $utf8)
  [System.IO.File]::WriteAllText($repoB, '[{"login":"alice","html_url":"https://example.invalid/alice-fork","contributions":25,"type":"User"},{"login":"carol","html_url":"https://github.com/carol","contributions":60,"type":"User"}]', $utf8)

  $out = Join-Path $root "contributors.json"
  & $generate -FromJson @($repoA, $repoB) -OutFile $out | Out-Null
  if (-not (Test-Path -LiteralPath $out)) { throw "Output not written." }
  $parsed = ConvertFrom-Json -InputObject (Get-Content -LiteralPath $out -Raw -Encoding UTF8)
  $result = @($parsed)

  if ($result.Count -ne 3) { throw "Expected 3 contributors, got $($result.Count)." }
  foreach ($entry in $result) {
    $names = @($entry.PSObject.Properties.Name) -join ','
    if ($names -ne 'login,html_url,contributions') { throw "Unexpected keys: $names" }
  }
  if ($result[0].login -ne 'alice' -or $result[0].contributions -ne 75) { throw "alice must lead with 75; got $($result[0].login) $($result[0].contributions)." }
  if ($result[0].html_url -ne 'https://github.com/alice') { throw "The first html_url must win; got $($result[0].html_url)." }
  if ($result[1].login -ne 'carol' -or $result[1].contributions -ne 60) { throw "carol must be second." }
  if ($result[2].login -ne 'bob' -or $result[2].contributions -ne 10) { throw "bob must be last." }

  $first = [Convert]::ToBase64String([System.IO.File]::ReadAllBytes($out))
  & $generate -FromJson @($repoA, $repoB) -OutFile $out | Out-Null
  $second = [Convert]::ToBase64String([System.IO.File]::ReadAllBytes($out))
  if ($first -ne $second) { throw "A second run must produce a byte-identical file." }

  $empty = Join-Path $root "empty.json"
  [System.IO.File]::WriteAllText($empty, '[]', $utf8)
  & $generate -FromJson @($empty) -OutFile $out 3>$null | Out-Null
  if ($LASTEXITCODE -ne 0) { throw "Empty input must exit 0; got $LASTEXITCODE." }
  $after = [Convert]::ToBase64String([System.IO.File]::ReadAllBytes($out))
  if ($first -ne $after) { throw "Empty input must leave the existing file untouched." }

  Write-Host "Contributor generator tests passed."
}
finally {
  if (Test-Path -LiteralPath $root) { Remove-Item -LiteralPath $root -Recurse -Force }
}
