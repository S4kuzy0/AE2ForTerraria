using System.Collections.Generic;
using AE2ForTerraria.Content.Tiles;
using Terraria;
using Terraria.DataStructures;
using Terraria.ModLoader;

namespace AE2ForTerraria.Systems;

/// <summary>Legacy IDs stay readable while their world tiles are upgraded to the larger footprint.</summary>
public static class MachineTiles
{
    private static readonly HashSet<Point16> PendingTerminals = new();
    private static Dictionary<int, int> _legacy;
    private static Dictionary<int, int> _large;
    private static Dictionary<int, int> Legacy => _legacy ??= new()
    {
        [ModContent.TileType<MEAccessTerminalTile2x2>()] = ModContent.TileType<MEAccessTerminalTile>(),
        [ModContent.TileType<MECraftingTerminalTile2x2>()] = ModContent.TileType<MECraftingTerminalTile>(),
        [ModContent.TileType<MECraftingCpuTile2x2>()] = ModContent.TileType<MECraftingCpuTile>(),
        [ModContent.TileType<MEDiskDriveTile2x2>()] = ModContent.TileType<MEDiskDriveTile>(),
        [ModContent.TileType<MEMolecularAssemblerTile2x2>()] = ModContent.TileType<MEMolecularAssemblerTile>(),
        [ModContent.TileType<MEStorageUnitTile2x2>()] = ModContent.TileType<MEStorageUnitTile>(),
        [ModContent.TileType<MEDropCollectorTile2x2>()] = ModContent.TileType<MEDropCollectorTile>(),
        [ModContent.TileType<MEWirelessBridgeTile2x2>()] = ModContent.TileType<MEWirelessBridgeTile>()
    };
    public static int CanonicalType(int type) => Legacy.TryGetValue(type, out int old) ? old : type;
    public static bool IsLarge(int type) => Legacy.ContainsKey(type);
    internal static bool TryGetLargeType(int legacyType, out int largeType)
    {
        if (_large == null)
        {
            _large = new();
            foreach (var pair in Legacy) _large[pair.Value] = pair.Key;
        }
        return _large.TryGetValue(legacyType, out largeType);
    }
    public static Point16 Origin(int x, int y)
    {
        if (!WorldGen.InWorld(x, y, 0)) return new Point16(x, y);
        Tile tile = Main.tile[x, y];
        return tile.HasTile && IsLarge(tile.TileType)
            ? new Point16(x - tile.TileFrameX / 18 % 2, y - tile.TileFrameY / 18 % 2)
            : new Point16(x, y);
    }
    public static bool IsEntityAnchor(int x, int y, int legacyType) => WorldGen.InWorld(x, y, 0)
        && Main.tile[x, y].HasTile && CanonicalType(Main.tile[x, y].TileType) == legacyType
        && Origin(x, y) == new Point16(x, y);
    public static void Reset() { _legacy = null; _large = null; PendingTerminals.Clear(); }
    public static void EnsureTerminal(int x, int y)
    {
        if (Main.netMode == Terraria.ID.NetmodeID.MultiplayerClient
            || !IsEntityAnchor(x, y, ModContent.TileType<MECraftingTerminalTile>())
            || TileEntity.ByPosition.ContainsKey(new Point16(x, y))) return;
        // Discovery can run inside TileEntity.Update. Defer mutations of the entity dictionaries
        // until their update iteration is over; preserve the legacy tile and all neighbouring tiles.
        PendingTerminals.Add(new Point16(x, y));
    }
    internal static void ClearPending() => PendingTerminals.Clear();
    internal static void FlushPending()
    {
        if (Main.netMode == Terraria.ID.NetmodeID.MultiplayerClient) { PendingTerminals.Clear(); return; }
        foreach (var p in PendingTerminals)
        {
            if (!IsEntityAnchor(p.X, p.Y, ModContent.TileType<MECraftingTerminalTile>()) || TileEntity.ByPosition.ContainsKey(p)) continue;
            int id = ModContent.GetInstance<MECraftingTerminalEntity>().Place(p.X, p.Y);
            if (Main.netMode == Terraria.ID.NetmodeID.Server)
                NetMessage.SendData(Terraria.ID.MessageID.TileEntitySharing, -1, -1, null, id, p.X, p.Y);
        }
        PendingTerminals.Clear();
    }
}

internal sealed class MachineMigrationSystem : ModSystem
{
    public override void PostUpdateEverything() => MachineTiles.FlushPending();
    public override void ClearWorld() => MachineTiles.ClearPending();
}
