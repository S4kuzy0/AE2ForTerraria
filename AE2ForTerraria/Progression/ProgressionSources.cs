using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using AE2ForTerraria.Core;
using AE2ForTerraria.Systems;
using Terraria;
using Terraria.ID;
using Terraria.ModLoader;

namespace AE2ForTerraria.Progression;

/// <summary>Declared unlock gates. Unknown mod predicates are not treated as early-game availability.</summary>
internal static class ProgressionSources
{
    private static readonly Dictionary<Condition,string> VanillaConditions = typeof(Condition)
        .GetFields(BindingFlags.Public|BindingFlags.Static).Where(f=>f.FieldType==typeof(Condition))
        .GroupBy(f=>(Condition)f.GetValue(null)).ToDictionary(g=>g.Key,g=>g.First().Name);
    internal static int BossTier(ProgressionGraph graph, params int[] npcs)
    {
        var tiers=graph.Tiers.Where(t=>t.NpcIds.Any(n=>npcs.Contains(n))).Select(t=>t.Index).ToArray();
        return tiers.Length>0 ? tiers.Min() : -1;
    }
    private static int Gate(ProgressionGraph graph,string name) => name switch
    {
        "Hardmode"=>BossTier(graph,NPCID.WallofFlesh),
        "DownedKingSlime"=>BossTier(graph,NPCID.KingSlime),
        "DownedEyeOfCthulhu"=>BossTier(graph,NPCID.EyeofCthulhu),
        "DownedEowOrBoc"=>BossTier(graph,NPCID.EaterofWorldsHead,NPCID.BrainofCthulhu),
        "DownedEaterOfWorlds"=>BossTier(graph,NPCID.EaterofWorldsHead),
        "DownedBrainOfCthulhu"=>BossTier(graph,NPCID.BrainofCthulhu),
        "DownedQueenBee"=>BossTier(graph,NPCID.QueenBee),
        "DownedSkeletron"=>BossTier(graph,NPCID.SkeletronHead),
        "DownedDeerclops"=>BossTier(graph,NPCID.Deerclops),
        "DownedQueenSlime"=>BossTier(graph,NPCID.QueenSlimeBoss),
        "DownedMechBossAny"=>BossTier(graph,NPCID.TheDestroyer,NPCID.Retinazer,NPCID.SkeletronPrime),
        "DownedTwins"=>BossTier(graph,NPCID.Retinazer),
        "DownedDestroyer"=>BossTier(graph,NPCID.TheDestroyer),
        "DownedSkeletronPrime"=>BossTier(graph,NPCID.SkeletronPrime),
        "DownedMechBossAll"=>new[]{BossTier(graph,NPCID.TheDestroyer),BossTier(graph,NPCID.Retinazer),BossTier(graph,NPCID.SkeletronPrime)}.Max(),
        "DownedPlantera"=>BossTier(graph,NPCID.Plantera),
        "DownedGolem"=>BossTier(graph,NPCID.Golem),
        "DownedDukeFishron"=>BossTier(graph,NPCID.DukeFishron),
        "DownedEmpressOfLight"=>BossTier(graph,NPCID.HallowBoss),
        "DownedCultist"=>BossTier(graph,NPCID.CultistBoss),
        "DownedMoonLord"=>BossTier(graph,NPCID.MoonLordCore),
        _=>-1
    };
    internal static int ConditionTier(ProgressionGraph graph,Condition condition)
    {
        if(!VanillaConditions.TryGetValue(condition,out string name)) return -1;
        if(name=="Hardmode" || name.StartsWith("Downed")) return Gate(graph,name);
        // Day, weather, biome, research and pre-hardmode conditions do not add a
        // permanent boss unlock. Their runtime execution checks remain separate.
        return 0;
    }
    internal static int NaturalStationTier(ProgressionGraph graph,int tile) => tile switch
    {
        TileID.DemonAltar or TileID.Hellforge=>0,
        TileID.AlchemyTable=>BossTier(graph,NPCID.SkeletronHead),
        _=>-1
    };
    internal static void Anchor(ProgressionGraph graph,Dictionary<ItemKey,int> anchors)
    {
        void Add(int type,int tier)
        {
            if(type<=0 || tier<0) return;
            var key=ItemKey.Crafted(type);
            if(!anchors.TryGetValue(key,out int old) || tier<old) anchors[key]=tier;
        }
        foreach(int type in new[]{ItemID.Wood,ItemID.BorealWood,ItemID.PalmWood,ItemID.RichMahogany,ItemID.Ebonwood,ItemID.Shadewood,
            ItemID.DirtBlock,ItemID.StoneBlock,ItemID.SandBlock,ItemID.ClayBlock,ItemID.MudBlock,ItemID.SnowBlock,ItemID.IceBlock,
            ItemID.CopperOre,ItemID.TinOre,ItemID.IronOre,ItemID.LeadOre,ItemID.SilverOre,ItemID.TungstenOre,ItemID.GoldOre,ItemID.PlatinumOre,
            ItemID.DemoniteOre,ItemID.CrimtaneOre,ItemID.Obsidian,ItemID.Hellforge,ItemID.Amber,ItemID.Amethyst,
            ItemID.Topaz,ItemID.Sapphire,ItemID.Emerald,ItemID.Ruby,ItemID.Diamond}) Add(type,0);
        int hardmode=Gate(graph,"Hardmode");
        Add(ItemID.Hellstone,Gate(graph,"DownedEowOrBoc"));
        Add(ItemID.ChlorophyteOre,Gate(graph,"DownedMechBossAll"));
        foreach(int type in new[]{ItemID.CobaltOre,ItemID.PalladiumOre,ItemID.MythrilOre,ItemID.OrichalcumOre,
            ItemID.AdamantiteOre,ItemID.TitaniumOre,ItemID.Pearlwood,ItemID.CrystalShard}) Add(type,hardmode);
        foreach(int npc in new[]{NPCID.BlueSlime,NPCID.GreenSlime,NPCID.Zombie,NPCID.DemonEye,NPCID.Skeleton,NPCID.Piranha,
            NPCID.Hornet,NPCID.JungleBat,NPCID.Demon,NPCID.Harpy,NPCID.AngryBones})
            foreach(var drop in GameDataIndex.GetNpcDropRecords(npc))
                if(drop.Conditions.Count==0) Add(drop.ItemType,0);
        foreach(var shop in NPCShopDatabase.AllShops.OfType<NPCShop>())
        {
            int arrival=shop.NpcType switch
            {
                NPCID.Merchant or NPCID.Guide or NPCID.Nurse or NPCID.ArmsDealer or NPCID.Demolitionist or NPCID.Stylist or NPCID.Painter=>0,
                NPCID.Dryad=>BossTier(graph,NPCID.EyeofCthulhu,NPCID.EaterofWorldsHead,NPCID.BrainofCthulhu,NPCID.SkeletronHead),
                NPCID.Clothier or NPCID.Mechanic=>BossTier(graph,NPCID.SkeletronHead),
                NPCID.Wizard=>hardmode,
                NPCID.Steampunker=>Gate(graph,"DownedMechBossAny"),
                NPCID.Cyborg=>BossTier(graph,NPCID.Plantera),
                _=>-1
            };
            if(arrival<0) continue;
            foreach(var entry in shop.Entries)
            {
                if(entry.Disabled) continue;
                int tier=arrival; bool known=true;
                foreach(var condition in entry.Conditions)
                { int gate=ConditionTier(graph,condition);if(gate<0){known=false;break;}tier=Math.Max(tier,gate); }
                if(known) Add(entry.Item.type,tier);
            }
        }
    }
    internal static void AssignCheckpointNpcs(ProgressionTier tier)
    {
        if(tier.NpcIds.Count>0) return;
        int[] ids=tier.Id switch
        {
            "king_slime"=>new int[]{NPCID.KingSlime}, "eye"=>new int[]{NPCID.EyeofCthulhu},
            "evil"=>new int[]{NPCID.EaterofWorldsHead,NPCID.BrainofCthulhu},
            "skeletron"=>new int[]{NPCID.SkeletronHead}, "queen_bee"=>new int[]{NPCID.QueenBee},
            "hardmode"=>new int[]{NPCID.WallofFlesh}, "mech"=>new int[]{NPCID.TheDestroyer,NPCID.Retinazer,NPCID.SkeletronPrime},
            "plantera"=>new int[]{NPCID.Plantera}, "golem"=>new int[]{NPCID.Golem},
            "duke_fishron"=>new int[]{NPCID.DukeFishron}, "empress"=>new int[]{NPCID.HallowBoss}, "moonlord"=>new int[]{NPCID.MoonLordCore},
            _=>Array.Empty<int>()
        };
        tier.NpcIds.AddRange(ids);
    }
}
