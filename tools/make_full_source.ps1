# Concatenates the whole AE2ForTerraria source tree into one Markdown listing.
$ErrorActionPreference = 'Stop'

$root    = Split-Path $PSScriptRoot -Parent
$modDir  = Join-Path $root 'AE2ForTerraria'
$outDir = Join-Path $root 'work'
New-Item -ItemType Directory -Path $outDir -Force | Out-Null
$outFile = Join-Path $outDir 'AE2ForTerraria-FULL-SOURCE.md'

$exts = @('.cs', '.csproj', '.hjson', '.md', '.txt', '.json')

$files = Get-ChildItem -LiteralPath $modDir -Recurse -File |
    Where-Object { $exts -contains $_.Extension.ToLower() } |
    Sort-Object { $_.FullName }

$sb = [System.Text.StringBuilder]::new()

[void]$sb.AppendLine('# AE2ForTerraria - 全部源码 (full source dump)')
[void]$sb.AppendLine()
[void]$sb.AppendLine("生成时间: $(Get-Date -Format 'yyyy-MM-dd HH:mm')")
[void]$sb.AppendLine()
[void]$sb.AppendLine("文件数: $($files.Count)")
[void]$sb.AppendLine()
[void]$sb.AppendLine('## 文件索引')
[void]$sb.AppendLine()

foreach ($f in $files) {
    $rel = $f.FullName.Substring($modDir.Length + 1) -replace '\\', '/'
    [void]$sb.AppendLine("- [$rel](#$($rel.ToLower() -replace '[^a-z0-9]', '-'))")
}

[void]$sb.AppendLine()
[void]$sb.AppendLine('---')
[void]$sb.AppendLine()

$langOf = @{
    '.cs'     = 'csharp'
    '.csproj' = 'xml'
    '.hjson'  = 'json'
    '.json'   = 'json'
    '.md'     = 'markdown'
    '.txt'    = 'text'
}

foreach ($f in $files) {
    $rel  = $f.FullName.Substring($modDir.Length + 1) -replace '\\', '/'
    $lang = $langOf[$f.Extension.ToLower()]
    if (-not $lang) { $lang = 'text' }

    [void]$sb.AppendLine("## $rel")
    [void]$sb.AppendLine()
    $body = [System.IO.File]::ReadAllText($f.FullName, [System.Text.Encoding]::UTF8)
    $body = $body -replace "`r`n", "`n"

    [void]$sb.AppendLine('```' + $lang)
    [void]$sb.AppendLine($body)
    [void]$sb.AppendLine('```')
    [void]$sb.AppendLine()
}

[System.IO.File]::WriteAllText($outFile, $sb.ToString(), (New-Object System.Text.UTF8Encoding($false)))
Write-Output "wrote $outFile"
Write-Output ("files: " + $files.Count)
Write-Output ("bytes: " + (Get-Item -LiteralPath $outFile).Length)
