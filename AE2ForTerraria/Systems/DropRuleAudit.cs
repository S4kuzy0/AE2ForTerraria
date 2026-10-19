using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using Terraria;
using Terraria.GameContent.ItemDropRules;
using Terraria.ModLoader;

namespace AE2ForTerraria.Systems;

// Diagnostic only. Does not replace the current query model or change visible drop information.
public sealed class DropRuleAudit : ModSystem
{
    public override void OnWorldLoad()
    {
        if (Environment.GetEnvironmentVariable("AE2_DROP_AUDIT") != "1"
            || !Main.dedServ || Program.LaunchParameters.ContainsKey("-subworld")
            || Main.worldName != "AE2ValidationCore20261003") return;
        int rows = 0, conditional = 0, zero = 0, abnormal = 0, errors = 0;
        var examples = new List<object>();
        for (int npc = 1; npc < NPCLoader.NPCCount; npc++)
        {
            foreach (var rule in Main.ItemDropsDB.GetRulesForNPCID(npc))
            {
                var drops = new List<DropRateInfo>();
                try { rule.ReportDroprates(drops, new DropRateInfoChainFeed(1f)); }
                catch (Exception ex)
                {
                    errors++;
                    if (examples.Count < 30) examples.Add(new { npc, rule = rule.GetType().FullName, error = ex.GetType().Name });
                    continue;
                }
                foreach (var drop in drops)
                {
                    rows++;
                    int conditions = drop.conditions?.Count ?? 0;
                    if (conditions > 0) conditional++;
                    if (drop.dropRate == 0) zero++;
                    if (!float.IsFinite(drop.dropRate) || drop.dropRate < 0 || drop.dropRate > 1) abnormal++;
                    if (examples.Count < 30 && (drop.dropRate <= 0 || !float.IsFinite(drop.dropRate)))
                        examples.Add(new { npc, npcName = Lang.GetNPCNameValue(npc), item = drop.itemId,
                            itemName = Lang.GetItemNameValue(drop.itemId), rate = drop.dropRate.ToString("R", System.Globalization.CultureInfo.InvariantCulture),
                            minimum = drop.stackMin, maximum = drop.stackMax, conditions,
                            rule = rule.GetType().FullName });
                }
            }
        }
        Directory.CreateDirectory(RegressionPaths.Root);
        string path = Path.Combine(RegressionPaths.Root, "drop-rule-audit.json");
        File.WriteAllText(path, JsonSerializer.Serialize(new { version = Mod.Version.ToString(),
            npcTypes = NPCLoader.NPCCount, rows, conditional, zero, abnormal, errors, examples }, new JsonSerializerOptions { WriteIndented = true }));
        Mod.Logger.Info($"AE2 DROP AUDIT: rows={rows} conditional={conditional} zero={zero} abnormal={abnormal} errors={errors}; {path}");
    }
}
