using System;
using System.Collections.Generic;
using AE2ForTerraria.Core;
using AE2ForTerraria.Content.Items;
using AE2ForTerraria.Content.Tiles;
using Terraria;
using Terraria.DataStructures;
using Terraria.ID;
using Terraria.ModLoader;

namespace AE2ForTerraria.Systems;

/// <summary>Shared single-player/server inventory actions. UI never reconstructs or destroys stored items.</summary>
public static class StorageOperations
{
    /// <summary>Called after authority/permission checks; client UI only sends an upgrade intent.</summary>
    public static bool TryUpgradeStorageUnit(MEStorageUnitEntity unit, Player player, int inventorySlot = -1)
    {
        int slot = inventorySlot >= 0 ? inventorySlot : player?.selectedItem ?? -1;
        if (Main.netMode == NetmodeID.MultiplayerClient || unit == null || player == null
            || slot < 0 || slot >= player.inventory.Length) return false;
        Item held = player.inventory[slot];
        if (held == null || held.IsAir || held.type != ModContent.ItemType<MEStorageUpgrade>()
            || !unit.TryUpgrade()) return false;
        if (--held.stack <= 0) held.TurnToAir();
        return true;
    }

    public static long DepositInventory(List<IMEStorage> storage, Player player, bool extras = false,
        int onlySlot = -1, int onlyType = 0, ulong slotMask = ulong.MaxValue, bool syncEquipment = true)
    {
        if (Main.netMode == NetmodeID.MultiplayerClient || storage == null || player == null) return 0;
        long moved = 0;
        int count = InventoryScope.DepositSlotCount(player, extras);
        for (int slot = 0; slot < count; slot++)
        {
            if ((onlySlot >= 0 && slot != onlySlot) || (slotMask & (1UL << slot)) == 0) continue;
            Item item = player.inventory[slot];
            if (item == null || item.IsAir || item.favorited || (onlyType > 0 && item.type != onlyType)) continue;
            long deposited = MENetwork.Deposit(storage, item);
            moved += deposited;
            if (syncEquipment && deposited > 0 && Main.netMode == NetmodeID.Server)
                NetMessage.SendData(MessageID.SyncEquipment, -1, -1, null, player.whoAmI, slot);
        }
        return moved;
    }

    public static long Withdraw(List<IMEStorage> storage, ItemKey key, long amount, Player player, bool ground = false)
    {
        if (storage == null || key.IsAir || amount <= 0 || player == null || !SavedItemGuard.IsValidType(key.Type)) return 0;
        if (!storage.Exists(unit => unit.Ledger.GetAmount(key) > 0)) return 0;
        Item template = key.CreateItem();
        int maxStack = Math.Max(1, template.maxStack);
        long taken = 0;
        // NewItem overwrites existing world drops when all slots are occupied. Never call it then.
        while (taken < amount)
        {
            long available = 0;
            foreach (var unit in storage) available = (long)Math.Min(long.MaxValue, (decimal)available + unit.Ledger.GetAmount(key));
            if (available <= 0 || Array.FindIndex(Main.item, 0, Main.maxItems, i => i == null || !i.active) < 0) break;
            int chunk = (int)Math.Min(Math.Min(available, amount - taken), maxStack);
            Item output = template.Clone();
            output.stack = chunk;
            int index = Item.NewItem(player.GetSource_Misc("AE2ForTerraria"), (int)player.Center.X,
                (int)player.Center.Y, 1, 1, output, true);
            if (index < 0 || index >= Main.maxItems) break;
            long extracted = MENetwork.TryWithdraw(storage, key, chunk);
            Main.item[index].stack = (int)extracted;
            if (extracted == 0) Main.item[index].active = false;
            if (ground) Main.item[index].noGrabDelay = 60;
            if (Main.netMode == NetmodeID.Server) NetMessage.SendData(MessageID.SyncItem, -1, -1, null, index);
            taken += extracted;
            if (extracted < chunk) break;
        }
        return taken;
    }
}
