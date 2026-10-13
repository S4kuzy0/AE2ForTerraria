using AE2ForTerraria.Content.Tiles;
using Microsoft.Xna.Framework;
using Terraria;
using Terraria.DataStructures;
using Terraria.ID;

namespace AE2ForTerraria.Systems;

internal static class BridgeRecovery
{
    internal static bool InRange(Point16 position, Player player) => player != null && player.active && !player.dead
        && Vector2.DistanceSquared(player.Center, new Vector2(position.X * 16f + 8, position.Y * 16f + 8)) <= 192f * 192f;
    internal static bool TryDisconnect(Point16 position, Player player)
    {
        if (Main.netMode == NetmodeID.MultiplayerClient || !InRange(position, player) || !TileEntity.ByPosition.TryGetValue(position, out var entity)
            || entity is not MEWirelessBridgeEntity bridge || !bridge.IsTileValidForEntity(position.X, position.Y)) return false;
        // Collect before reading the preserved membership. This never grants access to the merged inventory.
        MENetwork.Collect(position.X, position.Y);
        if (!NetworkRegistry.CanManageOriginalBlock(position, player.name)) return false;
        bridge.SetEnabled(false);
        MENetwork.Collect(position.X, position.Y);
        return true;
    }
}
