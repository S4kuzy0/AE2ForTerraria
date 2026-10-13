using System;
using System.Collections.Generic;
using System.Linq;
using Terraria.GameContent.ItemDropRules;

namespace AE2ForTerraria.Systems;

/// <summary>One reported rule; independent conditional rules are never summed into a fabricated chance.</summary>
internal sealed record DropRecord(int ItemType, int NpcType, int ContainerType, float? Probability,
    int StackMin, int StackMax, IReadOnlyList<IItemDropRuleCondition> Conditions)
{
    public static DropRecord From(DropRateInfo info, int npc = 0, int container = 0) => new(
        info.itemId, npc, container,
        float.IsFinite(info.dropRate) && info.dropRate >= 0 && info.dropRate <= 1 ? info.dropRate : null,
        info.stackMin, info.stackMax, Array.AsReadOnly(info.conditions?.ToArray() ?? Array.Empty<IItemDropRuleCondition>()));
    public string Chance => Probability.HasValue ? (Probability.Value * 100).ToString("0.##") + "%" : "概率未知";
    public string Quantity => StackMin > 0 && StackMax >= StackMin
        ? "数量 " + StackMin + (StackMin == StackMax ? "" : "–" + StackMax) : "数量未知";
    public string Summary => Chance + " · " + Quantity;
    public IEnumerable<string> ConditionDescriptions()
    {
        foreach (var condition in Conditions)
        {
            string description;
            try { description = condition?.GetConditionDescription(); }
            catch (Exception ex)
            { AE2ForTerraria.Instance.Logger.Warn("Drop condition description failed: " + condition?.GetType().FullName + " " + ex.Message); description = null; }
            yield return string.IsNullOrWhiteSpace(description) ? "条件：" + (condition?.GetType().Name ?? "未知") : description;
        }
    }
}
