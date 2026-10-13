using System;
using System.Linq;
using AE2ForTerraria.Content.Items;
using AE2ForTerraria.Content.Tiles;
using AE2ForTerraria.Core;
using Terraria;
using Terraria.DataStructures;
using Terraria.ID;
using Terraria.ModLoader;
using Terraria.ObjectData;

namespace AE2ForTerraria.Systems;

/// <summary>Checks real registered tile data and temporary native tiles; never opens a UI or saves a world.</summary>
public static class MachineTileRegression
{
    public static void Run(Action<bool, string> check)
    {
        var machines = new (ModItem Item, ModTile Legacy, ModTile Large, Func<ModTileEntity> Entity)[]
        {
            (ModContent.GetInstance<MEAccessTerminal>(), ModContent.GetInstance<MEAccessTerminalTile>(), ModContent.GetInstance<MEAccessTerminalTile2x2>(), null),
            (ModContent.GetInstance<MECraftingTerminal>(), ModContent.GetInstance<MECraftingTerminalTile>(), ModContent.GetInstance<MECraftingTerminalTile2x2>(), () => new MECraftingTerminalEntity()),
            (ModContent.GetInstance<MECraftingCpu>(), ModContent.GetInstance<MECraftingCpuTile>(), ModContent.GetInstance<MECraftingCpuTile2x2>(), () => new MECraftingCpuEntity()),
            (ModContent.GetInstance<MEDiskDrive>(), ModContent.GetInstance<MEDiskDriveTile>(), ModContent.GetInstance<MEDiskDriveTile2x2>(), () => new MEDiskDriveEntity()),
            (ModContent.GetInstance<MEStorageUnit>(), ModContent.GetInstance<MEStorageUnitTile>(), ModContent.GetInstance<MEStorageUnitTile2x2>(), () => new MEStorageUnitEntity()),
            (ModContent.GetInstance<MEMolecularAssembler>(), ModContent.GetInstance<MEMolecularAssemblerTile>(), ModContent.GetInstance<MEMolecularAssemblerTile2x2>(), () => new MEMolecularAssemblerEntity()),
            (ModContent.GetInstance<MEDropCollector>(), ModContent.GetInstance<MEDropCollectorTile>(), ModContent.GetInstance<MEDropCollectorTile2x2>(), () => new MEDropCollectorEntity()),
            (ModContent.GetInstance<MEWirelessBridge>(), ModContent.GetInstance<MEWirelessBridgeTile>(), ModContent.GetInstance<MEWirelessBridgeTile2x2>(), () => new MEWirelessBridgeEntity())
        };
        check(machines.Select(machine => machine.Legacy.Type).Distinct().Count() == 8
            && machines.Select(machine => machine.Large.Type).Distinct().Count() == 8,
            "all eight legacy machine IDs and eight new machine IDs remain distinct and registered");
        using var world = new RegressionWorld();
        foreach (var machine in machines)
        {
            TileObjectData oldData = TileObjectData.GetTileData(machine.Legacy.Type, 0);
            TileObjectData newData = TileObjectData.GetTileData(machine.Large.Type, 0);
            check(!Main.tileSolid[machine.Legacy.Type] && !Main.tileSolid[machine.Large.Type]
                && !Main.tileSolidTop[machine.Legacy.Type] && !Main.tileSolidTop[machine.Large.Type],
                machine.Item.Name+" old and new tiles allow the player to walk through them");
            check(oldData != null && oldData.Width == 1 && oldData.Height == 1
                && newData != null && newData.Width == 2 && newData.Height == 2
                && newData.CoordinateWidth == 16 && newData.CoordinatePadding == 2
                && newData.CoordinateHeights.SequenceEqual(new[] { 16, 16 }),
                machine.Item.Name + " keeps legacy one-cell tile data and registers the new two-cell frame layout");
            check(!oldData.LavaDeath && !newData.LavaDeath,
                machine.Item.Name + " old and new machine templates explicitly resist lava destruction");
            check(new Item(machine.Item.Type).createTile == machine.Large.Type
                && machine.Large.Type != machine.Legacy.Type
                && MachineTiles.CanonicalType(machine.Large.Type) == machine.Legacy.Type
                && MachineTiles.CanonicalType(machine.Legacy.Type) == machine.Legacy.Type,
                machine.Item.Name + " newly places the larger tile while retaining the original registered identity");

            world.Reset();
            PlaceLarge(world, machine.Entity?.Invoke(), machine.Large.Type, 40, 40);
            check(!Collision.SolidCollision(new Microsoft.Xna.Framework.Vector2(40*16,40*16),32,32),
                machine.Item.Name+" native collision check allows movement through the placed machine");
            bool allOrigins = true, validOnlyAtAnchor = true;
            ModTileEntity entity = machine.Entity?.Invoke();
            for (int dx = 0; dx < 2; dx++)
            for (int dy = 0; dy < 2; dy++)
            {
                int x = 40 + dx, y = 40 + dy;
                bool anchor = dx == 0 && dy == 0;
                allOrigins &= MachineTiles.Origin(x, y) == new Point16(40, 40)
                    && MachineTiles.IsEntityAnchor(x, y, machine.Legacy.Type) == anchor;
                if (entity != null) validOnlyAtAnchor &= entity.IsTileValidForEntity(x, y) == anchor;
            }
            check(allOrigins && validOnlyAtAnchor,
                machine.Item.Name + " resolves all four interaction cells to one origin and accepts an entity only at that origin");

            world.Reset();
            if (machine.Entity != null) world.Place(machine.Entity(), machine.Legacy.Type, 40, 40);
            else world.SetTile(40, 40, machine.Legacy.Type);
            world.SetTile(41, 40, TileID.Stone);
            MENetwork.Collect(40, 40);
            MachineTiles.EnsureTerminal(40, 40);
            check(Main.tile[40, 40].TileType == machine.Legacy.Type && Main.tile[41, 40].TileType == TileID.Stone
                && MachineTiles.Origin(40, 40) == new Point16(40, 40)
                && (entity == null || entity.IsTileValidForEntity(40, 40)),
                machine.Item.Name + " legacy world tile remains one cell with its original ID and untouched neighbour");
        }
        DriveProtection(check, world);
        DirectDestructionProtection(check, world);
        EdgeConnections(check, world);
    }

    private static void PlaceLarge(RegressionWorld world, ModTileEntity entity, int type, int x, int y)
    {
        if (entity != null) world.Place(entity, type, x, y);
        for (int dx = 0; dx < 2; dx++)
        for (int dy = 0; dy < 2; dy++)
            world.SetTile(x + dx, y + dy, type, (short)(dx * 18), (short)(dy * 18));
    }

    private static void DriveProtection(Action<bool, string> check, RegressionWorld world)
    {
        world.Reset();
        var drive = new MEDiskDriveEntity();
        var tile = ModContent.GetInstance<MEDiskDriveTile2x2>();
        PlaceLarge(world, drive, tile.Type, 40, 40);
        drive.Disks[0] = new Item(ModContent.ItemType<MEDigitalSingularity>()); drive.RefreshCapacity();
        drive.Ledger.Restore(ItemKey.Crafted(ItemID.Wood), long.MaxValue);
        int previousMode = Main.netMode;
        try
        {
            // Suppress client toasts while exercising the actual public tile guards.
            Main.netMode = NetmodeID.Server;
            bool allRefused = true;
            for (int dx = 0; dx < 2; dx++)
            for (int dy = 0; dy < 2; dy++)
            {
                bool damaged = false;
                allRefused &= !tile.CanKillTile(40 + dx, 40 + dy, ref damaged)
                    && !tile.CanExplode(40 + dx, 40 + dy);
            }
            check(allRefused && drive.Ledger.StoredTotalAmount == long.MaxValue
                && TileEntity.ByPosition.Count == 1 && !drive.Disks[0].IsAir,
                "all four large-drive cells refuse mining and explosions without changing full singularity stock or disk");
            drive.Ledger.Extract(ItemKey.Crafted(ItemID.Wood), long.MaxValue);
            bool blocked = false;
            check(!tile.CanKillTile(41, 41, ref blocked), "an empty ledger still cannot destroy an installed disk");
            drive.Disks[0] = new Item(); drive.RefreshCapacity();
            check(tile.CanKillTile(41, 41, ref blocked) && tile.CanExplode(41, 41),
                "removing all inventory and disks releases the large-drive destruction guard");
        }
        finally { Main.netMode = previousMode; }
    }

    private static void DirectDestructionProtection(Action<bool, string> check, RegressionWorld world)
    {
        var cases = new (ModTile Old, ModTile Large, Func<ModTileEntity> Create, Func<ModTileEntity, bool> Preserved)[]
        {
            (ModContent.GetInstance<MEDiskDriveTile>(), ModContent.GetInstance<MEDiskDriveTile2x2>(),
                () => { var drive = new MEDiskDriveEntity(); drive.Disks[0] = new Item(ModContent.ItemType<MEDigitalSingularity>());
                    drive.RefreshCapacity(); drive.Ledger.Restore(ItemKey.Crafted(ItemID.Wood), long.MaxValue); return drive; },
                entity => entity is MEDiskDriveEntity drive && drive.Ledger.StoredTotalAmount == long.MaxValue
                    && drive.Disks[0].type == ModContent.ItemType<MEDigitalSingularity>()),
            (ModContent.GetInstance<MEStorageUnitTile>(), ModContent.GetInstance<MEStorageUnitTile2x2>(),
                () => { var unit = new MEStorageUnitEntity(); unit.Ledger.Restore(ItemKey.Crafted(ItemID.Wood), 23); return unit; },
                entity => entity is MEStorageUnitEntity unit && unit.Ledger.StoredTotalAmount == 23),
            (ModContent.GetInstance<MEMolecularAssemblerTile>(), ModContent.GetInstance<MEMolecularAssemblerTile2x2>(),
                () => { var assembler = new MEMolecularAssemblerEntity(); assembler.Stations[0] = new Item(ItemID.WorkBench); return assembler; },
                entity => entity is MEMolecularAssemblerEntity assembler && assembler.Stations[0].type == ItemID.WorkBench),
            (ModContent.GetInstance<MECraftingTerminalTile>(), ModContent.GetInstance<MECraftingTerminalTile2x2>(),
                () => { var terminal = new MECraftingTerminalEntity(); terminal.Stations[0] = new Item(ItemID.WorkBench); return terminal; },
                entity => entity is MECraftingTerminalEntity terminal && terminal.Stations[0].type == ItemID.WorkBench)
        };
        int previousMode = Main.netMode;
        try
        {
            Main.netMode = NetmodeID.Server;
            foreach (var machine in cases)
            foreach (bool large in new[] { false, true })
            {
                world.Reset();
                var entity = machine.Create(); ModTile tile = large ? machine.Large : machine.Old;
                if (large) PlaceLarge(world, entity, tile.Type, 40, 40);
                else world.Place(entity, tile.Type, 40, 40);
                bool denied = true;
                for (int dx = 0; dx < (large ? 2 : 1); dx++)
                for (int dy = 0; dy < (large ? 2 : 1); dy++)
                {
                    bool fail = false, effectOnly = false, noItem = false;
                    // Liquid destruction calls KillTile directly, bypassing the normal CanKillTile preflight.
                    tile.KillTile(40 + dx, 40 + dy, ref fail, ref effectOnly, ref noItem);
                    denied &= fail && TileEntity.ByPosition.TryGetValue(new Point16(40, 40), out var found)
                        && ReferenceEquals(found, entity) && machine.Preserved(entity);
                }
                check(denied, tile.Name + " direct destruction rejects every occupied cell and preserves its entity and stored contents");
            }
        }
        finally { Main.netMode = previousMode; }
    }

    private static void EdgeConnections(Action<bool, string> check, RegressionWorld world)
    {
        ItemKey wood = ItemKey.Crafted(ItemID.Wood);
        var edges = new[] { new Point16(39, 40), new Point16(42, 41), new Point16(41, 39), new Point16(40, 42) };
        foreach (Point16 edge in edges)
        {
            world.Reset();
            var drive = new MEDiskDriveEntity();
            PlaceLarge(world, drive, ModContent.TileType<MEDiskDriveTile2x2>(), 40, 40);
            drive.Disks[0] = new Item(ModContent.ItemType<MEDigitalSingularity>()); drive.RefreshCapacity();
            drive.Ledger.Restore(wood, 37);
            world.SetTile(edge.X, edge.Y, ModContent.TileType<MECableTile>());
            var units = MENetwork.CollectStorage(edge.X, edge.Y);
            bool unique = units.Count == 1 && ReferenceEquals(units[0], drive)
                && MENetwork.Collect(edge.X, edge.Y).Count == 5
                && MENetwork.Aggregate(units)[wood] == 37;
            for (int dx = 0; dx < 2; dx++)
            for (int dy = 0; dy < 2; dy++)
                unique &= MENetwork.CollectStorage(40 + dx, 40 + dy).Count == 1;
            check(unique, "cable at " + edge + " connects to the complete machine and counts its inventory exactly once");
        }
        var legacy = world.Place(new MEStorageUnitEntity(), ModContent.TileType<MEStorageUnitTile>(), 41, 42);
        legacy.Ledger.Restore(wood, 5);
        var mixed = MENetwork.CollectStorage(40, 40);
        check(mixed.Count == 2 && MENetwork.Aggregate(mixed)[wood] == 42
            && Main.tile[41, 42].TileType == ModContent.TileType<MEStorageUnitTile>(),
            "new large drive and old one-cell storage interoperate without duplicating inventory or rewriting old tile IDs");
    }
}
