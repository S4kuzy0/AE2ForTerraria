param(
    [string]$TmlInstall = $env:TML_INSTALL,
    [string]$BuildRoot = '',
    [string]$OutputDirectory = ''
)
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
if (-not $TmlInstall) { throw 'Specify -TmlInstall or TML_INSTALL.' }
$tml = (Resolve-Path -LiteralPath $TmlInstall).Path
foreach ($name in @('tModLoader.dll', 'tMLMod.targets')) {
    if (-not (Test-Path -LiteralPath (Join-Path $tml $name))) { throw "Missing $name in tModLoader installation." }
}
if (-not $BuildRoot) { $BuildRoot = Join-Path $root ('work/build-' + [Guid]::NewGuid().ToString('N')) }
if (Test-Path -LiteralPath $BuildRoot) { throw 'BuildRoot must be a new directory; existing data is preserved.' }
if (-not $OutputDirectory) { $OutputDirectory = Join-Path $root 'dist' }
$build = [IO.Path]::GetFullPath($BuildRoot)
New-Item -ItemType Directory -Path $build | Out-Null
$output = [IO.Path]::GetFullPath($OutputDirectory)
New-Item -ItemType Directory -Path $output -Force | Out-Null
$version = ((Get-Content -LiteralPath (Join-Path $root 'AE2ForTerraria/build.txt') | Where-Object { $_ -match '^version\s*=' }) -split '=', 2)[1].Trim()
$package = Join-Path $output ("AE2ForTerraria-$version.tmod")
if (Test-Path -LiteralPath $package) { throw 'Output package already exists; choose a new OutputDirectory.' }
$dotnet = Join-Path $tml 'dotnet/dotnet.exe'
if (-not (Test-Path -LiteralPath $dotnet)) { $dotnet = (Get-Command dotnet -ErrorAction Stop).Source }
Push-Location $tml
$priorTmlInstall = $env:TML_INSTALL
try {
    $env:TML_INSTALL = $tml
    & $dotnet (Join-Path $tml 'tModLoader.dll') -build (Join-Path $root 'AE2ForTerraria') -tmlsavedirectory $build -nosteam
    if ($LASTEXITCODE -ne 0) { throw 'tModLoader build failed; preserve the build directory for diagnostics.' }
} finally {
    $env:TML_INSTALL = $priorTmlInstall
    Pop-Location
}
$built = Join-Path $build 'Mods/AE2ForTerraria.tmod'
if (-not (Test-Path -LiteralPath $built)) { throw 'No .tmod package was produced.' }
Copy-Item -LiteralPath $built -Destination $package
Copy-Item -LiteralPath (Join-Path $root 'LICENSE') -Destination (Join-Path $output 'LICENSE')
Copy-Item -LiteralPath (Join-Path $root 'AE2ForTerraria/ThirdPartyNotices.txt') -Destination (Join-Path $output 'ThirdPartyNotices.txt')
$digest = (Get-FileHash -LiteralPath $package -Algorithm SHA256).Hash.ToLowerInvariant()
($digest + '  ' + [IO.Path]::GetFileName($package)) | Set-Content -LiteralPath (Join-Path $output 'SHA256SUMS.txt') -Encoding ascii
[pscustomobject]@{ Package = $package; Version = $version; SHA256 = $digest; BuildRoot = $build }
