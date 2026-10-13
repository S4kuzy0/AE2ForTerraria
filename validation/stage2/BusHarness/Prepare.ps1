param([Parameter(Mandatory=$true)][string]$Source)
$sourceText = Get-Content -LiteralPath $Source -Raw
$exportStart = $sourceText.IndexOf('private void ExportInto(')
$exportEnd = $sourceText.IndexOf('public override void SaveData(', $exportStart)
if ($exportStart -lt 0 -or $exportEnd -lt 0) { throw 'Cannot locate unchanged bus export method region.' }
$methods = $sourceText.Substring($exportStart, $exportEnd - $exportStart)
$updateStart = $sourceText.IndexOf('public override void Update()')
$updateEnd = $sourceText.IndexOf('private IEnumerable<int> AdjacentChestIndices()', $updateStart)
if ($updateStart -lt 0 -or $updateEnd -lt 0) { throw 'Cannot locate actual bus Update and Run methods.' }
$updateMethods = $sourceText.Substring($updateStart, $updateEnd - $updateStart).Replace('public override void Update()', 'public void Update()')
$header = @'
using System;
using System.Collections.Generic;
using AE2ForTerraria.Core;
using AE2ForTerraria.Systems;
using Terraria;
using Terraria.DataStructures;
using Terraria.ID;
namespace AE2ForTerraria.Content.Tiles {
public class SourceBus {
 private const int IntervalTicks = 30;
 private int _timer;
 private ulong _nextErrorLogTick;
 public bool ExportMode => true;
 public Point16 Position;
 public bool Allows(int type) => true;
 public void Export(Chest chest, StorageLedger ledger) => ExportInto(0, chest, new List<IMEStorage> { new HarnessStorage(ledger) }, false);
 public void Prepare(Chest chest, StorageLedger ledger) { Main.chest = new[] { chest }; MENetwork.Storage = new List<IMEStorage> { new HarnessStorage(ledger) }; }
 private IEnumerable<int> AdjacentChestIndices() => new[] { 0 };
 private void ImportFrom(int index, Chest chest, List<IMEStorage> network) => throw new InvalidOperationException("Export harness must not import.");
'@
Set-Content -LiteralPath (Join-Path $PSScriptRoot 'SourceBus.cs') -Value ($header + [Environment]::NewLine + $updateMethods + $methods + [Environment]::NewLine + '}}') -Encoding utf8
Get-FileHash -LiteralPath $Source -Algorithm SHA256 | Format-List
