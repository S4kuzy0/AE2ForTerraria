using System;
using System.Collections.Generic;
using AE2ForTerraria.Core;
namespace Terraria
{
 public class Item {
  public int type, prefix, stack, maxStack = 2;
  public string Data;
  public bool IsAir => type == 0 || stack <= 0;
  public Item(int type = 0, int stack = 1) { this.type = type; this.stack = type == 0 ? 0 : stack; }
  public Item Clone() => (Item)MemberwiseClone();
  public void TurnToAir() { type = 0; stack = 0; Data = null; }
 }
 public class Chest { public Item[] item = new Item[40]; }
 public static class Main { public static int netMode; public static ulong GameUpdateCount; public static Chest[] chest; }
 public static class NetMessage { public static void SendData(int id, int a, int b, object c, int d, int e) {} }
}
namespace Terraria.DataStructures { public struct Point16 { public int X,Y; } }
namespace Terraria.ID { public static class NetmodeID { public const int Server = 2, MultiplayerClient = 1; } public static class MessageID { public const int SyncChestItem = 1; } }
namespace AE2ForTerraria
{
 public sealed class AE2ForTerraria { public static AE2ForTerraria Instance = new(); public TestLogger Logger = new(); }
 public sealed class TestLogger { public int Errors; public void Error(string text, Exception exception) { Errors++; Console.WriteLine("DIAGNOSTIC " + text + " " + exception.Message); } }
}
namespace AE2ForTerraria.Core
{
 public interface IMEStorage { StorageLedger Ledger {get;} }
 public sealed class HarnessStorage(StorageLedger ledger) : IMEStorage { public StorageLedger Ledger {get;} = ledger; }
 public static class ItemPersistence {
  public static string Fault;
  public static int Creates, WoodReads;
  public static Terraria.Item MutateTarget;
  public static ItemKey KeyOf(Terraria.Item item) {
   if (item == null || item.IsAir) return ItemKey.None;
   if (item.type == 1 && ++WoodReads == 2)
   {
    if (Fault == "stack identity") throw new InvalidOperationException("injected identity failure");
    if (Fault == "mutating identity") { MutateTarget.Data = "next"; throw new InvalidOperationException("injected identity mutation then failure"); }
   }
   return new ItemKey(item.type,item.prefix,data:item.Data);
  }
  public static Terraria.Item Create(ItemKey key, int stack = 1) {
   if (++Creates == 2 && Fault == "second reconstruction") throw new InvalidOperationException("injected reconstruction failure");
   if (key.Data == "invalid") Convert.FromBase64String(key.Data);
   return new Terraria.Item(key.Type,stack) { prefix=key.Prefix, Data=key.Data, maxStack = Fault == "lower stack limit" ? 1 : 2 };
  }
 }
}
namespace AE2ForTerraria.Systems
{
 public static class SavedItemGuard { public static bool IsValidType(int type) => type > 0; public static int MaxStackOf(int type) => 2; }
 public static class MENetwork {
  public static List<IMEStorage> Storage;
  public static List<IMEStorage> CollectStorage(int x, int y) => Storage;
  public static List<Terraria.DataStructures.Point16> Collect(int x,int y) => new();
  public static Terraria.DataStructures.Point16 CanonicalAnchor(List<Terraria.DataStructures.Point16> blocks) => default;
 }
 public static class NetworkRegistry { public static bool IsLocked(Terraria.DataStructures.Point16 a,int b) => false; }
}
