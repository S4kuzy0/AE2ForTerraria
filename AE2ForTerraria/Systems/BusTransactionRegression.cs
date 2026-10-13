using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.ExceptionServices;
using AE2ForTerraria.Content.Tiles;
using AE2ForTerraria.Core;
using Terraria;
using Terraria.ID;
using Terraria.ModLoader;

namespace AE2ForTerraria.Systems;

/// <summary>In-memory exception tests. The public-entry probe also compiles against the pre-fix bus.</summary>
public static class BusTransactionRegression
{
    public static void Run(Action<bool, string> check)
    {
        using var world = new RegressionWorld();
        Chest chest = world.PlaceChest(40, 40);
        var bus = world.Place(new MEStorageBusEntity(), ModContent.TileType<MEStorageBusTile>(), 42, 40);
        var storage = world.Place(new MEStorageUnitEntity(), ModContent.TileType<MEStorageUnitTile>(), 43, 40);
        bus.SetExportMode(true);
        var damaged = new ItemKey(ItemID.Wood, data: "injected invalid serialized item");
        storage.Ledger.Restore(damaged, 3);
        Item[] originals = chest.item.ToArray();
        int version = storage.Ledger.Version;
        bool threw = false;
        long exported = -1;
        try { exported = MEStorageBusEntity.ExportNetworkToChests(storage.Position.X, storage.Position.Y); }
        catch (FormatException) { threw = true; }
        check(storage.Ledger.GetAmount(damaged) == 3 && storage.Ledger.Version == version
            && chest.item.Select((item, slot) => ReferenceEquals(item, originals[slot]) && item.IsAir).All(value => value),
            "bus public export reconstruction failure preserves source ledger and every chest slot; source remaining=" + storage.Ledger.GetAmount(damaged));
        check(!threw && exported == 0, "bus public export diagnoses a failed candidate and never counts it as a successful transfer");
        for (int tick = 0; tick < 60; tick++) bus.Update();
        check(storage.Ledger.GetAmount(damaged) == 3 && storage.Ledger.Version == version
            && chest.item.Select((item, slot) => ReferenceEquals(item, originals[slot]) && item.IsAir).All(value => value),
            "bus automatic update diagnoses repeated item hook failures without escaping the world tick or changing stock");

        ItemKey stone = ItemKey.Crafted(ItemID.StoneBlock);
        storage.Ledger.Clear(); storage.Ledger.Restore(damaged, 3); storage.Ledger.Restore(stone, 7);
        version = storage.Ledger.Version;
        check(storage.Ledger.Entries.First().Key.Equals(damaged),
            "mixed bus probe starts with damaged data before healthy stock");
        for (int tick = 0; tick < 30; tick++) bus.Update();
        check(storage.Ledger.GetAmount(damaged) == 3 && storage.Ledger.GetAmount(stone) == 0
            && storage.Ledger.Version == version + 1
            && chest.item.Where(item => item.type == ItemID.StoneBlock).Sum(item => item.stack) == 7
            && chest.item.All(item => item.type != ItemID.Wood),
            "bus actual Update skips a broken first candidate and exports later healthy stock without debiting the broken entry");
        for (int tick = 0; tick < 60; tick++) bus.Update();
        check(storage.Ledger.GetAmount(damaged) == 3 && storage.Ledger.Version == version + 1
            && chest.item.Where(item => item.type == ItemID.StoneBlock).Sum(item => item.stack) == 7,
            "repeated bus updates retain damaged stock without duplicating already exported healthy items");

        storage.Ledger.Clear(); storage.Ledger.Restore(damaged, 3); storage.Ledger.Restore(stone, 7);
        for (int slot = 0; slot < chest.item.Length; slot++) chest.item[slot] = new Item();
        exported = MEStorageBusEntity.ExportNetworkToChests(storage.Position.X, storage.Position.Y);
        check(exported == 7 && storage.Ledger.GetAmount(damaged) == 3 && storage.Ledger.GetAmount(stone) == 0
            && chest.item.Where(item => item.type == ItemID.StoneBlock).Sum(item => item.stack) == 7,
            "manual bus export uses the same candidate isolation and returns only healthy stock actually moved");

        // The seam invokes the same whole prepare/commit operation used by ExportInto. It injects hooks,
        // not private active state, and allows errors after some results have already been prepared.
        foreach (string fault in new[] { "second reconstruction", "stack identity", "late identity" })
        {
            storage.Ledger.Clear();
            ItemKey key = ItemKey.Crafted(ItemID.Wood);
            storage.Ledger.Restore(key, 6);
            ResetChest(chest);
            originals = chest.item.ToArray();
            ItemKey[] identities = chest.item.Select(ItemKey.FromItem).ToArray();
            int[] amounts = chest.item.Select(item => item.stack).ToArray();
            version = storage.Ledger.Version;
            int creates = 0, reads = 0;
            Func<ItemKey, Item> create = candidate =>
            {
                if (++creates == 2 && fault == "second reconstruction") throw new InvalidOperationException("injected load hook");
                return candidate.CreateItem();
            };
            Func<Item, ItemKey> identify = item =>
            {
                ++reads;
                if ((fault == "stack identity" && reads == 1) || (fault == "late identity" && reads == 3))
                    throw new InvalidOperationException("injected identity hook");
                return ItemKey.FromItem(item);
            };
            threw = false;
            object scan = CreateScan(chest, identify, create);
            try { Transfer(scan, storage.Ledger, key, 5, 2, out _); }
            catch (InvalidOperationException ex) when (ex.Message.StartsWith("injected ", StringComparison.Ordinal)) { threw = true; }
            check(threw && storage.Ledger.GetAmount(key) == 6 && storage.Ledger.Version == version
                && chest.item.Select((item, slot) => ReferenceEquals(item, originals[slot])
                    && item.stack == amounts[slot] && ItemKey.FromItem(item).Equals(identities[slot])).All(value => value),
                "bus " + fault + " failure leaves all ledger quantities and original chest slots untouched");
            int moved = Transfer(scan, storage.Ledger, key, 5, 2, out List<int> touched);
            check(moved == 5 && storage.Ledger.GetAmount(key) == 1 && touched.SequenceEqual(new[] { 0, 1, 2 })
                && chest.item[0].stack == 2 && chest.item[1].stack == 2 && chest.item[2].stack == 2,
                "bus retries after " + fault + " without a stale scan or duplicate partial results");
        }

        storage.Ledger.Clear();
        ItemKey ordinary = ItemKey.Crafted(ItemID.Wood);
        storage.Ledger.Restore(ordinary, 6);
        ResetChest(chest);
        chest.item[0].maxStack = 1;
        object limitedScan = CreateScan(chest, null, candidate =>
        {
            Item item = candidate.CreateItem();
            item.maxStack = 1;
            return item;
        });
        int limited = Transfer(limitedScan, storage.Ledger, ordinary, 5, 9999, out List<int> limitedSlots);
        check(limited == 2 && storage.Ledger.GetAmount(ordinary) == 4 && limitedSlots.SequenceEqual(new[] { 1, 2 })
            && chest.item[0].stack == 1 && chest.item[1].stack == 1 && chest.item[2].stack == 1,
            "bus respects actual stack limits on existing and reconstructed custom items below the default type limit");
    }

    private static void ResetChest(Chest chest)
    {
        for (int slot = 0; slot < chest.item.Length; slot++)
        {
            chest.item[slot] = new Item(ItemID.StoneBlock);
            chest.item[slot].stack = chest.item[slot].maxStack;
        }
        chest.item[0] = new Item(ItemID.Wood, 1);
        chest.item[1] = new Item();
        chest.item[2] = new Item();
    }

    private static object CreateScan(Chest chest, Func<Item, ItemKey> identify, Func<ItemKey, Item> create)
    {
        Type type = typeof(MEStorageBusEntity).GetNestedType("ChestScan", BindingFlags.NonPublic)
            ?? throw new InvalidOperationException("Transactional chest scan was not installed.");
        return Activator.CreateInstance(type, new object[] { chest, identify, create });
    }

    private static int Transfer(object scan, StorageLedger ledger, ItemKey key, int amount, int maxStack, out List<int> touched)
    {
        MethodInfo method = typeof(MEStorageBusEntity).GetMethod("TransferToChest", BindingFlags.Static | BindingFlags.NonPublic)
            ?? throw new InvalidOperationException("Transactional chest export was not installed.");
        object[] args = { scan, ledger, key, amount, maxStack, null };
        try
        {
            int moved = (int)method.Invoke(null, args);
            touched = (List<int>)args[5];
            return moved;
        }
        catch (TargetInvocationException ex) when (ex.InnerException != null)
        {
            ExceptionDispatchInfo.Capture(ex.InnerException).Throw();
            throw;
        }
    }
}
