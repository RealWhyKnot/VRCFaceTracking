param(
    [Parameter(Mandatory = $true)][string]$Base,
    [Parameter(Mandatory = $true)][string]$Head
)

$ErrorActionPreference = 'Stop'
$pattern = '^(feat|fix|perf|refactor|revert|docs|style|test|ci|build|chore)(\([A-Za-z0-9._/-]+\))?!?: \S.*$'

if ($Base -match '^0+$' -or [string]::IsNullOrWhiteSpace($Base)) {
    $range = "$Head~20..$Head"
} else {
    $range = "$Base..$Head"
}

$subjects = git log --no-merges --format=%s $range
if ($LASTEXITCODE -ne 0) {
    throw "git log failed for range $range"
}

$bad = @()
foreach ($subject in $subjects) {
    if ($subject -notmatch $pattern) {
        $bad += $subject
    }
}

if ($bad.Count -gt 0) {
    Write-Host "Commit subjects must look like 'type(scope): description' with type in feat|fix|perf|refactor|revert|docs|style|test|ci|build|chore."
    foreach ($s in $bad) {
        Write-Host "  bad: $s"
    }
    exit 1
}

Write-Host "Checked $(@($subjects).Count) commit subject(s) in $range."
