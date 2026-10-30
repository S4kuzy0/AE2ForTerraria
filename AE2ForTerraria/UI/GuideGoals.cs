using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using AE2ForTerraria.Core;
using AE2ForTerraria.Systems;
using Terraria;

namespace AE2ForTerraria.UI;

/// <summary>Incremental view of the same ordered, shared-stock trees used by the pinned HUD.</summary>
internal sealed class GuideGoals
{
    internal readonly List<BookmarkTree> Trees = new();
    internal string StockLabel { get; private set; }
    private Dictionary<int, long> _stock = new();
    private object _index;
    private int _recipeRevision = -1, _favoritesRevision = -1, _cursor;
    private ulong _checkedAt;
    internal bool Pending => _cursor < Trees.Count;

    internal bool Update(bool force = false)
    {
        bool changed = false;
        if (force || _checkedAt == 0 || Main.GameUpdateCount - _checkedAt >= 30
            || _favoritesRevision != BrowserFavorites.Revision || !ReferenceEquals(_index, StorageSystem.Recipes)
            || _recipeRevision != StorageSystem.Recipes.Revision)
        {
            _checkedAt = Main.GameUpdateCount;
            var stock = WikiGuidance.Stock(out string label);
            bool reset = force || _favoritesRevision != BrowserFavorites.Revision || !ReferenceEquals(_index, StorageSystem.Recipes)
                || _recipeRevision != StorageSystem.Recipes.Revision || StockLabel != label
                || stock.Count != _stock.Count || stock.Any(p => _stock.GetValueOrDefault(p.Key) != p.Value);
            if (reset)
            {
                _stock = stock; StockLabel = label; _index = StorageSystem.Recipes;
                _recipeRevision = StorageSystem.Recipes.Revision; _favoritesRevision = BrowserFavorites.Revision;
                Trees.Clear(); var pool = new RecipeTreeStock(stock);
                foreach (var bookmark in BrowserFavorites.Entries) Trees.Add(new(bookmark, pool));
                _cursor = 0; changed = true;
            }
        }
        long started = Stopwatch.GetTimestamp();
        while (Pending && Stopwatch.GetElapsedTime(started).TotalMilliseconds < 2)
        {
            var tree = Trees[_cursor]; tree.Advance(32); changed = true;
            if (tree.Complete) _cursor++; else break;
        }
        return changed;
    }

    internal static IEnumerable<GuideRow> NextSteps(BookmarkTree tree)
    {
        if (tree.MissingContent != null) { yield return new("内容未加载：" + tree.MissingContent + "；收藏仍保留。"); yield break; }
        if (!tree.Complete) { yield return new("正在计算下一步…"); yield break; }
        if (tree.PreferredRecipeMissing) yield return new("原收藏配方已变更或移除，以下按当前可用配方重新计算；点击目标名称可重新查看路线。");
        if (tree.Nodes.Count == 0) yield break;
        var root = tree.Nodes[0];
        if (root.Npc > 0) { yield return new("查看 NPC 的出没与掉落", Npc: root.Npc, Link: GuideLink.Npc); yield break; }
        if (root.Missing == 0) { yield return new("目标数量已在当前库存中。", Satisfied: true); yield break; }
        var parents = tree.Nodes.Where(n => n.Parent >= 0).Select(n => n.Parent).ToHashSet();
        for (int i = 0; i < tree.Nodes.Count; i++)
        {
            var node = tree.Nodes[i];
            if (node.Overflow) { yield return new("数量超出范围，请减少目标数量。", Satisfied: false); continue; }
            if (node.RecipeIndex >= 0 && node.Missing > 0 && !parents.Contains(i)
                && node.RecipeIndex < Recipe.numRecipes)
                yield return new("材料已备：制作 " + Lang.GetItemNameValue(node.Item) + " ×" + UIFormat.Count(node.Missing) + "；点击检查工作站与条件",
                    node.Item, Link: GuideLink.Recipe, Quantity: node.Missing, Recipe: BrowserContent.RecipeKey(Main.recipe[node.RecipeIndex]));
        }
        foreach (var node in tree.Nodes.Where(n => n.Leaf && n.Missing > 0 && !n.Overflow))
            yield return new("先获取：" + (node.Group ?? Lang.GetItemNameValue(node.Item)) + " ×" + UIFormat.Count(node.Missing)
                + (node.Group != null ? "（可在配方检查中选择替代物）" : ""), node.Item, Link: GuideLink.Sources, Quantity: node.Missing);
    }

    internal List<GuideRow> Rows()
    {
        var rows = new List<GuideRow> { new("收藏目标 · 下一步", Heading: true),
            new("库存：" + StockLabel + "；所有目标按收藏顺序共用一次库存抵扣。材料准备状态不代替制作条件或权限检查。") };
        if (Trees.Count == 0) rows.Add(new("在物品上按 Alt＋左键加入目标，随后在这里查看下一步。"));
        foreach (var tree in Trees)
        {
            if (tree.Nodes.Count > 0)
            {
                var root = tree.Nodes[0];
                rows.Add(new((root.Npc > 0 ? NpcIndex.GetName(root.Npc) : Lang.GetItemNameValue(root.Item)) + " ×" + UIFormat.Count(root.Amount),
                    root.Item, root.Npc, root.Npc > 0 ? GuideLink.Npc : GuideLink.Sources, Heading: true));
            }
            rows.AddRange(NextSteps(tree));
        }
        rows.Add(new("合并采集清单", Heading: true));
        if (Pending) rows.Add(new("等待全部目标展开后汇总，避免显示不完整数量。"));
        else
        {
            var leaves = Trees.SelectMany(t => t.Nodes).Where(n => n.Leaf && n.Missing > 0 && !n.Overflow);
            foreach (var group in leaves.GroupBy(n => (n.Item, n.Group)))
            {
                long total = 0; foreach (var node in group) total = StorageAmounts.Add(total, node.Missing);
                rows.Add(new((group.Key.Group ?? Lang.GetItemNameValue(group.Key.Item)) + "：还需 " + UIFormat.Count(total),
                    group.Key.Item, Link: GuideLink.Sources, Quantity: total));
            }
            if (!leaves.Any()) rows.Add(new("当前没有需要另行采集的已知材料；仍可检查各目标的工作站与条件。"));
        }
        return rows;
    }
}
