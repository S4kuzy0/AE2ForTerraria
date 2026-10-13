using System;
using System.Collections.Generic;
using AE2ForTerraria.Content.Tiles;
using AE2ForTerraria.Core;
using Terraria;
using Terraria.ID;
using Terraria.ModLoader;
using Terraria.ModLoader.IO;

namespace AE2ForTerraria.Systems;

public static class BusScanRegression
{
    private static Item CustomWood(int charge)
    {
        TagCompound saved = ItemIO.Save(new Item(ItemID.Wood));
        saved["globalData"] = new List<TagCompound>
        {
            new() { ["mod"] = "AE2RegressionMissingGlobal", ["name"] = "MutableState",
                ["data"] = new TagCompound { ["charge"] = charge } }
        };
        return ItemIO.Load(saved);
    }

    public static void Run(Action<bool, string> check)
    {
        using var world = new RegressionWorld();
        Chest chest = world.PlaceChest(40, 40);
        var bus = world.Place(new MEStorageBusEntity(), ModContent.TileType<MEStorageBusTile>(), 42, 40);
        var storage = world.Place(new MEStorageUnitEntity(), ModContent.TileType<MEStorageUnitTile>(), 43, 40);
        bus.SetExportMode(true);
        for (int slot = 0; slot < chest.item.Length; slot++)
        {
            chest.item[slot] = new Item(ItemID.StoneBlock);
            chest.item[slot].stack = chest.item[slot].maxStack;
        }
        Item first = CustomWood(1), second = CustomWood(2);
        ItemKey firstKey = ItemKey.FromItem(first), secondKey = ItemKey.FromItem(second);
        check(firstKey.Data != null && secondKey.Data != null && !firstKey.Equals(secondKey)
            && first.type == second.type && first.prefix == second.prefix,
            "bus scan fixture has distinct custom data with identical type and prefix");
        chest.item[0] = first;
        storage.Ledger.Restore(secondKey, 4);
        for (int tick = 0; tick < 30; tick++) bus.Update();
        check(storage.Ledger.GetAmount(secondKey) == 4 && chest.item[0].stack == 1,
            "bus scan refuses same-type stacks with different custom data");

        // Mutate the same instance without changing type/prefix/stack: the next scan must not reuse its old identity.
        ItemIO.Load(chest.item[0], ItemIO.Save(second));
        check(ItemKey.FromItem(chest.item[0]).Equals(secondKey), "custom data mutation is observable without replacing the stack");
        for (int tick = 0; tick < 30; tick++) bus.Update();
        check(storage.Ledger.GetAmount(secondKey) == 0 && chest.item[0].stack == 5
            && ItemKey.FromItem(chest.item[0]).Equals(secondKey),
            "bus scan expires identities between periods even if type prefix and stack were unchanged");

        chest.item[0] = CustomWood(2);
        storage.Ledger.Restore(firstKey, 2); storage.Ledger.Restore(secondKey, 3);
        for (int tick = 0; tick < 30; tick++) bus.Update();
        check(storage.Ledger.GetAmount(firstKey) == 2 && storage.Ledger.GetAmount(secondKey) == 0
            && chest.item[0].stack == 4 && ItemKey.FromItem(chest.item[0]).Equals(secondKey),
            "candidate scan preserves identity while reusing one slot across multiple custom variants");

        chest.item[10].TurnToAir();
        for (int tick = 0; tick < 30; tick++) bus.Update();
        check(storage.Ledger.StoredTotalAmount == 0 && chest.item[10].stack == 2
            && ItemKey.FromItem(chest.item[10]).Equals(firstKey) && chest.item[0].stack == 4,
            "filling a new slot retains custom data and never merges a different variant");
    }
}
