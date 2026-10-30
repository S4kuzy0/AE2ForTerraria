using System;
using System.Collections.Generic;
using System.Linq;
using AE2ForTerraria.Core;
using AE2ForTerraria.Progression;
using AE2ForTerraria.Systems;
using Terraria;
using Terraria.ID;

namespace AE2ForTerraria.UI;

internal enum GuidePage { Sources, Goals, Recipe, Storage, Tutorial, Npc }
internal enum GuideLink { None, Sources, Recipe, Npc, Tutorial, Storage, Network }
internal sealed record GuideRow(string Text, int Item = 0, int Npc = 0, GuideLink Link = GuideLink.None,
    bool Heading = false, bool? Satisfied = null, long Quantity = 1, string Recipe = "");

/// <summary>Read-only guidance over the currently registered content. Never runs acquisition callbacks.</summary>
internal static class WikiGuidance
{
    internal static bool Loaded(int type) => type > 0 && ContentSamples.ItemsByType.TryGetValue(type, out var item)
        && !item.IsAir && item.ModItem is not Terraria.ModLoader.Default.UnloadedItem;
    internal static GuideRow ItemLink(int type, string prefix = "查看获取方法：", long quantity = 1) =>
        new(prefix + Lang.GetItemNameValue(type), type, Link: GuideLink.Sources, Quantity: quantity);

    internal static bool? ConditionState(Condition condition)
    {
        if (Main.gameMenu || condition == null) return null;
        if (!AdaptiveProgressionConditions.TryRead(condition.Predicate, out var expression)) return null;
        try
        {
            return expression.Evaluate(field => field.IsStatic ? field.GetValue(null) as bool?
                : field.DeclaringType == typeof(Player) ? field.GetValue(Main.LocalPlayer) as bool? : null);
        }
        catch { return null; }
    }

    internal static List<GuideRow> Sources(int type, bool includeDrops = true)
    {
        var rows = new List<GuideRow>();
        if (!Loaded(type)) { rows.Add(new("内容当前未加载；重新启用对应模组后可继续查询。")); return rows; }
        GameDataIndex.EnsureBuilt();
        rows.Add(new(Lang.GetItemNameValue(type) + " · 获取说明", type, Heading: true));
        rows.Add(new("点击带图标的条目可继续查工具、材料或 NPC；以下为当前已登记路线，未收录不代表无法获得。"));
        if (StorageSystem.Progression is { } progression)
        {
            rows.Add(new("最早已知路线：" + progression.GetExplanation(type)));
            var hints = progression.GetSourceHints(type).GroupBy(h => h.Text + "|" + string.Join(",", h.Inputs));
            foreach (var group in hints)
            {
                var hint = group.First();
                rows.Add(new(hint.Text, Heading: true));
                if (hint.Instructions.Length > 0) rows.Add(new(hint.Instructions));
                foreach (int input in hint.Inputs.Distinct().Where(Loaded)) rows.Add(ItemLink(input, "所需工具 / 输入："));
            }
        }
        // catchItem is a current declaration. A modded catch/spawn hook may still add restrictions.
        foreach (var npc in ContentSamples.NpcsByNetId.Values.Where(n => n.type > 0 && n.catchItem == type).GroupBy(n => n.type).Select(g => g.First()))
        {
            rows.Add(new("捕捉：" + npc.FullName, Npc: npc.type, Link: GuideLink.Npc, Heading: true));
            rows.Add(new("操作：使用合适的捕虫网捕捉活的小动物；特殊生物可能要求专用网或额外条件。"));
            string habitat = StorageSystem.Acquisition.GetSpawnCondition(npc.type);
            rows.Add(new("出没信息：" + (string.IsNullOrWhiteSpace(habitat) ? "未提供公开地点 / 时间信息" : habitat)));
            if (npc.ModNPC != null) rows.Add(new("模组捕捉限制尚需在游戏中核对，不能仅凭 catchItem 判定当前可捕捉。"));
        }
        foreach (var shop in GameDataIndex.GetShops(type))
        {
            rows.Add(new("购买：" + shop.Name, Npc: shop.Npc, Link: GuideLink.Npc, Heading: true));
            rows.Add(new("操作：找到该 NPC 并打开商店。基础价格：" + shop.Price + "；实际价格以商店为准。"));
            foreach (var condition in shop.Conditions) rows.Add(new("出售条件：" + condition.Description.Value, Satisfied: ConditionState(condition)));
            if (shop.Conditions.Length == 0) rows.Add(new("商品声明没有附加条件；仍需 NPC 在场，动态商店可能另有调整。"));
        }
        foreach (var drop in includeDrops ? GameDataIndex.GetNpcSources(type) : Array.Empty<DropRecord>())
        {
            rows.Add(new("击败：" + NpcIndex.GetName(drop.NpcType), Npc: Math.Max(0, drop.NpcType),
                Link: drop.NpcType > 0 ? GuideLink.Npc : GuideLink.None, Heading: true));
            rows.Add(new("掉落：" + drop.Summary));
            string habitat = StorageSystem.Acquisition.GetSpawnCondition(drop.NpcType);
            rows.Add(new("出没信息：" + (string.IsNullOrWhiteSpace(habitat) ? "该 NPC 未登记公开生成条件；点名称查看详情。" : habitat)));
            foreach (string condition in drop.ConditionDescriptions()) rows.Add(new("掉落条件：" + condition));
        }
        foreach (var drop in includeDrops ? GameDataIndex.GetContainerSources(type) : Array.Empty<DropRecord>())
        {
            rows.Add(new("开启容器：" + Lang.GetItemNameValue(drop.ContainerType), drop.ContainerType, Link: GuideLink.Sources, Heading: true));
            rows.Add(new("操作：先获得容器，再在背包中右键开启；钥匙、模式和特殊限制以该容器要求为准。"));
            rows.Add(new("产出：" + drop.Summary));
            foreach (string condition in drop.ConditionDescriptions()) rows.Add(new("开启 / 掉落条件：" + condition));
        }
        var recipes = StorageSystem.Recipes?.GetRecipesFor(ItemKey.Crafted(type)) ?? Array.Empty<int>();
        if (recipes.Count > 0)
        {
            rows.Add(new("制作路线（" + recipes.Count + " 种）", Heading: true));
            foreach (int index in recipes)
            {
                var recipe = Main.recipe[index];
                string stations = string.Join("、", RecipeStation.GetRequiredStations(recipe).Select(RecipeStation.DescribeStation));
                rows.Add(new("检查配方：产出 " + recipe.createItem.stack + "；" + (stations.Length == 0 ? "徒手" : stations), type,
                    Link: GuideLink.Recipe, Recipe: BrowserContent.RecipeKey(recipe)));
            }
        }
        // Keep explicitly registered information, including third-party Mod.Call additions.
        var extra = StorageSystem.Acquisition.GetSources(ItemKey.Crafted(type)).Where(s => !GameDataIndex.IsIndexedDropText(s)).Distinct();
        foreach (string text in extra) rows.Add(new("补充登记：" + text));
        rows.Add(new("条件没有状态标记时，表示尚不能安全自动判断；阶段排序不等于当前满足全部条件。"));
        return rows;
    }

    internal static Dictionary<int, long> Stock(out string source)
    {
        var result = new Dictionary<int, long>();
        var snapshot = PinnedInventory.Snapshot(); source = PinnedInventory.Status;
        void Add(int type, long count) => result[type] = StorageAmounts.Add(result.GetValueOrDefault(type), count);
        if (snapshot != null) foreach (var entry in snapshot) Add(entry.Key.Type, entry.Value);
        else if (Main.LocalPlayer?.inventory != null)
            foreach (var item in Main.LocalPlayer.inventory) if (item != null && !item.IsAir) Add(item.type, item.stack);
        return result;
    }

    internal static List<GuideRow> Npc(int type)
    {
        var rows = new List<GuideRow>();
        if (type <= 0 || !ContentSamples.NpcsByNetId.TryGetValue(type, out var npc)) return new() { new("NPC 当前未加载。") };
        GameDataIndex.EnsureBuilt();
        rows.Add(new(npc.FullName, Npc: type, Heading: true));
        rows.Add(new("生命 " + npc.lifeMax + "；伤害 " + npc.damage + "；防御 " + npc.defense));
        string habitat = StorageSystem.Acquisition.GetSpawnCondition(type);
        rows.Add(new("出没 / 入住信息：" + (string.IsNullOrWhiteSpace(habitat) ? "未登记公开条件，特殊生成规则尚不能自动确认。" : habitat)));
        foreach (var drop in GameDataIndex.GetNpcDropRecords(type))
        {
            rows.Add(new(Lang.GetItemNameValue(drop.ItemType) + " · " + drop.Summary, drop.ItemType, Link: GuideLink.Sources));
            foreach (string condition in drop.ConditionDescriptions()) rows.Add(new("掉落条件：" + condition));
        }
        return rows;
    }

    internal static int StationItem(int tile) => ContentSamples.ItemsByType.Values
        .Where(i => !i.IsAir && i.createTile == tile).Select(i => i.type).FirstOrDefault();

    internal static List<GuideRow> RecipeHelp(int type, string signature, long amount)
    {
        var rows = new List<GuideRow>();
        if (!Loaded(type)) return new() { new("物品当前未加载；记录保留，不能跳转到其他物品。") };
        var recipes = StorageSystem.Recipes.GetRecipesFor(ItemKey.Crafted(type));
        int index = string.IsNullOrEmpty(signature) ? recipes.FirstOrDefault(-1)
            : recipes.FirstOrDefault(i => BrowserContent.RecipeKey(Main.recipe[i]) == signature, -1);
        rows.Add(new("制作检查：" + Lang.GetItemNameValue(type), type, Heading: true));
        if (index < 0)
        {
            rows.Add(new("该配方当前不可用；可能已被模组修改或移除。"));
            rows.Add(ItemLink(type, "重新选择获取路线：")); return rows;
        }
        var recipe = Main.recipe[index];
        var stock = Stock(out string source);
        var pool = new RecipeTreeStock(stock);
        amount = Math.Max(1, amount);
        long output = Math.Max(1, recipe.createItem.stack), batches = amount / output + (amount % output == 0 ? 0 : 1);
        rows.Add(new("本次制作 " + UIFormat.Count(amount) + " 件；库存来源：" + source + "。这里只读检查，不消耗物品。"));
        rows.Add(new("直接材料 · 点击缺项查来源", Heading: true));
        foreach (var item in recipe.requiredItem.Where(i => !i.IsAir))
        {
            RecipeGroup group = recipe.acceptedGroups.Select(id => RecipeGroup.recipeGroups.GetValueOrDefault(id))
                .FirstOrDefault(g => g != null && g.ValidItems.Contains(item.type));
            bool overflow = batches > long.MaxValue / Math.Max(1, item.stack);
            long need = overflow ? long.MaxValue : batches * item.stack, missing = need;
            foreach (int candidate in group?.ValidItems.OrderBy(t => t) ?? Enumerable.Repeat(item.type, 1).OrderBy(t => t))
            { missing -= pool.Take(candidate, missing); if (missing == 0) break; }
            rows.Add(new((group?.GetText() ?? item.Name) + "：需求 " + UIFormat.Count(need) + "，缺少 " + UIFormat.Count(missing)
                + (overflow ? "（数量超出范围）" : ""), item.type, Link: GuideLink.Sources, Satisfied: !overflow && missing == 0));
            if (group != null)
                foreach (int alternative in group.ValidItems.Where(Loaded)) rows.Add(ItemLink(alternative, "可替代："));
        }
        rows.Add(new("工作站与环境 · 当前状态", Heading: true));
        var terminal = PinnedInventory.HasOpenTerminal && UISystem.TerminalReadable ? UISystem.ActiveCraftingTerminal : null;
        var stations = terminal?.ConnectedStations();
        foreach (int tile in RecipeStation.GetRequiredStations(recipe))
        {
            bool? ready = Main.gameMenu ? null : terminal != null ? TerminalCraftingExecutor.HasStation(stations, tile)
                : Main.LocalPlayer?.adjTile is { } adjacent && tile >= 0 && tile < adjacent.Length ? adjacent[tile] : null;
            int station = StationItem(tile);
            rows.Add(new("工作站：" + RecipeStation.DescribeStation(tile) + (terminal != null ? "（终端工作站槽）" : "（角色附近）"),
                station, Link: station > 0 ? GuideLink.Sources : GuideLink.None, Satisfied: ready));
        }
        if (recipe.requiredTile.Count == 0) rows.Add(new("工作站：徒手", Satisfied: true));
        foreach (var condition in recipe.Conditions)
            rows.Add(new("环境 / 解锁：" + condition.Description.Value, Satisfied: ConditionState(condition)));
        rows.Add(new(terminal == null ? "普通制作：靠近工作站并满足上方环境后，在原版制作栏操作。"
            : "终端制作：将工作站物品放入终端工作站槽，环境按下单玩家判断。绿色材料状态仍须经过权限及完整计划复核。"));
        if (PinnedInventory.HasOpenTerminal)
        {
            var plan = TerminalCraftingCacheSystem.Current.Plan(ItemKey.Crafted(type), amount);
            rows.Add(new("终端完整路线检查（终端可能选择其他可用配方）", Heading: true));
            foreach (string reason in plan.BlockedReasons) rows.Add(new(reason, Link: GuideLink.Tutorial, Satisfied: false));
            foreach (var missing in plan.Missing) rows.Add(new("缺少：" + missing.ItemName + " ×" + UIFormat.Count(missing.Amount), missing.Key.Type, Link: GuideLink.Sources, Satisfied: false));
            if (plan.FullySatisfied) rows.Add(new("完整计划已满足；执行前仍会复核输出容量和动态条件。", Satisfied: true));
            else if (plan.Limited) rows.Add(new("规划尚未完成；可缩小数量或分批制作。"));
            rows.Add(new("排查库存容量、过滤和权限", Link: GuideLink.Storage));
        }
        rows.Add(new("查看终端入门：工作站怎么放、怎样存取与合成", Link: GuideLink.Tutorial));
        return rows;
    }
}
