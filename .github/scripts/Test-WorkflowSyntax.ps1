#!/usr/bin/env pwsh
[CmdletBinding()]
param(
    [string] $Root = (Get-Location).Path
)

$ErrorActionPreference = 'Stop'

$errors = [System.Collections.Generic.List[string]]::new()

function Add-ParserErrors {
    param([string] $Source, $ParseErrors)
    if (-not $ParseErrors) { return }
    foreach ($e in $ParseErrors) {
        $errors.Add("$Source (line $($e.Extent.StartLineNumber):$($e.Extent.StartColumnNumber)): $($e.Message)") | Out-Null
    }
}

$scriptFiles = @()
$scriptDir = Join-Path $Root '.github/scripts'
if (Test-Path -LiteralPath $scriptDir) { $scriptFiles += @(Get-ChildItem -LiteralPath $scriptDir -Filter '*.ps1' -Recurse) }
$scriptFiles += @(Get-ChildItem -LiteralPath $Root -Filter '*.ps1')
$scriptsDir = Join-Path $Root 'scripts'
if (Test-Path -LiteralPath $scriptsDir) { $scriptFiles += @(Get-ChildItem -LiteralPath $scriptsDir -Filter '*.ps1') }
foreach ($file in $scriptFiles) {
    $parseErrors = $null
    [void][System.Management.Automation.Language.Parser]::ParseFile($file.FullName, [ref]$null, [ref]$parseErrors)
    Add-ParserErrors -Source $file.FullName -ParseErrors $parseErrors
}

$workflowDir = Join-Path $Root '.github/workflows'
if (Test-Path -LiteralPath $workflowDir) {
    $ghaPattern = [regex]'\$\{\{[^}]*\}\}'
    Get-ChildItem -LiteralPath $workflowDir -Filter '*.yml' | ForEach-Object {
        $wfPath = $_.FullName
        $workflowText = Get-Content -LiteralPath $wfPath -Raw
        if ($workflowText -match 'git push' -and $workflowText -notmatch 'persist-credentials:\s*false') {
            $errors.Add("${wfPath}: a workflow that pushes must check out with persist-credentials: false and push with an explicit token.") | Out-Null
        }

        $lines = Get-Content -LiteralPath $wfPath
        $stepName = '<unnamed>'
        $isPwsh = $false
        $inRun = $false
        $runIndent = -1
        $runStartLn = 0
        $blockLines = [System.Collections.Generic.List[string]]::new()

        $flushBlock = {
            if ($blockLines.Count -eq 0) { return }
            $baseline = -1
            foreach ($bl in $blockLines) {
                if ($bl.Trim().Length -eq 0) { continue }
                $baseline = $bl.Length - $bl.TrimStart(' ').Length
                break
            }
            if ($baseline -lt 0) { return }
            $body = ($blockLines | ForEach-Object { if ($_.Length -gt $baseline) { $_.Substring($baseline) } else { '' } }) -join "`n"
            $stubbed = $ghaPattern.Replace($body, '__GHA_EXPR__')
            $parseErrors = $null
            [void][System.Management.Automation.Language.Parser]::ParseInput($stubbed, [ref]$null, [ref]$parseErrors)
            Add-ParserErrors -Source "$wfPath step '$stepName' (run: starting at line $runStartLn)" -ParseErrors $parseErrors

            $suppresses = $body -match '2>\$null'
            $tolerates = $body -match '\$LASTEXITCODE\s+-eq\s+0'
            $resets = $body -match '\$global:LASTEXITCODE\s*=\s*0'
            if ($suppresses -and $tolerates -and -not $resets) {
                $errors.Add("$wfPath step '$stepName' (run: starting at line $runStartLn): tolerates a native command failure without resetting `$LASTEXITCODE; the step will exit with the stale code.") | Out-Null
            }
        }

        for ($i = 0; $i -lt $lines.Count; $i++) {
            $line = $lines[$i]
            $indent = $line.Length - $line.TrimStart(' ').Length
            $trimmed = $line.Trim()

            if ($inRun) {
                if ($trimmed.Length -gt 0 -and $indent -le $runIndent) {
                    & $flushBlock
                    $blockLines.Clear()
                    $inRun = $false
                } else {
                    $blockLines.Add($line) | Out-Null
                    continue
                }
            }

            if ($trimmed -match '^- name:\s*(.+?)\s*$') {
                if ($blockLines.Count -gt 0) { & $flushBlock; $blockLines.Clear(); $inRun = $false }
                $stepName = $Matches[1]
                $isPwsh = $false
                continue
            }
            if ($trimmed -match '^shell:\s*(.+?)\s*$') {
                $isPwsh = ($Matches[1] -eq 'pwsh')
                continue
            }
            if ($isPwsh -and $trimmed -match '^run:\s*\|\s*$') {
                $inRun = $true
                $runIndent = $indent
                $runStartLn = $i + 1
                $blockLines.Clear()
                continue
            }
        }
        if ($inRun) { & $flushBlock }
    }
}

if ($errors.Count -gt 0) {
    Write-Host 'PowerShell syntax errors:'
    foreach ($e in $errors) { Write-Host "  $e" }
    throw "Found $($errors.Count) problem(s) across scripts and workflows."
}

Write-Host "All PowerShell scripts and workflow inline pwsh blocks parsed cleanly."
