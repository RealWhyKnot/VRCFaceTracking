$ErrorActionPreference = 'Stop'
$repo = (git rev-parse --show-toplevel)
Set-Location $repo

if (-not (Get-Command dotnet -ErrorAction SilentlyContinue)) {
    Write-Host "pre-push: dotnet not found, skipping format check"
    exit 0
}

$upstream = git rev-parse --abbrev-ref --symbolic-full-name '@{u}' 2>$null
if ($LASTEXITCODE -ne 0 -or [string]::IsNullOrWhiteSpace($upstream)) {
    $upstream = 'origin/master'
}
$changed = git diff --name-only "$upstream...HEAD" -- '*.cs'
if ($LASTEXITCODE -ne 0) {
    $changed = git diff --name-only HEAD~1 -- '*.cs'
}
$changed = @($changed | Where-Object { $_ -and (Test-Path $_) })
if ($changed.Count -eq 0) {
    Write-Host "pre-push: no C# changes to check"
    exit 0
}

Write-Host "pre-push: checking format of $($changed.Count) file(s)"
$includeArgs = @()
foreach ($f in $changed) { $includeArgs += $f }
& dotnet format VRCFaceTracking.sln --verify-no-changes --no-restore --include @includeArgs
if ($LASTEXITCODE -ne 0) {
    Write-Host ""
    Write-Host "pre-push: formatting drift. Run 'dotnet format VRCFaceTracking.sln' and commit, or bypass once with 'git push --no-verify'."
    exit 1
}

$subjects = git log --no-merges --format=%s "$upstream..HEAD"
$pattern = '^(feat|fix|perf|refactor|revert|docs|style|test|ci|build|chore)(\([A-Za-z0-9._/-]+\))?!?: \S.*$'
$bad = @($subjects | Where-Object { $_ -and ($_ -notmatch $pattern) })
if ($bad.Count -gt 0) {
    Write-Host "pre-push: commit subjects must be 'type(scope): description':"
    foreach ($s in $bad) { Write-Host "  bad: $s" }
    exit 1
}
exit 0
