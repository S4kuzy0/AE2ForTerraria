param(
    [Parameter(Mandatory)][string]$Message,
    [string]$Python = 'python',
    [string]$GitHubCli = ''
)
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
Push-Location $root
try {
    $remote = & git remote get-url origin
    if ($LASTEXITCODE -ne 0 -or $remote -ne 'https://github.com/S4kuzy0/AE2ForTerraria.git') {
        throw 'Expected the authorized AE2ForTerraria GitHub remote.'
    }
    if (!$GitHubCli) {
        $portable = Join-Path $PSScriptRoot '.local\github-cli\bin\gh.exe'
        if (Test-Path -LiteralPath $portable) { $GitHubCli = $portable }
        else {
            $installed = Get-Command gh -ErrorAction SilentlyContinue
            if (!$installed) { throw 'GitHub CLI is required to verify repository visibility.' }
            $GitHubCli = $installed.Source
        }
    }
    $repoJson = & $GitHubCli repo view 'S4kuzy0/AE2ForTerraria' --json nameWithOwner,isPrivate
    if ($LASTEXITCODE -ne 0) { throw 'Could not verify repository visibility; upload stopped.' }
    $repo = $repoJson | ConvertFrom-Json
    if ($repo.nameWithOwner -ne 'S4kuzy0/AE2ForTerraria' -or $repo.isPrivate) {
        throw 'The authorized repository must remain public; upload stopped.'
    }
    $branch = & git branch --show-current
    if ($LASTEXITCODE -ne 0 -or !$branch) { throw 'A checked-out branch is required.' }
    & git add --all -- .gitignore README.md AE2ForTerraria docs tools ui-preview validation
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
    if ($LASTEXITCODE -ne 0) { throw 'Could not inspect the local commit.' }
    $remoteHead = & git ls-remote origin ('refs/heads/' + $branch)
    if ($LASTEXITCODE -ne 0 -or !$remoteHead -or ($remoteHead -split '\s+')[0] -ne $local) {
        throw 'Could not verify that the remote branch matches the local commit.'
    }
    Write-Output ('Uploaded and verified: ' + $local)
} finally {
    Pop-Location
}
