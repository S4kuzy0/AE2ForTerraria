# Removes the throwaway preview files created while checking the source browser.
$ErrorActionPreference = 'Stop'

$tools = 'C:\Users\ROG\Documents\ChatGPT\ae2 for tr\tools'

$files = @(
    '_preview.html',
    '_preview2.html',
    '_frag_test.html',
    '_jscheck.js',
    '_shot1.png',
    '_shot2.png',
    '_shot3.png',
    '_shot4.png'
)

foreach ($name in $files) {
    $path = Join-Path $tools $name
    if (Test-Path -LiteralPath $path) {
        Remove-Item -LiteralPath $path -Force
        Write-Output "removed $name"
    }
}

$profile = Join-Path $tools '_chromeprofile'
if (Test-Path -LiteralPath $profile) {
    $resolved = (Resolve-Path -LiteralPath $profile).Path
    if (-not $resolved.StartsWith($tools, [System.StringComparison]::OrdinalIgnoreCase)) {
        throw "refusing to delete outside tools: $resolved"
    }

    Remove-Item -LiteralPath $resolved -Recurse -Force
    Write-Output "removed _chromeprofile"
}

Get-ChildItem -LiteralPath $tools | Select-Object Name, Length
