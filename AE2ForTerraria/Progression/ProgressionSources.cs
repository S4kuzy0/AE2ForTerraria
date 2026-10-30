using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using AE2ForTerraria.Core;
using AE2ForTerraria.Systems;
using Terraria;
using Terraria.ID;
using Terraria.ModLoader;
using Terraria.GameContent.ItemDropRules;

namespace AE2ForTerraria.Progression;

/// <summary>Declared unlock gates. Unknown mod predicates are not treated as early-game availability.</summary>
internal static class ProgressionSources
{
    private static readonly Dictionary<Condition,string> VanillaConditions = typeof(Condition)
        .GetFields(BindingFlags.Public|BindingFlags.Static).Where(f=>f.FieldType==typeof(Condition))
        .GroupBy(f=>(Condition)f.GetValue(null)).ToDictionary(g=>g.Key,g=>g.First().Name);
    internal static int BossTier(ProgressionGraph graph, params int[] npcs)
    {
        // A boss may also be listed as a member of an event. Prefer its own
        // checkpoint before taking the earliest of the alternative bosses.
        var tiers=npcs.Select(npc=>graph.Tiers.Where(t=>t.NpcIds.Contains(npc))
            .OrderBy(t=>t.NpcIds.Count).ThenBy(t=>t.Index).FirstOrDefault())
            .Where(t=>t!=null).Select(t=>t.Index).ToArray();
        return tiers.Length>0 ? tiers.Min() : -1;
    }
    private static int AllGates(params int[] gates) => gates.Any(g=>g<0) ? -1 : gates.Max();
    private static int AnyGate(params int[] gates) => gates.Where(g=>g>=0).DefaultIfEmpty(-1).Min();
    internal static int EventTier(ProgressionGraph graph, string id) =>
        graph.Tiers.FirstOrDefault(t => t.Id == "bc_Terraria " + id)?.Index ?? -1;
    internal static int EventNpcGate(ProgressionGraph graph,int npc)
    {
        if(npc is NPCID.Clown or NPCID.GoblinSummoner or NPCID.BloodNautilus or NPCID.BloodSquid
            or NPCID.GoblinShark or NPCID.BloodEelHead or NPCID.BloodEelBody or NPCID.BloodEelTail)
            return BossTier(graph,NPCID.WallofFlesh);
        if(npc==NPCID.DD2Betsy) return BossTier(graph,NPCID.Golem);
        string name=npc>0 && npc<NPCID.Count ? NPCID.Search.GetName(npc) : "";
        if(name?.StartsWith("DD2",StringComparison.Ordinal)==true)
        {
            if(name.EndsWith("T3",StringComparison.Ordinal)) return BossTier(graph,NPCID.Golem);
            if(name.EndsWith("T2",StringComparison.Ordinal)) return BossTier(graph,NPCID.TheDestroyer,NPCID.Retinazer,NPCID.SkeletronPrime);
        }
        return 0;
    }
    private static int OldOnesArmyCompletionGate(ProgressionGraph graph, int minimumDifficulty)
    {
        int eventGate = EventTier(graph, "OldOnesArmy");
        int evilBoss = Gate(graph, "DownedEowOrBoc");
        int hardmode = Gate(graph, "Hardmode");
        int first = AllGates(eventGate, evilBoss, BossTier(graph, NPCID.DD2DarkMageT1));
        int second = AllGates(eventGate, evilBoss, hardmode, Gate(graph, "DownedMechBossAny"), BossTier(graph, NPCID.DD2OgreT2));
        int third = AllGates(eventGate, evilBoss, hardmode, BossTier(graph, NPCID.Golem), BossTier(graph, NPCID.DD2Betsy));
        // WinInvasionInternal sets every completion flag at or below the
        // completed difficulty, so higher tiers are valid alternative routes.
        return minimumDifficulty switch
        {
            1 => AnyGate(first, second, third),
            2 => AnyGate(second, third),
            _ => third
        };
    }
    internal static int Gate(ProgressionGraph graph,string name) => name switch
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
        "DownedEarlygameBoss"=>BossTier(graph,NPCID.EyeofCthulhu,NPCID.EaterofWorldsHead,NPCID.BrainofCthulhu,
            NPCID.SkeletronHead,NPCID.QueenBee,NPCID.WallofFlesh),
        "DownedQueenSlime"=>BossTier(graph,NPCID.QueenSlimeBoss),
        "DownedMechBossAny"=>BossTier(graph,NPCID.TheDestroyer,NPCID.Retinazer,NPCID.SkeletronPrime),
        "DownedTwins"=>BossTier(graph,NPCID.Retinazer),
        "DownedDestroyer"=>BossTier(graph,NPCID.TheDestroyer),
        "DownedSkeletronPrime"=>BossTier(graph,NPCID.SkeletronPrime),
        "DownedMechBossAll"=>AllGates(BossTier(graph,NPCID.TheDestroyer),BossTier(graph,NPCID.Retinazer),BossTier(graph,NPCID.SkeletronPrime)),
        "DownedPlantera"=>BossTier(graph,NPCID.Plantera),
        "DownedGolem"=>BossTier(graph,NPCID.Golem),
        "DownedDukeFishron"=>BossTier(graph,NPCID.DukeFishron),
        "DownedEmpressOfLight"=>BossTier(graph,NPCID.HallowBoss),
        "DownedCultist"=>BossTier(graph,NPCID.CultistBoss),
        "DownedMoonLord"=>BossTier(graph,NPCID.MoonLordCore),
        "DownedMourningWood"=>BossTier(graph,NPCID.MourningWood),
        "DownedPumpking"=>BossTier(graph,NPCID.Pumpking),
        "DownedEverscream"=>BossTier(graph,NPCID.Everscream),
        "DownedSantaNK1"=>BossTier(graph,NPCID.SantaNK1),
        "DownedIceQueen"=>BossTier(graph,NPCID.IceQueen),
        "DownedClown"=>AllGates(EventTier(graph,"BloodMoon"),BossTier(graph,NPCID.WallofFlesh)),
        "DownedGoblinArmy"=>EventTier(graph,"GoblinArmy"),
        "DownedPirates"=>AllGates(EventTier(graph,"PirateInvasion"),BossTier(graph,NPCID.WallofFlesh)),
        "DownedMartians"=>AllGates(EventTier(graph,"MartianMadness"),BossTier(graph,NPCID.Golem)),
        "DownedFrostLegion"=>AllGates(EventTier(graph,"FrostLegion"),BossTier(graph,NPCID.WallofFlesh)),
        "DownedSolarPillar"=>BossTier(graph,NPCID.LunarTowerSolar),
        "DownedVortexPillar"=>BossTier(graph,NPCID.LunarTowerVortex),
        "DownedNebulaPillar"=>BossTier(graph,NPCID.LunarTowerNebula),
        "DownedStardustPillar"=>BossTier(graph,NPCID.LunarTowerStardust),
        "DownedOldOnesArmyT1" or "DownedOldOnesArmyAny"=>OldOnesArmyCompletionGate(graph, 1),
        "DownedOldOnesArmyT2"=>OldOnesArmyCompletionGate(graph, 2),
        "DownedOldOnesArmyT3"=>OldOnesArmyCompletionGate(graph, 3),
        _=>-1
    };
    internal static int ConditionTier(ProgressionGraph graph,Condition condition)
    {
        if (condition == null) return -1;
        if (TryNativeNotZenith(condition,out var notZenith))
            return AdaptiveProgressionConditions.Earliest(graph,new[]{notZenith});
        if(!VanillaConditions.TryGetValue(condition,out string name))
            return AdaptiveProgressionConditions.TryRead(condition?.Predicate, out var formula)
                ? AdaptiveProgressionConditions.Earliest(graph, new[] { formula }) : -1;
        if(name=="Hardmode" || name.StartsWith("Downed")) return Gate(graph,name);
        int? eventGate=name switch
        {
            "BloodMoon"=>EventTier(graph,"BloodMoon"),
            "Eclipse"=>AllGates(EventTier(graph,"Eclipse"),Gate(graph,"Hardmode")),
            "EclipseOrBloodMoon"=>AnyGate(EventTier(graph,"BloodMoon"),AllGates(EventTier(graph,"Eclipse"),Gate(graph,"Hardmode"))),
            "BloodMoonOrHardmode"=>AnyGate(EventTier(graph,"BloodMoon"),Gate(graph,"Hardmode")),
            "InOldOneArmy"=>AllGates(EventTier(graph,"OldOnesArmy"),Gate(graph,"DownedEowOrBoc")),
            "InTowerSolar" or "InTowerVortex" or "InTowerNebula" or "InTowerStardust"=>Gate(graph,"DownedCultist"),
            "InHallow"=>Gate(graph,"Hardmode"),
            _=>null
        };
        if(eventGate.HasValue) return eventGate.Value;
        if (NativeEnvironmentConditions.Names.Contains(name)) return 0;
        return AdaptiveProgressionConditions.TryRead(condition.Predicate,out var remaining)
            ? AdaptiveProgressionConditions.Earliest(graph,new[]{remaining}) : -1;
    }
    internal static int NaturalStationTier(ProgressionGraph graph,int tile) => tile switch
    {
        TileID.DemonAltar or TileID.Hellforge=>0,
        TileID.AlchemyTable=>BossTier(graph,NPCID.SkeletronHead),
        _=>-1
    };
    internal static int ConditionsTier(ProgressionGraph graph,IEnumerable<Condition> conditions,int minimum)
    {
        if (minimum < 0) return -1;
        int tier = minimum;
        var predicates = new List<AdaptivePredicate.Formula>();
        foreach (var condition in conditions)
        {
            if (AdaptiveProgressionConditions.TryRead(condition?.Predicate,out var formula)
                && AdaptiveProgressionConditions.TryEarliest(graph,new[]{formula},0,out _))
            { predicates.Add(formula); continue; }
            int gate = ConditionTier(graph,condition);
            if (gate < 0) return -1;
            tier = Math.Max(tier,gate);
        }
        return AdaptiveProgressionConditions.Earliest(graph,predicates,tier);
    }
    internal static void Anchor(ProgressionGraph graph,Dictionary<ItemKey,int> anchors)
    {
        void Add(int type,int tier,string reason = "基础资源 / 生成来源")
        {
            if(type<=0 || tier<0) return;
            graph.AddSourceHint(type, reason);
            var key=ItemKey.Crafted(type);
            if(!anchors.TryGetValue(key,out int old) || tier<old) { anchors[key]=tier; graph.SourceReason(type, reason); }
        }
        foreach(int type in new[]{ItemID.Wood,ItemID.BorealWood,ItemID.PalmWood,ItemID.RichMahogany,ItemID.Ebonwood,ItemID.Shadewood,
            ItemID.DirtBlock,ItemID.StoneBlock,ItemID.SandBlock,ItemID.ClayBlock,ItemID.MudBlock,ItemID.SnowBlock,ItemID.IceBlock,
            ItemID.CopperOre,ItemID.TinOre,ItemID.IronOre,ItemID.LeadOre,ItemID.SilverOre,ItemID.TungstenOre,ItemID.GoldOre,ItemID.PlatinumOre,
            ItemID.CopperPickaxe,ItemID.TinPickaxe,ItemID.Obsidian,ItemID.Hellforge,ItemID.Amber,ItemID.Amethyst,
            ItemID.Topaz,ItemID.Sapphire,ItemID.Emerald,ItemID.Ruby,ItemID.Diamond}) Add(type,0);
        ProgressionWorldSources.Seed(graph,Add);
        ProgressionCalamitySources.Seed(graph,Add);
        int hardmode=Gate(graph,"Hardmode");
        foreach(int type in new[]{ItemID.Pearlwood,ItemID.CrystalShard}) Add(type,hardmode,"困难模式世界变化");
        Add(ItemID.LifeFruit,Gate(graph,"DownedMechBossAny"),"击败任意机械 Boss 后，地下丛林自然生长");
        // Read declared drop rules; never execute CanDrop or temporarily mutate world flags.
        var spawnGates = new Dictionary<int,int>();
        foreach(int npc in new[]{NPCID.BlueSlime,NPCID.GreenSlime,NPCID.Zombie,NPCID.DemonEye,NPCID.Skeleton,NPCID.Piranha,
            NPCID.Hornet,NPCID.JungleBat,NPCID.Demon,NPCID.Harpy,NPCID.Vulture,NPCID.Antlion,
            NPCID.Worm,NPCID.Goldfish,NPCID.Bunny,NPCID.Firefly,NPCID.Shark,NPCID.Pinky,NPCID.Crab,
            NPCID.SeaSnail,NPCID.Squid,NPCID.ManEater,NPCID.JungleSlime,NPCID.SpikedJungleSlime,
            NPCID.SpikedIceSlime,NPCID.UndeadMiner,NPCID.Tim,NPCID.FireImp,NPCID.BoneSerpentHead,
            NPCID.VoodooDemon,NPCID.Hellbat,NPCID.CaveBat,NPCID.IceBat,NPCID.SnowFlinx,
            NPCID.GiantWalkingAntlion,NPCID.GiantFlyingAntlion,NPCID.TombCrawlerHead,NPCID.GraniteGolem,
            NPCID.GraniteFlyer,NPCID.GreekSkeleton,NPCID.Nymph,NPCID.Salamander,
            NPCID.GiantShelly,NPCID.Crawdad,NPCID.GoblinScout,NPCID.EaterofSouls,NPCID.DevourerHead,
            NPCID.Crimera,NPCID.FaceMonster,NPCID.BloodCrawler,NPCID.WallCreeper,
            NPCID.BlueJellyfish,NPCID.PinkJellyfish,NPCID.ZombieRaincoat,NPCID.UmbrellaSlime}) spawnGates[npc] = 0;
        foreach(int npc in new[]{NPCID.AngryBones,NPCID.DarkCaster,NPCID.CursedSkull})
            spawnGates[npc] = BossTier(graph,NPCID.SkeletronHead);
        foreach(int npc in new[]{NPCID.Pixie,NPCID.Unicorn,NPCID.ChaosElemental,NPCID.WyvernHead,NPCID.GiantBat,
            NPCID.ArmoredSkeleton,NPCID.SkeletonArcher,NPCID.Wraith,NPCID.IchorSticker,NPCID.Clinger,
            NPCID.AnglerFish,NPCID.Arapaima,NPCID.IceElemental,NPCID.Derpling,NPCID.MossHornet,NPCID.Medusa,
            NPCID.Mimic,NPCID.IceMimic,NPCID.BigMimicCorruption,NPCID.BigMimicCrimson,NPCID.BigMimicHallow,
            NPCID.Werewolf,NPCID.IceGolem,NPCID.SandElemental,NPCID.Mummy,NPCID.LightMummy,NPCID.DarkMummy,
            NPCID.Moth,NPCID.RainbowSlime,NPCID.EnchantedSword,NPCID.CursedHammer,NPCID.CrimsonAxe,
            NPCID.RuneWizard,NPCID.Crimslime,NPCID.BlackRecluse,NPCID.GreenJellyfish,
            NPCID.AngryTrapper,NPCID.GiantTortoise,NPCID.IceTortoise,NPCID.SeekerHead,NPCID.PigronCorruption,
            NPCID.PigronHallow,NPCID.PigronCrimson,NPCID.AngryNimbus}) spawnGates[npc] = hardmode;
        foreach(int npc in new[]{NPCID.RedDevil,NPCID.Lavabat}) spawnGates[npc] = Gate(graph,"DownedMechBossAny");
        foreach(int npc in new[]{NPCID.DungeonSpirit,NPCID.BlueArmoredBones,NPCID.RustyArmoredBonesAxe,NPCID.HellArmoredBones,
            NPCID.Necromancer,NPCID.RaggedCaster,NPCID.DiabolistRed,NPCID.Paladin}) spawnGates[npc] = BossTier(graph,NPCID.Plantera);
        foreach(var spawn in spawnGates)
            foreach(var drop in GameDataIndex.GetNpcDropRecords(spawn.Key))
            {
                int gate = DropTier(graph,drop,spawn.Value);
                int tier = gate < 0 || spawn.Value < 0 ? -1 : Math.Max(spawn.Value,gate);
                Add(drop.ItemType,tier,"世界 / 小怪掉落：" + Lang.GetNPCNameValue(spawn.Key));
            }
        foreach(var shop in NPCShopDatabase.AllShops.OfType<NPCShop>())
        {
            int arrival=shop.NpcType switch
            {
                NPCID.Merchant or NPCID.Guide or NPCID.Nurse or NPCID.ArmsDealer or NPCID.Demolitionist or NPCID.Stylist or NPCID.Painter
                    or NPCID.DyeTrader or NPCID.Golfer or NPCID.BestiaryGirl or NPCID.PartyGirl or NPCID.TravellingMerchant=>0,
                NPCID.GoblinTinkerer=>EventTier(graph,"GoblinArmy"),
                NPCID.DD2Bartender=>BossTier(graph,NPCID.EaterofWorldsHead,NPCID.BrainofCthulhu),
                NPCID.WitchDoctor=>BossTier(graph,NPCID.QueenBee),
                NPCID.Dryad=>BossTier(graph,NPCID.EyeofCthulhu,NPCID.EaterofWorldsHead,NPCID.BrainofCthulhu,NPCID.SkeletronHead),
                NPCID.Clothier or NPCID.Mechanic=>BossTier(graph,NPCID.SkeletronHead),
                NPCID.Wizard or NPCID.Truffle or NPCID.TaxCollector=>hardmode,
                NPCID.Pirate=>EventTier(graph,"PirateInvasion"),
                NPCID.Steampunker=>Gate(graph,"DownedMechBossAny"),
                NPCID.Cyborg=>BossTier(graph,NPCID.Plantera),
                _=>-1
            };
            if(arrival<0) continue;
            foreach(var entry in shop.Entries)
            {
                if(entry.Disabled) continue;
                int tier=ConditionsTier(graph,entry.Conditions,arrival);
                if(tier>=0) Add(entry.Item.type,tier,"商店：" + Lang.GetNPCNameValue(shop.NpcType) + "（含已知到达及商品条件）");
            }
        }
    }
    internal static int DropTier(ProgressionGraph graph,DropRecord drop,int minimum = 0)
    {
        if (minimum < 0) return -1;
        int tier=minimum;
        var predicates = new List<AdaptivePredicate.Formula>();
        foreach(var condition in drop.Conditions)
        {
            if (condition == null) return -1;
            int gate = DropConditionTier(graph,condition.GetType(),drop.NpcType);
            bool parsed = AdaptiveProgressionConditions.TryRead(condition,drop.NpcType,out var formula)
                && AdaptiveProgressionConditions.TryEarliest(graph,new[]{formula},0,out _);
            if (!parsed) parsed = TryNativeDifficulty(condition.GetType(),out formula);
            if(parsed)
                predicates.Add(formula);
            else if(gate < 0) return -1;
            // Keep independent declared gates as well: notably the mechanical
            // spawner report dummy is true although its actual drop is hardmode.
            if(gate >= 0) tier = Math.Max(tier,gate);
        }
        return AdaptiveProgressionConditions.Earliest(graph, predicates, tier);
    }

    private static bool TryNativeDifficulty(Type type,out AdaptivePredicate.Formula formula)
    {
        formula = null;
        if (type.Assembly != typeof(Terraria.GameContent.ItemDropRules.Conditions).Assembly) return false;
        bool expert = type.Name is "IsExpert" or "NotExpert";
        bool master = type.Name is "IsMasterMode" or "NotMasterMode";
        if (!expert && !master) return false;
        bool negated = type.Name is "NotExpert" or "NotMasterMode";
        var method = type.GetMethod(nameof(IItemDropRuleCondition.CanDrop),new[]{typeof(DropAttemptInfo)});
        if (!MethodIl.TryRead(method,out var body,out var all) || body.ExceptionHandlingClauses.Count != 0) return false;
        var code = all.Where(instruction=>instruction.OpCode != OpCodes.Nop).ToArray();
        var getter = typeof(Main).GetProperty(expert ? nameof(Main.expertMode) : nameof(Main.masterMode))?.GetMethod;
        if (code.Length != (negated ? 4 : 2) || code[0].OpCode != OpCodes.Call || !Equals(code[0].Operand,getter)
            || code[^1].OpCode != OpCodes.Ret) return false;
        if (negated && (!MethodIl.TryIntegerConstant(code[1],out int zero) || zero != 0 || code[2].OpCode != OpCodes.Ceq)) return false;
        string proxy = expert ? (negated ? nameof(NotExpertMode) : nameof(ExpertMode))
            : (negated ? nameof(NotMasterMode) : nameof(MasterMode));
        return AdaptiveProgressionConditions.TryRead(typeof(ProgressionSources).GetMethod(proxy,BindingFlags.Static|BindingFlags.NonPublic),out formula);
    }

    private static bool ExpertMode(DropAttemptInfo info) => info.IsExpertMode;
    private static bool NotExpertMode(DropAttemptInfo info) => !info.IsExpertMode;
    private static bool MasterMode(DropAttemptInfo info) => info.IsMasterMode;
    private static bool NotMasterMode(DropAttemptInfo info) => !info.IsMasterMode;

    private static bool TryNativeNotZenith(Condition condition,out AdaptivePredicate.Formula formula)
    {
        formula = null;
        if (!ReferenceEquals(condition,Condition.NotZenithWorld) || condition.Predicate?.GetInvocationList().Length != 1
            || !MethodIl.TryRead(condition.Predicate.Method,out var body,out var all) || body.ExceptionHandlingClauses.Count != 0) return false;
        var code = all.Where(instruction=>instruction.OpCode != OpCodes.Nop).ToArray();
        if (code.Length != 5 || code[0].OpCode != OpCodes.Ldsfld || code[0].Operand is not FieldInfo field
            || field.DeclaringType != typeof(Condition) || field.Name != nameof(Condition.ZenithWorld)
            || code[1].OpCode != OpCodes.Callvirt || !Equals(code[1].Operand,typeof(Condition).GetMethod(nameof(Condition.IsMet)))
            || !MethodIl.TryIntegerConstant(code[2],out int zero) || zero != 0 || code[3].OpCode != OpCodes.Ceq
            || code[4].OpCode != OpCodes.Ret
            || !AdaptiveProgressionConditions.TryRead(Condition.ZenithWorld.Predicate,out var source)) return false;
        formula = new AdaptivePredicate.Formula(new AdaptivePredicate.Node { Kind = AdaptivePredicate.Kind.Not,
            Left = source.root, Canonical = "!(" + source.Canonical + ")" });
        return true;
    }
    private static int DropConditionTier(ProgressionGraph graph,Type type,int npc)
    {
        // Mod predicates are read from their current method bodies. A package
        // version is neither proof that a rule is unchanged nor a reason to reject it.
        if(type.Assembly != typeof(Terraria.GameContent.ItemDropRules.Conditions).Assembly) return -1;
        // MechanicalBossesDummyCondition only labels ReportDroprates; the real
        // MechBossSpawnersDropRule.CanDrop requires hardMode in native 1.4.4.9.
        return type.Name switch
        {
            "IsHardmode" or "SoulOfLight" or "SoulOfNight" or "LivingFlames" or "PirateMap"
                or "JungleKeyCondition" or "CorruptKeyCondition" or "CrimsonKeyCondition" or "FrozenKeyCondition"
                or "HallowKeyCondition" or "DesertKeyCondition" or "YoyosAmarok" or "YoyosHelFire"
                or "RemixSeedHardmode" or "MechanicalBossesDummyCondition" => BossTier(graph,NPCID.WallofFlesh),
            "BeatAnyMechBoss" or "YoyosYelets" => Gate(graph,"DownedMechBossAny"),
            "YoyoCascade" => BossTier(graph,NPCID.SkeletronHead),
            "YoyosKraken" => BossTier(graph,NPCID.Plantera),
            "DownedAllMechBosses" or "MechdusaKill" => Gate(graph,"DownedMechBossAll"),
            "DownedPlantera" => BossTier(graph,NPCID.Plantera),
            "NotExpert" or "IsExpert" or "IsMasterMode" or "NotMasterMode" or "NotRemixSeed" or "RemixSeed" or "NotFromStatue"
                or "IsPreHardmode" or "RemixSeedEasymode"
                or "IsCorruptionAndNotExpert" or "IsCrimsonAndNotExpert" or "LegacyHack_IsABoss"
                or "LegacyHack_IsBossAndNotExpert" or "MissingTwin" or "EmpressOfLightIsGenuinelyEnraged"
                or "DontStarveIsNotUp" or "DontStarveIsUp" or "TenthAnniversaryIsNotUp" or "TenthAnniversaryIsUp"
                or "WindyEnoughForKiteDrops" or "IsChristmas" or "HalloweenGoodieBagDrop" or "HalloweenWeapons"
                or "XmasPresentDrop" or "PumpkinMoonDropGatingChance" or "FrostMoonDropGatingChance"
                or "PumpkinMoonDropGateForTrophies" or "FrostMoonDropGateForTrophies" or "FromCertainWaveAndAbove"
                or "NotUsedDemonHeart"
                => 0,
            _ => -1
        };
    }
    internal readonly record struct MiningRoute(int Item,int Gate,int Pick,string Reason,
        IReadOnlyList<AdaptivePredicate.Formula> Conditions = null);
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
            new(ItemID.Meteorite,BossTier(graph,NPCID.EaterofWorldsHead,NPCID.BrainofCthulhu),50,"邪恶 Boss 后陨石坠落矿脉"),
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
        result.AddRange(ProgressionCalamitySources.MiningRoutes(graph));
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
