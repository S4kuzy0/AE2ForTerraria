using System.Collections.Generic;
using AE2ForTerraria.Core;
using Microsoft.Xna.Framework;
using Terraria;
using Terraria.DataStructures;
using Terraria.ID;
using Terraria.ModLoader;

namespace AE2ForTerraria.Systems;

/// <summary>Read-only bookmark inventory; background reads use the existing authorized snapshot protocol.</summary>
internal static class PinnedInventory
{
    private static readonly TerminalReadState Read = new();
    private static Point16 _lastTerminal = new(-1, -1);
    private static Dictionary<ItemKey, long> _remote;
    private static ulong _requestedAt;
    internal static int Revision { get; private set; }
    internal static bool HasOpenTerminal => UISystem.TerminalState != null
        && ReferenceEquals(UISystem.Interface?.CurrentState, UISystem.TerminalState);
    internal static string Status { get; private set; } = "背包";
    internal static void Track(Point16 position) { if (position.X >= 0) _lastTerminal = position; }
    internal static bool Accepts(Point16 position, int epoch) => !HasOpenTerminal
        && Read.Position == position && Read.Epoch == epoch && !Read.Denied;
    internal static bool Apply(Point16 position, int epoch, int level, Dictionary<ItemKey, long> contents)
    {
        if (!Accepts(position, epoch) || !Read.Apply(position, epoch, level)) return false;
        _remote = Read.Readable ? contents : null; Revision++; return true;
    }
    internal static IReadOnlyDictionary<ItemKey, long> Snapshot()
    {
        if (HasOpenTerminal)
        {
            Track(UISystem.ActiveTerminalPos); Read.Reset(); _remote = null;
            Status = UISystem.TerminalReadable ? "网络" : "库存未就绪 · 背包";
            return UISystem.TerminalReadable ? UISystem.ActiveNetworkContents : null;
        }
        var player = Main.LocalPlayer;
        if (player == null || Main.gameMenu) { Status = "背包"; return null; }
        Point16 wanted = _lastTerminal;
        if (Main.netMode != NetmodeID.MultiplayerClient && !ValidTerminal(wanted)) wanted = new(-1, -1);
        var bindings = player.GetModPlayer<MEPlayer>();
        if (wanted.X < 0 && bindings.HasTerminalBinding) wanted = bindings.PrimaryTerminal;
        if (wanted.X < 0 || !WorldGen.InWorld(wanted.X, wanted.Y, 0))
        { Read.Reset(); _remote = null; Status = "背包"; return null; }
        if (Main.netMode != NetmodeID.MultiplayerClient)
        {
            if (!ValidTerminal(wanted)) { Status = "终端失效 · 背包"; return null; }
            var blocks = MENetwork.Collect(wanted.X, wanted.Y);
            var anchor = MENetwork.CanonicalAnchor(blocks);
            bool readable = anchor.X >= 0 && (NetworkRegistry.GetLevel(anchor, player.name) >= NetworkRegistry.LevelReadOnly
                || NetworkRegistry.GetOwner(anchor) == null && Vector2.DistanceSquared(player.Center,
                    new Vector2(wanted.X * 16f + 8, wanted.Y * 16f + 8)) <= 512f * 512f);
            Status = readable ? "网络" : "无读取权限 · 背包";
            return readable ? MENetwork.Aggregate(MENetwork.CollectStorage(wanted.X, wanted.Y)) : null;
        }
        if (Read.Position != wanted || Main.GameUpdateCount - _requestedAt >= (Read.Denied ? 300u : 60u))
        {
            if (Read.Position != wanted || Read.Denied)
            { Read.Begin(wanted); _remote = null; }
            _requestedAt = Main.GameUpdateCount;
            MENet.RequestTerminalRead(wanted, Read.Epoch);
        }
        Status = Read.Denied ? "网络不可读 · 背包" : Read.Readable && _remote != null ? "网络" : "正在同步 · 背包";
        return Read.Readable ? _remote : null;
    }
    private static bool ValidTerminal(Point16 position)
    {
        if (position.X < 0 || !WorldGen.InWorld(position.X, position.Y, 0)) return false;
        var tile = Main.tile[position.X, position.Y];
        int type = tile.HasTile ? MachineTiles.CanonicalType(tile.TileType) : -1;
        return type == ModContent.TileType<Content.Tiles.MEAccessTerminalTile>()
            || type == ModContent.TileType<Content.Tiles.MECraftingTerminalTile>();
    }
    internal static void Reset()
    { Read.Reset(); _lastTerminal = new(-1, -1); _remote = null; _requestedAt = 0; Status = "背包"; Revision++; }
}
