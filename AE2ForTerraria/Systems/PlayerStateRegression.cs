using System;
using System.IO;
using System.Linq;
using System.Reflection;
using AE2ForTerraria.Content.Tiles;
using Terraria;
using Terraria.DataStructures;
using Terraria.ID;
using Terraria.ModLoader;

namespace AE2ForTerraria.Systems;

/// <summary>Exercises the real PlayerState packet route without sockets or saved characters.</summary>
internal static class PlayerStateRegression
{
    public static void Run(Action<bool, string> check)
    {
        if (!RegressionChecks.IsRunning) throw new InvalidOperationException("Player state checks require the isolated runner.");
        for (int i = 0; i < Main.maxPlayers; i++)
            if (Main.player[i]?.active == true) throw new InvalidOperationException("Player state checks refuse active players.");
        Player previousPlayer = Main.player[0];
        int previousMode = Main.netMode;
        using var world = new RegressionWorld();
        var player = new Player { whoAmI = 0, name = "PlayerStateRegressionOwner", active = false };
        var state = player.GetModPlayer<MEPlayer>();
        var oldBinding = new Point16(20, 20);
        var allowed = new Point16(30, 30);
        var denied = new Point16(40, 40);
        var missing = new Point16(50, 50);
        void KeepOldState()
        {
            state.MagnetEnabled = true;
            state.BoundTerminals.Clear(); state.BoundTerminals.Add(oldBinding);
        }
        bool OldStateIntact() => state.MagnetEnabled && state.BoundTerminals.SequenceEqual(new[] { oldBinding });
        try
        {
            Main.player[0] = player;
            Main.netMode = NetmodeID.Server;
            world.SetTile(allowed.X, allowed.Y, ModContent.TileType<MEAccessTerminalTile>());
            world.SetTile(denied.X, denied.Y, ModContent.TileType<MEAccessTerminalTile>());
            Point16 allowedAnchor = MENetwork.CanonicalAnchor(MENetwork.Collect(allowed.X, allowed.Y));
            Point16 deniedAnchor = MENetwork.CanonicalAnchor(MENetwork.Collect(denied.X, denied.Y));
            NetworkRegistry.TryClaim(allowedAnchor, player.name);
            NetworkRegistry.TryClaim(deniedAnchor, "DifferentPlayerStateOwner");
            byte[] complete = Packet(false, allowed, denied);

            KeepOldState();
            check(Dispatch(complete, 0) == complete.Length && OldStateIntact(),
                "early inactive PlayerState consumes its complete bounded payload without changing saved bindings or magnet state");
            foreach (int invalidSender in new[] { -1, Main.maxPlayers })
                check(Dispatch(complete, invalidSender) == complete.Length && OldStateIntact(),
                    "out-of-range PlayerState sender is refused after decoding the complete bounded payload");
            Main.player[0] = null;
            check(Dispatch(complete, 0) == complete.Length && OldStateIntact(),
                "missing PlayerState sender consumes its payload without dereferencing or replacing player state");
            Main.player[0] = player; player.active = true;

            complete = Packet(false, allowed, allowed, denied, missing, new Point16(-1, -1));
            check(Dispatch(complete, 0) == complete.Length && !state.MagnetEnabled
                && state.BoundTerminals.SequenceEqual(new[] { allowed }),
                "active PlayerState atomically accepts actual readable terminals, deduplicates and excludes inaccessible or invalid coordinates");
            NetworkRegistry.SetLevel(deniedAnchor, player.name, NetworkRegistry.LevelReadOnly);
            complete = Packet(true, denied, allowed);
            check(Dispatch(complete, 0) == complete.Length && state.MagnetEnabled
                && state.BoundTerminals.SequenceEqual(new[] { denied, allowed }),
                "PlayerState preserves requested binding order and accepts a read-only terminal permission");

            int largeAccess = ModContent.TileType<MEAccessTerminalTile2x2>();
            for (int x = 0; x < 2; x++)
                for (int y = 0; y < 2; y++) world.SetTile(allowed.X + x, allowed.Y + y, largeAccess, (short)(18 * x), (short)(18 * y));
            complete = Packet(false, new Point16(allowed.X + 1, allowed.Y + 1), allowed, denied);
            check(Dispatch(complete, 0) == complete.Length && !state.MagnetEnabled
                && state.BoundTerminals.SequenceEqual(new[] { allowed, denied }),
                "PlayerState accepts large access terminals and normalizes every segment before deduplicating bindings");
            world.SetTile(denied.X, denied.Y, ModContent.TileType<MECraftingTerminalTile>());
            complete = Packet(true, denied, allowed);
            check(Dispatch(complete, 0) == complete.Length && state.BoundTerminals.SequenceEqual(new[] { allowed }),
                "large-terminal compatibility does not permit crafting terminals as remote storage bindings");
            complete = Packet(false);
            check(Dispatch(complete, 0) == complete.Length && !state.MagnetEnabled && state.BoundTerminals.Count == 0,
                "active empty PlayerState snapshot intentionally clears bindings and applies the magnet preference");

            KeepOldState();
            byte[] truncated = Packet(false, allowed, denied)[..^1];
            Dispatch(truncated, 0);
            check(OldStateIntact(),
                "truncated PlayerState cannot publish a partially decoded binding list or magnet preference");
            foreach (int invalidCount in new[] { -1, MEPlayer.MaxBindings + 1, int.MaxValue })
            {
                byte[] invalid = PacketWithCount(invalidCount);
                // The rejected count is bounded immediately; unrelated trailing bytes
                // are not globally skipped to hide a malformed/unknown packet layout.
                check(Dispatch(invalid, 0) == 10 && OldStateIntact(),
                    "invalid PlayerState count is rejected before allocating or reading bindings and preserves the previous state");
            }
        }
        finally
        {
            Main.player[0] = previousPlayer;
            Main.netMode = previousMode;
        }
    }

    private static byte MessageId => (byte)(typeof(MENet).GetField("ClientPlayerState",
        BindingFlags.Static | BindingFlags.NonPublic)?.GetRawConstantValue()
        ?? throw new MissingFieldException("MENet.ClientPlayerState"));

    private static byte[] Packet(bool magnet, params Point16[] bindings)
    {
        using var stream = new MemoryStream(); using var writer = new BinaryWriter(stream);
        writer.Write(MessageId); writer.Write((short)-1); writer.Write((short)-1);
        writer.Write(magnet); writer.Write(bindings.Length);
        foreach (Point16 pos in bindings) { writer.Write(pos.X); writer.Write(pos.Y); }
        return stream.ToArray();
    }

    private static byte[] PacketWithCount(int count)
    {
        using var stream = new MemoryStream(); using var writer = new BinaryWriter(stream);
        writer.Write(MessageId); writer.Write((short)-1); writer.Write((short)-1);
        writer.Write(false); writer.Write(count); writer.Write(123456);
        return stream.ToArray();
    }

    private static long Dispatch(byte[] payload, int who)
    {
        using var stream = new MemoryStream(payload); using var reader = new BinaryReader(stream);
        MENet.HandlePayload(reader, who);
        return stream.Position;
    }
}
