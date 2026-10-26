using System;
using System.Collections.Generic;
using System.Reflection;
using System.Linq;
using AE2ForTerraria.Core;
using AE2ForTerraria.Progression;
using Terraria;
using Terraria.GameContent.ItemDropRules;
using Terraria.ID;

namespace AE2ForTerraria.Systems;

internal static class AdaptiveProgressionRegressionChecks
{
    private static bool First = false;
    private static bool Second = false;
    private static int Invocations;
    private static bool Unknown = false;

    private static bool Both() => First && Second;
    private static bool Either() => First || Second;
    private static bool Contradiction() => First && !First;
    private static bool BeforeFirst() => !First;
    private static bool Opaque() { Invocations++; return Unknown; }

    internal static void Run(Action<bool,string> check)
    {
        var graph = new ProgressionGraph(new RecipeIndex());
        graph.Tiers.Add(new(0,"start","Start","fixture"));
        Func<bool> firstCallback = () => First;
        graph.Tiers.Add(new(1,"first","First","fixture") { IsDowned = new Func<bool>(firstCallback.Invoke) });
        graph.Tiers.Add(new(2,"second","Second","fixture") { IsDowned = () => Second });
        int Gate(string method, int minimum = 0)
        {
            var info = typeof(AdaptiveProgressionRegressionChecks).GetMethod(method, BindingFlags.Static | BindingFlags.NonPublic);
            return AdaptiveProgressionConditions.TryRead(info, out var formula)
                ? AdaptiveProgressionConditions.Earliest(graph,new[]{formula},minimum) : -1;
        }
        check(Gate(nameof(Both)) == 2,"new mod conjunction follows both actual BossChecklist flags without a version table");
        check(AdaptiveProgressionConditions.TryRead(graph.Tiers[1].IsDowned,out var wrapped)
            && wrapped.Fields.Count==1,"BossChecklist's CLR delegate forwarding wrapper preserves the original flag identity");
        Func<bool> deep = firstCallback;
        for(int depth=0;depth<10;depth++) deep = new Func<bool>(deep.Invoke);
        check(!AdaptiveProgressionConditions.TryRead(deep,out _),"delegate forwarding chains have a bounded inspection depth");
        check(Gate(nameof(Either)) == 1,"new mod alternative chooses its earliest actual checkpoint");
        check(Gate(nameof(Contradiction)) == -1,"correlated contradictory flags cannot create a fake progression route");
        check(Gate(nameof(BeforeFirst)) == 0 && Gate(nameof(BeforeFirst),1) == -1,
            "before-boss conditions reject a source that only spawns after that boss");
        int before = Invocations;
        check(Gate(nameof(Opaque)) == -1 && Invocations == before,
            "unrecognized side-effecting progression predicates stay unknown and are never executed");
        var unknownCondition = new Condition("Unknown progression", () => Unknown);
        check(ProgressionSources.ConditionTier(graph,unknownCondition) == -1,
            "a new unrecognized field does not silently become a pre-boss condition");
        var actualCondition = new Condition("Actual progression", Both);
        check(ProgressionSources.ConditionTier(graph,actualCondition) == 2,
            "newly registered recipe conditions use the loaded predicate instead of a hardcoded mod name");
        var firstCondition = new Condition("First",()=>First);
        var beforeCondition = new Condition("Before first",BeforeFirst);
        check(ProgressionSources.ConditionsTier(graph,new[]{firstCondition,beforeCondition},0)==-1,
            "mutually exclusive recipe and shop conditions are evaluated jointly");
        check(ProgressionSources.ConditionsTier(graph,new[]{beforeCondition},2)==-1,
            "materials and workstation stages are respected before applying a before-boss condition");
        Func<bool> multicast = Opaque;
        multicast += Both;
        check(!AdaptiveProgressionConditions.TryRead(multicast,out _) && Invocations==before,
            "multicast predicates are not reduced to the final invocation");
        var npc = new NPC { type = NPCID.IceMimic };
        var natural = new SimpleItemDropRuleCondition(Terraria.Localization.LocalizedText.Empty,
            ()=>!npc.SpawnedFromStatue,default,true);
        check(AdaptiveProgressionConditions.TryRead(natural,NPCID.IceMimic,out var naturalFormula)
            && AdaptiveProgressionConditions.Earliest(graph,new[]{naturalFormula})==0,
            "registered natural-NPC capture is read by field path without relying on compiler lambda numbering");
        check(!AdaptiveProgressionConditions.TryRead(natural,NPCID.Zombie,out _),
            "a captured NPC from another source cannot masquerade as this natural drop");
        var forwarding = new ForwardedDrop(info=>!info.IsInSimulation && !npc.SpawnedFromStatue && First);
        check(AdaptiveProgressionConditions.TryRead(forwarding,NPCID.IceMimic,out var forwarded)
            && AdaptiveProgressionConditions.Earliest(graph,new[]{forwarded})==1,
            "a transparent drop wrapper preserves the actual captured predicate and boss gate");
        Func<bool> gate = () => First;
        var nested = new ForwardedDrop(_=>gate());
        check(AdaptiveProgressionConditions.TryRead(nested,NPCID.IceMimic,out var nestedFormula)
            && AdaptiveProgressionConditions.Earliest(graph,new[]{nestedFormula})==1,
            "nested captured Func<bool> adapters preserve a drop's actual checkpoint");
        Func<bool> cycle = null;
        cycle = () => cycle();
        check(!AdaptiveProgressionConditions.TryRead(new ForwardedDrop(_=>cycle()),NPCID.IceMimic,out _),
            "cyclic captured delegate forwarding is bounded without invocation");
        check(!AdaptiveProgressionConditions.TryRead(new MutatingWrapper(),NPCID.IceMimic,out _) && Invocations==before,
            "a drop wrapper with a side effect is neither unwrapped nor executed");
        var drop = new DropRecord(ItemID.StoneBlock,0,0,1,1,1,new[]{new BeforeDrop()});
        check(ProgressionSources.DropTier(graph,drop)==0 && ProgressionSources.DropTier(graph,drop,2)==-1,
            "drop inference checks an upper bound together with the source's earliest available stage");
        CheckNativeGates(check);
        graph.ApplyOrder(new[]{"start","second","first"});
        check(Gate(nameof(BeforeFirst),1)==1 && Gate(nameof(BeforeFirst),2)==-1,
            "rebuilt checkpoint mappings follow an edited boss order");
        check(!First && !Second,"adaptive progression queries preserve the original world-style flags");
    }

    private static void CheckNativeGates(Action<bool,string> check)
    {
        var graph = new ProgressionGraph(new RecipeIndex());
        graph.Tiers.Add(new(0,"start","Start","fixture"));
        var hardmode = new ProgressionTier(1,"hardmode","Hardmode","fixture");
        hardmode.NpcIds.Add(NPCID.WallofFlesh); graph.Tiers.Add(hardmode);
        DropRecord Drop(params IItemDropRuleCondition[] conditions) => new(ItemID.StoneBlock,NPCID.Zombie,0,1,1,1,conditions);
        var preHardmode = Drop(new Conditions.IsPreHardmode());
        check(ProgressionSources.DropTier(graph,preHardmode)==0 && ProgressionSources.DropTier(graph,preHardmode,1)==-1,
            "native pre-hardmode drop conditions keep their upper bound after the source's minimum stage is applied");
        check(ProgressionSources.DropTier(graph,Drop(new Conditions.IsExpert(),new Conditions.NotExpert()))==-1,
            "native expert and non-expert drop requirements cannot coexist");
        check(ProgressionSources.DropTier(graph,Drop(new Conditions.IsMasterMode(),new Conditions.NotExpert()))==-1,
            "master mode and a non-expert requirement cannot create an impossible drop route");
        check(ProgressionSources.DropTier(graph,Drop(new Conditions.MechanicalBossesDummyCondition()))==1,
            "the true mechanical-spawner report dummy still retains its separate hardmode gate");
        bool remix = Main.remixWorld, worthy = Main.getGoodWorld;
        try
        {
            Main.remixWorld=true; Main.getGoodWorld=false;
            check(ProgressionSources.ConditionTier(graph,Condition.ZenithWorld)==-1
                && ProgressionSources.ConditionTier(graph,Condition.NotZenithWorld)==0,
                "Remix without For the Worthy is not silently treated as a Zenith world");
            Main.getGoodWorld=true;
            check(ProgressionSources.ConditionTier(graph,Condition.ZenithWorld)==0
                && ProgressionSources.ConditionTier(graph,Condition.NotZenithWorld)==-1,
                "the audited NotZenith wrapper uses the actual Zenith predicate without invoking it");
            check(ProgressionSources.DropTier(graph,Drop(new Conditions.NotExpert()))==-1,
                "For the Worthy cannot satisfy a non-expert drop requirement");
            Main.remixWorld=false; Main.getGoodWorld=false;
            check(ProgressionSources.ConditionTier(graph,Condition.ZenithWorld)==-1
                && ProgressionSources.ConditionTier(graph,Condition.NotZenithWorld)==0,
                "changing seed flags on the same graph invalidates cached condition answers");
        }
        finally { Main.remixWorld=remix; Main.getGoodWorld=worthy; }
    }

    private sealed class BeforeDrop : IItemDropRuleCondition
    {
        public bool CanDrop(DropAttemptInfo info) => !First;
        public bool CanShowItemDropInUI() => true;
        public string GetConditionDescription() => "fixture";
    }

    private sealed class ForwardedDrop : IItemDropRuleCondition
    {
        private delegate bool DropPredicate(DropAttemptInfo info);
        private readonly DropPredicate predicate;
        internal ForwardedDrop(Func<DropAttemptInfo,bool> value) => predicate = new DropPredicate(value.Invoke);
        public bool CanDrop(DropAttemptInfo info) => predicate(info);
        public bool CanShowItemDropInUI() => true;
        public string GetConditionDescription() => "fixture";
    }

    private sealed class MutatingWrapper : IItemDropRuleCondition
    {
        private readonly Func<DropAttemptInfo,bool> predicate = _ => true;
        public bool CanDrop(DropAttemptInfo info) { Invocations++; return predicate(info); }
        public bool CanShowItemDropInUI() => true;
        public string GetConditionDescription() => "fixture";
    }

    internal static void LogLoadedConditions(ProgressionGraph graph)
    {
        var log = AE2ForTerraria.Instance.Logger;
        foreach (var tier in graph.Tiers.Where(t=>t.Id.Contains("VoidDestroyer") || t.Id.Contains("Leviathan")))
            log.Info("AE2 ADAPT CHECKPOINT: " + tier.Id + "; " + tier.IsDowned?.Method
                + "; " + (AdaptiveProgressionConditions.TryRead(tier.IsDowned,out var formula) ? formula.Canonical : "unknown"));
        var npcs = new List<int>{NPCID.ChaosElemental,NPCID.IceMimic};
        if (Terraria.ModLoader.ModLoader.TryGetMod("CalamityMod",out var calamity))
            foreach (string name in new[]{"LuminousCorvina","DevilFish"})
                if(calamity.TryFind<Terraria.ModLoader.ModNPC>(name,out var npc)) npcs.Add(npc.Type);
        foreach(int npc in npcs)
            foreach(var drop in GameDataIndex.GetNpcDropRecords(npc))
            {
                if (!new int[]{ItemID.RodofDiscord,ItemID.Frostbrand}.Contains(drop.ItemType)
                    && !Lang.GetItemNameValue(drop.ItemType).Contains("深渊细胞") && !Lang.GetItemNameValue(drop.ItemType).Contains("流明晶")) continue;
                foreach (var condition in drop.Conditions)
                    log.Info("AE2 ADAPT DROP: " + npc + "/" + drop.ItemType + "; " + condition.GetType().FullName
                        + "; " + (condition is SimpleItemDropRuleCondition simple ? simple.Predicate.Method.ToString() : "CanDrop")
                        + "; " + (AdaptiveProgressionConditions.TryRead(condition,npc,out var formula)
                            ? formula.Canonical + " => " + AdaptiveProgressionConditions.Earliest(graph,new[]{formula}) : "unknown"));
            }
    }
}
