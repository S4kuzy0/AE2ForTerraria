using System.Collections.Generic;
using AE2ForTerraria.UI;
using Microsoft.Xna.Framework;
using Terraria;
using Terraria.ModLoader;

namespace AE2ForTerraria.Systems;

public sealed class ArmorSetTooltip : GlobalItem
{
    public override void ModifyTooltips(Item item, List<TooltipLine> tooltips)
    {
        if (!BrowserHover.IsCurrent(item) || !ArmorSetIndex.IsArmor(item)) return;
        var descriptions = ArmorSetIndex.Request(item.type, out bool pending);
        int index = 0;
        foreach (string text in descriptions)
            tooltips.Add(new TooltipLine(Mod, "ArmorSet" + index++, "套装效果：" + text) { OverrideColor = new Color(170, 220, 255) });
        var player = Main.LocalPlayer;
        if (!Main.gameMenu && player?.armor != null && !string.IsNullOrWhiteSpace(player.setBonus)
            && (player.armor[0].type == item.type || player.armor[1].type == item.type || player.armor[2].type == item.type)
            && !System.Linq.Enumerable.Contains(descriptions, player.setBonus))
            tooltips.Add(new TooltipLine(Mod, "ArmorSetCurrent", "当前穿戴效果：" + player.setBonus) { OverrideColor = Color.Gold });
        if (pending) tooltips.Add(new TooltipLine(Mod, "ArmorSetLoading", "正在索引套装效果…") { OverrideColor = Color.Gray });
    }
}
