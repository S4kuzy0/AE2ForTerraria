using System;
using AE2ForTerraria.Content.Tiles;
using Microsoft.Xna.Framework;
using Terraria;
using Terraria.DataStructures;
using Terraria.ModLoader;

namespace AE2ForTerraria.Systems;

// Tests the spatial/network prerequisite only; no packets, player files or world saves are involved.
internal static class TerminalEntranceRegression
{
    internal static void Run(Action<bool, string> check)
    {
        using var world = new RegressionWorld();
        var target = new Point16(10, 40);
        var entrance = new Point16(60, 40);
        int cable = ModContent.TileType<MECableTile>();
        int access = ModContent.TileType<MEAccessTerminalTile>();
        world.Place(new MECraftingTerminalEntity(), ModContent.TileType<MECraftingTerminalTile>(), target.X, target.Y);
        for (int x = target.X + 1; x < entrance.X; x++) world.SetTile(x, entrance.Y, cable);
        world.SetTile(entrance.X, entrance.Y, access);
        var player = new Player { active = true, name = "AE2EntranceRegression" };
        var nearEntrance = new Vector2(entrance.X * 16f + 8f, entrance.Y * 16f + 8f);
        player.Center = nearEntrance;

        check(Vector2.DistanceSquared(player.Center, new Vector2(target.X * 16f + 8f, target.Y * 16f + 8f)) > 512f * 512f
            && MENet.IsNearbyLinkedEntrance(target, entrance, player),
            "nearby linked access entrance permits a canonical crafting target beyond direct interaction range");

        world.ClearTile(35, entrance.Y);
        check(!MENet.IsNearbyLinkedEntrance(target, entrance, player), "disconnected entrance cannot authorize a distant target");
        world.SetTile(35, entrance.Y, cable);
        check(MENet.IsNearbyLinkedEntrance(target, entrance, player), "reconnecting entrance invalidates the disconnected network cache");

        player.dead = true;
        check(!MENet.IsNearbyLinkedEntrance(target, entrance, player), "dead player cannot use a linked entrance");
        player.dead = false; player.active = false;
        check(!MENet.IsNearbyLinkedEntrance(target, entrance, player), "inactive player cannot use a linked entrance");
        player.active = true;
        player.Center = nearEntrance + new Vector2(513f, 0f);
        check(!MENet.IsNearbyLinkedEntrance(target, entrance, player), "linked entrance beyond player interaction range is refused");
        player.Center = nearEntrance;

        world.SetTile(entrance.X, entrance.Y, cable);
        check(!MENet.IsNearbyLinkedEntrance(target, entrance, player), "ordinary network cable cannot impersonate a terminal entrance");
        world.ClearTile(entrance.X, entrance.Y);
        check(!MENet.IsNearbyLinkedEntrance(target, entrance, player), "removed terminal entrance is refused");
        world.SetTile(entrance.X, entrance.Y, ModContent.TileType<MECraftingTerminalTile>());
        check(MENet.IsNearbyLinkedEntrance(target, entrance, player), "nearby crafting terminal is also a valid linked entrance");
        check(!MENet.IsNearbyLinkedEntrance(target, entrance, null)
            && !MENet.IsNearbyLinkedEntrance(target, new Point16(-1, -1), player),
            "missing player and invalid entrance coordinates are refused");
    }
}
