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
    public readonly record struct DepositPreview(long Requested, long Accepted, string Reason)
    {
        public string Message => Accepted <= 0 ? "无法存入：" + Reason
            : Accepted < Requested ? "可存入 " + Accepted + " 件；其余保留。" + Reason
            : "可存入全部 " + Accepted + " 件";
    }

    /// <summary>Read-only prediction using the same identity, partitions and ledger rules as Deposit.</summary>
    public static DepositPreview PreviewDeposit(List<IMEStorage> storage, Item item)
    {
        long requested = item == null || item.IsAir ? 0 : item.stack;
        if (requested <= 0) return new(0, 0, "请先选择物品");
        if (storage == null || storage.Count == 0) return new(requested, 0, "没有连接的存储设备");
        ItemKey key = ItemKey.FromItem(item);
        long remaining = requested;
        bool allowed = false, types = false, excess = false, quantity = false;
        foreach (var unit in storage)
        {
            if (!unit.Allows(key)) continue;
            allowed = true;
            var ledger = unit.Ledger;
            remaining -= ledger.Insert(key, remaining, simulate: true);
            if (remaining <= 0) return new(requested, requested, null);
            excess |= ledger.StoredTypeCount > ledger.MaxTypes;
            types |= !ledger.Contains(key) && ledger.StoredTypeCount >= ledger.MaxTypes;
            quantity |= ledger.GetAmount(key) >= ledger.MaxAmountPerType || ledger.StoredTotalAmount == long.MaxValue;
        }
        string reason = !allowed ? "所有存储设备的过滤规则都拒绝此物品"
            : excess ? "已有类别超过容量；可右键驱动器继续加盘，或先取出物品"
            : types ? "物品类别已满；请右键驱动器加盘，已有类别仍可存入"
            : quantity ? "该物品数量已达上限；请增加驱动器或使用数字奇点"
            : "可用容量不足；请右键驱动器加盘";
        return new(requested, requested - remaining, reason);
    }

    internal static string DepositRemainderReason(List<IMEStorage> storage, Player player, bool extras = false,
        int onlySlot = -1, int onlyType = 0, ulong slotMask = ulong.MaxValue)
    {
        for (int slot = 0; slot < InventoryScope.DepositSlotCount(player, extras); slot++)
        {
            if ((onlySlot >= 0 && slot != onlySlot) || (slotMask & (1UL << slot)) == 0) continue;
            var item = player.inventory[slot];
            if (item == null || item.IsAir || item.favorited || onlyType > 0 && item.type != onlyType) continue;
            var preview = PreviewDeposit(storage, item);
            if (preview.Accepted < preview.Requested) return preview.Reason;
        }
        return "没有可存入的物品，或背包物品已变化；收藏物品会保留";
    }

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
