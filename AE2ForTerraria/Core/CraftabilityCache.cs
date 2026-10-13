using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using Terraria;

namespace AE2ForTerraria.Core;

/// <summary>Incremental catalogue planning. Unchecked results never count as craftable.</summary>
public sealed class CraftabilityCache
{
    private sealed class Entry
    {
        public int Type;
        public bool? Ready;
        public readonly Dictionary<Recipe, bool> Conditions = new();
        public readonly HashSet<int> Dependencies = new();
        public bool FeasibilityBlocked;
        public bool Queued;
    }

    private readonly Dictionary<int, Entry> _entries = new();
    private readonly List<Entry> _order = new();
    private readonly Queue<Entry> _pending = new();
    private Dictionary<ItemKey, long> _stock = new();
    private CraftPlanner _planner;
    private Func<Recipe, bool> _filter;
    private object _context;
    private int _validationCursor;
    private HashSet<int> _stockTypes=new(), _reachable=new();
    public int FullPlansComputed { get; private set; }
    public double ReachabilityMilliseconds { get; private set; }
    public int Revision { get; private set; }
    public int PendingCount => _pending.Count;
    public int PlansComputed { get; private set; }

    public void Clear()
    {
        _entries.Clear(); _order.Clear(); _pending.Clear(); _stock.Clear();
        _stockTypes.Clear(); _reachable.Clear();
        _context = null; _planner = null; _validationCursor = 0; Revision++;
    }

    public static bool SameStock(IReadOnlyDictionary<ItemKey, long> left, IReadOnlyDictionary<ItemKey, long> right)
    {
        if (left.Count != right.Count) return false;
        foreach (var entry in left)
            if (!right.TryGetValue(entry.Key, out long amount) || entry.Value != amount) return false;
        return true;
    }

    public void SetContext(CraftPlanner planner, IReadOnlyDictionary<ItemKey, long> stock,
        object context, Func<Recipe, bool> filter)
    {
        stock ??= new Dictionary<ItemKey, long>();
        bool reset = !ReferenceEquals(planner, _planner) || !Equals(context, _context);
        bool changed = reset || !SameStock(_stock, stock);
        _planner = planner; _filter = filter; _context = context;
        if (!changed) return;
        var changedTypes = new HashSet<int>();
        foreach (var entry in _stock)
            if (!stock.TryGetValue(entry.Key, out long amount) || amount != entry.Value) changedTypes.Add(entry.Key.Type);
        foreach (var entry in stock)
            if (!_stock.TryGetValue(entry.Key, out long amount) || amount != entry.Value) changedTypes.Add(entry.Key.Type);
        _stock = new(stock);
        var types=stock.Where(pair=>pair.Value>0).Select(pair=>pair.Key.Type).ToHashSet();
        bool newTypes=reset || !_stockTypes.SetEquals(types);
        if(newTypes) { _stockTypes=types; BuildReachability(); }
        foreach (var entry in _order)
        {
            if (reset || entry.Dependencies.Overlaps(changedTypes)
                || entry.FeasibilityBlocked && _reachable.Contains(entry.Type)) Invalidate(entry);
        }
        Revision++;
    }

    public bool IsCraftable(int type)
    {
        if (!_entries.TryGetValue(type, out var entry))
        {
            entry = new Entry { Type = type };
            _entries[type] = entry; _order.Add(entry); Queue(entry);
        }
        if (entry.Ready != true) return false;
        if (ConditionsChanged(entry)) { Invalidate(entry); return false; }
        return true;
    }

    private bool ConditionsChanged(Entry entry)
    {
        foreach (var condition in entry.Conditions)
            if ((_filter?.Invoke(condition.Key) ?? true) != condition.Value) return true;
        return false;
    }
    private void Queue(Entry entry)
    {
        if (entry.Queued) return;
        entry.Queued = true; _pending.Enqueue(entry);
    }
    private void Invalidate(Entry entry)
    {
        entry.Ready = null; entry.Conditions.Clear(); Queue(entry); Revision++;
    }

    // Each recipe has its own arbitrary mod condition callback. Validate dependencies
    // of negative results too, so newly available conditions restart only affected plans.
    public void Advance(int maxPlans = 64, double milliseconds = 4)
    {
        if (_planner == null) return;
        long started = Stopwatch.GetTimestamp();
        for (int i = 0; i < Math.Min(12, _order.Count); i++)
        {
            var entry = _order[_validationCursor];
            _validationCursor = (_validationCursor + 1) % _order.Count;
            if (entry.Ready.HasValue && ConditionsChanged(entry)) Invalidate(entry);
            if (Stopwatch.GetElapsedTime(started).TotalMilliseconds >= milliseconds) return;
        }
        for (int i = 0; i < maxPlans && _pending.Count > 0; i++)
        {
            var entry = _pending.Dequeue(); entry.Queued = false;
            entry.Conditions.Clear();
            entry.Dependencies.Clear(); entry.Dependencies.Add(entry.Type);
            bool Filter(Recipe recipe)
            {
                foreach (var item in recipe.requiredItem) if (!item.IsAir) entry.Dependencies.Add(item.type);
                foreach (int groupId in recipe.acceptedGroups)
                    if (RecipeGroup.recipeGroups.TryGetValue(groupId, out var group)) entry.Dependencies.UnionWith(group.ValidItems);
                bool allowed = _filter?.Invoke(recipe) ?? true;
                entry.Conditions[recipe] = allowed;
                return allowed;
            }
            // Retain only the result and condition dependencies, not the planner's
            // per-target inventory copies, diagnostic strings or complete trees.
            entry.FeasibilityBlocked=!_reachable.Contains(entry.Type);
            if(entry.FeasibilityBlocked) entry.Ready=false;
            else {entry.Ready = _planner.Plan(_stock, ItemKey.Crafted(entry.Type), 1, Filter, useExistingTarget: false).FullySatisfied;FullPlansComputed++;}
            PlansComputed++; Revision++;
            if (Stopwatch.GetElapsedTime(started).TotalMilliseconds >= milliseconds) break;
        }
    }

    public void Preload(IEnumerable<int> types)
    {
        foreach (int type in types)
            if (!_entries.ContainsKey(type))
            {
                var entry = new Entry { Type = type };
                _entries[type] = entry; _order.Add(entry); Queue(entry);
            }
    }
    public void Prioritize(IEnumerable<int> types)
    {
        var wanted=new HashSet<int>(types);
        if(wanted.Count==0 || _pending.Count==0) return;
        var entries=_pending.ToArray(); _pending.Clear();
        foreach(var entry in entries) if(wanted.Contains(entry.Type)) _pending.Enqueue(entry);
        foreach(var entry in entries) if(!wanted.Contains(entry.Type)) _pending.Enqueue(entry);
    }
    private void BuildReachability()
    {
        long started=Stopwatch.GetTimestamp();
        // Optimistic type presence only: true still requires the full quantity,
        // group-sharing, station, condition and permission checks in CraftPlanner.
        _reachable=new(_stockTypes);
        if(_planner==null)return;
        var queue=new Queue<int>();var queued=new bool[Recipe.numRecipes];
        void Enqueue(int index) {if(index>=0 && index<queued.Length && !queued[index]){queued[index]=true;queue.Enqueue(index);}}
        foreach(int type in _planner.Index.OutputTypes)
            foreach(int index in _planner.Index.GetRecipesFor(ItemKey.Crafted(type))) Enqueue(index);
        while(queue.Count>0)
        {
            int index=queue.Dequeue();queued[index]=false;var recipe=Main.recipe[index];
            if(recipe==null || recipe.Disabled || recipe.createItem.IsAir || _reachable.Contains(recipe.createItem.type)) continue;
            bool possible=true;
            foreach(var item in recipe.requiredItem)
            {
                if(item.IsAir || _reachable.Contains(item.type))continue;
                bool alternative=false;
                foreach(int groupId in recipe.acceptedGroups)
                    if(RecipeGroup.recipeGroups.TryGetValue(groupId,out var group) && group.ValidItems.Contains(item.type)
                        && group.ValidItems.Any(_reachable.Contains)) {alternative=true;break;}
                if(!alternative){possible=false;break;}
            }
            if(!possible)continue;
            _reachable.Add(recipe.createItem.type);
            foreach(int dependent in _planner.Index.GetRecipesUsing(recipe.createItem.type)) Enqueue(dependent);
        }
        ReachabilityMilliseconds=Stopwatch.GetElapsedTime(started).TotalMilliseconds;
    }
    internal void StationsChanged()
    {
        foreach(var entry in _order)
            if(entry.Conditions.Keys.Any(recipe=>recipe.requiredTile.Any(tile=>tile>=0))) Invalidate(entry);
    }
}
