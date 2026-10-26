using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.CompilerServices;
using Terraria;
using Terraria.GameContent.ItemDropRules;
using Terraria.ID;

namespace AE2ForTerraria.Progression;

/// <summary>Reads declared Boolean gates without calling another mod or changing world state.</summary>
internal static class AdaptiveProgressionConditions
{
    private sealed class Checkpoints
    {
        internal int Revision;
        internal readonly Dictionary<FieldInfo, int> Fields = new();
        internal readonly Dictionary<(string Formula,int Minimum,int Seeds),(bool Known,int Tier)> Results = new();
    }

    private static ConditionalWeakTable<ProgressionGraph, Checkpoints> _checkpoints = new();
    private static readonly Dictionary<MethodInfo, AdaptivePredicate.Formula> Predicates = new();

    internal static void Reset()
    {
        _checkpoints = new();
        Predicates.Clear();
    }

    internal static bool TryRead(MethodInfo method, out AdaptivePredicate.Formula formula)
    {
        formula = null;
        if (method == null) return false;
        if (!Predicates.TryGetValue(method, out formula))
        {
            AdaptivePredicate.TryRead(method, out formula);
            Predicates[method] = formula;
        }
        return formula != null;
    }

    internal static bool TryRead(Delegate predicate, out AdaptivePredicate.Formula formula)
    {
        formula = null;
        return TryUnwrap(predicate,out var source) && TryRead(source.Method,out formula);
    }

    private static bool TryUnwrap(Delegate predicate,out Delegate source)
    {
        source = predicate;
        for(int depth=0;depth<8;depth++)
        {
            if(source == null || source.GetInvocationList().Length != 1) return false;
            // BossChecklist exposes new Func<bool>(original.Invoke). This
            // exact CLR delegate forwarding wrapper has no user code to run.
            if(source.Target is Delegate inner && IsDropDelegate(source.GetType(),out bool takesInfo)
                && IsDropDelegate(inner.GetType(),out bool innerTakesInfo) && takesInfo == innerTakesInfo
                && source.Method == inner.GetType().GetMethod("Invoke"))
            { source = inner; continue; }
            return true;
        }
        return false;
    }

    internal static bool TryRead(IItemDropRuleCondition condition, int npcType, out AdaptivePredicate.Formula formula)
    {
        formula = null;
        if (condition == null) return false;
        if (condition is SimpleItemDropRuleCondition simple)
            return TryReadDropPredicate(simple.Predicate,npcType,out formula);
        try
        {
            var map = condition.GetType().GetInterfaceMap(typeof(IItemDropRuleCondition));
            int index = Array.FindIndex(map.InterfaceMethods, method => method.Name == nameof(IItemDropRuleCondition.CanDrop));
            if (index < 0) return false;
            if (TryRead(map.TargetMethods[index], out formula)) return true;
            if (TryForwardedDropPredicate(condition,map.TargetMethods[index],out var forwarded))
                return TryReadDropPredicate(forwarded,npcType,out formula);
            if (npcType != 0 && AdaptivePredicate.TryRead(map.TargetMethods[index],path=>NaturalDropField(path,null,npcType),out formula)) return true;
            // This audited wrapper only excludes statue-origin NPCs. Read its
            // actual Active predicate; changes to the wrapper invalidate only this route.
            if (condition.GetType().FullName == "CalamityEntropy.Content.Events.VoidDestroyerAftermath.PostVoidDestroyerCondition"
                && AdaptiveMethodShape.TryHash(map.TargetMethods[index],out string shape)
                && shape == "97BDEC9C0705784EA8738492BA34E645CBF728322DCD239C820E8291945BC4C8")
                return TryRead(condition.GetType().Assembly.GetType("CalamityEntropy.Content.Events.VoidDestroyerAftermath.VDAftermath")
                    ?.GetProperty("Active",BindingFlags.Static|BindingFlags.Public|BindingFlags.NonPublic)?.GetMethod,out formula);
            return false;
        }
        catch (Exception) { return false; }
    }

    private static bool TryReadDropPredicate(Delegate source,int npcType,out AdaptivePredicate.Formula formula)
    {
        formula = null;
        for(int depth=0;depth<8;depth++)
        {
            if (!TryUnwrap(source,out var predicate)) return false;
            if (TryRead(predicate.Method,out formula)) return true;
            if (AdaptivePredicate.TryRead(predicate.Method,path=>NaturalDropField(path,predicate.Target,npcType),out formula)) return true;
            // Mods may adapt Func<bool> to a drop-info delegate through another
            // capture. Follow only an exact field/Invoke forwarding body.
            if (!TryForwardedDropPredicate(predicate.Target,predicate.Method,out source)) return false;
        }
        return false;
    }

    private static bool TryForwardedDropPredicate(object target,MethodInfo method,out Delegate predicate)
    {
        predicate = null;
        if (!MethodIl.TryRead(method,out var body,out var all) || body.ExceptionHandlingClauses.Count != 0) return false;
        var code = all.Where(instruction=>instruction.OpCode != OpCodes.Nop).ToArray();
        if (code.Length is not (4 or 5) || !MethodIl.TryVariable(code[0],out string receiver) || receiver != "ldarg:0"
            || code[1].OpCode != OpCodes.Ldfld || code[1].Operand is not FieldInfo field || field.IsStatic
            || !field.DeclaringType.IsInstanceOfType(target)) return false;
        if (!IsDropDelegate(field.FieldType,out bool takesInfo)) return false;
        if (takesInfo && (code.Length != 5 || !MethodIl.TryVariable(code[2],out string argument) || argument != "ldarg:1")) return false;
        if (!takesInfo && code.Length != 4) return false;
        if (code[^2].OpCode != OpCodes.Callvirt || !Equals(code[^2].Operand,field.FieldType.GetMethod("Invoke"))
            || code[^1].OpCode != OpCodes.Ret) return false;
        predicate = field.GetValue(target) as Delegate;
        return predicate != null;
    }

    private static bool IsDropDelegate(Type type,out bool takesInfo)
    {
        takesInfo = false;
        if(type.BaseType != typeof(MulticastDelegate)) return false;
        var invoke = type.GetMethod("Invoke");
        if(invoke?.ReturnType != typeof(bool)) return false;
        var parameters = invoke.GetParameters();
        takesInfo = parameters.Length == 1 && parameters[0].ParameterType == typeof(DropAttemptInfo);
        return parameters.Length == 0 || takesInfo;
    }

    private static FieldInfo NaturalDropField(IReadOnlyList<FieldInfo> path, object target, int npcType)
    {
        if (npcType == 0 || path.Count != 2 || path[1].DeclaringType != typeof(NPC)
            || path[1].Name != nameof(NPC.SpawnedFromStatue)) return null;
        if (path[0].DeclaringType == typeof(DropAttemptInfo) && path[0].Name == nameof(DropAttemptInfo.npc)) return path[1];
        var capture = path[0];
        if (target != null && capture.DeclaringType.IsInstanceOfType(target) && capture.FieldType == typeof(NPC)
            && capture.DeclaringType.IsDefined(typeof(CompilerGeneratedAttribute),false)
            && capture.GetValue(target) is NPC source && source.type == npcType) return path[1];
        return null;
    }

    private static Dictionary<FieldInfo, int> Fields(ProgressionGraph graph)
    {
        var cache = _checkpoints.GetValue(graph, _ => new Checkpoints { Revision = -1 });
        if (cache.Revision == graph.Revision) return cache.Fields;
        cache.Fields.Clear();
        cache.Results.Clear();
        cache.Revision = graph.Revision;
        foreach (var tier in graph.Tiers)
        {
            if (!TryRead(tier.IsDowned, out var formula) || formula.Fields.Count != 1) continue;
            var field = formula.Fields.Single();
            // A negation or a composite checkpoint does not establish the time
            // of an individual flag. Only an exact positive identity is safe.
            if (formula.Evaluate(_ => false) != false || formula.Evaluate(_ => true) != true) continue;
            if (!cache.Fields.TryGetValue(field, out int old) || tier.Index < old) cache.Fields[field] = tier.Index;
        }
        return cache.Fields;
    }

    internal static int Earliest(ProgressionGraph graph, IEnumerable<AdaptivePredicate.Formula> conditions, int minimum = 0)
        => TryEarliest(graph, conditions, minimum, out int tier) ? tier : -1;

    internal static bool TryEarliest(ProgressionGraph graph, IEnumerable<AdaptivePredicate.Formula> conditions, int minimum, out int tier)
    {
        tier = -1;
        if (minimum < 0) return true;
        var formulas = conditions.ToArray();
        if (formulas.Length == 0) { tier = minimum; return true; }
        var checkpoints = Fields(graph);
        var results = _checkpoints.GetValue(graph, _ => new Checkpoints()).Results;
        int seeds = (Main.remixWorld ? 1 : 0) | (Main.getGoodWorld ? 2 : 0) | (Main.tenthAnniversaryWorld ? 4 : 0)
            | (Main.drunkWorld ? 8 : 0) | (Main.notTheBeesWorld ? 16 : 0) | (Main.dontStarveWorld ? 32 : 0)
            | (Main.noTrapsWorld ? 64 : 0) | (Main.zenithWorld ? 128 : 0);
        var key = (string.Join("\n",formulas.Select(f=>f.Canonical).OrderBy(value=>value,StringComparer.Ordinal)),minimum,seeds);
        if (results.TryGetValue(key,out var cached)) { tier = cached.Tier; return cached.Known; }
        if (results.Count >= 32768) results.Clear();
        var gates = new Dictionary<FieldInfo, int>();
        var constants = new Dictionary<FieldInfo, bool>();
        var options = new List<FieldInfo>();
        foreach (var field in formulas.SelectMany(f => f.Fields).Distinct())
        {
            int gate = NativeGate(graph, field);
            bool hasGate = gate >= 0 || checkpoints.TryGetValue(field, out gate);
            if (hasGate) { gates[field] = gate; continue; }
            if (field.DeclaringType == typeof(DropAttemptInfo) && field.Name == nameof(DropAttemptInfo.IsInSimulation))
                constants[field] = false;
            else if (field.DeclaringType == typeof(NPC) && field.Name == nameof(NPC.SpawnedFromStatue))
                constants[field] = false;
            else if (TryWorldConstant(field,out bool worldValue))
                constants[field] = worldValue;
            else if (IsOptionalEnvironment(field)) options.Add(field);
            else { results[key] = (false,-1); return false; }
        }
        // Correlated alternatives are evaluated together: A and !A must never
        // become a fabricated route. Unknown fields remain unknown, not false.
        if (options.Count > 8) { results[key] = (false,-1); return false; }
        var candidates = gates.Values.Where(stage => stage > minimum && stage < graph.Tiers.Count).Append(minimum).Distinct().OrderBy(stage=>stage);
        foreach (int stage in candidates)
            for (int assignment = 0; assignment < (1 << options.Count); assignment++)
            {
                if (!DifficultyAssignmentPossible(options,assignment)) continue;
                bool? Resolve(FieldInfo field)
                {
                    if (gates.TryGetValue(field, out int gate)) return stage >= gate;
                    if (constants.TryGetValue(field, out bool value)) return value;
                    int index = options.IndexOf(field);
                    return index < 0 ? null : (assignment & (1 << index)) != 0;
                }
                if (formulas.All(f => f.Evaluate(Resolve) == true))
                { tier = stage; results[key] = (true,tier); return true; }
            }
        results[key] = (true,-1);
        return true;
    }

    private static bool DifficultyAssignmentPossible(List<FieldInfo> fields,int assignment)
    {
        bool? expert = null, master = null;
        for(int index=0;index<fields.Count;index++)
        {
            var field = fields[index];
            if (field.DeclaringType != typeof(Main) && field.DeclaringType != typeof(DropAttemptInfo)) continue;
            bool value = (assignment & (1 << index)) != 0;
            if (field.Name is "expertMode" or "IsExpertMode")
            {
                if (expert.HasValue && expert.Value != value) return false;
                expert = value;
            }
            else if (field.Name is "masterMode" or "IsMasterMode")
            {
                if (master.HasValue && master.Value != value) return false;
                master = value;
            }
        }
        return !(expert == false && (master == true || Main.getGoodWorld));
    }

    private static bool IsOptionalEnvironment(FieldInfo field)
    {
        if (field.DeclaringType == typeof(DropAttemptInfo)) return field.Name is "IsMasterMode" or "IsExpertMode";
        if (field.DeclaringType == typeof(Main))
            return field.Name is "dayTime" or "raining" or "expertMode" or "masterMode";
        // Mode flags are alternatives; progression flags from the same mod are
        // resolved from its actual BossChecklist callback instead.
        if (field.DeclaringType?.FullName == "CalamityMod.World.CalamityWorld")
            return field.Name is "revenge" or "death";
        return field.DeclaringType?.FullName == "FargowiltasSouls.Core.Systems.WorldSavingSystem"
            && field.Name is "eternityMode" or "_eternityMode" or "EternityMode" or "<EternityMode>k__BackingField";
    }

    private static bool TryWorldConstant(FieldInfo field,out bool value)
    {
        bool? known = field.DeclaringType != typeof(Main) ? null : field.Name switch
        {
            nameof(Main.remixWorld) => Main.remixWorld,
            nameof(Main.getGoodWorld) => Main.getGoodWorld,
            nameof(Main.tenthAnniversaryWorld) => Main.tenthAnniversaryWorld,
            nameof(Main.drunkWorld) => Main.drunkWorld,
            nameof(Main.notTheBeesWorld) => Main.notTheBeesWorld,
            nameof(Main.dontStarveWorld) => Main.dontStarveWorld,
            nameof(Main.noTrapsWorld) => Main.noTrapsWorld,
            nameof(Main.zenithWorld) => Main.zenithWorld,
            _ => null
        };
        value = known ?? false;
        return known.HasValue;
    }

    private static int NativeGate(ProgressionGraph graph, FieldInfo field)
    {
        if (field.DeclaringType == typeof(Main) && field.Name == nameof(Main.hardMode))
            return ProgressionSources.BossTier(graph, NPCID.WallofFlesh);
        if (field.DeclaringType != typeof(NPC)) return -1;
        string name = field.Name switch
        {
            "downedSlimeKing" => "DownedKingSlime", "downedBoss1" => "DownedEyeOfCthulhu",
            "downedBoss2" => "DownedEowOrBoc", "downedBoss3" => "DownedSkeletron",
            "downedQueenBee" => "DownedQueenBee", "downedDeerclops" => "DownedDeerclops",
            "downedQueenSlime" => "DownedQueenSlime", "downedMechBossAny" => "DownedMechBossAny",
            "downedMechBoss1" => "DownedDestroyer", "downedMechBoss2" => "DownedTwins", "downedMechBoss3" => "DownedSkeletronPrime",
            "downedPlantBoss" => "DownedPlantera", "downedGolemBoss" => "DownedGolem", "downedFishron" => "DownedDukeFishron",
            "downedEmpressOfLight" => "DownedEmpressOfLight", "downedAncientCultist" => "DownedCultist", "downedMoonlord" => "DownedMoonLord",
            "downedGoblins" => "DownedGoblinArmy", "downedPirates" => "DownedPirates", "downedMartians" => "DownedMartians",
            "downedClown" => "DownedClown", "downedFrost" => "DownedFrostLegion",
            _ => null
        };
        return name == null ? -1 : ProgressionSources.Gate(graph, name);
    }
}
