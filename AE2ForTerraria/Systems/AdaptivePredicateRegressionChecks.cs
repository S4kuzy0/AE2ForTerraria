using System;
using System.Reflection;
using System.Reflection.Emit;
using AE2ForTerraria.Progression;

namespace AE2ForTerraria.Systems;

internal static class AdaptivePredicateRegressionChecks
{
    internal static void Run(Action<bool, string> check)
    {
        bool Read(string name, out AdaptivePredicate.Formula formula) => AdaptivePredicate.TryRead(
            typeof(Fixtures).GetMethod(name, BindingFlags.Static | BindingFlags.Public), out formula);
        bool? Assign(FieldInfo field) => field.Name == nameof(Fixtures.First) ? true : field.Name == nameof(Fixtures.Second) ? false : null;
        check(Read(nameof(Fixtures.One), out var one) && one.Fields.Count == 1 && one.Evaluate(Assign) == true,
            "adaptive predicate reads a static flag without evaluating the current world value");
        check(Read(nameof(Fixtures.Both), out var both) && both.Fields.Count == 2 && both.Evaluate(Assign) == false && both.Canonical != one.Canonical,
            "changed requirement from one boss to two bosses is inferred from the loaded boolean body");
        check(Read(nameof(Fixtures.Either), out var either) && either.Evaluate(Assign) == true,
            "adaptive predicate preserves alternative boss requirements");
        check(Read(nameof(Fixtures.Negated), out var negated) && negated.Evaluate(Assign) == false,
            "adaptive predicate preserves a negated upper-bound gate");
        check(Read(nameof(Fixtures.Helper), out var helper) && helper.Evaluate(Assign) == false,
            "adaptive predicate follows pure static boolean getters without invoking them");
        check(both.Evaluate(field => field.Name == nameof(Fixtures.First) ? true : null) == null
            && both.Evaluate(field => field.Name == nameof(Fixtures.First) ? false : null) == false,
            "unmapped progression flags remain unknown unless a known false branch excludes the route");
        check(Read(nameof(Fixtures.Branch), out var branch) && branch.Evaluate(Assign) == false,
            "forward conditional control flow and boolean locals are interpreted without execution");
        check(Read(nameof(Fixtures.Drop), out var drop) && drop.Fields.Count == 2
            && drop.Evaluate(field => field.Name == nameof(Attempt.Simulation) ? false : true) == true,
            "direct boolean fields on the sole drop-attempt argument are symbolic rather than read from a fabricated instance");
        int before = Fixtures.Effects;
        check(!Read(nameof(Fixtures.Write), out _) && !Read(nameof(Fixtures.UnsafeHelper), out _) && Fixtures.Effects == before,
            "a direct or helper side effect is rejected and never executed");
        check(!Read(nameof(Fixtures.Loop), out _) && !Read(nameof(Fixtures.Throw), out _) && !Read(nameof(Fixtures.Catch), out _)
            && !Read(nameof(Fixtures.Recursive), out _),
            "backward jumps, recursive helpers, throws and exception regions are rejected without entering them");
        check(!Read(nameof(Fixtures.UnknownCall), out _) && !Read(nameof(Fixtures.Nested), out _) && !Read(nameof(Fixtures.TwoArguments), out _),
            "unmodelled calls, nested receiver state and ambiguous multiple arguments remain unknown");
        check(!AdaptivePredicate.TryRead(typeof(Captured).GetMethod(nameof(Captured.Read)), out _),
            "captured instance fields cannot be confused with global progression flags");
        check(AdaptivePredicate.TryRead(typeof(Initialized).GetMethod(nameof(Initialized.Read)), out _) && Fixtures.Effects == before,
            "reading static field metadata does not execute its declaring type initializer");

        var original = Recompiled(new Version(1, 0), false, false);
        var updated = Recompiled(new Version(2, 7, 4), true, false);
        var changed = Recompiled(new Version(3, 0), true, true);
        check(AdaptivePredicate.TryRead(original, out var originalFormula) && AdaptivePredicate.TryRead(updated, out var updatedFormula)
            && originalFormula.Canonical == updatedFormula.Canonical,
            "identical behavior survives changed assembly version, field token and an unrelated added member");
        check(AdaptiveMethodShape.TryHash(original, out string originalHash) && AdaptiveMethodShape.TryHash(updated, out string updatedHash)
            && originalHash == updatedHash,
            "method-shape proof ignores metadata-token allocation, version numbers and nonsemantic no-ops");
        check(AdaptiveMethodShape.TryHash(changed, out string changedHash) && changedHash != originalHash,
            "method-shape proof rejects an actual added progression requirement");
        check(AdaptivePredicate.TryRead(changed, out var changedFormula) && changedFormula.Fields.Count == 2,
            "boolean inference follows changed semantics instead of requiring an old binary fingerprint");
        var shortBranch = BranchEncoding(true);
        var longBranch = BranchEncoding(false);
        check(AdaptiveMethodShape.TryHash(shortBranch, out string shortHash) && AdaptiveMethodShape.TryHash(longBranch, out string longHash)
            && shortHash == longHash, "method-shape proof normalizes short and long forward branch encodings");
        check(!AdaptivePredicate.TryRead(Oversized(), out _),
            "oversized predicates are bounded and left unknown without executing their bodies");
        check(MethodIl.TypeIdentity(typeof(bool[])) != MethodIl.TypeIdentity(typeof(bool).MakeArrayType(1)),
            "method identities preserve vector versus non-vector array calling signatures");
        var nestedMethod = typeof(Fixtures).GetMethod(nameof(Fixtures.Nested));
        FieldInfo ApproveNested(System.Collections.Generic.IReadOnlyList<FieldInfo> path) => path.Count == 2
            && path[0].DeclaringType == typeof(NestedState) && path[1].DeclaringType == typeof(Captured) ? path[1] : null;
        check(AdaptivePredicate.TryRead(nestedMethod, ApproveNested, out var approved)
            && approved.Fields.Count == 1 && approved.Evaluate(_ => true) == true && approved.Evaluate(_ => false) == false,
            "explicitly approved field paths stay symbolic and never need a fabricated or live target instance");
        check(!AdaptivePredicate.TryRead(nestedMethod, _ => null, out _) && !AdaptivePredicate.TryRead(nestedMethod, out _),
            "an approved context cannot leak through the cache into rejected contexts or the default reader");
        var oldCapture = RecompiledCapture(17, false);
        var newCapture = RecompiledCapture(42, true);
        FieldInfo ApproveCapture(System.Collections.Generic.IReadOnlyList<FieldInfo> path) => path.Count == 2
            && path[0].FieldType.FullName == "AdaptiveFixture.Npc" && path[1].Name == "SpawnedFromStatue" ? path[1] : null;
        check(AdaptivePredicate.TryRead(oldCapture, ApproveCapture, out var oldCaptureFormula)
            && AdaptivePredicate.TryRead(newCapture, ApproveCapture, out var newCaptureFormula)
            && oldCaptureFormula.Canonical == newCaptureFormula.Canonical && oldCaptureFormula.Evaluate(_ => false) == true,
            "approved captured-NPC boolean paths survive compiler closure numbering and token changes");
    }

    private static MethodInfo BranchEncoding(bool shortForm)
    {
        var assembly = AssemblyBuilder.DefineDynamicAssembly(new AssemblyName("AE2AdaptiveBranchFixture"), AssemblyBuilderAccess.RunAndCollect);
        var type = assembly.DefineDynamicModule("main").DefineType("AdaptiveFixture.Flags", TypeAttributes.Public | TypeAttributes.Abstract | TypeAttributes.Sealed);
        var field = type.DefineField("Flag", typeof(bool), FieldAttributes.Public | FieldAttributes.Static);
        var method = type.DefineMethod("Read", MethodAttributes.Public | MethodAttributes.Static, typeof(bool), Type.EmptyTypes);
        var il = method.GetILGenerator(); var no = il.DefineLabel();
        il.Emit(OpCodes.Ldsfld, field); il.Emit(shortForm ? OpCodes.Brfalse_S : OpCodes.Brfalse, no);
        il.Emit(OpCodes.Ldc_I4_1); il.Emit(OpCodes.Ret); il.MarkLabel(no); il.Emit(OpCodes.Ldc_I4_0); il.Emit(OpCodes.Ret);
        return type.CreateType().GetMethod("Read");
    }

    private static MethodInfo Oversized()
    {
        var assembly = AssemblyBuilder.DefineDynamicAssembly(new AssemblyName("AE2AdaptiveBoundFixture"), AssemblyBuilderAccess.RunAndCollect);
        var type = assembly.DefineDynamicModule("main").DefineType("AdaptiveFixture.Large", TypeAttributes.Public | TypeAttributes.Abstract | TypeAttributes.Sealed);
        var method = type.DefineMethod("Read", MethodAttributes.Public | MethodAttributes.Static, typeof(bool), Type.EmptyTypes);
        var il = method.GetILGenerator();
        for (int i = 0; i < 1024; i++) il.Emit(OpCodes.Nop);
        il.Emit(OpCodes.Ldc_I4_1); il.Emit(OpCodes.Ret);
        return type.CreateType().GetMethod("Read");
    }

    private static MethodInfo RecompiledCapture(int number, bool shifted)
    {
        var assembly = AssemblyBuilder.DefineDynamicAssembly(new AssemblyName("AE2AdaptiveCaptureFixture") { Version = new Version(number, 0) }, AssemblyBuilderAccess.RunAndCollect);
        var module = assembly.DefineDynamicModule("main");
        var npcType = module.DefineType("AdaptiveFixture.Npc", TypeAttributes.Public);
        if (shifted) npcType.DefineField("Unrelated", typeof(int), FieldAttributes.Public);
        npcType.DefineField("SpawnedFromStatue", typeof(bool), FieldAttributes.Public);
        Type npc = npcType.CreateType();
        var closure = module.DefineType("AdaptiveFixture.<>c__DisplayClass" + number + "_0", TypeAttributes.Public);
        var captured = closure.DefineField("npc", npc, FieldAttributes.Public);
        var method = closure.DefineMethod("Read", MethodAttributes.Public, typeof(bool), Type.EmptyTypes);
        var il = method.GetILGenerator();
        il.Emit(OpCodes.Ldarg_0); il.Emit(OpCodes.Ldfld, captured); il.Emit(OpCodes.Ldfld, npc.GetField("SpawnedFromStatue"));
        il.Emit(OpCodes.Ldc_I4_0); il.Emit(OpCodes.Ceq); il.Emit(OpCodes.Ret);
        return closure.CreateType().GetMethod("Read");
    }

    private static MethodInfo Recompiled(Version version, bool shifted, bool additionalGate)
    {
        var assembly = AssemblyBuilder.DefineDynamicAssembly(new AssemblyName("AE2AdaptivePredicateFixture") { Version = version }, AssemblyBuilderAccess.RunAndCollect);
        var type = assembly.DefineDynamicModule("main").DefineType("AdaptiveFixture.Flags", TypeAttributes.Public | TypeAttributes.Abstract | TypeAttributes.Sealed);
        if (shifted) type.DefineField("UnrelatedNewField", typeof(int), FieldAttributes.Public | FieldAttributes.Static);
        var first = type.DefineField("First", typeof(bool), FieldAttributes.Public | FieldAttributes.Static);
        var second = type.DefineField("Second", typeof(bool), FieldAttributes.Public | FieldAttributes.Static);
        var method = type.DefineMethod("CanAcquire", MethodAttributes.Public | MethodAttributes.Static, typeof(bool), Type.EmptyTypes);
        var il = method.GetILGenerator();
        if (shifted) il.Emit(OpCodes.Nop);
        il.Emit(OpCodes.Ldsfld, first);
        if (additionalGate) { il.Emit(OpCodes.Ldsfld, second); il.Emit(OpCodes.And); }
        il.Emit(OpCodes.Ret);
        return type.CreateType().GetMethod("CanAcquire");
    }

    private struct Attempt { public bool Simulation; public Attempt(bool simulation) { Simulation = simulation; } }
    private sealed class Captured { public bool Flag = false; public bool Read() => Flag; }
    private sealed class NestedState { public Captured Child = null; }
    private static class Initialized
    {
        public static bool Flag;
        static Initialized() { Fixtures.Effects++; Flag = true; }
        public static bool Read() => Flag;
    }
    private static class Fixtures
    {
        public static bool First = false;
        public static bool Second = false;
        public static int Effects;
        public static bool One() => First;
        public static bool Both() => First && Second;
        public static bool Either() => First || Second;
        public static bool Negated() => !First;
        private static bool PureGetter => First && Second;
        public static bool Helper() => PureGetter;
        public static bool Branch() { bool result; if (First) result = Second; else result = !Second; return result; }
        public static bool Drop(Attempt attempt) => !attempt.Simulation && First;
        public static bool Write() { Effects++; return First; }
        public static bool UnsafeHelper() => Write();
        public static bool Loop() { while (First) { } return Second; }
        public static bool Recursive() => Recursive();
        public static bool Throw() => throw new InvalidOperationException("Must not execute");
        public static bool Catch() { try { return First; } catch { return Second; } }
        public static bool UnknownCall() => DateTime.UtcNow.Ticks > 0;
        public static bool Nested(NestedState state) => state.Child.Flag;
        public static bool TwoArguments(Attempt first, Attempt second) => first.Simulation && second.Simulation;
    }
}
