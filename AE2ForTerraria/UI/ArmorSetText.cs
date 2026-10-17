using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Globalization;
using Mono.Cecil;
using Mono.Cecil.Cil;
using MonoMod.Utils;
using Terraria;
using Terraria.Localization;
using Terraria.ModLoader;

namespace AE2ForTerraria.UI;

/// <summary>Reads declared bonus text; equipment hooks and combat effects are never invoked.</summary>
internal static class ArmorSetText
{
    private static readonly Dictionary<MethodInfo, MethodInfo> Methods = new();
    private static readonly Dictionary<MethodInfo, string[]> LiteralKeys = new();
    private static readonly Dictionary<MethodInfo, List<string>> GuardReasons = new();
    private static readonly Dictionary<ModItem, Action<Player>> ModReaders = new(ReferenceEqualityComparer.Instance);
    private static readonly HashSet<object> Arguments = new(ReferenceEqualityComparer.Instance);
    private static readonly Dictionary<Type, bool> ConstantInitializers = new();
    private static Dictionary<(string Assembly, string Name), Type> _registeredTypes;
    private static Action<Player, int> _vanilla;
    private static Player _probe;
    private static string _description;
    private static bool _warned, _vanillaDisabled, _invalid;
    internal static string UnavailableReason { get; private set; }
    private static bool Capture(Player target, string text)
    {
        if (_invalid || !ReferenceEquals(target, _probe)) return true;
        if (string.IsNullOrWhiteSpace(text)) return false;
        if (!System.Text.RegularExpressions.Regex.IsMatch(text, @"\{\d+(?:[^}]*)\}")) _description = text;
        return true;
    }
    private static object[] NewArguments(int length)
    {
        if (length < 0 || length > 64) { _invalid = true; length = 0; }
        var array = new object[length]; Arguments.Add(array); return array;
    }
    private static bool Scalar(object value) => value == null || value is string or int or long or float or double or bool or byte or short or uint or ulong or decimal;
    private static void StoreArgument(object[] array, int index, object value)
    {
        if (array == null || !Arguments.Contains(array) || index < 0 || index >= array.Length || !Scalar(value)) { _invalid = true; return; }
        array[index] = value;
    }
    private static LocalizedText ReadLocalization(ILocalizedModType owner, string suffix, Func<string> unused)
    {
        if (owner is ModItem item && unused == null)
        {
            string name = item.DisplayName.Key;
            string key = name.EndsWith(".DisplayName", StringComparison.Ordinal) ? name[..^12] + "." + suffix : "";
            if (Language.Exists(key)) return Language.GetText(key);
        }
        _invalid = true; UnavailableReason = "Unresolved localization owner: " + (owner as ModItem)?.DisplayName.Key + "." + suffix; return LocalizedText.Empty;
    }
    private static string ReadLocalizedValue(ILocalizedModType owner, string suffix) => ReadLocalization(owner, suffix, null).Value;
    private static LocalizedText ReadText(string key)
    { if (Language.Exists(key)) return Language.GetText(key); _invalid = true; return LocalizedText.Empty; }
    private static string ReadTextValue(string key) => ReadText(key).Value;
    private static string FormatText(LocalizedText text, object[] values)
    {
        if (_invalid || text == null || values == null || !Arguments.Contains(values) || !values.All(Scalar)) { _invalid = true; return ""; }
        return text.Format(values);
    }
    private static string RoundFloat(float value, string format)
    {
        if (!float.IsFinite(value) || format != null && !System.Text.RegularExpressions.Regex.IsMatch(format, @"\A[NnFfGg][0-9]?\z")) { _invalid = true; return ""; }
        return float.TryParse(value.ToString(format, CultureInfo.CurrentCulture), NumberStyles.Float | NumberStyles.AllowThousands, CultureInfo.CurrentCulture, out float number)
            ? number.ToString(CultureInfo.CurrentCulture) : "";
    }
    private static string RoundDouble(double value, string format)
    {
        if (!double.IsFinite(value) || format != null && !System.Text.RegularExpressions.Regex.IsMatch(format, @"\A[NnFfGg][0-9]?\z")) { _invalid = true; return ""; }
        return float.TryParse(value.ToString(format, CultureInfo.CurrentCulture), NumberStyles.Float | NumberStyles.AllowThousands, CultureInfo.CurrentCulture, out float number)
            ? number.ToString(CultureInfo.CurrentCulture) : "";
    }
    private static string PercentFloat(float value, string format) => RoundFloat(value * 100f, format);
    private static string PercentDouble(double value, string format) => RoundDouble(value * 100d, format);
    private static string Stealth(float value) => PercentFloat(value, "N0");
    private static bool Numeric(TypeReference type) => type.FullName is "System.Int32" or "System.Int64" or "System.Single" or "System.Double" or "System.UInt32" or "System.UInt64";
    private static bool RegisteredConstant(FieldReference field)
    {
        if (!Numeric(field.FieldType)) return false;
        _registeredTypes ??= Terraria.ID.ContentSamples.ItemsByType.Values.Where(item => item.ModItem != null)
            .Select(item => item.ModItem.GetType()).Distinct().ToDictionary(type => (type.Assembly.GetName().Name, type.FullName));
        string assembly = field.DeclaringType.Scope.Name;
        if (assembly.EndsWith(".dll", StringComparison.OrdinalIgnoreCase)) assembly = assembly[..^4];
        if (!_registeredTypes.TryGetValue((assembly, field.DeclaringType.FullName.Replace('/', '+')), out var type)) return false;
        if (ConstantInitializers.TryGetValue(type, out bool safe)) return safe;
        safe = true;
        if (type.TypeInitializer is { } constructor)
        {
            using var initializer = new DynamicMethodDefinition(constructor);
            safe = initializer.Definition.Body.Instructions.All(instruction =>
                instruction.OpCode.Code.ToString().StartsWith("Ldc_", StringComparison.Ordinal)
                || instruction.OpCode.Code is Code.Ldstr or Code.Nop or Code.Ret
                || instruction.OpCode.Code == Code.Stsfld && instruction.Operand is FieldReference target
                    && target.DeclaringType.FullName == field.DeclaringType.FullName
                    && (Numeric(target.FieldType) || target.FieldType.FullName is "System.Boolean" or "System.String"));
        }
        return ConstantInitializers[type] = safe;
    }
    private static MethodInfo Compile(MethodInfo source)
    {
        if (Methods.TryGetValue(source, out var cached)) return cached;
        using var copy = new DynamicMethodDefinition(source);
        var body = copy.Definition.Body; body.InitLocals = true;
        var il = body.GetILProcessor(); var module = copy.Definition.Module;
        var capture = module.ImportReference(typeof(ArmorSetText).GetMethod(nameof(Capture), BindingFlags.Static | BindingFlags.NonPublic));
        MethodReference Helper(string name) => module.ImportReference(typeof(ArmorSetText).GetMethod(name, BindingFlags.Static | BindingFlags.NonPublic));
        LiteralKeys[source] = body.Instructions.Where(instruction => instruction.OpCode.Code == Code.Ldstr)
            .Select(instruction => (string)instruction.Operand).Distinct().ToArray();
        if (body.ExceptionHandlers.Count > 0)
        {
            // Keep queries with custom exception control flow out of the preview.
            body.ExceptionHandlers.Clear(); body.Instructions.Clear();
            il.Append(Instruction.Create(OpCodes.Ret));
            return Methods[source] = copy.Generate();
        }
        var original = body.Instructions.ToArray();
        GuardReasons[source] = new List<string>();
        var offsets = original.ToDictionary(i => i, i => i.Offset);
        var exit = Instruction.Create(OpCodes.Ret); il.Append(exit); offsets[exit] = int.MaxValue;
        void Guard(Instruction instruction)
        {
            GuardReasons[source].Add(instruction.ToString());
            // Keep the instruction identity so every incoming branch also exits.
            // Removing its operand prevents resolving unsupported callbacks or type initializers.
            instruction.OpCode = OpCodes.Leave; instruction.Operand = exit;
        }
        foreach (var instruction in original)
        {
            var code = instruction.OpCode.Code;
            if (instruction.OpCode.OperandType == OperandType.ShortInlineBrTarget)
                instruction.OpCode = (OpCode)typeof(OpCodes).GetField(code.ToString()[..^2]).GetValue(null);
            if (code == Code.Stfld)
            {
                if (instruction.Operand is FieldReference field && field.DeclaringType.FullName == "Terraria.Player" && field.Name == "setBonus")
                {
                    var next = instruction.Next ?? exit;
                    instruction.OpCode = OpCodes.Call; instruction.Operand = capture;
                    var keep = Instruction.Create(OpCodes.Brfalse, next); il.InsertAfter(instruction, keep);
                    il.InsertAfter(keep, Instruction.Create(OpCodes.Leave, exit));
                }
                else { instruction.OpCode = OpCodes.Pop; instruction.Operand = null; il.InsertAfter(instruction, Instruction.Create(OpCodes.Pop)); }
            }
            else if (code is Code.Stsfld or Code.Initobj)
            { instruction.OpCode = OpCodes.Pop; instruction.Operand = null; }
            else if (code.ToString().StartsWith("Stind", StringComparison.Ordinal) || code is Code.Cpobj or Code.Stobj)
            { instruction.OpCode = OpCodes.Pop; instruction.Operand = null; il.InsertAfter(instruction, Instruction.Create(OpCodes.Pop)); }
            else if (code == Code.Newarr)
            {
                if (instruction.Operand is TypeReference element && element.FullName == "System.Object")
                { instruction.OpCode = OpCodes.Call; instruction.Operand = Helper(nameof(NewArguments)); }
                else Guard(instruction);
            }
            else if (code == Code.Stelem_Ref)
            { instruction.OpCode = OpCodes.Call; instruction.Operand = Helper(nameof(StoreArgument)); }
            else if (code.ToString().StartsWith("Stelem", StringComparison.Ordinal))
            {
                instruction.OpCode = OpCodes.Pop; instruction.Operand = null;
                var pop = Instruction.Create(OpCodes.Pop); il.InsertAfter(instruction, pop); il.InsertAfter(pop, Instruction.Create(OpCodes.Pop));
            }
            else if (code is Code.Call or Code.Callvirt or Code.Newobj)
            {
                var called = (MethodReference)instruction.Operand; string owner = called.DeclaringType.FullName;
                string replacement = owner == "Terraria.ModLoader.ILocalizedModTypeExtensions" && called.Name == "GetLocalization" && called.Parameters.Count == 3 ? nameof(ReadLocalization)
                    : owner == "Terraria.ModLoader.ILocalizedModTypeExtensions" && called.Name == "GetLocalizedValue" && called.Parameters.Count == 2 ? nameof(ReadLocalizedValue)
                    : owner == "Terraria.Localization.Language" && called.Name == "GetText" && called.Parameters.Count == 1 ? nameof(ReadText)
                    : owner == "Terraria.Localization.Language" && called.Name == "GetTextValue" && called.Parameters.Count == 1 ? nameof(ReadTextValue)
                    : owner == "Terraria.Localization.LocalizedText" && called.Name == "Format" && called.Parameters.Count == 1 && called.Parameters[0].ParameterType.FullName == "System.Object[]" ? nameof(FormatText)
                    : null;
                if (replacement != null)
                { instruction.OpCode = OpCodes.Call; instruction.Operand = Helper(replacement); continue; }
                if (owner == "CalamityMod.CalamityUtils" && called.DeclaringType.Scope.Name == "CalamityMod"
                    && called.Name == "ToStealth" && called.Parameters.Count == 1 && called.Parameters[0].ParameterType.FullName == "System.Single")
                { instruction.OpCode = OpCodes.Call; instruction.Operand = Helper(nameof(Stealth)); continue; }
                if (owner == "CalamityMod.CalamityUtils" && called.DeclaringType.Scope.Name == "CalamityMod" && called.Parameters.Count == 2
                    && called.Parameters[1].ParameterType.FullName == "System.String" && called.Name is "ToPercent" or "Round")
                {
                    bool single = called.Parameters[0].ParameterType.FullName == "System.Single";
                    if (single || called.Parameters[0].ParameterType.FullName == "System.Double")
                    {
                        replacement = called.Name == "ToPercent" ? single ? nameof(PercentFloat) : nameof(PercentDouble) : single ? nameof(RoundFloat) : nameof(RoundDouble);
                        instruction.OpCode = OpCodes.Call; instruction.Operand = Helper(replacement); continue;
                    }
                }
                bool pure = owner == "Terraria.Localization.Language" && called.Name == "GetTextValue" && called.Parameters.Count == 1
                    || owner == "Terraria.Localization.LocalizedText" && called.Name == "get_Value"
                    || owner == "Terraria.ModLoader.ILocalizedModTypeExtensions" && called.Name == "GetLocalizedValue"
                    || owner == "System.String" && (called.Name is "Concat" or "IsNullOrEmpty") && called.Parameters.All(p => p.ParameterType.FullName == "System.String");
                pure &= called.DeclaringType.Scope.Name is "tModLoader" or "tModLoader.dll" or "System.Runtime" or "System.Private.CoreLib" or "mscorlib" or "netstandard";
                if (!pure) Guard(instruction);
            }
            else if (code is Code.Ldsfld or Code.Ldsflda)
            {
                var field = (FieldReference)instruction.Operand;
                if (field.DeclaringType.Scope.Name != "tModLoader" && field.DeclaringType.Scope.Name != "tModLoader.dll"
                    && field.DeclaringType.FullName != "System.String" && !RegisteredConstant(field)) Guard(instruction);
            }
            else if (code is Code.Calli or Code.Jmp or Code.Cpblk or Code.Initblk or Code.Localloc
                || instruction.Operand is Instruction target && offsets[target] <= offsets[instruction]
                || instruction.Operand is Instruction[] targets && targets.Any(target => offsets[target] <= offsets[instruction])) Guard(instruction);
        }
        return Methods[source] = copy.Generate();
    }
    internal static string Read(Item head, Item body, Item legs, ModItem owner = null)
    {
        _probe ??= (Player)RuntimeHelpers.GetUninitializedObject(typeof(Player));
        _probe.whoAmI = 255; _probe.head = head.headSlot; _probe.body = body.bodySlot; _probe.legs = legs.legSlot;
        _probe.armor ??= new Item[20]; _probe.armor[0] = head; _probe.armor[1] = body; _probe.armor[2] = legs;
        _probe.setBonus = ""; _description = null; _invalid = false; UnavailableReason = null; Arguments.Clear();
        try
        {
            if (owner == null && !_vanillaDisabled)
            {
                _vanilla ??= (Action<Player, int>)Compile(typeof(Player).GetMethod(nameof(Player.UpdateArmorSets))).CreateDelegate(typeof(Action<Player, int>));
                _vanilla(_probe, 255);
            }
            else if (owner != null)
            {
                if (!ModReaders.TryGetValue(owner, out var read))
                    ModReaders[owner] = read = (Action<Player>)Compile(owner.GetType().GetMethod(nameof(ModItem.UpdateArmorSet))).CreateDelegate(typeof(Action<Player>), owner);
                read(_probe);
            }
        }
        catch (Exception ex)
        {
            if (owner == null) _vanillaDisabled = true; else ModReaders[owner] = _ => { };
            if (!_warned) { _warned = true; AE2ForTerraria.Instance?.Logger.Warn("Armor description preview unavailable: " + ex); }
        }
        if (!string.IsNullOrWhiteSpace(_description)) return _description;
        if (owner?.Mod != null)
        {
            string display = owner.DisplayName.Key;
            string prefix = display.EndsWith(".DisplayName", StringComparison.Ordinal) ? display[..^12] : $"Mods.{owner.Mod.Name}.{owner.LocalizationCategory}.{owner.Name}";
            var keys = new List<string> { prefix + ".SetBonus" };
            var source = owner.GetType().GetMethod(nameof(ModItem.UpdateArmorSet));
            if (LiteralKeys.TryGetValue(source, out var literals))
                foreach (string literal in literals.Where(key => key.Contains("SetBonus", StringComparison.OrdinalIgnoreCase)))
                { keys.Add(literal); keys.Add("Mods." + owner.Mod.Name + "." + literal); }
            foreach (string key in keys.Where(key => Language.Exists(key)))
            {
                string text = Language.GetTextValue(key);
                if (!System.Text.RegularExpressions.Regex.IsMatch(text, @"\{\d+(?:[^}]*)\}")) return text;
            }
            if (UnavailableReason == null && GuardReasons.TryGetValue(source, out var reasons))
                UnavailableReason = string.Join("; ", reasons.Take(4));
        }
        return null;
    }
    internal static void Reset()
    {
        Methods.Clear(); LiteralKeys.Clear(); GuardReasons.Clear(); ModReaders.Clear(); Arguments.Clear(); ConstantInitializers.Clear(); _registeredTypes = null;
        _vanilla = null; _probe = null; _description = null; _warned = _vanillaDisabled = _invalid = false;
    }
}
