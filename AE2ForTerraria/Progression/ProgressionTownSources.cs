using System;
using System.Collections.Generic;
using Terraria;
using Terraria.ID;

namespace AE2ForTerraria.Progression;

/// <summary>Audited vanilla town and living-world routes missing from static NPC shops.</summary>
internal static class ProgressionTownSources
{
    internal static void Seed(ProgressionGraph graph, Action<int, int, string> add)
    {
        // SetupTravelShop_GetItem rolls these independently of boss flags.
        // Traveling merchants can arrive with the Guide and Merchant present.
        add(ItemID.Gi, 0, "旅商随机出售：有两名城镇 NPC 即可到访，无 Boss 门槛");
        add(ItemID.WaterGun, 0, "旅商随机出售：有两名城镇 NPC 即可到访，无 Boss 门槛");
        if (!Main.remixWorld)
            add(ItemID.Katana, 0, "普通世界旅商随机出售，无 Boss 门槛；无需等地牢装甲骷髅掉落");

        // In 1.4.4 this is 10%, not the newer Wiki's 3%. tML's progress report
        // counts vanilla entries only; ordinary pre-boss wildlife and enemies
        // provide the required 54 discoveries without defeating a boss.
        add(ItemID.BlandWhip, 0, "动物学家出售：原版图鉴达到 10%，Boss 前探索即可达到");
        add(ItemID.GardenGnome, 0, Main.remixWorld
            ? "颠倒世界：将天然侏儒引到地狱下层石化后采集，无 Boss 门槛"
            : "将天然侏儒引到日照处石化后采集，无 Boss 门槛");
    }

    internal static List<ProgressionWorldSources.AcquisitionRoute> Routes(ProgressionGraph graph)
    {
        // The required biome is an input, so a legal earlier source of Hallow
        // also advances these fruits; no real-time world flag is simulated.
        var routes = new List<ProgressionWorldSources.AcquisitionRoute>();
        foreach (int substrate in new[] { ItemID.HallowedSeeds, ItemID.PearlsandBlock })
            routes.Add(new(ItemID.Starfruit, 0, new[] { substrate },
                "种植神圣草地或珍珠沙上的珍珠木树，摇树取得星星果"));
        routes.Add(new(ItemID.BlueFlare, 0, new int[] { ItemID.FlareGun }, "持有照明弹枪后，商人出售蓝照明弹"));
        return routes;
    }
}
