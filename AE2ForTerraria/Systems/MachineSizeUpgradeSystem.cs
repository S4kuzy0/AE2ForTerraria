using System;
using System.Collections.Generic;
using System.IO;
using Microsoft.Xna.Framework;
using Terraria;
using Terraria.Chat;
using Terraria.DataStructures;
using Terraria.ID;
using Terraria.Localization;
using Terraria.ModLoader;
using Terraria.Utilities;

namespace AE2ForTerraria.Systems;

/// <summary>Upgrade old machine tiles in place without recreating their inventory-bearing entities.</summary>
public sealed class MachineSizeUpgradeSystem : ModSystem
{
    private readonly List<Point16> _pending = new();
    private int _cursor, _ticks;
    private bool _backedUp, _backupFailed, _reportedWaiting;

    public override void ClearWorld()
    {
        _pending.Clear(); _cursor = _ticks = 0;
        _backedUp = _backupFailed = _reportedWaiting = false;
    }

    public override void PostWorldLoad()
    {
        ClearWorld();
        if (Main.netMode == NetmodeID.MultiplayerClient) return;
        // A single load-time pass also finds access terminals, which have no tile entity.
        // Later retries only visit these recorded legacy positions, never the whole world.
        for (int x = 0; x < Main.maxTilesX; x++)
        for (int y = 0; y < Main.maxTilesY; y++)
        {
            Tile tile = Main.tile[x, y];
            if (tile.HasTile && MachineTiles.TryGetLargeType(tile.TileType, out _))
                _pending.Add(new Point16(x, y));
        }
    }

    public override void PostUpdateEverything()
    {
        if (Main.gameMenu || Main.netMode == NetmodeID.MultiplayerClient || _backupFailed || _pending.Count == 0) return;
        if (_ticks > 0) { _ticks--; return; }
        _ticks = 59;
        int converted = 0, attempts = Math.Min(64, _pending.Count);
        for (int n = 0; n < attempts && _pending.Count > 0; n++)
        {
            if (_cursor >= _pending.Count) _cursor = 0;
            Point16 position = _pending[_cursor];
            Tile tile = Main.tile[position.X, position.Y];
            if (!tile.HasTile || !MachineTiles.TryGetLargeType(tile.TileType, out _))
            { RemoveCurrent(); continue; }
            if (!CanExpand(position)) { _cursor++; continue; }
            if (!_backedUp && !BackupOriginalWorld()) break;
            // Resolve old component policies before extending its physical footprint.
            MENetwork.Collect(position.X, position.Y);
            if (!TryExpand(position)) { _cursor++; continue; }
            RemoveCurrent(); converted++;
            MachineTiles.EnsureTerminal(position.X, position.Y);
            if (Main.netMode == NetmodeID.Server)
            {
                NetMessage.SendTileSquare(-1, position.X, position.Y, 2, 2);
                if (TileEntity.ByPosition.TryGetValue(position, out var entity))
                    NetMessage.SendData(MessageID.TileEntitySharing, -1, -1, null, entity.ID, position.X, position.Y);
            }
        }
        if (converted > 0)
        {
            MENetwork.InvalidateCache();
            Mod.Logger.Info($"Expanded {converted} legacy ME machines; {_pending.Count} positions remain pending.");
        }
        if (!_reportedWaiting && _pending.Count > 0 && !_backupFailed)
        {
            _reportedWaiting = true;
            const string message = "旧 ME 机器会自动扩为两格宽、两格高；请腾空机器右侧、下方和右下方的占位。内部物品和任务保持不变。";
            if (Main.netMode == NetmodeID.Server)
                ChatHelper.BroadcastChatMessage(NetworkText.FromLiteral(message), new Color(150, 205, 235));
            else Main.NewText(message, 150, 205, 235);
        }
    }

    private void RemoveCurrent()
    {
        _pending[_cursor] = _pending[^1];
        _pending.RemoveAt(_pending.Count - 1);
    }

    internal static bool CanExpand(Point16 position)
    {
        int x = position.X, y = position.Y;
        if (!WorldGen.InWorld(x, y, 1) || !WorldGen.InWorld(x + 1, y + 1, 1)) return false;
        Tile origin = Main.tile[x, y];
        if (!origin.HasTile || !MachineTiles.TryGetLargeType(origin.TileType, out _)) return false;
        for (int dx = 0; dx < 2; dx++)
        for (int dy = 0; dy < 2; dy++)
        {
            if (dx == 0 && dy == 0) continue;
            if (Main.tile[x + dx, y + dy].HasTile
                || TileEntity.ByPosition.ContainsKey(new Point16(x + dx, y + dy))) return false;
        }
        return true;
    }

    internal static bool TryExpand(Point16 position)
    {
        if (Main.netMode == NetmodeID.MultiplayerClient || !CanExpand(position)) return false;
        int x = position.X, y = position.Y;
        Tile origin = Main.tile[x, y];
        MachineTiles.TryGetLargeType(origin.TileType, out int type);
        byte paint = origin.TileColor;
        bool invisible = origin.IsTileInvisible, fullbright = origin.IsTileFullbright, actuated = origin.IsActuated;
        // Never call KillTile/Place or replace a TE: its ID, position, disks, ledger,
        // stations, jobs and access bindings continue to refer to the same object.
        for (int dx = 0; dx < 2; dx++)
        for (int dy = 0; dy < 2; dy++)
        {
            Tile part = Main.tile[x + dx, y + dy];
            part.HasTile = true;
            part.TileType = (ushort)type;
            part.TileFrameX = (short)(dx * 18); part.TileFrameY = (short)(dy * 18);
            part.Slope = SlopeType.Solid; part.IsHalfBlock = false;
            part.TileColor = paint; part.IsTileInvisible = invisible; part.IsTileFullbright = fullbright;
            part.IsActuated = actuated;
            // Existing walls, liquids, wires and actuators are deliberately retained.
        }
        return true;
    }

    private bool BackupOriginalWorld()
    {
        try
        {
            var world = Main.ActiveWorldFileData;
            if (world == null || string.IsNullOrEmpty(world.Path) || !FileUtilities.Exists(world.Path, world.IsCloudSave))
                throw new IOException("Cannot find the original world file for the machine-size migration backup.");
            string directory = Path.Combine(Main.SavePath, "AE2-Backups", "machine-size-0.24.1", world.UniqueId.ToString(),
                DateTime.UtcNow.ToString("yyyyMMdd-HHmmss") + "-" + Guid.NewGuid().ToString("N")[..8]);
            Directory.CreateDirectory(directory);
            foreach (string path in new[] { world.Path, Path.ChangeExtension(world.Path, ".twld") })
            {
                if (!FileUtilities.Exists(path, world.IsCloudSave)) continue;
                byte[] data = FileUtilities.ReadAllBytes(path, world.IsCloudSave);
                using var file = new FileStream(Path.Combine(directory, Path.GetFileName(path)), FileMode.CreateNew, FileAccess.Write);
                file.Write(data);
            }
            File.WriteAllText(Path.Combine(directory, "restore.txt"),
                "Original world before the ME machine size upgrade. Close the game and preserve newer progress before restoring this .wld/.twld pair. Original path: " + world.Path);
            _backedUp = true;
            Mod.Logger.Info("ME machine-size migration backup: " + directory);
            return true;
        }
        catch (Exception ex)
        {
            _backupFailed = true;
            Mod.Logger.Error("Old ME machines were left unchanged because their original world could not be backed up.", ex);
            return false;
        }
    }
}
