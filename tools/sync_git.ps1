param(
    [Parameter(Mandatory)][string]$Message,
    [string]$Python = 'python',
    [string]$Repository = 'S4kuzy0/AE2ForTerraria'
)
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
if ($Repository -notmatch '^[A-Za-z0-9_.-]+/[A-Za-z0-9_.-]+$') { throw 'Invalid repository name.' }
Push-Location $root
try {
    $remote = & git remote get-url origin
    if ($LASTEXITCODE -ne 0 -or $remote -ne "https://github.com/$Repository.git") { throw 'Remote does not match the explicitly selected repository.' }
    $repo = Invoke-RestMethod -Uri "https://api.github.com/repos/$Repository" -Headers @{ 'User-Agent' = 'AE2-source-maintenance' }
    if ($repo.full_name -ine $Repository -or $repo.private -or $repo.visibility -ne 'public') { throw 'Expected the public repository; no visibility change is performed by this script.' }
    $branch = & git branch --show-current
    if ($LASTEXITCODE -ne 0 -or -not $branch) { throw 'A checked-out branch is required.' }
    & git add --all -- .gitignore README.md LICENSE AE2ForTerraria docs tools ui-preview validation
    if ($LASTEXITCODE -ne 0) { throw 'Could not prepare the Git index.' }
    & $Python (Join-Path $PSScriptRoot 'check_git_upload.py')
    if ($LASTEXITCODE -ne 0) { throw 'Upload checks failed; nothing was committed or pushed.' }
    & git diff --cached --quiet
    $changed = $LASTEXITCODE
    if ($changed -gt 1) { throw 'Could not inspect staged changes.' }
    if ($changed -eq 1) {
        & git commit -m $Message
        if ($LASTEXITCODE -ne 0) { throw 'Commit failed.' }
    }
    & git push -u origin $branch
    if ($LASTEXITCODE -ne 0) { throw 'Push failed; the local commit is preserved.' }
    $local = & git rev-parse HEAD
    $remoteHead = & git ls-remote origin ('refs/heads/' + $branch)
    if ($LASTEXITCODE -ne 0 -or -not $remoteHead -or ($remoteHead -split '\s+')[0] -ne $local) { throw 'Remote branch verification failed.' }
    Write-Output ('Uploaded and verified: ' + $local)
} finally { Pop-Location }
