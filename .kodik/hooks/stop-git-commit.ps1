$ErrorActionPreference = 'Stop'

$root = git rev-parse --show-toplevel 2>$null
if (-not $root) {
    exit 0
}

Set-Location $root

$status = git status --porcelain
if (-not $status) {
    Write-Output "No changes to commit."
    exit 0
}

git add -A

$timestamp = Get-Date -Format "yyyy-MM-dd HH:mm:ss"
git commit -m "chore: auto-commit on session stop ($timestamp)"

Write-Output "Auto-committed pending changes on session stop (push skipped)."
exit 0
