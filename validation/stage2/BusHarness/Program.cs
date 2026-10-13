using System;
using System.Linq;
using AE2ForTerraria.Core;
using AE2ForTerraria.Content.Tiles;
using Terraria;

int failed = 0, passed = 0;
void Check(bool value, string name) { Console.WriteLine((value ? "PASS " : "FAIL ") + name); if (value) passed++; else failed++; }
foreach (bool automatic in new[] { true, false })
{
 var ledger = new StorageLedger();
 var damaged = new ItemKey(1, data: "invalid");
 var ordinary = new ItemKey(2);
 ledger.Restore(damaged, 3); ledger.Restore(ordinary, 7);
 var chest = new Chest();
 for (int i = 0; i < chest.item.Length; i++) chest.item[i] = new Item();
 var bus = new SourceBus(); bus.Prepare(chest, ledger);
 ItemPersistence.Fault = "none"; ItemPersistence.Creates = 0; ItemPersistence.WoodReads = 0;
 Main.GameUpdateCount = 0; AE2ForTerraria.AE2ForTerraria.Instance.Logger.Errors = 0;
 bool threw = false;
 try
 {
  if (automatic) for (int tick = 0; tick < 90; tick++) { Main.GameUpdateCount++; bus.Update(); }
  else bus.Export(chest, ledger);
 }
 catch (Exception ex) { threw = true; Console.WriteLine("OBSERVED " + ex.GetType().Name + ": " + ex.Message); }
 Check(!threw && ledger.GetAmount(damaged) == 3 && ledger.GetAmount(ordinary) == 0
  && chest.item.Where(item => item.type == 2).Sum(item => item.stack) == 7,
  (automatic ? "actual Update/Run" : "manual export core") + " skips a malformed first entry and exports later good stock without debit of the bad entry");
 Check(AE2ForTerraria.AE2ForTerraria.Instance.Logger.Errors == 1,
  "mixed candidate " + (automatic ? "automatic" : "manual") + " failure has one bounded diagnostic");
 Console.WriteLine("OBSERVED mixed damaged=" + ledger.GetAmount(damaged) + " stone=" + ledger.GetAmount(ordinary));
}
foreach (string fault in new[] { "invalid payload", "second reconstruction", "stack identity", "none", "lower stack limit" })
{
 var ledger = new StorageLedger();
 var key = new ItemKey(1, data: fault == "invalid payload" ? "invalid" : null);
 ledger.Restore(key, 6);
 var chest = new Chest();
 for (int i = 0; i < chest.item.Length; i++) chest.item[i] = new Item(2, 2);
 chest.item[0] = fault == "invalid payload" ? new Item() : new Item(1);
 if (fault == "lower stack limit") chest.item[0].maxStack = 1;
 chest.item[1] = new Item(); chest.item[2] = new Item();
 var originals = chest.item.ToArray();
 var amounts = chest.item.Select(item => item.stack).ToArray();
 int version = ledger.Version;
 ItemPersistence.Fault = fault; ItemPersistence.Creates = 0; ItemPersistence.WoodReads = 0;
 bool threw = false;
 try { new SourceBus().Export(chest, ledger); }
 catch (Exception ex) when (ex is FormatException or InvalidOperationException) { threw = true; Console.WriteLine("OBSERVED " + ex.GetType().Name + ": " + ex.Message); }
 if (fault == "lower stack limit")
  Check(!threw && ledger.GetAmount(key) == 4 && chest.item.Take(3).All(item => item.stack == 1 && item.maxStack == 1),
   "actual existing and reconstructed stack limits override a larger default type limit");
 else if (fault == "none")
  Check(!threw && ledger.GetAmount(key) == 1 && chest.item[0].stack == 2 && chest.item[1].stack == 2 && chest.item[2].stack == 2,
   "successful partial export conserves stock across three target slots");
 else
  Check(!threw && ledger.GetAmount(key) == 6 && ledger.Version == version
   && chest.item.Select((item, i) => ReferenceEquals(item, originals[i]) && item.stack == amounts[i]).All(value => value),
   fault + " leaves source ledger and every original chest slot unchanged");
 Console.WriteLine("OBSERVED ledger=" + ledger.GetAmount(key) + " targetWood=" + chest.item.Where(item => item.type == 1).Sum(item => item.stack));
}
{
 var ledger = new StorageLedger();
 var before = new ItemKey(1, data: "before"); var next = new ItemKey(1, data: "next");
 ledger.Restore(before, 1); ledger.Restore(next, 1);
 var chest = new Chest();
 for (int i = 0; i < chest.item.Length; i++) chest.item[i] = new Item(2, 2);
 chest.item[0] = new Item(1) { Data = "before" };
 chest.item[1] = new Item(1) { Data = "unrelated" };
 ItemPersistence.MutateTarget = chest.item[0]; ItemPersistence.WoodReads = 0;
 ItemPersistence.Fault = "mutating identity"; Main.GameUpdateCount = 0;
 var bus = new SourceBus(); bus.Prepare(chest, ledger);
 for (int i = 0; i < 30; i++) { Main.GameUpdateCount++; bus.Update(); }
 Check(ledger.GetAmount(before) == 1 && ledger.GetAmount(next) == 0
  && chest.item[0].Data == "next" && chest.item[0].stack == 2 && chest.item[1].stack == 1,
  "FreeSpaceFor failure invalidates already cached identities before a later custom-data candidate continues");
 ItemPersistence.MutateTarget = null;
}
Console.WriteLine($"SUMMARY passed={passed} failed={failed}; exact bus export source body, lightweight Terraria stubs (not a tML load test)");
return failed == 0 ? 0 : 1;
