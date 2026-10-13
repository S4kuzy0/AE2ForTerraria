using System;
using System.Collections.Generic;
using System.Linq;
using AE2ForTerraria.Content.Tiles;
using AE2ForTerraria.Core;
using Microsoft.Xna.Framework;
using Terraria;
using Terraria.DataStructures;
using Terraria.ID;
using Terraria.ModLoader;

namespace AE2ForTerraria.Systems;

/// <summary>Bounded, world-local warm caches. Game recipe callbacks stay on the main thread.</summary>
public sealed class TerminalCraftingCacheSystem : ModSystem
{
    private sealed class Cached
    {
        internal readonly TerminalCraftingAvailability Availability = new();
        internal ulong LastUsed;
    }
    // Point16 only implements Equals(object) in the installed tML version. The
    // default comparer boxes the key on each hit in this per-frame lookup.
    private sealed class TilePositionComparer : IEqualityComparer<Point16>
    {
        public bool Equals(Point16 left, Point16 right) => left.X == right.X && left.Y == right.Y;
        public int GetHashCode(Point16 position) => position.GetHashCode();
    }
    private static readonly Dictionary<Point16, Cached> Caches = new(new TilePositionComparer());
    private static RecipeIndex _index;
    private static int _indexRevision;
    private static CraftPlanner _planner;
    private static Point16 _warming = new(-1,-1);
    private ulong _lastSelection, _lastRefresh;

    internal static CraftPlanner AvailabilityPlanner
    {
        get
        {
            if (!ReferenceEquals(_index, StorageSystem.Recipes) || _indexRevision != (StorageSystem.Recipes?.Revision ?? 0))
            { _index = StorageSystem.Recipes; _indexRevision = _index?.Revision ?? 0; _planner = _index == null ? null : new CraftPlanner(_index); Caches.Clear(); }
            return _planner;
        }
    }
    internal static TerminalCraftingAvailability Current => For(UISystem.ActiveTerminalPos);
    private static TerminalCraftingAvailability For(Point16 position)
    {
        _ = AvailabilityPlanner;
        if (!Caches.TryGetValue(position,out var cached))
        {
            if (Caches.Count >= 4) Caches.Remove(Caches.MinBy(pair=>pair.Value.LastUsed).Key);
            cached = new Cached();
            // The recipe revision replaces the whole cache above. Populate each
            // catalogue once; unchanged frames only advance its pending work.
            if (_index != null) cached.Availability.Catalogue.Preload(_index.OutputTypes);
            Caches[position] = cached;
        }
        cached.LastUsed = Main.GameUpdateCount;
        return cached.Availability;
    }
    public override void PostUpdateEverything()
    {
        if (Main.dedServ || Main.gameMenu || !StorageSystem.Ready) return;
        if (UISystem.ActiveCraftingTerminal != null && UISystem.Interface?.CurrentState == UISystem.TerminalState)
        {
            var current = Current;
            if (Main.GameUpdateCount - _lastRefresh >= 30) { _lastRefresh=Main.GameUpdateCount; current.Refresh(); }
            if(current.CanUse) current.Catalogue.Advance();
            return;
        }
        // Closed multiplayer terminals have no live authorized snapshot. Their cache
        // is retained for reuse after the next authoritative read, never guessed.
        if (Main.netMode == NetmodeID.MultiplayerClient) return;
        if (Main.GameUpdateCount - _lastSelection >= 120 || _warming.X < 0 && _lastSelection == 0)
        {
            _lastSelection=Main.GameUpdateCount;
            var candidates=TileEntity.ByPosition.Where(pair=>pair.Value is MECraftingTerminalEntity)
                .OrderBy(pair=>Vector2.DistanceSquared(Main.LocalPlayer.Center, pair.Key.ToVector2()*16)).Take(1);
            _warming=candidates.Select(pair=>pair.Key).DefaultIfEmpty(new Point16(-1,-1)).First();
        }
        if (!TileEntity.ByPosition.TryGetValue(_warming,out var entity) || entity is not MECraftingTerminalEntity terminal) return;
        var availability=For(_warming);
        if (Main.GameUpdateCount - _lastRefresh >= 30)
        {
            _lastRefresh=Main.GameUpdateCount;
            var network=MENetwork.Collect(_warming.X,_warming.Y); var anchor=MENetwork.CanonicalAnchor(network);
            int level=NetworkRegistry.GetLevel(anchor,Main.LocalPlayer.name);
            if (NetworkRegistry.GetOwner(anchor)==null && Vector2.DistanceSquared(Main.LocalPlayer.Center,_warming.ToVector2()*16)<=512f*512f)
                level=NetworkRegistry.LevelManage;
            availability.Refresh(terminal,_warming,MENetwork.Aggregate(MENetwork.CollectStorage(_warming.X,_warming.Y)),level);
        }
        availability.Catalogue.Advance(32,2);
    }
    public override void ClearWorld() { Caches.Clear(); _warming=new(-1,-1); _lastSelection=_lastRefresh=0; }
    public override void OnWorldLoad() => ClearWorld();
    public override void Unload() { ClearWorld(); _index=null; _planner=null; }
}
