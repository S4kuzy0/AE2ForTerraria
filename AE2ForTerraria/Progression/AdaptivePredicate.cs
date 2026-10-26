using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using System.Text;

namespace AE2ForTerraria.Progression;

/// <summary>
/// Reads a small, side-effect-free boolean language from IL. It never invokes
/// the inspected methods, reads static values, or runs a type initializer.
/// Unsupported code is unknown rather than assumed to be available early.
/// </summary>
internal static class AdaptivePredicate
{
    private const int MaxInstructions = 512;
    private const int MaxSteps = 4096;
    private const int MaxDepth = 8;
    private const int MaxNodes = 1024;
    private static readonly ConditionalWeakTable<MethodInfo, ReadResult> Cache = new();

    internal static bool TryRead(MethodInfo method, out Formula formula)
    {
        formula = null;
        if (method == null) return false;
        var result = Cache.GetValue(method, Read);
        formula = result.Formula;
        return formula != null;
    }

    internal static bool TryRead(MethodInfo method, Func<IReadOnlyList<FieldInfo>, FieldInfo> mapInstancePath, out Formula formula)
    {
        // Approval can depend on the registered delegate target and the NPC
        // supplying the drop record. Never cache it by MethodInfo alone.
        formula = null;
        if (method == null || mapInstancePath == null) return false;
        try { formula = new Reader(mapInstancePath).Parse(method, 0); }
        catch (Exception) { return false; }
        return formula != null;
    }

    internal static string FieldIdentity(FieldInfo field) => MethodIl.MemberIdentity(field);

    private static ReadResult Read(MethodInfo method)
    {
        try
        {
            var reader = new Reader();
            return new ReadResult(reader.Parse(method, 0));
        }
        catch (Exception)
        {
            // Reflection over an unavailable/changed method must not prevent
            // the remaining loaded content from being indexed.
            return new ReadResult(null);
        }
    }

    private sealed record ReadResult(Formula Formula);

    internal sealed class Formula
    {
        internal readonly Node root;
        internal string Canonical => root.Canonical;
        internal IReadOnlyList<FieldInfo> Fields { get; }

        internal Formula(Node expression)
        {
            root = expression;
            var fields = new Dictionary<string, FieldInfo>(StringComparer.Ordinal);
            expression.Collect(fields);
            Fields = Array.AsReadOnly(fields.OrderBy(p => p.Key, StringComparer.Ordinal).Select(p => p.Value).ToArray());
        }

        internal bool? Evaluate(Func<FieldInfo, bool?> value) => root.Evaluate(value);
    }

    internal enum Kind { Constant, Field, Not, And, Or, Argument }

    internal sealed class Node
    {
        internal Kind Kind;
        internal bool Constant;
        internal FieldInfo Field;
        internal Type ArgumentType;
        internal bool ExplicitArgument;
        internal FieldInfo[] Path = Array.Empty<FieldInfo>();
        internal Node Left;
        internal Node Right;
        internal string Canonical;

        internal bool? Evaluate(Func<FieldInfo, bool?> value)
        {
            if (Kind == Kind.Constant) return Constant;
            if (Kind == Kind.Field) return value(Field);
            bool? left = Left.Evaluate(value);
            if (Kind == Kind.Not) return left.HasValue ? !left.Value : null;
            if (Kind == Kind.And && left == false) return false;
            if (Kind == Kind.Or && left == true) return true;
            bool? right = Right.Evaluate(value);
            if (Kind == Kind.And)
                return right == false ? false : left == true && right == true ? true : null;
            return right == true ? true : left == false && right == false ? false : null;
        }

        internal void Collect(Dictionary<string, FieldInfo> fields)
        {
            if (Kind == Kind.Field) fields.TryAdd(FieldIdentity(Field), Field);
            Left?.Collect(fields);
            Right?.Collect(fields);
        }
    }

    private sealed class Reader
    {
        private readonly HashSet<MethodInfo> active = new();
        private readonly Dictionary<string, Node> nodes = new(StringComparer.Ordinal);
        private readonly Func<IReadOnlyList<FieldInfo>, FieldInfo> mapInstancePath;
        private int steps;

        internal Reader(Func<IReadOnlyList<FieldInfo>, FieldInfo> mapInstancePath = null) => this.mapInstancePath = mapInstancePath;

        internal Formula Parse(MethodInfo method, int depth)
        {
            if (depth > MaxDepth || method.ReturnType != typeof(bool) || method.ContainsGenericParameters || !active.Add(method))
                return null;
            try
            {
                if (!MethodIl.TryRead(method, out var body, out var instructions) || instructions.Count > MaxInstructions
                    || body.ExceptionHandlingClauses.Count != 0 || instructions.Count == 0)
                    return null;
                foreach (var instruction in instructions)
                {
                    if (!Allowed(instruction.OpCode)) return null;
                    if (instruction.OpCode.FlowControl == FlowControl.Branch || instruction.OpCode.FlowControl == FlowControl.Cond_Branch)
                    {
                        if (instruction.Operand is not int target || target <= instruction.Offset) return null;
                    }
                }
                var offsets = instructions.Select((instruction, index) => (instruction.Offset, index))
                    .ToDictionary(pair => pair.Offset, pair => pair.index);
                var locals = new Node[body.LocalVariables.Count];
                var result = Walk(method, instructions, offsets, new State(0, new List<Node>(), locals, Constant(true)), depth);
                return result == null ? null : new Formula(result);
            }
            finally { active.Remove(method); }
        }

        private Node Walk(MethodInfo method, IReadOnlyList<MethodIl.Instruction> instructions, Dictionary<int, int> offsets, State state, int depth)
        {
            while (state.Index < instructions.Count)
            {
                if (++steps > MaxSteps) return null;
                var instruction = instructions[state.Index++];
                var code = instruction.OpCode;
                if (code == OpCodes.Nop) continue;
                if (code == OpCodes.Ldc_I4_0 || code == OpCodes.Ldc_I4_1)
                    state.Stack.Add(Constant(code == OpCodes.Ldc_I4_1));
                else if (code == OpCodes.Ldc_I4 || code == OpCodes.Ldc_I4_S)
                {
                    int number = Convert.ToInt32(instruction.Operand, CultureInfo.InvariantCulture);
                    if (number is not (0 or 1)) return null;
                    state.Stack.Add(Constant(number == 1));
                }
                else if (code == OpCodes.Ldsfld)
                {
                    if (instruction.Operand is not FieldInfo field || !field.IsStatic || field.FieldType != typeof(bool)) return null;
                    state.Stack.Add(Intern(new Node { Kind = Kind.Field, Field = field, Canonical = "field(" + FieldIdentity(field) + ")" }));
                }
                else if (IsLoadArgument(code))
                {
                    // A caller may explicitly approve a metadata-only instance
                    // path. Without that policy only the sole argument's direct
                    // boolean fields are accepted.
                    int index = MethodIl.VariableIndex(instruction) - (method.IsStatic ? 0 : 1);
                    var parameters = method.GetParameters();
                    bool receiver = !method.IsStatic && index == -1 && mapInstancePath != null && parameters.Length <= 1;
                    if (!receiver && (parameters.Length != 1 || index != 0)) return null;
                    Type type = receiver ? method.DeclaringType : parameters[0].ParameterType;
                    if (type.IsByRef) type = type.GetElementType();
                    state.Stack.Add(new Node { Kind = Kind.Argument, ArgumentType = type, ExplicitArgument = !receiver, Canonical = "argument" });
                }
                else if (code == OpCodes.Ldfld)
                {
                    if (instruction.Operand is not FieldInfo field || field.IsStatic
                        || !Pop(state, out var receiver) || receiver.Kind != Kind.Argument
                        || !field.DeclaringType.IsAssignableFrom(receiver.ArgumentType)) return null;
                    var path = receiver.Path.Append(field).ToArray();
                    if (path.Length > 4) return null;
                    if (field.FieldType == typeof(bool))
                    {
                        FieldInfo mapped = receiver.ExplicitArgument && path.Length == 1 ? field
                            : mapInstancePath?.Invoke(Array.AsReadOnly(path));
                        if (mapped?.FieldType != typeof(bool)) return null;
                        state.Stack.Add(Intern(new Node { Kind = Kind.Field, Field = mapped, Canonical = "field(" + FieldIdentity(mapped) + ")" }));
                    }
                    else
                    {
                        if (mapInstancePath == null) return null;
                        state.Stack.Add(new Node { Kind = Kind.Argument, ArgumentType = field.FieldType, ExplicitArgument = receiver.ExplicitArgument,
                            Path = path, Canonical = "argument-path" });
                    }
                }
                else if (code == OpCodes.Call)
                {
                    // Parameterless static helpers/getters are recursively read,
                    // never called. Helpers with arguments need a richer model.
                    if (instruction.Operand is not MethodInfo helper || !helper.IsStatic || helper.GetParameters().Length != 0)
                        return null;
                    var formula = Parse(helper, depth + 1);
                    if (formula == null) return null;
                    state.Stack.Add(formula.root);
                }
                else if (IsLoadLocal(code))
                {
                    int index = MethodIl.VariableIndex(instruction);
                    if (index < 0 || index >= state.Locals.Length || state.Locals[index] == null) return null;
                    state.Stack.Add(state.Locals[index]);
                }
                else if (IsStoreLocal(code))
                {
                    int index = MethodIl.VariableIndex(instruction);
                    if (index < 0 || index >= state.Locals.Length || !Pop(state, out var value)) return null;
                    state.Locals[index] = value;
                }
                else if (code == OpCodes.Dup)
                {
                    if (state.Stack.Count == 0) return null;
                    state.Stack.Add(state.Stack[^1]);
                }
                else if (code == OpCodes.Pop)
                {
                    if (!Pop(state, out _)) return null;
                }
                else if (code == OpCodes.And || code == OpCodes.Or || code == OpCodes.Xor || code == OpCodes.Ceq)
                {
                    if (!PopBoolean(state, out var right) || !PopBoolean(state, out var left)) return null;
                    Node combined = code == OpCodes.And ? And(left, right) : code == OpCodes.Or ? Or(left, right)
                        : code == OpCodes.Ceq ? Or(And(left, right), And(Not(left), Not(right)))
                        : Or(And(left, Not(right)), And(Not(left), right));
                    state.Stack.Add(combined);
                }
                else if (code == OpCodes.Br || code == OpCodes.Br_S)
                {
                    if (!offsets.TryGetValue((int)instruction.Operand, out int target)) return null;
                    state.Index = target;
                }
                else if (code == OpCodes.Brtrue || code == OpCodes.Brtrue_S || code == OpCodes.Brfalse || code == OpCodes.Brfalse_S)
                {
                    if (!PopBoolean(state, out var test) || !offsets.TryGetValue((int)instruction.Operand, out int target)) return null;
                    if (code == OpCodes.Brfalse || code == OpCodes.Brfalse_S) test = Not(test);
                    var taken = state.Copy(target, And(state.Path, test));
                    var skipped = state.Copy(state.Index, And(state.Path, Not(test)));
                    Node whenTrue = Walk(method, instructions, offsets, taken, depth);
                    Node whenFalse = Walk(method, instructions, offsets, skipped, depth);
                    return whenTrue == null || whenFalse == null ? null : Or(whenTrue, whenFalse);
                }
                else if (code == OpCodes.Ret)
                {
                    if (!PopBoolean(state, out var value) || state.Stack.Count != 0) return null;
                    return And(state.Path, value);
                }
                else return null;
            }
            return null;
        }

        private static bool Allowed(OpCode code) => code == OpCodes.Nop || code == OpCodes.Ldc_I4_0 || code == OpCodes.Ldc_I4_1
            || code == OpCodes.Ldc_I4 || code == OpCodes.Ldc_I4_S || code == OpCodes.Ldsfld || code == OpCodes.Ldfld || IsLoadArgument(code) || code == OpCodes.Call
            || IsLoadLocal(code) || IsStoreLocal(code) || code == OpCodes.Dup || code == OpCodes.Pop
            || code == OpCodes.And || code == OpCodes.Or || code == OpCodes.Xor || code == OpCodes.Ceq
            || code == OpCodes.Br || code == OpCodes.Br_S || code == OpCodes.Brtrue || code == OpCodes.Brtrue_S
            || code == OpCodes.Brfalse || code == OpCodes.Brfalse_S || code == OpCodes.Ret;

        private static bool IsLoadLocal(OpCode code) => code == OpCodes.Ldloc || code == OpCodes.Ldloc_S
            || code == OpCodes.Ldloc_0 || code == OpCodes.Ldloc_1 || code == OpCodes.Ldloc_2 || code == OpCodes.Ldloc_3;
        private static bool IsStoreLocal(OpCode code) => code == OpCodes.Stloc || code == OpCodes.Stloc_S
            || code == OpCodes.Stloc_0 || code == OpCodes.Stloc_1 || code == OpCodes.Stloc_2 || code == OpCodes.Stloc_3;
        private static bool IsLoadArgument(OpCode code) => code == OpCodes.Ldarg || code == OpCodes.Ldarg_S
            || code == OpCodes.Ldarg_0 || code == OpCodes.Ldarg_1 || code == OpCodes.Ldarg_2 || code == OpCodes.Ldarg_3
            || code == OpCodes.Ldarga || code == OpCodes.Ldarga_S;
        private static bool Pop(State state, out Node value)
        {
            value = null;
            if (state.Stack.Count == 0) return false;
            value = state.Stack[^1]; state.Stack.RemoveAt(state.Stack.Count - 1); return true;
        }
        private static bool PopBoolean(State state, out Node value) => Pop(state, out value) && value.Kind != Kind.Argument;

        private Node Constant(bool value) => Intern(new Node { Kind = Kind.Constant, Constant = value, Canonical = value ? "true" : "false" });
        private Node Not(Node value)
        {
            if (value.Kind == Kind.Constant) return Constant(!value.Constant);
            if (value.Kind == Kind.Not) return value.Left;
            return Intern(new Node { Kind = Kind.Not, Left = value, Canonical = "!(" + value.Canonical + ")" });
        }
        private Node And(Node left, Node right) => Binary(Kind.And, left, right);
        private Node Or(Node left, Node right) => Binary(Kind.Or, left, right);
        private Node Binary(Kind kind, Node left, Node right)
        {
            bool conjunction = kind == Kind.And;
            if (left.Kind == Kind.Constant) return left.Constant == conjunction ? right : left;
            if (right.Kind == Kind.Constant) return right.Constant == conjunction ? left : right;
            if (left.Canonical == right.Canonical) return left;
            if (left.Kind == Kind.Not && left.Left.Canonical == right.Canonical || right.Kind == Kind.Not && right.Left.Canonical == left.Canonical)
                return Constant(!conjunction);
            if (StringComparer.Ordinal.Compare(left.Canonical, right.Canonical) > 0) (left, right) = (right, left);
            return Intern(new Node { Kind = kind, Left = left, Right = right,
                Canonical = (conjunction ? "and(" : "or(") + left.Canonical + "," + right.Canonical + ")" });
        }
        private Node Intern(Node node)
        {
            if (nodes.TryGetValue(node.Canonical, out var existing)) return existing;
            if (nodes.Count >= MaxNodes || node.Canonical.Length > 32768) throw new InvalidOperationException("Predicate complexity limit");
            nodes[node.Canonical] = node;
            return node;
        }
        private sealed record State(int InitialIndex, List<Node> Stack, Node[] Locals, Node Path)
        {
            internal int Index = InitialIndex;
            internal State Copy(int index, Node path) => new(index, new List<Node>(Stack), (Node[])Locals.Clone(), path);
        }
    }
}

/// <summary>
/// A per-method compatibility proof for declarations not expressible as a
/// boolean predicate. It excludes assembly versions and metadata tokens, while
/// preserving operands, calls, local types, branches and exception regions.
/// A matching shape only proves this method; callers must check every relevant
/// helper/registration hook and the actual loaded content separately.
/// </summary>
internal static class AdaptiveMethodShape
{
    private static readonly ConditionalWeakTable<MethodInfo, ShapeResult> Cache = new();
    private sealed record ShapeResult(string Canonical, string Hash);

    internal static bool TryHash(MethodInfo method, out string hash)
    {
        hash = null;
        if (method == null) return false;
        hash = Cache.GetValue(method, Read).Hash;
        return hash != null;
    }

    internal static bool TryRead(MethodInfo method, out string shape)
    {
        shape = null;
        if (method == null) return false;
        shape = Cache.GetValue(method, Read).Canonical;
        return shape != null;
    }

    private static ShapeResult Read(MethodInfo method)
    {
        string shape = ReadUncached(method);
        return new ShapeResult(shape, shape == null ? null : Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(shape))));
    }

    private static string ReadUncached(MethodInfo method)
    {
        try
        {
            if (!MethodIl.TryRead(method, out var body, out var instructions) || instructions.Count > 16384) return null;
            var meaningful = instructions.Where(i => i.OpCode != OpCodes.Nop).ToArray();
            var offsets = new Dictionary<int, int>();
            int ordinal = 0;
            foreach (var instruction in instructions)
            {
                offsets[instruction.Offset] = ordinal;
                if (instruction.OpCode != OpCodes.Nop) ordinal++;
            }
            offsets[body.GetILAsByteArray().Length] = ordinal;
            var result = new StringBuilder();
            result.Append("returns:").Append(MethodIl.TypeIdentity(method.ReturnType)).Append(";static:").Append(method.IsStatic);
            foreach (var parameter in method.GetParameters()) result.Append(";arg:").Append(MethodIl.TypeIdentity(parameter.ParameterType));
            result.Append(";init:").Append(body.InitLocals);
            foreach (var local in body.LocalVariables) result.Append(";local:").Append(MethodIl.TypeIdentity(local.LocalType)).Append(':').Append(local.IsPinned);
            foreach (var instruction in meaningful)
            {
                var code = instruction.OpCode;
                result.Append('\n');
                if (MethodIl.TryIntegerConstant(instruction, out int constant)) result.Append("ldc.i4:").Append(constant);
                else if (MethodIl.TryVariable(instruction, out string variable)) result.Append(variable);
                else
                {
                    bool branch = code.OperandType is OperandType.ShortInlineBrTarget or OperandType.InlineBrTarget;
                    result.Append(branch ? code.Name.Replace(".s", "", StringComparison.Ordinal) : code.Name);
                    if (branch) result.Append(':').Append(offsets[(int)instruction.Operand]);
                    else if (code == OpCodes.Switch) result.Append(':').Append(string.Join(',', ((int[])instruction.Operand).Select(target => offsets[target])));
                    else if (instruction.Operand is MemberInfo member) result.Append(':').Append(MethodIl.MemberIdentity(member));
                    else if (instruction.Operand is string literal) result.Append(":string:").Append(Convert.ToBase64String(Encoding.UTF8.GetBytes(literal)));
                    else if (instruction.Operand is float single) result.Append(":f32:").Append(BitConverter.SingleToInt32Bits(single).ToString("X8", CultureInfo.InvariantCulture));
                    else if (instruction.Operand is double number) result.Append(":f64:").Append(BitConverter.DoubleToInt64Bits(number).ToString("X16", CultureInfo.InvariantCulture));
                    else if (instruction.Operand != null) result.Append(':').Append(Convert.ToString(instruction.Operand, CultureInfo.InvariantCulture));
                }
            }
            foreach (var clause in body.ExceptionHandlingClauses)
            {
                result.Append("\neh:").Append(clause.Flags).Append(':').Append(offsets[clause.TryOffset]).Append(':').Append(offsets[clause.TryOffset + clause.TryLength])
                    .Append(':').Append(offsets[clause.HandlerOffset]).Append(':').Append(offsets[clause.HandlerOffset + clause.HandlerLength]);
                if (clause.Flags == ExceptionHandlingClauseOptions.Clause) result.Append(':').Append(MethodIl.TypeIdentity(clause.CatchType));
                if (clause.Flags == ExceptionHandlingClauseOptions.Filter) result.Append(':').Append(offsets[clause.FilterOffset]);
            }
            return result.ToString();
        }
        catch (Exception) { return null; }
    }
}

internal static class MethodIl
{
    internal sealed record Instruction(int Offset, OpCode OpCode, object Operand);
    private static readonly Dictionary<short, OpCode> Codes = typeof(OpCodes).GetFields(BindingFlags.Public | BindingFlags.Static)
        .Where(field => field.FieldType == typeof(OpCode)).Select(field => (OpCode)field.GetValue(null)).ToDictionary(code => code.Value);

    internal static bool TryRead(MethodInfo method, out MethodBody body, out List<Instruction> instructions)
    {
        body = null; instructions = null;
        if (method == null) return false;
        try
        {
            body = method.GetMethodBody();
            byte[] bytes = body?.GetILAsByteArray();
            if (bytes == null || bytes.Length == 0 || bytes.Length > 262144) return false;
            Type[] typeArguments = method.DeclaringType?.IsGenericType == true ? method.DeclaringType.GetGenericArguments() : null;
            Type[] methodArguments = method.IsGenericMethod ? method.GetGenericArguments() : null;
            instructions = new List<Instruction>();
            int cursor = 0;
            while (cursor < bytes.Length)
            {
                int offset = cursor;
                short value = bytes[cursor++];
                if (value == 0xfe) value = unchecked((short)(0xfe00 | bytes[cursor++]));
                if (!Codes.TryGetValue(value, out var code)) return false;
                object operand;
                switch (code.OperandType)
                {
                    case OperandType.InlineNone: operand = null; break;
                    case OperandType.ShortInlineI: operand = (int)unchecked((sbyte)bytes[cursor++]); break;
                    case OperandType.InlineI: operand = BitConverter.ToInt32(bytes, cursor); cursor += 4; break;
                    case OperandType.InlineI8: operand = BitConverter.ToInt64(bytes, cursor); cursor += 8; break;
                    case OperandType.ShortInlineR: operand = BitConverter.ToSingle(bytes, cursor); cursor += 4; break;
                    case OperandType.InlineR: operand = BitConverter.ToDouble(bytes, cursor); cursor += 8; break;
                    case OperandType.ShortInlineVar: operand = (int)bytes[cursor++]; break;
                    case OperandType.InlineVar: operand = (int)BitConverter.ToUInt16(bytes, cursor); cursor += 2; break;
                    case OperandType.ShortInlineBrTarget:
                        int shortDelta = unchecked((sbyte)bytes[cursor++]); operand = cursor + shortDelta; break;
                    case OperandType.InlineBrTarget:
                        int delta = BitConverter.ToInt32(bytes, cursor); cursor += 4; operand = cursor + delta; break;
                    case OperandType.InlineSwitch:
                        int count = BitConverter.ToInt32(bytes, cursor); cursor += 4;
                        if (count < 0 || count > (bytes.Length - cursor) / 4) return false;
                        int next = cursor + count * 4;
                        var targets = new int[count];
                        for (int i = 0; i < count; i++, cursor += 4) targets[i] = next + BitConverter.ToInt32(bytes, cursor);
                        operand = targets; break;
                    case OperandType.InlineField:
                    case OperandType.InlineMethod:
                    case OperandType.InlineType:
                    case OperandType.InlineTok:
                        int token = BitConverter.ToInt32(bytes, cursor); cursor += 4;
                        operand = method.Module.ResolveMember(token, typeArguments, methodArguments); break;
                    case OperandType.InlineString:
                        int textToken = BitConverter.ToInt32(bytes, cursor); cursor += 4;
                        operand = method.Module.ResolveString(textToken); break;
                    default: return false;
                }
                if (cursor > bytes.Length) return false;
                instructions.Add(new Instruction(offset, code, operand));
            }
            return cursor == bytes.Length;
        }
        catch (Exception) { body = null; instructions = null; return false; }
    }

    internal static string TypeIdentity(Type type)
    {
        if (type == null) return "?";
        if (type.IsByRef) return TypeIdentity(type.GetElementType()) + "&";
        if (type.IsPointer) return TypeIdentity(type.GetElementType()) + "*";
        if (type.IsArray) return TypeIdentity(type.GetElementType()) + (type.IsSZArray ? "[]"
            : "[" + (type.GetArrayRank() == 1 ? "*" : new string(',', type.GetArrayRank() - 1)) + "]");
        if (type.IsGenericParameter) return (type.DeclaringMethod == null ? "!" : "!!") + type.GenericParameterPosition;
        string name = type.Assembly.GetName().Name + ":" + (type.IsGenericType ? type.GetGenericTypeDefinition().FullName : type.FullName);
        return type.IsGenericType ? name + "<" + string.Join(',', type.GetGenericArguments().Select(TypeIdentity)) + ">" : name;
    }

    internal static string MemberIdentity(MemberInfo member) => member switch
    {
        Type type => TypeIdentity(type),
        FieldInfo field => TypeIdentity(field.DeclaringType) + "::" + field.Name + ":" + TypeIdentity(field.FieldType),
        MethodBase method => TypeIdentity(method.DeclaringType) + "::" + method.Name
            + (method.IsGenericMethod ? "<" + string.Join(',', method.GetGenericArguments().Select(TypeIdentity)) + ">" : "")
            + "(" + string.Join(',', method.GetParameters().Select(p => TypeIdentity(p.ParameterType))) + ")"
            + (method is MethodInfo info ? ":" + TypeIdentity(info.ReturnType) : ""),
        _ => member?.ToString() ?? "?"
    };

    internal static int VariableIndex(Instruction instruction)
    {
        var code = instruction.OpCode;
        if (code == OpCodes.Ldloc_0 || code == OpCodes.Stloc_0 || code == OpCodes.Ldarg_0) return 0;
        if (code == OpCodes.Ldloc_1 || code == OpCodes.Stloc_1 || code == OpCodes.Ldarg_1) return 1;
        if (code == OpCodes.Ldloc_2 || code == OpCodes.Stloc_2 || code == OpCodes.Ldarg_2) return 2;
        if (code == OpCodes.Ldloc_3 || code == OpCodes.Stloc_3 || code == OpCodes.Ldarg_3) return 3;
        return instruction.Operand is int index ? index : -1;
    }

    internal static bool TryVariable(Instruction instruction, out string variable)
    {
        variable = null;
        string name = instruction.OpCode.Name;
        foreach (string prefix in new[] { "ldloc", "stloc", "ldarg", "starg" })
        {
            if (name != prefix && name != prefix + ".s" && !(name.StartsWith(prefix + ".", StringComparison.Ordinal) && char.IsDigit(name[^1]))) continue;
            variable = prefix + ":" + VariableIndex(instruction); return true;
        }
        return false;
    }

    internal static bool TryIntegerConstant(Instruction instruction, out int value)
    {
        var code = instruction.OpCode;
        value = 0;
        if (code == OpCodes.Ldc_I4 || code == OpCodes.Ldc_I4_S) { value = (int)instruction.Operand; return true; }
        if (code == OpCodes.Ldc_I4_M1) { value = -1; return true; }
        string name = code.Name;
        if (name?.Length == 8 && name.StartsWith("ldc.i4.", StringComparison.Ordinal) && name[^1] is >= '0' and <= '8')
        { value = name[^1] - '0'; return true; }
        return false;
    }
}
