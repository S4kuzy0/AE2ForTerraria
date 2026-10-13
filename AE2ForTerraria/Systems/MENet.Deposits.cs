using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using AE2ForTerraria.Core;
using Terraria;
using Terraria.DataStructures;
using Terraria.ID;
using Terraria.ModLoader;

namespace AE2ForTerraria.Systems;

public static partial class MENet
{
    // New message IDs leave the old v3 layouts intact. The old unacknowledged
    // deposit requests are retired rather than accepted without a slot lock.
    internal const byte ClientInventoryDeposit = (MessageTag << ProtocolShift) | 33;
    internal const byte ServerInventoryDepositComplete = (MessageTag << ProtocolShift) | 39;
    private const ulong DepositSlotsMask = (1UL << InventoryScope.ExtendedSlots) - 1;
    private static int _depositSequence;
    private static InventoryDepositRequest _pendingDeposit;
    private static Player _pendingDepositPlayer;
    private static readonly Dictionary<int, int> LastDepositRequest = new();
    public static bool InventoryDepositPending => _pendingDeposit != null;
    public static bool InventoryTransactionPending => StorageUpgradePending || InventoryDepositPending;

    internal sealed class InventoryDepositRequest
    {
        public int Nonce;
        public ulong Slots;
        public bool Extras;
        public int OnlySlot = -1;
        public int OnlyType;
        public readonly List<DepositIdentity> Items = new();
    }

    internal readonly struct DepositIdentity
    {
        public readonly int Type, Prefix;
        public readonly byte[] DataHash;
        public DepositIdentity(Item item)
        {
            ItemKey key = ItemKey.FromItem(item);
            Type = key.Type; Prefix = key.Prefix;
            DataHash = HashData(key.Data);
        }
        public DepositIdentity(int type, int prefix, byte[] dataHash)
        { Type = type; Prefix = prefix; DataHash = dataHash; }
        public bool Matches(Item item)
        {
            if (item == null || item.IsAir || item.type != Type || item.prefix != Prefix) return false;
            return CryptographicOperations.FixedTimeEquals(DataHash, HashData(ItemKey.FromItem(item).Data));
        }
        private static byte[] HashData(string data)
            => SHA256.HashData(Encoding.UTF8.GetBytes(data ?? string.Empty));
    }

    internal static InventoryDepositRequest BeginInventoryDeposit(Player player, bool extras,
        int onlySlot = -1, int onlyType = 0)
    {
        if (!IsClient || player == null || InventoryTransactionPending || CursorPending
            || player.HasLockedInventory() || _depositSequence == int.MaxValue) return null;
        int count = InventoryScope.DepositSlotCount(player, extras);
        if (onlySlot < -1 || onlySlot >= count || onlyType < 0
            || (onlyType > 0 && !SavedItemGuard.IsValidType(onlyType))) return null;
        var intent = new InventoryDepositRequest { Extras = extras, OnlySlot = onlySlot, OnlyType = onlyType };
        for (int slot = 0; slot < count; slot++)
        {
            Item item = player.inventory[slot];
            if ((onlySlot >= 0 && slot != onlySlot) || item == null || item.IsAir || item.favorited
                || (onlyType > 0 && item.type != onlyType)) continue;
            intent.Slots |= 1UL << slot;
            intent.Items.Add(new DepositIdentity(item));
        }
        if (intent.Slots == 0) return null;
        intent.Nonce = ++_depositSequence;
        _pendingDeposit = intent;
        _pendingDepositPlayer = player;
        for (int slot = 0; slot < count; slot++)
            if ((intent.Slots & (1UL << slot)) != 0) player.inventoryChestStack[slot] = true;
        GuardInventoryTransactionInput(player);
        return intent;
    }

    private static void SubmitInventoryDeposit(Point16 position, bool extras, int onlySlot = -1, int onlyType = 0)
    {
        if (!IsClient) return;
        Player player = Main.LocalPlayer;
        InventoryDepositRequest intent = BeginInventoryDeposit(player, extras, onlySlot, onlyType);
        if (intent == null) return;
        // Native updates precede the intent on the same ordered connection. Only
        // nonempty candidates are sent: empty slots remain available for pickups.
        for (int slot = 0; slot < InventoryScope.ExtendedSlots; slot++)
            if ((intent.Slots & (1UL << slot)) != 0)
                NetMessage.SendData(MessageID.SyncEquipment, -1, -1, null, player.whoAmI, slot);
        var packet = CreatePacket();
        WriteInventoryDeposit(packet, position, intent);
        packet.Send();
    }

    internal static void WriteInventoryDeposit(BinaryWriter writer, Point16 position, InventoryDepositRequest intent)
    {
        writer.Write(ClientInventoryDeposit); writer.Write(position.X); writer.Write(position.Y);
        writer.Write(intent.Nonce); writer.Write(intent.Slots);
        writer.Write(intent.Extras); writer.Write((short)intent.OnlySlot); writer.Write(intent.OnlyType);
        foreach (DepositIdentity item in intent.Items)
        {
            writer.Write(item.Type); writer.Write(item.Prefix); writer.Write(item.DataHash);
        }
    }

    internal static void CompleteInventoryDeposit(int request)
    {
        if (_pendingDeposit == null || request != _pendingDeposit.Nonce) return;
        // Every native snapshot is sent before completion; it also updates
        // Main.clientPlayer, preventing the next vanilla update from restoring stock.
        // Never unlock here: a newer vanilla operation may already own a slot flag.
        _pendingDeposit = null;
        _pendingDepositPlayer = null;
    }

    internal static void ResetInventoryDeposits()
    {
        if (_pendingDeposit != null && _pendingDepositPlayer != null)
            for (int slot = 0; slot < InventoryScope.ExtendedSlots; slot++)
                if ((_pendingDeposit.Slots & (1UL << slot)) != 0)
                    _pendingDepositPlayer.inventoryChestStack[slot] = false;
        _pendingDeposit = null;
        _pendingDepositPlayer = null;
        LastDepositRequest.Clear();
    }

    internal static bool IsInventoryTransactionItem(Item item)
    {
        if(IsClient && CursorPending && (ReferenceEquals(item,Main.mouseItem) || ReferenceEquals(item,Main.LocalPlayer.inventory[58]))) return true;
        if (IsStorageUpgradeItem(item)) return true;
        if (!IsClient || _pendingDeposit == null || _pendingDepositPlayer == null) return false;
        for (int slot = 0; slot < InventoryScope.ExtendedSlots; slot++)
            if ((_pendingDeposit.Slots & (1UL << slot)) != 0
                && ReferenceEquals(item, _pendingDepositPlayer.inventory[slot])) return true;
        return false;
    }

    internal static void GuardInventoryTransactionInput(Player player)
    {
        GuardStorageUpgradeInput(player);
        if(IsClient && CursorPending && player.whoAmI==Main.myPlayer)
        { player.controlThrow=player.controlUseItem=player.controlUseTile=false; player.noThrow=2; }
        if (!IsClient || _pendingDeposit == null || !ReferenceEquals(player, _pendingDepositPlayer)) return;
        player.controlThrow = player.controlUseItem = player.controlUseTile = false;
        player.noThrow = 2;
    }

    internal static void ForgetInventoryDepositPlayer(int who) => LastDepositRequest.Remove(who);

    private static void HandleInventoryDeposit(Point16 position, BinaryReader reader, int who)
    {
        int request = reader.ReadInt32();
        ulong slots = reader.ReadUInt64();
        if (who < 0 || who >= Main.maxPlayers || Main.player[who]?.active != true) return;
        Player player = Main.player[who];
        try
        {
            if (request <= 0 || slots == 0 || (slots & ~DepositSlotsMask) != 0
                || (LastDepositRequest.TryGetValue(who, out int previous) && request <= previous)) return;
            LastDepositRequest[who] = request;
            bool extras = reader.ReadBoolean();
            int onlySlot = reader.ReadInt16(), onlyType = reader.ReadInt32();
            int count = InventoryScope.DepositSlotCount(player, extras);
            ulong allowedSlots = (1UL << count) - 1;
            if ((slots & ~allowedSlots) != 0 || onlySlot < -1 || onlySlot >= count
                || (onlySlot >= 0 && slots != (1UL << onlySlot)) || onlyType < 0
                || (onlyType > 0 && !SavedItemGuard.IsValidType(onlyType))) return;
            // Decode and validate the whole manifest before changing any item.
            ulong eligibleSlots = 0;
            for (int slot = 0; slot < count; slot++)
            {
                if ((slots & (1UL << slot)) == 0) continue;
                int type = reader.ReadInt32(), prefix = reader.ReadInt32();
                byte[] dataHash = reader.ReadBytes(32);
                if (dataHash.Length != 32) throw new EndOfStreamException("Truncated deposit identity");
                if (!SavedItemGuard.IsValidType(type) || prefix < 0 || prefix >= PrefixLoader.PrefixCount
                    || (onlyType > 0 && type != onlyType)) return;
                Item item = player.inventory[slot];
                var identity = new DepositIdentity(type, prefix, dataHash);
                if (item?.favorited == false && identity.Matches(item)) eligibleSlots |= 1UL << slot;
            }
            if (!CheckPermission(ClientInventoryDeposit, position, who)) return;
            long moved = StorageOperations.DepositInventory(MENetwork.CollectStorage(position.X, position.Y),
                player, extras, onlySlot, onlyType, eligibleSlots, syncEquipment: false);
            SendNetworkLedgers(position.X, position.Y);
            AuditDeposit(position, who, moved);
            Notify(who, moved == 0
                ? "未存入：请检查存储容量、过滤规则或背包物品是否已变化"
                : onlySlot >= 0 || onlyType > 0
                    ? "已存入 " + moved + " 件；未存入的物品保留在背包"
                    : "已存入 " + moved + " 件；收藏和未能存入的物品保留在背包");
        }
        finally
        {
            // Include unchanged and refused candidates: they must receive a reply
            // too, so a full network or changed permissions cannot leave a slot locked.
            for (int slot = 0; slot < InventoryScope.DepositSlotCount(player, true); slot++)
                if ((slots & (1UL << slot)) != 0)
                    NetMessage.SendData(MessageID.SyncEquipment, -1, -1, null, who, slot);
            var response = CreatePacket();
            response.Write(ServerInventoryDepositComplete); response.Write((short)0); response.Write((short)0);
            response.Write(request); response.Send(who);
        }
    }
}
