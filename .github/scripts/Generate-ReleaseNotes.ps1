param(
    [Parameter(Mandatory = $true)][string]$Tag,
    [string]$PreviousTag,
    [Parameter(Mandatory = $true)][string]$OutFile
)

$ErrorActionPreference = 'Stop'

$range = if ([string]::IsNullOrWhiteSpace($PreviousTag)) { $Tag } else { "$PreviousTag..$Tag" }
$lines = git log --no-merges --format='%s|%h' $range
if ($LASTEXITCODE -ne 0) {
    throw "git log failed for $range"
}

$categories = [ordered]@{
    feat     = 'Features'
    fix      = 'Bug fixes'
    perf     = 'Performance'
    refactor = 'Refactoring'
    revert   = 'Reverts'
    docs     = 'Documentation'
    build    = 'Build'
    ci       = 'CI'
    test     = 'Tests'
    style    = 'Style'
    chore    = 'Chores'
}
$buckets = @{}
foreach ($key in $categories.Keys) { $buckets[$key] = New-Object System.Collections.ArrayList }
$other = New-Object System.Collections.ArrayList

foreach ($line in $lines) {
    if ([string]::IsNullOrWhiteSpace($line)) { continue }
    $parts = $line -split '\|', 2
    $subject = $parts[0]
    $sha = $parts[1]
    if ($subject -match '^(?<type>[a-z]+)(\((?<scope>[^)]+)\))?!?: (?<desc>.+)$' -and $buckets.ContainsKey($Matches.type)) {
        $scope = if ($Matches.scope) { "**$($Matches.scope)**: " } else { '' }
        [void]$buckets[$Matches.type].Add("- $scope$($Matches.desc) ($sha)")
    } else {
        [void]$other.Add("- $subject ($sha)")
    }
}

$sb = New-Object System.Text.StringBuilder
foreach ($key in $categories.Keys) {
    if ($buckets[$key].Count -eq 0) { continue }
    [void]$sb.AppendLine("## $($categories[$key])")
    foreach ($entry in $buckets[$key]) { [void]$sb.AppendLine($entry) }
    [void]$sb.AppendLine()
}
if ($other.Count -gt 0) {
    [void]$sb.AppendLine('## Other changes')
    foreach ($entry in $other) { [void]$sb.AppendLine($entry) }
    [void]$sb.AppendLine()
}
if ($sb.Length -eq 0) {
    [void]$sb.AppendLine('No changes recorded.')
}

[System.IO.File]::WriteAllText($OutFile, $sb.ToString(), (New-Object System.Text.UTF8Encoding($false)))
Write-Host "Wrote release notes for $range to $OutFile"
