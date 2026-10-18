using System.Collections.Generic;
using AE2ForTerraria.UI;
using Microsoft.Xna.Framework;
using Terraria;
using Terraria.ModLoader;
using Terraria.GameContent;

namespace AE2ForTerraria.Systems;

public sealed class ProgressionTooltip : GlobalItem
{
    public override void ModifyTooltips(Item item, List<TooltipLine> tooltips)
    {
        if (!BrowserHover.IsCurrent(item) || !BrowserHover.ShowProgression || StorageSystem.Progression == null) return;
        int index = 0;
        void Add(string text) => tooltips.Add(new TooltipLine(Mod, "AcquisitionStage" + index++, text)
            { OverrideColor = new Color(170, 220, 255) });
        float width = System.Math.Min(560, Main.screenWidth * .6f);
        foreach (string paragraph in StorageSystem.Progression.GetExplanation(item.type).Split('\n'))
        {
            string line = "";
            foreach (char c in paragraph)
            {
                if (line.Length > 0 && FontAssets.MouseText.Value.MeasureString(line + c).X > width) { Add(line); line = ""; }
                line += c;
            }
            if (line.Length > 0) Add(line);
        }
    }
}
