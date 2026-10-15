param(
    [Parameter(Mandatory)][ValidatePattern('^[a-z0-9-]{1,40}$')][string]$Run,
    [ValidateSet('core','journey','interactions')][string]$Mode = 'core',
    [string]$SourceWorld
)
$ErrorActionPreference = 'Stop'
$workspace = Split-Path $PSScriptRoot -Parent
$tml = 'D:\SteamLibrary\steamapps\common\tModLoader'
$saveRoot = 'C:\Users\ROG\Documents\My Games\Terraria\tModLoader'
$running = Get-CimInstance Win32_Process | Where-Object {
    $_.Name -eq 'dotnet.exe' -and $_.CommandLine -match 'tModLoader.dll' -and $_.CommandLine -notmatch '-terrariasteamclient'
}
if ($running) { throw 'An existing tModLoader process is running. Preserve that session before validation.' }
$outputRoot = Join-Path $workspace 'validation'
if ($Mode -ne 'core') {
    $journeyRoot = Join-Path $outputRoot 'player-journey'
    $runRoot = Join-Path $journeyRoot $Run
    if (Test-Path -LiteralPath $runRoot) { throw 'Use a fresh run name; existing test artifacts are preserved.' }
    New-Item -ItemType Directory -Path $runRoot -Force | Out-Null
    $source = Join-Path $outputRoot 'stage1\live\core-20261003\AE2ValidationCore20261003'
    if ($Mode -eq 'interactions') {
        if (-not $SourceWorld) { throw 'Copied-world interactions require an explicit source .wld.' }
        $sourceFile = (Resolve-Path -LiteralPath $SourceWorld).Path
        $worldRoot = [IO.Path]::GetFullPath((Join-Path $saveRoot 'Worlds')) + [IO.Path]::DirectorySeparatorChar
        if (-not $sourceFile.StartsWith($worldRoot, [StringComparison]::OrdinalIgnoreCase) -or [IO.Path]::GetExtension($sourceFile) -ne '.wld') {
            throw 'The read-only snapshot source must be an existing .wld under the actual Worlds directory.'
        }
        $source = $sourceFile.Substring(0, $sourceFile.Length - 4)
    }
    Copy-Item -LiteralPath ($source + '.wld') -Destination (Join-Path $runRoot 'sandbox.wld')
    Copy-Item -LiteralPath ($source + '.twld') -Destination (Join-Path $runRoot 'sandbox.twld')
    if ($Mode -eq 'interactions') {
        @{ WorldHash = (Get-FileHash -LiteralPath (Join-Path $runRoot 'sandbox.wld')).Hash;
           ModWorldHash = (Get-FileHash -LiteralPath (Join-Path $runRoot 'sandbox.twld')).Hash } |
            ConvertTo-Json | Set-Content -LiteralPath (Join-Path $runRoot 'snapshot.json')
    }
}
$start = [System.Diagnostics.ProcessStartInfo]::new()
$start.FileName = Join-Path $tml 'dotnet\dotnet.exe'
$start.WorkingDirectory = $tml
$start.UseShellExecute = $false
$start.CreateNoWindow = $true
$start.ArgumentList.Add((Join-Path $tml 'tModLoader.dll'))
$start.ArgumentList.Add('-tmlsavedirectory')
$start.ArgumentList.Add($saveRoot)
# Steam discovery is required: -nosteam would exclude the Workshop mod pack.
foreach ($key in @($start.Environment.Keys | Where-Object { $_ -like 'AE2_*' })) {
    $start.Environment.Remove($key) | Out-Null
}
if ($Mode -eq 'core') {
    $start.Environment['AE2_RUN_REGRESSION'] = '1'
    $start.Environment['AE2_UI_REGRESSION'] = '1'
} else {
    $start.Environment['AE2_PLAYER_JOURNEY'] = $Run
    if ($Mode -eq 'interactions') { $start.Environment['AE2_JOURNEY_COPIED_WORLD'] = '1' }
}
Copy-Item -LiteralPath (Join-Path $saveRoot 'Mods\enabled.json') -Destination (Join-Path $outputRoot ($Run + '-enabled.json'))
$process = [System.Diagnostics.Process]::Start($start)
$process.Id | Set-Content -LiteralPath (Join-Path $outputRoot ($Run + '.pid'))
[pscustomobject]@{ ProcessId = $process.Id; Mode = $Mode; Run = $Run; SaveRoot = $saveRoot }
