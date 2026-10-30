using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using AE2ForTerraria.Core;
using AE2ForTerraria.Systems;
using Terraria;

namespace AE2ForTerraria.UI;

/// <summary>A query-only stock pool shared by ordered bookmarks; never changes real inventory.</summary>
internal sealed class RecipeTreeStock(IReadOnlyDictionary<int, long> source)
{
    private readonly Dictionary<int, long> _remaining = new(source);
    internal long Take(int type, long amount)
    {
        _remaining.TryGetValue(type, out long have);
        long taken = Math.Min(Math.Max(0, have), amount);
        _remaining[type] = have - taken; return taken;
    }
    internal void Refund(int type, long amount)
    { _remaining.TryGetValue(type, out long have); _remaining[type] = StorageAmounts.Add(have, amount); }
}

/// <summary>Incremental tree with reversible preview reservations and cycle-branch rollback.</summary>
internal sealed class BookmarkTree
{
    internal sealed record Node(int Item, int Npc, long Amount, int Parent, int Depth, bool Last,
        int Reference = -1, bool Cycle = false, bool Overflow = false, long Missing = 0, string Group = null, bool Leaf = false, int RecipeIndex = -1);
    private sealed class Branch(int node, int type, int checkpoint, int[] recipes)
    {
        internal readonly int Node = node, Type = type, Checkpoint = checkpoint;
        internal readonly int[] Recipes = recipes;
        internal int Next;
    }
    private readonly record struct Work(int Type, long Amount, int Parent, int Depth, bool Last,
        RecipeGroup Group = null, Branch Exit = null, bool Overflow = false);
    private readonly Stack<Work> _pending = new();
    private readonly Dictionary<int, Branch> _path = new();
    private readonly Dictionary<int, int> _expanded = new();
    private readonly List<(int Type, long Amount)> _reservations = new();
    private readonly RecipeTreeStock _stock;
    private readonly int _rootRecipe;
    private bool _finalized;
    internal readonly List<Node> Nodes = new();
    internal bool Complete => _pending.Count == 0;
    internal int Revision { get; private set; }
    internal int RootRecipe { get; private set; }
    internal bool PreferredRecipeMissing { get; }
    internal int MaxDepth { get; private set; }
    internal string MissingContent { get; }
    internal bool MaterialsReady => Complete && Nodes.Count > 0 && Nodes[0].Npc == 0
        && !Nodes.Any(node => node.Leaf && node.Missing > 0 || node.Overflow);
    internal BookmarkTree(BrowserBookmark bookmark, RecipeTreeStock stock = null)
    {
        _stock = stock;
        if (!BrowserContent.Resolve(bookmark.Content, out int type, out bool npc))
        { MissingContent = bookmark.Content; return; }
        if (npc) { Nodes.Add(new Node(0, type, 1, -1, 0, true)); return; }
        _rootRecipe = BrowserContent.ResolveRecipe(type, bookmark.Recipe);
        PreferredRecipeMissing = !string.IsNullOrEmpty(bookmark.Recipe)
            && (_rootRecipe < 0 || BrowserContent.RecipeKey(Main.recipe[_rootRecipe]) != bookmark.Recipe);
        RootRecipe = _rootRecipe;
        _pending.Push(new Work(type, bookmark.Quantity, -1, 0, true));
    }
    internal void Advance(int maximumNodes = 96)
    {
        long started = Stopwatch.GetTimestamp(); int steps = 0;
        while (_pending.Count > 0 && steps++ < maximumNodes && Stopwatch.GetElapsedTime(started).TotalMilliseconds < 1.5)
        {
            var work = _pending.Pop();
            if (work.Exit != null)
            { _path.Remove(work.Type); if (_stock == null) _expanded[work.Type] = work.Exit.Node; continue; }
            if (work.Group == null && _path.TryGetValue(work.Type, out var cyclic)) { Retry(cyclic); continue; }
            long missing = Reserve(work);
            if (_stock != null && missing == 0 && work.Parent >= 0) continue;
            // Stock-aware trees must reserve every occurrence, even when its recipe appeared earlier.
            int reference = _stock == null && _expanded.TryGetValue(work.Type, out int previous) ? previous : -1;
            int node = Nodes.Count;
            bool leaf = work.Group != null || work.Overflow;
            Nodes.Add(new Node(work.Type, 0, work.Amount, work.Parent, work.Depth, work.Last,
                reference, Overflow: work.Overflow, Missing: missing, Group: work.Group?.GetText(), Leaf: leaf));
            Revision++; MaxDepth = Math.Max(MaxDepth, work.Depth);
            if (leaf || missing == 0 || reference >= 0) continue;
            var recipes = StorageSystem.Recipes.GetRecipesFor(ItemKey.Crafted(work.Type));
            int preferred = work.Parent < 0 ? _rootRecipe : -1;
            var candidates = recipes.OrderBy(index => index == preferred ? 0 : 1).ToArray();
            var branch = new Branch(node, work.Type, _reservations.Count, candidates);
            if (!Schedule(branch)) Nodes[node] = Nodes[node] with { Leaf = true };
        }
        if (Complete && !_finalized)
        {
            _finalized = true;
            MaxDepth = Nodes.Count == 0 ? 0 : Nodes.Max(node => node.Depth);
            var last = new Dictionary<int, int>();
            for (int i = 0; i < Nodes.Count; i++) last[Nodes[i].Parent] = i;
            for (int i = 0; i < Nodes.Count; i++) Nodes[i] = Nodes[i] with { Last = last[Nodes[i].Parent] == i };
            Revision++;
        }
    }
    private long Reserve(Work work)
    {
        if (_stock == null || work.Overflow) return work.Amount;
        long missing = work.Amount;
        IEnumerable<int> types = work.Group == null ? new[] { work.Type } : work.Group.ValidItems.OrderBy(type => type);
        foreach (int type in types)
        {
            long taken = _stock.Take(type, missing);
            if (taken > 0) _reservations.Add((type, taken));
            missing -= taken; if (missing == 0) break;
        }
        return missing;
    }
    private bool Schedule(Branch branch)
    {
        if (branch.Next >= branch.Recipes.Length) return false;
        var node = Nodes[branch.Node]; var recipe = Main.recipe[branch.Recipes[branch.Next++]];
        Nodes[branch.Node] = node with { RecipeIndex = branch.Recipes[branch.Next - 1] };
        if (node.Parent < 0) RootRecipe = branch.Recipes[branch.Next - 1];
        _path[branch.Type] = branch;
        _pending.Push(new Work(branch.Type, 0, 0, 0, false, Exit: branch));
        long output = Math.Max(1, recipe.createItem.stack);
        long batches = node.Missing / output + (node.Missing % output > 0 ? 1 : 0);
        var ingredients = recipe.requiredItem.Where(item => !item.IsAir).ToArray();
        for (int i = ingredients.Length - 1; i >= 0; i--)
        {
            var ingredient = ingredients[i];
            var group = recipe.acceptedGroups.Select(id => RecipeGroup.recipeGroups[id])
                .FirstOrDefault(candidate => candidate.ValidItems.Contains(ingredient.type));
            bool overflow = batches > long.MaxValue / ingredient.stack;
            _pending.Push(new Work(ingredient.type, overflow ? long.MaxValue : batches * ingredient.stack,
                branch.Node, node.Depth + 1, i == ingredients.Length - 1, group, Overflow: overflow));
        }
        return true;
    }
    private void Retry(Branch branch)
    {
        // Roll back from the first cyclic material, retaining its real missing requirement.
        while (_pending.Count > 0 && !ReferenceEquals(_pending.Pop().Exit, branch)) { }
        foreach (int type in _path.Where(pair => pair.Value.Node >= branch.Node).Select(pair => pair.Key).ToArray()) _path.Remove(type);
        foreach (int type in _expanded.Where(pair => pair.Value >= branch.Node).Select(pair => pair.Key).ToArray()) _expanded.Remove(type);
        if (Nodes.Count > branch.Node + 1) Nodes.RemoveRange(branch.Node + 1, Nodes.Count - branch.Node - 1);
        for (int i = _reservations.Count - 1; i >= branch.Checkpoint; i--)
        { var taken = _reservations[i]; _stock?.Refund(taken.Type, taken.Amount); }
        if (_reservations.Count > branch.Checkpoint) _reservations.RemoveRange(branch.Checkpoint, _reservations.Count - branch.Checkpoint);
        if (!Schedule(branch)) Nodes[branch.Node] = Nodes[branch.Node] with { Leaf = true, RecipeIndex = -1 };
        Revision++;
    }
}
