using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using AE2ForTerraria.Core;
using AE2ForTerraria.Systems;
using Terraria;

namespace AE2ForTerraria.UI;

/// <summary>Incremental dependency graph. Repeated subtrees link to their first expansion.</summary>
internal sealed class BookmarkTree
{
    internal sealed record Node(int Item, int Npc, long Amount, int Parent, int Depth, bool Last,
        int Reference = -1, bool Cycle = false, bool Overflow = false);
    private readonly record struct Work(int Type, long Amount, int Parent, int Depth, bool Last, bool Exit = false, bool Overflow = false);
    private readonly Stack<Work> _pending = new();
    private readonly HashSet<int> _path = new();
    private readonly Dictionary<int, int> _expanded = new();
    private readonly int _rootRecipe;
    internal readonly List<Node> Nodes = new();
    internal bool Complete => _pending.Count == 0;
    internal int MaxDepth { get; private set; }
    internal string MissingContent { get; }
    internal BookmarkTree(BrowserBookmark bookmark)
    {
        if (!BrowserContent.Resolve(bookmark.Content, out int type, out bool npc))
        { MissingContent = bookmark.Content; return; }
        if (npc) { Nodes.Add(new Node(0, type, 1, -1, 0, true)); return; }
        _rootRecipe = BrowserContent.ResolveRecipe(type, bookmark.Recipe);
        _pending.Push(new Work(type, bookmark.Quantity, -1, 0, true));
    }
    internal void Advance(int maximumNodes = 96)
    {
        long started = Stopwatch.GetTimestamp(); int added = 0;
        while (_pending.Count > 0 && added < maximumNodes && Stopwatch.GetElapsedTime(started).TotalMilliseconds < 1.5)
        {
            var work = _pending.Pop();
            if (work.Exit) { _path.Remove(work.Type); continue; }
            bool cycle = _path.Contains(work.Type);
            int reference = !cycle && _expanded.TryGetValue(work.Type, out int previous) ? previous : -1;
            int node = Nodes.Count;
            Nodes.Add(new Node(work.Type, 0, work.Amount, work.Parent, work.Depth, work.Last, reference, cycle, work.Overflow));
            MaxDepth = Math.Max(MaxDepth, work.Depth); added++;
            if (work.Overflow || cycle || reference >= 0) continue;
            int recipeIndex = work.Parent < 0 ? _rootRecipe : BrowserContent.ResolveRecipe(work.Type, "");
            if (recipeIndex < 0) continue;
            _expanded[work.Type] = node;
            var recipe = Main.recipe[recipeIndex];
            long output = Math.Max(1, recipe.createItem.stack);
            long batches = work.Amount / output + (work.Amount % output > 0 ? 1 : 0);
            var ingredients = recipe.requiredItem.Where(item => !item.IsAir).ToArray();
            _path.Add(work.Type); _pending.Push(work with { Exit = true });
            for (int i = ingredients.Length - 1; i >= 0; i--)
            {
                var ingredient = ingredients[i];
                bool overflow = batches > long.MaxValue / ingredient.stack;
                _pending.Push(new Work(ingredient.type, overflow ? long.MaxValue : batches * ingredient.stack,
                    node, work.Depth + 1, i == ingredients.Length - 1, Overflow: overflow));
            }
        }
    }
}
