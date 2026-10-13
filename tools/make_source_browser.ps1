# Builds an in-conversation source browser fragment for the AE2 for Terraria mod.
$ErrorActionPreference = 'Stop'

$modDir  = 'C:\Users\ROG\Documents\ChatGPT\ae2 for tr\AE2ForTerraria'
$outDir  = 'C:\Users\ROG\.codex\visualizations\2026\09\16\01a0aa66-33bd-7aa2-b49c-9e190be16388'
$outFile = Join-Path $outDir 'ae2-source-browser.html'

$modules = @(
    @{ Key = 'entry';    Title = '入口与配置' }
    @{ Key = 'core';     Title = '核心数据' }
    @{ Key = 'net';      Title = '网络与同步' }
    @{ Key = 'systems';  Title = '数据索引' }
    @{ Key = 'progress'; Title = '进度与来源' }
    @{ Key = 'tiles';    Title = '方块' }
    @{ Key = 'items';    Title = '物品与成就' }
    @{ Key = 'ui';       Title = '界面' }
)

$netFiles = @(
    'Systems/MENet.cs',
    'Systems/MENet.Audit.cs',
    'Systems/MENet.Client.cs',
    'Systems/MENet.Crafting.cs',
    'Systems/MENet.Ledger.cs',
    'Systems/MENet.Network.cs',
    'Systems/MENet.Serialization.cs',
    'Systems/MENet.Server.cs',
    'Systems/MENet.Storage.cs',
    'Systems/MENetwork.cs',
    'Systems/NetworkRegistry.cs',
    'Systems/NetworkHistory.cs',
    'Systems/WirelessRegistry.cs',
    'Systems/MEPlayer.cs',
    'Systems/InventoryScope.cs',
    'Systems/MEBlocks.cs',
    'Systems/MEBlockWatcher.cs'
)

function Get-ModuleKey([string]$rel) {
    if ($netFiles -contains $rel) { return 'net' }
    if ($rel -like 'Core/*') { return 'core' }
    if ($rel -like 'Progression/*' -or $rel -like 'Acquisition/*') { return 'progress' }
    if ($rel -like 'UI/*') { return 'ui' }
    if ($rel -like 'Content/Tiles/*') { return 'tiles' }
    if ($rel -like 'Content/Items/*' -or $rel -like 'Content/Achievements/*') { return 'items' }
    if ($rel -like 'Systems/*') { return 'systems' }
    return 'entry'
}

function Get-Lang([string]$ext) {
    switch ($ext.ToLower()) {
        '.cs'     { return 'cs' }
        '.csproj' { return 'text' }
        '.hjson'  { return 'text' }
        '.md'     { return 'text' }
        default   { return 'text' }
    }
}

function ConvertTo-Markup([string]$text) {
    return ($text -replace '&', '&amp;' -replace '<', '&lt;' -replace '>', '&gt;')
}

$exts = @('.cs', '.csproj', '.hjson', '.md', '.txt')

$records = @()
foreach ($file in (Get-ChildItem -LiteralPath $modDir -Recurse -File | Sort-Object FullName)) {
    if ($exts -notcontains $file.Extension.ToLower()) { continue }

    $rel = $file.FullName.Substring($modDir.Length + 1) -replace '\\', '/'
    $text = ([System.IO.File]::ReadAllText($file.FullName, [System.Text.Encoding]::UTF8)) -replace "`r`n", "`n" -replace "`r", "`n"
    $text = $text.TrimEnd("`n")

    $records += [PSCustomObject]@{
        Rel     = $rel
        Name    = $file.Name
        Module  = Get-ModuleKey $rel
        Lang    = Get-Lang $file.Extension
        Lines   = ($text -split "`n").Count
        Text    = $text
    }
}

# Tabs + panels with one tile per file.
$tabs   = [System.Text.StringBuilder]::new()
$panels = [System.Text.StringBuilder]::new()
$firstFile = $null

for ($m = 0; $m -lt $modules.Count; $m++) {
    $module = $modules[$m]
    $files = $records | Where-Object { $_.Module -eq $module.Key } | Sort-Object Rel
    if (-not $files -or $files.Count -eq 0) { continue }

    $tabId = "ae2s-tab-$($module.Key)"
    $panelId = "ae2s-panel-$($module.Key)"
    $active = if ($m -eq 0) { ' active' } else { '' }
    $selected = if ($m -eq 0) { 'true' } else { 'false' }

    [void]$tabs.AppendLine("    <button class=""nav-link$active"" id=""$tabId"" role=""tab"" aria-controls=""$panelId"" aria-selected=""$selected"" type=""button"">$($module.Title)</button>")

    $hiddenAttr = if ($m -eq 0) { '' } else { ' hidden' }
    [void]$panels.AppendLine("  <div id=""$panelId"" role=""tabpanel"" aria-labelledby=""$tabId""$hiddenAttr>")
    [void]$panels.AppendLine('    <div class="viz-grid">')

    foreach ($file in $files) {
        $pressed = 'false'
        if ($null -eq $firstFile) {
            $firstFile = $file
            $pressed = 'true'
        }

        $label = ConvertTo-Markup $file.Name
        [void]$panels.AppendLine("      <button class=""btn viz-tile ae2s-tile"" type=""button"" data-file=""$($file.Rel)"" aria-pressed=""$pressed"">$label</button>")
    }

    [void]$panels.AppendLine('    </div>')
    [void]$panels.AppendLine('  </div>')
}

# Hidden store holding every file verbatim.
$store = [System.Text.StringBuilder]::new()
foreach ($record in ($records | Sort-Object Rel)) {
    $escaped = ConvertTo-Markup $record.Text
    [void]$store.AppendLine("<div data-file=""$($record.Rel)"" data-lang=""$($record.Lang)"" data-lines=""$($record.Lines)"">$escaped</div>")
}

$totalLines = ($records | Measure-Object -Property Lines -Sum).Sum
$firstLabel = ConvertTo-Markup $firstFile.Name

$head = @'
<div id="ae2-source-browser">
<style>
#ae2-source-browser { display: block; }
#ae2-source-browser .ae2s-panels { margin-top: 0.75rem; }
#ae2-source-browser .ae2s-code { margin: 0.5rem 0 0; max-height: 34rem; overflow: auto; }
#ae2-source-browser .ae2s-code code { display: block; counter-reset: ae2s-line; white-space: pre; }
#ae2-source-browser .ae2s-l { display: block; counter-increment: ae2s-line; }
#ae2-source-browser .ae2s-l::before { content: counter(ae2s-line); display: inline-block; width: 2.75em; padding-right: 0.75em; text-align: right; color: var(--muted-foreground); -webkit-user-select: none; user-select: none; }
#ae2-source-browser .ae2s-kw { color: var(--blue); }
#ae2-source-browser .ae2s-ty { color: var(--purple); }
#ae2-source-browser .ae2s-st { color: var(--green); }
#ae2-source-browser .ae2s-nu { color: var(--orange); }
#ae2-source-browser .ae2s-cm { color: var(--muted-foreground); }
</style>

'@

$viewer = @'
<div class="viz-row" aria-live="polite">
  <code id="ae2s-path">__PATH__</code>
  <span class="text-small text-muted" id="ae2s-meta">__LINES__ 行</span>
</div>
<div class="viz-controls">
  <button class="btn" id="ae2s-copy" type="button">复制本文件</button>
  <button class="btn" id="ae2s-copy-all" type="button">复制全部源码</button>
</div>
<pre class="ae2s-code"><code id="ae2s-code"></code></pre>

'@

$script = @'
<script>
(function () {
  var root = document.getElementById('ae2-source-browser');
  if (!root) { return; }

  var store = document.getElementById('ae2s-store');
  var codeEl = document.getElementById('ae2s-code');
  var pathEl = document.getElementById('ae2s-path');
  var metaEl = document.getElementById('ae2s-meta');
  var copyBtn = document.getElementById('ae2s-copy');
  var copyAllBtn = document.getElementById('ae2s-copy-all');
  if (!store || !codeEl) { return; }

  var FIRST_FILE = '__PATH__';
  var files = {};
  var order = [];
  var nodes = store.querySelectorAll('[data-file]');
  for (var i = 0; i < nodes.length; i++) {
    var node = nodes[i];
    var path = node.getAttribute('data-file');
    files[path] = {
      path: path,
      lang: node.getAttribute('data-lang') || 'text',
      lines: parseInt(node.getAttribute('data-lines'), 10) || 0,
      text: node.textContent.replace(/\r\n/g, '\n')
    };
    order.push(path);
  }

  function esc(s) {
    return s.replace(/&/g, '&amp;').replace(/</g, '&lt;').replace(/>/g, '&gt;');
  }

  var KEYWORDS = ['abstract','as','async','await','base','bool','break','byte','case','catch','char','checked','class','const','continue','decimal','default','delegate','do','double','else','enum','event','explicit','extern','false','finally','fixed','float','for','foreach','get','global','goto','if','implicit','in','int','interface','internal','is','lock','long','nameof','namespace','new','null','object','operator','out','override','params','partial','private','protected','public','readonly','record','ref','return','sbyte','sealed','set','short','sizeof','stackalloc','static','string','struct','switch','this','throw','true','try','typeof','uint','ulong','unchecked','unsafe','ushort','using','value','var','virtual','void','volatile','when','where','while','yield'];
  var KW = {};
  for (var k = 0; k < KEYWORDS.length; k++) { KW[KEYWORDS[k]] = true; }

  function wrap(cls, s) { return '<span class="' + cls + '">' + esc(s) + '</span>'; }

  function highlightLine(line, state) {
    var out = '';
    var i = 0;
    while (i < line.length) {
      if (state.block) {
        var close = line.indexOf('*/', i);
        if (close < 0) { out += wrap('ae2s-cm', line.slice(i)); i = line.length; break; }
        out += wrap('ae2s-cm', line.slice(i, close + 2));
        i = close + 2;
        state.block = false;
        continue;
      }
      if (state.text) {
        var quote = line.indexOf('"', i);
        if (quote < 0) { out += wrap('ae2s-st', line.slice(i)); i = line.length; break; }
        if (line.charAt(quote + 1) === '"') { out += wrap('ae2s-st', line.slice(i, quote + 2)); i = quote + 2; continue; }
        out += wrap('ae2s-st', line.slice(i, quote + 1));
        i = quote + 1;
        state.text = false;
        continue;
      }

      var c = line.charAt(i);

      if (c === '/' && line.charAt(i + 1) === '/') { out += wrap('ae2s-cm', line.slice(i)); break; }
      if (c === '/' && line.charAt(i + 1) === '*') { out += wrap('ae2s-cm', '/*'); state.block = true; i += 2; continue; }
      if (c === '@' && line.charAt(i + 1) === '"') { out += wrap('ae2s-st', '@"'); state.text = true; i += 2; continue; }

      if (c === '"' || c === "'") {
        var j = i + 1;
        while (j < line.length) {
          if (line.charAt(j) === '\\') { j += 2; continue; }
          if (line.charAt(j) === c) { j++; break; }
          j++;
        }
        out += wrap('ae2s-st', line.slice(i, j));
        i = j;
        continue;
      }

      if (c >= '0' && c <= '9' && !/[A-Za-z0-9_]/.test(line.charAt(i - 1))) {
        var mnum = /^(0[xX][0-9a-fA-F]+|\d+(\.\d+)?([eE][+-]?\d+)?[fFdDmMuUlL]*)/.exec(line.slice(i));
        if (mnum) { out += wrap('ae2s-nu', mnum[0]); i += mnum[0].length; continue; }
      }

      if (/[A-Za-z_]/.test(c)) {
        var mword = /^[A-Za-z_][A-Za-z0-9_]*/.exec(line.slice(i));
        var word = mword[0];
        if (KW[word]) { out += wrap('ae2s-kw', word); }
        else if (/^[A-Z]/.test(word)) { out += wrap('ae2s-ty', word); }
        else { out += esc(word); }
        i += word.length;
        continue;
      }

      out += esc(c);
      i++;
    }
    return out;
  }

  function renderCode(text, lang) {
    var lines = text.split('\n');
    if (lines.length > 1 && lines[lines.length - 1] === '') { lines.pop(); }
    var html = '';
    var state = { block: false, text: false };
    for (var n = 0; n < lines.length; n++) {
      var body = lang === 'cs' ? highlightLine(lines[n], state) : esc(lines[n]);
      html += '<span class="ae2s-l">' + body + '</span>';
    }
    return html;
  }

  function select(path) {
    var record = files[path];
    if (!record) { return; }

    var tiles = root.querySelectorAll('button.ae2s-tile');
    for (var t = 0; t < tiles.length; t++) {
      var on = tiles[t].getAttribute('data-file') === path;
      tiles[t].setAttribute('aria-pressed', on ? 'true' : 'false');
      if (on) { tiles[t].classList.add('is-selected'); } else { tiles[t].classList.remove('is-selected'); }
    }

    pathEl.textContent = record.path;
    metaEl.textContent = record.lines + ' 行';
    codeEl.innerHTML = renderCode(record.text, record.lang);

    var pane = codeEl.parentNode;
    if (pane && pane.scrollTo) { pane.scrollTo(0, 0); }
  }

  for (var t2 = 0; t2 < root.querySelectorAll('button.ae2s-tile').length; t2++) {
    var tile = root.querySelectorAll('button.ae2s-tile')[t2];
    tile.addEventListener('click', function () { select(this.getAttribute('data-file')); });
  }

  function copyText(text) {
    if (navigator.clipboard && navigator.clipboard.writeText) { return navigator.clipboard.writeText(text); }
    return Promise.reject(new Error('clipboard unavailable'));
  }

  function flash(button, ok) {
    var label = button.textContent;
    button.textContent = ok ? '已复制' : '复制失败';
    window.setTimeout(function () { button.textContent = label; }, 1500);
  }

  if (copyBtn) {
    copyBtn.addEventListener('click', function () {
      var record = files[pathEl.textContent];
      if (!record) { return; }
      copyText(record.text).then(function () { flash(copyBtn, true); }, function () { flash(copyBtn, false); });
    });
  }

  if (copyAllBtn) {
    copyAllBtn.addEventListener('click', function () {
      var parts = [];
      for (var n = 0; n < order.length; n++) {
        var record = files[order[n]];
        parts.push('===== ' + record.path + ' =====\n' + record.text);
      }
      copyText(parts.join('\n\n')).then(function () { flash(copyAllBtn, true); }, function () { flash(copyAllBtn, false); });
    });
  }

  select(FIRST_FILE);
})();
</script>
'@

$sb = [System.Text.StringBuilder]::new()
[void]$sb.Append($head)
[void]$sb.AppendLine('<div class="nav nav-pills" role="tablist" aria-label="源码模块">')
[void]$sb.Append($tabs.ToString())
[void]$sb.AppendLine('</div>')
[void]$sb.AppendLine('<div class="ae2s-panels">')
[void]$sb.Append($panels.ToString())
[void]$sb.AppendLine('</div>')
[void]$sb.AppendLine('<hr>')
[void]$sb.Append(($viewer.Replace('__PATH__', $firstFile.Rel)).Replace('__LINES__', $firstFile.Lines))
[void]$sb.AppendLine('<div id="ae2s-store" hidden>')
[void]$sb.Append($store.ToString())
[void]$sb.AppendLine('</div>')
[void]$sb.Append($script.Replace('__PATH__', $firstFile.Rel))
[void]$sb.AppendLine('</div>')

$encoding = New-Object System.Text.UTF8Encoding($false)
[System.IO.File]::WriteAllText($outFile, $sb.ToString(), $encoding)

Write-Output "wrote $outFile"
Write-Output ("files: " + $records.Count)
Write-Output ("total lines: " + $totalLines)
Write-Output ("bytes: " + (Get-Item -LiteralPath $outFile).Length)
