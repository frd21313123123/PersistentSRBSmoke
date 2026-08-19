$ErrorActionPreference = "Stop"

if (-not (Get-Command git -ErrorAction SilentlyContinue)) {
    throw "Git is not installed. Install Git for Windows first."
}
if (-not (Get-Command gh -ErrorAction SilentlyContinue)) {
    throw "GitHub CLI (gh) is not installed. Install it from https://cli.github.com/"
}

gh auth status
if ($LASTEXITCODE -ne 0) {
    gh auth login --web --git-protocol https
    if ($LASTEXITCODE -ne 0) { throw "GitHub authentication failed." }
}

if (-not (Test-Path ".git")) {
    git init
    git checkout -b main
}

$hasCommit = $true
git rev-parse --verify HEAD *> $null
if ($LASTEXITCODE -ne 0) { $hasCommit = $false }

if (-not $hasCommit) {
    git add .
    git commit -m "Initial PersistentSRBSmoke MVP with GitHub Actions"
}

$remote = git remote get-url origin 2>$null
if ($LASTEXITCODE -eq 0 -and $remote) {
    Write-Host "origin already exists: $remote"
    git push -u origin main
} else {
    gh repo create frd21313123123/PersistentSRBSmoke `
        --public `
        --description "Persistent world-space SRB smoke trails for Kerbal Space Program 1" `
        --source . `
        --remote origin `
        --push
}

Write-Host "Repository: https://github.com/frd21313123123/PersistentSRBSmoke"
Write-Host "Actions:    https://github.com/frd21313123123/PersistentSRBSmoke/actions"
