using System;
using System.IO;
using System.Linq;
using AE2ForTerraria.Content.Tiles;
using Microsoft.Xna.Framework;
using Terraria;
using Terraria.DataStructures;
using Terraria.ID;
using Terraria.ModLoader;
using Terraria.ModLoader.IO;

namespace AE2ForTerraria.Systems;

/// <summary>Recovery never borrows a merged foreign policy to change channel or expose inventory.</summary>
public static class BridgeRecoveryRegression
{
    public static void Run(Action<bool, string> check)
    {
        using var world = new RegressionWorld();
        var registry = ModContent.GetInstance<NetworkRegistry>();
        var local = world.Place(new MEWirelessBridgeEntity(), ModContent.TileType<MEWirelessBridgeTile>(), 60, 50);
        world.Place(new MEStorageUnitEntity(), ModContent.TileType<MEStorageUnitTile>(), 61, 50);
        var remote = world.Place(new MEWirelessBridgeEntity(), ModContent.TileType<MEWirelessBridgeTile>(), 11, 20);
        world.Place(new MEStorageUnitEntity(), ModContent.TileType<MEStorageUnitTile>(), 10, 20);
        remote.SetChannel(1);
        Point16 localAnchor = Anchor(local.Position), remoteAnchor = Anchor(remote.Position);
        check(NetworkRegistry.TryClaim(localAnchor, "Alice") && NetworkRegistry.TryClaim(remoteAnchor, "Bob"),
            "bridge recovery fixture starts with two separate named owners");
        NetworkRegistry.SetName(localAnchor, "Alice network"); NetworkRegistry.SetName(remoteAnchor, "Bob network");
        NetworkRegistry.SetLevel(localAnchor, "Maintainer", NetworkRegistry.LevelManage);
        NetworkRegistry.SetLevel(remoteAnchor, "Maintainer", NetworkRegistry.LevelReadOnly);
        NetworkRegistry.SetLevel(localAnchor, "Viewer", NetworkRegistry.LevelReadOnly);
        NetworkRegistry.SetLevel(localAnchor, "User", NetworkRegistry.LevelUse);
        NetworkRegistry.SetLocked(localAnchor, ItemID.Wood, true);
        remote.SetChannel(0);
        Point16 merged = Anchor(local.Position);
        check(MENetwork.Collect(local.Position.X, local.Position.Y).Contains(remote.Position)
            && NetworkRegistry.GetLevel(merged, "Alice") == NetworkRegistry.LevelNone
            && NetworkRegistry.GetLevel(merged, "Bob") == NetworkRegistry.LevelNone,
            "same-channel bridge merge retains both owners and denies either owner foreign inventory access");
        check(NetworkRegistry.CanManageOriginalBlock(local.Position, "Alice")
            && !NetworkRegistry.CanManageOriginalBlock(remote.Position, "Alice")
            && NetworkRegistry.CanManageOriginalBlock(local.Position, "Maintainer")
            && !NetworkRegistry.CanManageOriginalBlock(remote.Position, "Maintainer"),
            "local bridge recovery checks the preserved original owner and management ACL only");

        // Its key sorts after Bob's remote policy: the old global-minimum assignment would grant Bob this local bridge.
        var addedLocal = world.Place(new MEWirelessBridgeEntity(), ModContent.TileType<MEWirelessBridgeTile>(), 62, 50);
        addedLocal.SetEnabled(false);
        Anchor(local.Position);
        check(NetworkRegistry.CanManageOriginalBlock(addedLocal.Position, "Alice")
            && !NetworkRegistry.CanManageOriginalBlock(addedLocal.Position, "Bob"),
            "new local bridge inherits its wired owner rather than the first foreign wireless policy");

        var savedPolicy = new TagCompound(); registry.SaveWorldData(savedPolicy);
        registry.ClearWorld(); registry.LoadWorldData(savedPolicy); MENetwork.InvalidateCache();
        merged = Anchor(local.Position);
        check(NetworkRegistry.CanManageOriginalBlock(local.Position, "Alice")
            && !NetworkRegistry.CanManageOriginalBlock(remote.Position, "Alice")
            && NetworkRegistry.GetLevel(merged, "Alice") == NetworkRegistry.LevelNone,
            "saved merged memberships retain original recovery authority without granting merged access after reload");

        Player alice = PlayerAt("Alice", local.Position);
        Player viewer = PlayerAt("Viewer", local.Position), user = PlayerAt("User", local.Position);
        check(!BridgeRecovery.TryDisconnect(local.Position, viewer) && !BridgeRecovery.TryDisconnect(local.Position, user)
            && local.Enabled, "read-only and storage-use permissions cannot invoke bridge recovery");
        alice.Center = new Vector2(remote.Position.X * 16 + 8, remote.Position.Y * 16 + 8);
        check(!BridgeRecovery.TryDisconnect(remote.Position, alice) && remote.Enabled,
            "nearby owner of another component cannot disconnect the foreign bridge");
        check(!BridgeRecovery.TryDisconnect(local.Position, alice) && local.Enabled,
            "even the original owner cannot recover a distant bridge");
        alice.Center = new Vector2(local.Position.X * 16 + 8, local.Position.Y * 16 + 8); alice.dead = true;
        check(!BridgeRecovery.TryDisconnect(local.Position, alice) && local.Enabled,
            "dead players cannot use recovery to mutate a bridge");
        alice.dead = false;
        int previousMode = Main.netMode;
        try
        {
            Main.netMode = NetmodeID.MultiplayerClient;
            check(!BridgeRecovery.TryDisconnect(local.Position, alice) && local.Enabled,
                "client-side recovery never commits a local authoritative bridge change");
        }
        finally { Main.netMode = previousMode; }

        check(BridgeRecovery.TryDisconnect(local.Position, alice) && !local.Enabled && remote.Enabled
            && local.Channel == 0 && remote.Channel == 0,
            "original owner recovery disables only the local bridge and never edits either channel");
        check(!WirelessRegistry.SameChannel(0).Contains(local.Position)
            && !MENetwork.Collect(local.Position.X, local.Position.Y).Contains(remote.Position)
            && !MENetwork.Collect(remote.Position.X, remote.Position.Y).Contains(local.Position),
            "disabled bridge is absent as both wireless source and destination");
        localAnchor = Anchor(local.Position); remoteAnchor = Anchor(remote.Position);
        check(NetworkRegistry.GetOwner(localAnchor) == "Alice" && NetworkRegistry.GetOwner(remoteAnchor) == "Bob"
            && NetworkRegistry.GetName(localAnchor) == "Alice network" && NetworkRegistry.GetName(remoteAnchor) == "Bob network"
            && NetworkRegistry.IsLocked(localAnchor, ItemID.Wood) && !NetworkRegistry.IsLocked(remoteAnchor, ItemID.Wood),
            "disconnect restores each network's original owner, name and locked items");
        local.CycleChannel();
        check(local.Enabled && local.Channel == 1 && !MENetwork.Collect(local.Position.X, local.Position.Y).Contains(remote.Position),
            "changing a disconnected bridge to the next channel explicitly reconnects only that channel");

        MetadataChecks(check);
    }

    private static Point16 Anchor(Point16 position) => MENetwork.CanonicalAnchor(MENetwork.Collect(position.X, position.Y));
    private static Player PlayerAt(string name, Point16 position)
    {
        var player = new Player { active = true, name = name };
        player.Center = new Vector2(position.X * 16 + 8, position.Y * 16 + 8);
        return player;
    }

    private static void MetadataChecks(Action<bool, string> check)
    {
        var oldBridge = new MEWirelessBridgeEntity();
        oldBridge.LoadData(new TagCompound { ["channel"] = 3 });
        check(oldBridge.Channel == 3 && oldBridge.Enabled, "legacy bridge saves without enabled field remain enabled");
        oldBridge.SetChannel(7); oldBridge.SetEnabled(false);
        var tag = new TagCompound(); oldBridge.SaveData(tag);
        var restored = new MEWirelessBridgeEntity(); restored.LoadData(tag);
        check(restored.Channel == 7 && !restored.Enabled, "bridge disabled state and retained channel survive save reload");
        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream, System.Text.Encoding.UTF8, true);
        oldBridge.NetSend(writer); stream.Position = 0;
        var client = new MEWirelessBridgeEntity();
        using var reader = new BinaryReader(stream, System.Text.Encoding.UTF8, true);
        client.NetReceive(reader);
        check(client.Channel == 7 && !client.Enabled && stream.Position == stream.Length,
            "native tile-entity metadata synchronizes channel and enabled state completely");
        bool rejected = false;
        try { using var truncated = new BinaryReader(new MemoryStream(new byte[] { 2 })); client.NetReceive(truncated); }
        catch (EndOfStreamException) { rejected = true; }
        check(rejected && client.Channel == 7 && !client.Enabled,
            "truncated bridge metadata does not partially publish a new channel");
    }
}
