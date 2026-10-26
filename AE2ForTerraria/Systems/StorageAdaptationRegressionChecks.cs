using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using AE2ForTerraria.Content.Tiles;
using AE2ForTerraria.Core;
using Terraria;
using Terraria.ID;
using Terraria.ModLoader;
using Terraria.ModLoader.IO;
using Terraria.Net;
using Terraria.Net.Sockets;

namespace AE2ForTerraria.Systems;

/// <summary>Missing-content fixtures use isolated tiles and synthetic identities, never user saves.</summary>
public static class StorageAdaptationRegressionChecks
{
    private const string MissingIdentity = "AE2RegressionMissingMod/OriginalItem";
    private static TagCompound MissingTag() => new()
    {
        ["mod"] = "AE2RegressionMissingMod", ["name"] = "OriginalItem", ["stack"] = 1,
        ["data"] = new TagCompound { ["charge"] = 123 }
    };
    private static List<int> MissingFilter() => ItemPersistence.LoadFilter(new TagCompound
        { ["filterItems"] = new List<TagCompound> { MissingTag() } }, "filter");

    public static void Run(Action<bool, string> check)
    {
        FilterChecks(check);
        CollectorConfigChecks(check);
        Recipe[] recipes = Main.recipe;
        int count = Recipe.numRecipes;
        try
        {
            Main.recipe = new[] { Recipe.Create(ItemID.Torch, 1).AddIngredient(ItemID.Wood) };
            Recipe.numRecipes = 1;
            StorageSystem.Recipes.Build();
            using var world = new RegressionWorld();
            var cpu = world.Place(new MECraftingCpuEntity(), ModContent.TileType<MECraftingCpuTile>(), 30, 30);
            var storage = world.Place(new MEStorageUnitEntity(), ModContent.TileType<MEStorageUnitTile>(), 31, 30);
            ItemKey missing = ItemKey.FromItem(ItemIO.Load(MissingTag()));
            var torch = ItemKey.Crafted(ItemID.Torch);
            cpu.Keep.Add(new() { Key = missing, Target = 1 });
            cpu.SetKeep(torch, 3);
            for (int tick = 0; tick < 120 && !cpu.Jobs.Any(job => job.Key.Equals(torch)); tick++) cpu.Update();
            check(cpu.Keep.Count == 2 && cpu.Jobs.Any(job => job.Key.Equals(torch)),
                "missing keep-stock target stays saved and does not block a later available target");
            check(cpu.SetKeep(missing, 0) && cpu.Keep.Count == 1 && cpu.Keep[0].Key.Equals(torch),
                "a player can explicitly remove a keep-stock target whose mod is missing");

            NetworkRegistry.SetLockedList(cpu.Position, MissingFilter());
            var locks = NetworkRegistry.GetLocked(cpu.Position);
            locks.Add(ItemID.Wood);
            NetworkRegistry.SetLockedList(cpu.Position, locks);
            check(NetworkRegistry.IsLocked(cpu.Position, ItemID.Wood)
                && ItemPersistence.HasMissingFilterReferences(NetworkRegistry.GetLocked(cpu.Position)),
                "editing a live item lock retains unavailable item locks");
            check(NetworkRegistry.IsLocked(cpu.Position, missing.Type),
                "unavailable locked items remain protected while represented as UnloadedItem");
            NetworkRegistry.SetLockedList(cpu.Position, new());
            check(!ItemPersistence.HasMissingFilterReferences(NetworkRegistry.GetLocked(cpu.Position)),
                "explicit empty lock replacement clears unavailable references too");
            NetworkRegistry.SetLocked(cpu.Position, missing.Type, true);
            check(!NetworkRegistry.IsLocked(cpu.Position, missing.Type),
                "legacy single-type lock requests cannot add a bare unloaded placeholder");

            world.Reset();
            var terminal = world.Place(new MECraftingTerminalEntity(), ModContent.TileType<MECraftingTerminalTile>(), 30, 30);
            storage = world.Place(new MEStorageUnitEntity(), ModContent.TileType<MEStorageUnitTile>(), 31, 30);
            storage.Ledger.MaxBytes = long.MaxValue;
            storage.Ledger.Restore(ItemKey.Crafted(ItemID.Wood), 5);
            var unavailable = world.Place(new MEMolecularAssemblerEntity(), ModContent.TileType<MEMolecularAssemblerTile>(), 32, 30);
            var available = world.Place(new MEMolecularAssemblerEntity(), ModContent.TileType<MEMolecularAssemblerTile>(), 33, 30);
            unavailable.Target = missing;
            available.Target = torch;
            for (int tick = 0; tick < 8 && !available.Target.IsAir; tick++) terminal.Update();
            check(unavailable.Target.Equals(missing) && available.Target.IsAir && storage.Ledger.GetAmount(torch) == 1,
                "unavailable legacy assembler target cannot starve a later available assembler");
            NetworkFilterChecks(world, check);
            NetworkSizeChecks(world, check);
        }
        finally
        {
            Main.recipe = recipes;
            Recipe.numRecipes = count;
            StorageSystem.Recipes.Build();
        }
    }

    private static void FilterChecks(Action<bool, string> check)
    {
        var missing = MissingFilter();
        var selectable = ItemPersistence.CloneFilter(missing);
        int placeholder = ModContent.ItemType<Terraria.ModLoader.Default.UnloadedItem>();
        check(!ItemPersistence.TryToggleFilterType(selectable, placeholder) && selectable.Count == 0
            && ItemPersistence.FilterIdentities(selectable).Contains(MissingIdentity),
            "all numeric filter selectors reject bare unloaded placeholders and preserve original missing references");
        check(ItemPersistence.TryToggleFilterType(selectable, ItemID.Wood) && selectable.Contains(ItemID.Wood)
            && ItemPersistence.TryToggleFilterType(selectable, ItemID.Wood) && selectable.Count == 0
            && ItemPersistence.HasMissingFilterReferences(selectable),
            "editing loaded filter items leaves missing stable references intact");
        var copied = ItemPersistence.CloneFilter(missing);
        var drive = new MEDiskDriveEntity { Filter = copied, IsWhitelist = true };
        check(ItemPersistence.HasMissingFilterReferences(copied) && !drive.Allows(ItemKey.Crafted(ItemID.Wood)),
            "copied missing-only whitelist remains restrictive");
        copied.Add(ItemID.Wood);
        var imported = ListIO.ImportLines(ListIO.ExportLines(copied));
        check(imported.Contains(ItemID.Wood) && ItemPersistence.HasMissingFilterReferences(imported)
            && ItemPersistence.FilterIdentities(imported).Contains(MissingIdentity),
            "filter text identity export/import preserves both available and unavailable items");
        var vanillaNamed = ListIO.ImportLines(new[] { "Terraria/Wood", MissingIdentity });
        var vanillaSaved = new TagCompound();
        ItemPersistence.SaveFilter(vanillaSaved, "filter", vanillaNamed);
        var vanillaReloaded = ItemPersistence.LoadFilter(vanillaSaved, "filter");
        var namedWhitelist = new MEDiskDriveEntity { Filter = vanillaReloaded, IsWhitelist = true };
        check(vanillaReloaded.SequenceEqual(new[] { (int)ItemID.Wood })
            && ItemPersistence.FilterIdentities(vanillaReloaded).Contains(MissingIdentity)
            && namedWhitelist.Allows(ItemKey.Crafted(ItemID.Wood)) && !namedWhitelist.Allows(ItemKey.Crafted(ItemID.StoneBlock)),
            "Terraria/name imports resolve before save/load and never turn a whitelist into accept-all");
        foreach (string invalid in new[] { "0", "-1", ItemID.Count.ToString(), int.MaxValue.ToString(),
            "Terraria/AE2RegressionMissingItem", "Terraria/None", "ModLoader/UnloadedItem" })
        {
            bool rejected = false;
            try { ListIO.ImportLines(new[] { invalid }); }
            catch (InvalidDataException) { rejected = true; }
            check(rejected, "invalid or mod numeric filter identity rejects the whole import: " + invalid);
        }
        bool blankRejected = false;
        try { ItemPersistence.FilterFromIdentities(new[] { " " }); }
        catch (InvalidDataException) { blankRejected = true; }
        check(blankRejected, "blank network identity is rejected instead of becoming an empty whitelist");
        using var stream = new MemoryStream();
        using (var writer = new BinaryWriter(stream, System.Text.Encoding.UTF8, true)) MENet.WriteFilter(writer, copied);
        stream.Position = 0;
        using var reader = new BinaryReader(stream);
        var received = MENet.ReadFilter(reader);
        check(received.Contains(ItemID.Wood) && ItemPersistence.HasMissingFilterReferences(received)
            && stream.Position == stream.Length, "network filter snapshot preserves missing stable references");
        var explicitClear = ItemPersistence.CloneFilter(new List<int>());
        check(explicitClear.Count == 0 && !ItemPersistence.HasMissingFilterReferences(explicitClear),
            "explicit empty filter replacement clears all references");
        ItemPersistence.ReplaceFilterContents(copied, new[] { (int)ItemID.Gel });
        check(copied.SequenceEqual(new[] { (int)ItemID.Gel }) && !ItemPersistence.HasMissingFilterReferences(copied),
            "explicit import replacement removes previous unavailable references instead of resurrecting them");
    }

    private static void CollectorConfigChecks(Action<bool, string> check)
    {
        var config = MEConfig.Instance;
        var originalFilter = config.FilteredItems;
        bool enabled = config.DropCollectorEnabled, blacklist = config.FilterIsBlacklist;
        float radius = config.DropCollectorRadius;
        try
        {
            config.DropCollectorEnabled = false;
            config.FilterIsBlacklist = false;
            config.DropCollectorRadius = 257;
            config.FilteredItems = new()
            {
                new Terraria.ModLoader.Config.ItemDefinition("Terraria", "Wood"),
                new Terraria.ModLoader.Config.ItemDefinition("AE2RegressionMissingMod", "OriginalItem")
            };
            var collector = new MEDropCollectorEntity();
            collector.ApplyDefaultsFromConfig();
            var tag = new TagCompound();
            collector.SaveData(tag);
            var restored = new MEDropCollectorEntity();
            restored.LoadData(tag);
            check(restored.Filter.SequenceEqual(new[] { (int)ItemID.Wood })
                && ItemPersistence.FilterIdentities(restored.Filter).Contains(MissingIdentity)
                && !restored.Enabled && !restored.IsBlacklist && restored.WorldRange,
                "new collector copies stable missing config references and preserves them across entity save/load");
            config.FilteredItems.RemoveAt(0);
            collector.ApplyDefaultsFromConfig();
            check(collector.Filter.Count == 0 && ItemPersistence.HasMissingFilterReferences(collector.Filter)
                && !collector.Allows(ItemID.Wood) && !collector.Allows(ModContent.ItemType<Terraria.ModLoader.Default.UnloadedItem>()),
                "missing-only configured collector whitelist never treats UnloadedItem as the original item");
            config.DropCollectorEnabled = true;
            config.FilterIsBlacklist = true;
            config.DropCollectorRadius = 12;
            config.FilteredItems.Add(new Terraria.ModLoader.Config.ItemDefinition("ModLoader", "UnloadedItem"));
            collector.ApplyDefaultsFromConfig();
            check(!collector.Enabled && !collector.IsBlacklist && collector.WorldRange && collector.Filter.Count == 0
                && ItemPersistence.FilterIdentities(collector.Filter).Contains(MissingIdentity)
                && config.FilteredItems.Count == 2,
                "invalid native config selection rejects all collector settings without deleting the saved definition");
            foreach (var invalid in new[]
            {
                new Terraria.ModLoader.Config.ItemDefinition("Terraria", "AE2RegressionMissingItem"),
                new Terraria.ModLoader.Config.ItemDefinition("", "OriginalItem")
            })
            {
                config.FilteredItems[1] = invalid;
                collector.ApplyDefaultsFromConfig();
                check(!collector.Enabled && !collector.IsBlacklist && collector.WorldRange
                    && ItemPersistence.FilterIdentities(collector.Filter).Contains(MissingIdentity)
                    && ReferenceEquals(config.FilteredItems[1], invalid),
                    "unknown vanilla or incomplete configured identity cannot clear or widen the previous whitelist");
            }
        }
        finally
        {
            config.FilteredItems = originalFilter;
            config.DropCollectorEnabled = enabled;
            config.FilterIsBlacklist = blacklist;
            config.DropCollectorRadius = radius;
        }
    }

    private sealed class FilterPacketSink : ISocket
    {
        internal readonly List<byte[]> Packets = new();
        public void AsyncSend(byte[] data, int offset, int size, SocketSendCallback callback, object state = null)
        { Packets.Add(data.AsSpan(offset, size).ToArray()); callback?.Invoke(state); }
        public bool IsConnected() => false;
        public bool IsDataAvailable() => false;
        public void Close() { }
        public void SendQueuedPackets() { }
        public void StopListening() { }
        public RemoteAddress GetRemoteAddress() => null;
        public void Connect(RemoteAddress address) => throw new NotSupportedException();
        public bool StartListening(SocketConnectionAccepted callback) => throw new NotSupportedException();
        public void AsyncReceive(byte[] data, int offset, int size, SocketReceiveCallback callback, object state = null)
            => throw new NotSupportedException();
    }

    private static void NetworkSizeChecks(RegressionWorld world, Action<bool, string> check)
    {
        foreach (int envelope in new[] { 7, 8 })
        {
            int boundary = ushort.MaxValue - envelope;
            check(MENet.TrySerializeFilterRequest(writer => writer.Write(new byte[boundary]), envelope, out var exact)
                && exact.Length == boundary && !MENet.TrySerializeFilterRequest(writer => writer.Write(new byte[boundary + 1]),
                    envelope, out var excessive) && excessive == null,
                "client filter payload accepts the exact frame boundary and rejects one byte over with mod header " + envelope);
        }
        world.Reset();
        var unit = world.Place(new MEStorageUnitEntity(), ModContent.TileType<MEStorageUnitTile>(), 30, 30);
        var bus = world.Place(new MEStorageBusEntity(), ModContent.TileType<MEStorageBusTile>(), 31, 30);
        var collector = world.Place(new MEDropCollectorEntity(), ModContent.TileType<MEDropCollectorTile>(), 32, 30);
        var large = ItemPersistence.FilterFromIdentities(Enumerable.Range(0, 1400)
            .Select(index => "AE2RegressionMissingMod/Entry" + index + new string('x', 50)));
        var sink = new FilterPacketSink();
        int oldMode = Main.netMode;
        var oldClient = Netplay.Clients[0];
        var oldSocket = Netplay.Connection.Socket;
        Mod mod = ModContent.GetInstance<AE2ForTerraria>();
        var netId = typeof(Mod).GetField("netID", BindingFlags.Instance | BindingFlags.NonPublic);
        var netMods = typeof(ModNet).GetField("netMods", BindingFlags.Static | BindingFlags.NonPublic);
        object oldNetId = netId.GetValue(mod), oldNetMods = netMods.GetValue(null);
        try
        {
            netMods.SetValue(null, new[] { mod }); netId.SetValue(mod, (short)0);
            Netplay.Clients[0] = new RemoteClient { State = 10, Socket = sink };
            Netplay.Connection.Socket = sink;
            foreach (var snapshot in new (Action send, Action<List<int>> set, Func<List<int>> get)[]
            {
                (() => MENet.SyncStorageFilter(unit.Position, 0), filter => unit.Filter = filter, () => unit.Filter),
                (() => MENet.SyncBusConfig(bus.Position, 0), filter => bus.Filter = filter, () => bus.Filter),
                (() => MENet.SyncCollector(collector.Position, 0), filter => collector.Filter = filter, () => collector.Filter)
            })
            {
                snapshot.set(large); sink.Packets.Clear(); Main.netMode = NetmodeID.Server;
                snapshot.send();
                check(sink.Packets.Count > 1 && sink.Packets.All(raw => raw.Length <= ushort.MaxValue
                    && BitConverter.ToUInt16(raw, 0) == raw.Length && (raw[7] & 63) == 36),
                    "large server filter snapshot uses only bounded existing fragment frames");
                snapshot.set(new()); Main.netMode = NetmodeID.MultiplayerClient;
                foreach (byte[] raw in sink.Packets)
                {
                    using var reader = new BinaryReader(new MemoryStream(raw[4..]));
                    MENet.Handle(reader, 256);
                }
                check(ItemPersistence.FilterIdentities(snapshot.get()).SequenceEqual(ItemPersistence.FilterIdentities(large)),
                    "fragmented filter snapshot reassembles all unavailable stable identities");
            }
            foreach (Func<bool> request in new Func<bool>[]
            {
                () => MENet.SendStorageFilter(unit.Position, true, large),
                () => MENet.SendBusConfig(bus.Position, true, true, large),
                () => MENet.SendCollectorConfig(collector.Position, false, 257, false, large),
                () => MENet.SetLockedList(unit.Position, large)
            })
            {
                sink.Packets.Clear(); Main.netMode = NetmodeID.MultiplayerClient;
                check(!request() && sink.Packets.Count == 1 && sink.Packets[0][7] == MENet.ClientRequestLedger,
                    "oversize filter request sends no mutation and requests the unchanged authoritative configuration");
            }
        }
        finally
        {
            Main.netMode = oldMode; Netplay.Clients[0] = oldClient; Netplay.Connection.Socket = oldSocket;
            netId.SetValue(mod, oldNetId); netMods.SetValue(null, oldNetMods);
        }
    }

    private static void NetworkFilterChecks(RegressionWorld world, Action<bool, string> check)
    {
        world.Reset();
        var unit = world.Place(new MEStorageUnitEntity(), ModContent.TileType<MEStorageUnitTile>(), 30, 30);
        var bus = world.Place(new MEStorageBusEntity(), ModContent.TileType<MEStorageBusTile>(), 31, 30);
        var collector = world.Place(new MEDropCollectorEntity(), ModContent.TileType<MEDropCollectorTile>(), 32, 30);
        var replacement = MissingFilter();
        replacement.Add(ItemID.StoneBlock);
        var serverHandler = typeof(MENet).GetMethod("HandleOnServer", BindingFlags.Static | BindingFlags.NonPublic);
        int oldMode = Main.netMode;
        try
        {
            bool Old(List<int> filter) => filter.SequenceEqual(new[] { (int)ItemID.Wood })
                && !ItemPersistence.HasMissingFilterReferences(filter);
            bool Changed(List<int> filter) => filter.SequenceEqual(new[] { (int)ItemID.StoneBlock })
                && ItemPersistence.HasMissingFilterReferences(filter);
            void Verify(string label, byte request, byte snapshot, Terraria.DataStructures.Point16 position,
                Action<BinaryWriter> requestHeader, Action<BinaryWriter> snapshotHeader, Action reset,
                Func<bool> unchanged, Func<bool> changed)
            {
                foreach (bool server in new[] { false, true })
                {
                    byte[] Packet(string invalid = null)
                    {
                        using var bytes = new MemoryStream();
                        using var writer = new BinaryWriter(bytes);
                        writer.Write(server ? request : snapshot); writer.Write(position.X); writer.Write(position.Y);
                        (server ? requestHeader : snapshotHeader)(writer);
                        if (invalid != null)
                        {
                            byte[] identity = System.Text.Encoding.UTF8.GetBytes(invalid);
                            writer.Write(1); writer.Write((ushort)identity.Length); writer.Write(identity);
                        }
                        else MENet.WriteFilter(writer, replacement);
                        return bytes.ToArray();
                    }
                    void Receive(byte[] bytes)
                    {
                        using var reader = new BinaryReader(new MemoryStream(bytes));
                        if (!server)
                        {
                            Main.netMode = NetmodeID.MultiplayerClient;
                            MENet.HandlePayload(reader, 256);
                            return;
                        }
                        // Exercise the authoritative decode/apply branch without sockets or changing player permissions.
                        Main.netMode = NetmodeID.SinglePlayer;
                        reader.ReadByte(); reader.ReadInt16(); reader.ReadInt16();
                        try { serverHandler.Invoke(null, new object[] { request, position, reader, 0 }); }
                        catch (TargetInvocationException ex) when (ex.InnerException is IOException or InvalidDataException) { }
                    }
                    byte[] complete = Packet();
                    foreach (var bad in new[] { complete[..^1], complete.Concat(new byte[] { 7 }).ToArray(),
                        Packet("0"), Packet(" "), Packet("Terraria/AE2RegressionMissingItem"), Packet("ModLoader/UnloadedItem") })
                    {
                        Main.netMode = server ? NetmodeID.SinglePlayer : NetmodeID.MultiplayerClient;
                        reset(); Receive(bad);
                        check(unchanged(), label + (server ? " server" : " client") + " rejects malformed filter without partial settings");
                    }
                    Main.netMode = server ? NetmodeID.SinglePlayer : NetmodeID.MultiplayerClient;
                    reset(); Receive(complete);
                    check(changed(), label + (server ? " server" : " client") + " publishes settings and missing references together");
                }
            }
            Action<BinaryWriter> unitHeader = writer => writer.Write(true);
            Verify("partition", MENet.ClientStorageFilter, MENet.ServerSyncStorageFilter, unit.Position, unitHeader, unitHeader,
                () => { unit.IsWhitelist = false; unit.Filter = new() { ItemID.Wood }; unit.MarkFilterDirty(); },
                () => !unit.IsWhitelist && Old(unit.Filter), () => unit.IsWhitelist && Changed(unit.Filter));
            Action<BinaryWriter> busHeader = writer => { writer.Write(true); writer.Write(true); };
            Verify("bus", MENet.ClientBusConfig, MENet.ServerSyncBusConfig, bus.Position, busHeader, busHeader,
                () => { bus.SetExportMode(false); bus.IsWhitelist = false; bus.Filter = new() { ItemID.Wood }; bus.MarkFilterDirty(); },
                () => !bus.ExportMode && !bus.IsWhitelist && Old(bus.Filter),
                () => bus.ExportMode && bus.IsWhitelist && Changed(bus.Filter));
            Action<BinaryWriter> collectorHeader = writer => { writer.Write(false); writer.Write(257f); writer.Write(false); };
            Verify("collector", MENet.ClientSetCollector, MENet.ServerSyncCollector, collector.Position, collectorHeader, collectorHeader,
                () => { collector.Enabled = true; collector.Radius = 24; collector.IsBlacklist = true;
                    collector.Filter = new() { ItemID.Wood }; collector.MarkFilterDirty(); },
                () => collector.Enabled && collector.Radius == 24 && collector.IsBlacklist && Old(collector.Filter),
                () => !collector.Enabled && collector.WorldRange && !collector.IsBlacklist && Changed(collector.Filter));
            Verify("locks", MENet.ClientSetLockedList, MENet.ServerSyncSecurity, unit.Position, _ => { },
                writer => { writer.Write("adaptation-fixture"); writer.Write(0); writer.Write(0); },
                () => NetworkRegistry.SetLockedList(unit.Position, new() { ItemID.Wood }),
                () => Old(NetworkRegistry.GetLocked(unit.Position)), () => Changed(NetworkRegistry.GetLocked(unit.Position)));
        }
        finally { Main.netMode = oldMode; }
    }
}
