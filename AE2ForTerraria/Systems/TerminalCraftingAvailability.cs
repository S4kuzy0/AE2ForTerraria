using System;
using System.Collections.Generic;
using AE2ForTerraria.Content.Tiles;
using AE2ForTerraria.Core;
using Terraria;
using Terraria.ID;
using Terraria.DataStructures;

namespace AE2ForTerraria.Systems;

/// <summary>Read-only planning with the same recipe checks and material permissions as execution.</summary>
internal sealed class TerminalCraftingAvailability
{
    private readonly Dictionary<ItemKey, long> _stock = new();
    private readonly Dictionary<Recipe, bool> _conditionsThisFrame = new();
    private IReadOnlyList<Item> _stations = Array.Empty<Item>();
    private HashSet<int> _stationTiles = new();
    private MECraftingTerminalEntity _terminal;
    private string _requester;
    private bool _canUse;
    private ulong _conditionsAt = ulong.MaxValue;
    private sealed record Preview(CraftPlanner Planner, MECraftingTerminalEntity Terminal, string Requester,
        Dictionary<ItemKey,long> Stock, Dictionary<Recipe,bool> Conditions, CraftPlan Plan);
    private readonly Dictionary<(int Type,long Quantity),Preview> _previews = new();
    internal int PreviewCacheHits { get; private set; }
    public readonly CraftabilityCache Catalogue = new();
    public int Revision => Catalogue.Revision;
    internal bool CanUse => _canUse;

    public void Reset() { Catalogue.Clear(); _stock.Clear(); _conditionsThisFrame.Clear(); _previews.Clear(); _terminal = null; }

    public void Refresh()
    {
        if(Main.netMode==NetmodeID.MultiplayerClient && (!UISystem.TerminalReadable || !UISystem.TerminalPolicyReady))
        { _canUse=false; return; }
        Refresh(UISystem.ActiveCraftingTerminal, UISystem.ActiveTerminalPos, UISystem.ActiveNetworkContents,
            !UISystem.TerminalReadable || !UISystem.TerminalPolicyReady ? NetworkRegistry.LevelNone
                : UISystem.TerminalCanManage ? NetworkRegistry.LevelManage
                : UISystem.TerminalCanUse ? NetworkRegistry.LevelUse : NetworkRegistry.LevelReadOnly);
    }

    internal void Refresh(MECraftingTerminalEntity terminal, Point16 position, IReadOnlyDictionary<ItemKey,long> contents, int level)
    {
        _terminal = terminal;
        _requester = Main.LocalPlayer?.name;
        _canUse = level >= NetworkRegistry.LevelUse && _terminal != null;
        _stations = _terminal?.ConnectedStations() ?? (IReadOnlyList<Item>)Array.Empty<Item>();
        var stationTiles=new HashSet<int>();
        foreach(var station in _stations) if(station!=null && !station.IsAir) stationTiles.Add(station.createTile);
        if(!_stationTiles.SetEquals(stationTiles)) { _stationTiles=stationTiles; Catalogue.StationsChanged(); }
        _stock.Clear();
        bool manage = level >= NetworkRegistry.LevelManage;
        HashSet<int> localLocked = null;
        if (!manage && Main.netMode != NetmodeID.MultiplayerClient)
        {
            var anchor = MENetwork.CanonicalAnchor(MENetwork.Collect(position.X, position.Y));
            localLocked = new(NetworkRegistry.GetLocked(anchor));
        }
        if (contents != null)
            foreach (var entry in contents)
                if (entry.Value > 0 && (manage || !(localLocked?.Contains(entry.Key.Type) ?? UISystem.TerminalItemLocked(entry.Key.Type)))) _stock[entry.Key] = entry.Value;
        _conditionsThisFrame.Clear();
        Catalogue.SetContext(TerminalCraftingCacheSystem.AvailabilityPlanner, _stock, (position, _requester, _canUse, manage), CanUseRecipe);
    }

    private bool CanUseRecipe(Recipe recipe)
    {
        if (!_canUse || recipe == null) return false;
        if (_conditionsAt != Main.GameUpdateCount)
        { _conditionsAt = Main.GameUpdateCount; _conditionsThisFrame.Clear(); }
        if (!_conditionsThisFrame.TryGetValue(recipe, out bool allowed))
        {
            allowed = _terminal.CanUseRecipe(recipe, _stations, out _, _requester);
            _conditionsThisFrame[recipe] = allowed;
        }
        return allowed;
    }

    public CraftPlan Plan(ItemKey key, long quantity)
    {
        Refresh();
        if (StorageSystem.Planner == null || !_canUse)
        {
            var unavailable = new CraftPlan { Target = key, Requested = quantity, Limited = true };
            unavailable.BlockedReasons.Add(!UISystem.TerminalReadable ? UISystem.TerminalAccessStatus
                : !UISystem.TerminalPolicyReady ? "正在同步材料权限…"
                : !UISystem.TerminalCanUse ? "需要网络使用权限" : "需要连接合成终端");
            return unavailable;
        }
        var id=(key.Type,quantity);
        if(_previews.TryGetValue(id,out var preview) && ReferenceEquals(preview.Planner,StorageSystem.Planner)
            && ReferenceEquals(preview.Terminal,_terminal) && preview.Requester==_requester
            && CraftabilityCache.SameStock(preview.Stock,_stock))
        {
            bool current=true;
            foreach(var condition in preview.Conditions)
                if(CanUseRecipe(condition.Key)!=condition.Value) {current=false;break;}
            if(current) {PreviewCacheHits++;return preview.Plan;}
        }
        var conditions=new Dictionary<Recipe,bool>();
        bool Filter(Recipe recipe) {bool allowed=CanUseRecipe(recipe);conditions[recipe]=allowed;return allowed;}
        var plan=StorageSystem.Planner.Plan(_stock,ItemKey.Crafted(key.Type),quantity,Filter,useExistingTarget:false);
        if(_previews.Count>=4 && !_previews.ContainsKey(id)) _previews.Clear();
        _previews[id]=new(StorageSystem.Planner,_terminal,_requester,new(_stock),conditions,plan);
        return plan;
    }
}
