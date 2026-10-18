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
        void Add(int type,int tier,string reason = "基础资源 / 生成来源")
        {
            if(type<=0 || tier<0) return;
            var key=ItemKey.Crafted(type);
            if(!anchors.TryGetValue(key,out int old) || tier<old) { anchors[key]=tier; graph.SourceReason(type, reason); }
        }
        foreach(int type in new[]{ItemID.Wood,ItemID.BorealWood,ItemID.PalmWood,ItemID.RichMahogany,ItemID.Ebonwood,ItemID.Shadewood,
            ItemID.DirtBlock,ItemID.StoneBlock,ItemID.SandBlock,ItemID.ClayBlock,ItemID.MudBlock,ItemID.SnowBlock,ItemID.IceBlock,
            ItemID.CopperOre,ItemID.TinOre,ItemID.IronOre,ItemID.LeadOre,ItemID.SilverOre,ItemID.TungstenOre,ItemID.GoldOre,ItemID.PlatinumOre,
            ItemID.CopperPickaxe,ItemID.TinPickaxe,ItemID.Obsidian,ItemID.Hellforge,ItemID.Amber,ItemID.Amethyst,
            ItemID.Topaz,ItemID.Sapphire,ItemID.Emerald,ItemID.Ruby,ItemID.Diamond}) Add(type,0);
        int hardmode=Gate(graph,"Hardmode");
        foreach(int type in new[]{ItemID.Pearlwood,ItemID.CrystalShard}) Add(type,hardmode,"困难模式世界变化");
        // Read declared drop rules; never execute CanDrop or temporarily mutate world flags.
        var spawnGates = new Dictionary<int,int>();
        foreach(int npc in new[]{NPCID.BlueSlime,NPCID.GreenSlime,NPCID.Zombie,NPCID.DemonEye,NPCID.Skeleton,NPCID.Piranha,
            NPCID.Hornet,NPCID.JungleBat,NPCID.Demon,NPCID.Harpy,NPCID.AngryBones,NPCID.Vulture,NPCID.Antlion,
            NPCID.Worm,NPCID.Goldfish,NPCID.Bunny,NPCID.Firefly}) spawnGates[npc] = 0;
        foreach(int npc in new[]{NPCID.Pixie,NPCID.Unicorn,NPCID.ChaosElemental,NPCID.WyvernHead,NPCID.GiantBat,
            NPCID.ArmoredSkeleton,NPCID.SkeletonArcher,NPCID.Wraith,NPCID.IchorSticker,NPCID.Clinger,
            NPCID.AnglerFish,NPCID.Arapaima,NPCID.IceElemental,NPCID.Derpling,NPCID.MossHornet}) spawnGates[npc] = hardmode;
        foreach(int npc in new[]{NPCID.DungeonSpirit,NPCID.BlueArmoredBones,NPCID.RustyArmoredBonesAxe,NPCID.HellArmoredBones,
            NPCID.Necromancer,NPCID.RaggedCaster,NPCID.DiabolistRed,NPCID.Paladin}) spawnGates[npc] = BossTier(graph,NPCID.Plantera);
        foreach(var spawn in spawnGates)
            foreach(var drop in GameDataIndex.GetNpcDropRecords(spawn.Key))
            {
                int tier = spawn.Value;
                foreach(var condition in drop.Conditions)
                {
                    int gate = DropConditionTier(graph, condition.GetType());
                    if(gate < 0) { tier = -1; break; }
                    tier = Math.Max(tier,gate);
                }
                Add(drop.ItemType,tier,"世界 / 小怪掉落：" + Lang.GetNPCNameValue(spawn.Key));
            }
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
                if(known) Add(entry.Item.type,tier,"商店：" + Lang.GetNPCNameValue(shop.NpcType) + "（含已知到达及商品条件）");
            }
        }
    }
    private static int DropConditionTier(ProgressionGraph graph,Type type)
    {
        if(type.Assembly != typeof(Terraria.GameContent.ItemDropRules.Conditions).Assembly) return -1;
        return type.Name switch
        {
            "IsHardmode" or "SoulOfLight" or "SoulOfNight" => BossTier(graph,NPCID.WallofFlesh),
            "DownedAllMechBosses" => Gate(graph,"DownedMechBossAll"),
            "DownedPlantera" => BossTier(graph,NPCID.Plantera),
            "NotExpert" or "IsExpert" or "IsMasterMode" or "NotRemixSeed" or "RemixSeed" or "NotFromStatue" => 0,
            _ => -1
        };
    }
    internal readonly record struct MiningRoute(int Item,int Gate,int Pick,string Reason);
    internal static List<MiningRoute> MiningRoutes(ProgressionGraph graph)
    {
        int hardmode = BossTier(graph,NPCID.WallofFlesh);
        var mechs = new[]{BossTier(graph,NPCID.TheDestroyer),BossTier(graph,NPCID.Retinazer),BossTier(graph,NPCID.SkeletronPrime)}.Order().ToArray();
        bool? rework = CalamityOreRework();
        int mythrilGate = rework == true ? mechs[0] : rework == null ? -1 : hardmode;
        int adamantiteGate = rework == true ? mechs[1] : rework == null ? -1 : hardmode;
        var result = new List<MiningRoute>
        {
            new(ItemID.DemoniteOre,0,55,"天然矿脉"),new(ItemID.CrimtaneOre,0,55,"天然矿脉"),
            new(ItemID.Hellstone,0,65,"地狱矿脉"),
            new(ItemID.CobaltOre,hardmode,100,"困难模式矿脉"),new(ItemID.PalladiumOre,hardmode,100,"困难模式矿脉"),
            new(ItemID.MythrilOre,mythrilGate,110,rework == true ? "灾厄重做：首个机械 Boss 解锁矿脉" : "困难模式祭坛矿脉"),
            new(ItemID.OrichalcumOre,mythrilGate,110,rework == true ? "灾厄重做：首个机械 Boss 解锁矿脉" : "困难模式祭坛矿脉"),
            new(ItemID.AdamantiteOre,adamantiteGate,150,rework == true ? "灾厄重做：第二个机械 Boss 解锁矿脉" : "困难模式祭坛矿脉"),
            new(ItemID.TitaniumOre,adamantiteGate,150,rework == true ? "灾厄重做：第二个机械 Boss 解锁矿脉" : "困难模式祭坛矿脉"),
            new(ItemID.ChlorophyteOre,hardmode,200,"困难模式丛林矿脉")
        };
        if(ModLoader.TryGetMod("CalamityMod",out var calamity))
        {
            void ModOre(string name,int gate,string reason)
            {
                if(calamity.TryFind<ModItem>(name,out var ore) && ore.Item.createTile >= 0)
                {
                    var tile = TileLoader.GetTile(ore.Item.createTile);
                    if(tile != null) result.Add(new(ore.Type,gate,tile.MinPick,reason));
                }
            }
            ModOre("PerennialOre",BossTier(graph,NPCID.Plantera),"世纪之花后生成多年生矿脉");
            if(rework == true) ModOre("HallowedOre",mechs.Max(),"灾厄重做：全部机械 Boss 后生成神圣矿脉");
        }
        return result;
    }
    private static bool? CalamityOreRework()
    {
        if(!ModLoader.TryGetMod("CalamityMod",out var calamity)) return false;
        try
        {
            var type = calamity.Code.GetType("CalamityMod.CalamityServerConfig");
            object instance = type?.GetProperty("Instance",BindingFlags.Public|BindingFlags.Static)?.GetValue(null)
                ?? type?.GetField("Instance",BindingFlags.Public|BindingFlags.Static)?.GetValue(null);
            return type?.GetProperty("EarlyHardmodeProgressionRework")?.GetValue(instance) as bool?
                ?? type?.GetField("EarlyHardmodeProgressionRework")?.GetValue(instance) as bool?;
        }
        catch(Exception ex) { AE2ForTerraria.Instance?.Logger.Warn("Cannot read Calamity ore progression setting: " + ex.Message); return null; }
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
