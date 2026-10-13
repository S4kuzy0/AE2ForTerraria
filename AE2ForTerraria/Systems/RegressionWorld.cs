using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using Terraria;
using Terraria.DataStructures;
using Terraria.ID;
using Terraria.ModLoader;
using Terraria.ObjectData;

namespace AE2ForTerraria.Systems;

/// <summary>Opt-in regression fixture. Borrows and restores a small native tile region without allocating a Tilemap.</summary>
internal sealed class RegressionWorld : IDisposable
{
    public const int Size = 96;
    private readonly Tilemap _tiles;
    private readonly int _maxX, _maxY, _mode;
    private readonly bool _menu;
    private readonly Chest[] _chests;
    private readonly Dictionary<int, TileEntity> _entities;
    private readonly Dictionary<Point16, TileEntity> _positions;
    private readonly NativeTile[] _saved = new NativeTile[Size * Size];
    private readonly List<Action> _restoreStatics = new();
    private bool _disposed;
    private int _nextId;

    private readonly struct NativeTile
    {
        private readonly TileTypeData _type;
        private readonly WallTypeData _wall;
        private readonly LiquidData _liquid;
        private readonly TileWallBrightnessInvisibilityData _light;
        private readonly TileWallWireStateData _state;
        public NativeTile(Tile tile)
        {
            _type = tile.Get<TileTypeData>();
            _wall = tile.Get<WallTypeData>();
            _liquid = tile.Get<LiquidData>();
            _light = tile.Get<TileWallBrightnessInvisibilityData>();
            _state = tile.Get<TileWallWireStateData>();
        }
        public void Restore(Tile tile)
        {
            tile.Get<TileTypeData>() = _type;
            tile.Get<WallTypeData>() = _wall;
            tile.Get<LiquidData>() = _liquid;
            tile.Get<TileWallBrightnessInvisibilityData>() = _light;
            tile.Get<TileWallWireStateData>() = _state;
        }
    }

    public RegressionWorld()
    {
        if (!Main.gameMenu || Main.tile.Width < Size || Main.tile.Height < Size)
            throw new InvalidOperationException("RegressionWorld requires the initialized tile map at the main menu; no world may be open.");
        _tiles = Main.tile;
        _maxX = Main.maxTilesX; _maxY = Main.maxTilesY;
        _mode = Main.netMode; _menu = Main.gameMenu;
        _chests = Main.chest;
        _entities = TileEntity.ByID; _positions = TileEntity.ByPosition;
        for (int x = 0; x < Size; x++)
            for (int y = 0; y < Size; y++) _saved[x * Size + y] = new NativeTile(Main.tile[x, y]);

        // Keep the original dictionaries and their values intact, including missing-mod filter references.
        try
        {
            foreach (Type type in new[] { typeof(NetworkRegistry), typeof(MENetwork), typeof(NetworkInventory), typeof(WirelessRegistry), typeof(MEBlocks) })
                SnapshotStatics(type);
            Main.maxTilesX = Main.maxTilesY = Size;
            Main.netMode = NetmodeID.SinglePlayer;
            TileEntity.ByID = new(); TileEntity.ByPosition = new();
            Reset();
        }
        catch { Dispose(); throw; }
    }

    private void SnapshotStatics(Type type)
    {
        foreach (FieldInfo field in type.GetFields(BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic))
        {
            if (field.IsLiteral) continue;
            object original = field.GetValue(null);
            if (original is IDictionary dictionary)
            {
                var entries = new List<DictionaryEntry>();
                foreach (DictionaryEntry entry in dictionary) entries.Add(entry);
                _restoreStatics.Add(() =>
                {
                    dictionary.Clear();
                    foreach (DictionaryEntry entry in entries) dictionary.Add(entry.Key, entry.Value);
                });
                dictionary.Clear();
            }
            else if (!field.IsInitOnly)
                _restoreStatics.Add(() => field.SetValue(null, original));
        }
    }

    public void Reset()
    {
        for (int x = 0; x < Size; x++)
            for (int y = 0; y < Size; y++) default(NativeTile).Restore(Main.tile[x, y]);
        Main.chest = new Chest[_chests.Length];
        TileEntity.ByID.Clear(); TileEntity.ByPosition.Clear(); _nextId = 0;
        ModContent.GetInstance<NetworkRegistry>().ClearWorld();
        MENetwork.InvalidateCache(); WirelessRegistry.Reset();
    }

    public void SetTile(int x, int y, int tileType, short frameX = 0, short frameY = 0)
    {
        if (x < 0 || y < 0 || x >= Size || y >= Size) throw new ArgumentOutOfRangeException(nameof(x));
        Tile tile = Main.tile[x, y];
        default(NativeTile).Restore(tile);
        tile.HasTile = true; tile.TileType = (ushort)tileType;
        tile.TileFrameX = frameX; tile.TileFrameY = frameY;
        MENetwork.InvalidateCache();
    }

    public void ClearTile(int x, int y)
    {
        if (x < 0 || y < 0 || x >= Size || y >= Size) throw new ArgumentOutOfRangeException(nameof(x));
        default(NativeTile).Restore(Main.tile[x, y]);
        MENetwork.InvalidateCache();
    }

    public T Place<T>(T entity, int tileType, int x, int y) where T : ModTileEntity
    {
        SetTile(x, y, tileType);
        entity.ID = _nextId++; entity.Position = new Point16(x, y);
        TileEntity.ByID.Add(entity.ID, entity); TileEntity.ByPosition.Add(entity.Position, entity);
        MENetwork.InvalidateCache();
        return entity;
    }

    public Chest PlaceChest(int x, int y, int tileType = TileID.Containers, int style = 0)
    {
        TileObjectData data = TileObjectData.GetTileData(tileType, style);
        if (data == null) throw new InvalidOperationException("Chest tile data is not initialized.");
        int index = Array.FindIndex(Main.chest, chest => chest == null);
        if (index < 0) throw new InvalidOperationException("Regression chest slots exhausted.");
        var chest = new Chest { x = x, y = y };
        for (int slot = 0; slot < chest.item.Length; slot++) chest.item[slot] = new Item();
        Main.chest[index] = chest;
        for (int dx = 0; dx < data.Width; dx++)
            for (int dy = 0; dy < data.Height; dy++)
                SetTile(x + dx, y + dy, tileType,
                    (short)(style * data.CoordinateFullWidth + dx * (data.CoordinateWidth + data.CoordinatePadding)),
                    (short)(dy * (data.CoordinateHeights[0] + data.CoordinatePadding)));
        return chest;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        MENetwork.InvalidateCache(); WirelessRegistry.Reset();
        Main.tile = _tiles;
        for (int x = 0; x < Size; x++)
            for (int y = 0; y < Size; y++) _saved[x * Size + y].Restore(Main.tile[x, y]);
        Main.maxTilesX = _maxX; Main.maxTilesY = _maxY;
        Main.netMode = _mode; Main.gameMenu = _menu;
        Main.chest = _chests; TileEntity.ByID = _entities; TileEntity.ByPosition = _positions;
        for (int i = _restoreStatics.Count - 1; i >= 0; i--) _restoreStatics[i]();
    }
}
