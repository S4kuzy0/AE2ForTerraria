using System.Collections.Generic;
using System.IO;
using AE2ForTerraria.Content.Items;
using AE2ForTerraria.Content.Tiles;
using Terraria;
using Terraria.DataStructures;
using Terraria.ID;
using Terraria.ModLoader;

namespace AE2ForTerraria.Systems;

public static partial class MENet
{
    // Separate message IDs preserve existing v3 packet layouts.
    private const byte ClientUpgradeStorageUnit = (MessageTag << ProtocolShift) | 32;
    private const byte ServerStorageUpgradeComplete = (MessageTag << ProtocolShift) | 38;
    private static int _upgradeSequence;
    private static int _pendingUpgrade;
    private static int _pendingUpgradeSlot = -1;
    private static Player _pendingUpgradePlayer;
    private static Item _pendingUpgradeItem;
    private static readonly Dictionary<int, int> LastUpgradeRequest = new();
    public static bool StorageUpgradePending => _pendingUpgrade != 0;

    internal static int BeginStorageUpgrade(Player player, int slot)
    {
        if (!IsClient || player == null || InventoryTransactionPending || CursorPending || player.HasLockedInventory()
            || slot < 0 || slot >= 50 || player.inventory[slot].IsAir
            || player.inventory[slot].type != ModContent.ItemType<MEStorageUpgrade>()) return 0;
        // Mouse slot 58 is not protected by vanilla's inventoryChestStack interaction guard.
        _upgradeSequence = _upgradeSequence == int.MaxValue ? 1 : _upgradeSequence + 1;
        _pendingUpgrade = _upgradeSequence;
        _pendingUpgradeSlot = slot;
        _pendingUpgradePlayer = player;
        _pendingUpgradeItem = player.inventory[slot];
        player.inventoryChestStack[slot] = true;
        GuardStorageUpgradeInput(player);
        return _pendingUpgrade;
    }

    internal static void CompleteStorageUpgrade(int request)
    {
        if (request != _pendingUpgrade || request == 0) return;
        // SyncEquipment precedes this packet and clears the vanilla slot lock itself.
        // Do not clear a flag here: a newer vanilla quick-stack may now own that flag.
        _pendingUpgrade = 0;
        _pendingUpgradeSlot = -1;
        _pendingUpgradePlayer = null;
        _pendingUpgradeItem = null;
    }

    internal static void ResetStorageUpgrades()
    {
        if (_pendingUpgradePlayer != null && _pendingUpgradeSlot >= 0)
            _pendingUpgradePlayer.inventoryChestStack[_pendingUpgradeSlot] = false;
        _pendingUpgrade = 0;
        _pendingUpgradeSlot = -1;
        _pendingUpgradePlayer = null;
        _pendingUpgradeItem = null;
        LastUpgradeRequest.Clear();
    }

    internal static bool IsStorageUpgradeItem(Item item)
        => IsClient && StorageUpgradePending && ReferenceEquals(item, _pendingUpgradeItem);

    internal static void GuardStorageUpgradeInput(Player player)
    {
        if (!IsClient || !StorageUpgradePending || !ReferenceEquals(player, _pendingUpgradePlayer)) return;
        player.controlThrow = false;
        player.controlUseItem = false;
        player.controlUseTile = false;
        player.noThrow = 2;
    }

    internal static void ForgetStorageUpgradePlayer(int who) => LastUpgradeRequest.Remove(who);

    private static void HandleStorageUpgrade(Point16 position, BinaryReader reader, int who)
    {
        int request = reader.ReadInt32();
        int slot = reader.ReadInt32();
        if (who < 0 || who >= Main.maxPlayers || !Main.player[who].active) return;
        Player player = Main.player[who];
        bool validSlot = slot >= 0 && slot < 50;
        try
        {
            if (request <= 0 || !validSlot
                || (LastUpgradeRequest.TryGetValue(who, out int previous) && request <= previous)) return;
            LastUpgradeRequest[who] = request;
            if (!CheckPermission(ClientUpgradeStorageUnit, position, who)) return;
            if (!TileEntity.ByPosition.TryGetValue(position, out var entity)
                || entity is not MEStorageUnitEntity unit) return;
            if (StorageOperations.TryUpgradeStorageUnit(unit, player, slot))
            {
                SendStorageUnit(position);
                SendNetworkLedgers(position.X, position.Y);
            }
        }
        finally
        {
            // Success, refusal and full-level requests all return the authoritative slot.
            // The requester is deliberately included, with its vanilla per-slot lock held.
            if (validSlot) NetMessage.SendData(MessageID.SyncEquipment, -1, -1, null, who, slot);
            var response = CreatePacket();
            response.Write(ServerStorageUpgradeComplete);
            response.Write((short)0); response.Write((short)0);
            response.Write(request);
            response.Send(who);
        }
    }
}

public sealed class StorageUpgradePlayer : ModPlayer
{
    public override void SetControls() => MENet.GuardInventoryTransactionInput(Player);
    public override void PlayerConnect()
    {
        MENet.ForgetStorageUpgradePlayer(Player.whoAmI);
        MENet.ForgetInventoryDepositPlayer(Player.whoAmI);
        MENet.ForgetCursorPlayer(Player.whoAmI);
    }
    public override void PlayerDisconnect()
    {
        MENet.ForgetStorageUpgradePlayer(Player.whoAmI);
        MENet.ForgetInventoryDepositPlayer(Player.whoAmI);
        MENet.ForgetCursorPlayer(Player.whoAmI);
        if (Main.netMode != NetmodeID.Server && Player.whoAmI == Main.myPlayer)
        {
            MENet.ResetStorageUpgrades();
            MENet.ResetInventoryDeposits();
            MENet.ResetCursorTransactions();
        }
    }
}
