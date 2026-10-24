// tests/Gordian.Engine.Tests/Architecture/TypeDependencyScanner.cs
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;

namespace Gordian.Engine.Tests.Architecture
{
    /// <summary>
    /// Lists the types each type in an assembly refers to: base types, interfaces, fields,
    /// method signatures, locals and every type, field or method token in the IL of its
    /// methods (which covers lambdas, iterators and async state machines, since those are
    /// nested types of the type that declares them). Used by the layering tests to find
    /// references that point the wrong way between layers.
    /// </summary>
    internal static class TypeDependencyScanner
    {
        private const BindingFlags AllDeclared =
            BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance |
            BindingFlags.Static | BindingFlags.DeclaredOnly;

        private static readonly OpCode[] OneByteOpCodes = new OpCode[0x100];
        private static readonly OpCode[] TwoByteOpCodes = new OpCode[0x100];

        static TypeDependencyScanner()
        {
            foreach (var field in typeof(OpCodes).GetFields(BindingFlags.Public | BindingFlags.Static))
            {
                if (field.GetValue(null) is not OpCode op) continue;
                ushort value = unchecked((ushort)op.Value);
                if (value < 0x100) OneByteOpCodes[value] = op;
                else if ((value & 0xFF00) == 0xFE00) TwoByteOpCodes[value & 0xFF] = op;
            }
        }

        /// <summary>Every (type, referenced type) pair in <paramref name="assembly"/>, nested types reported as their outermost declaring type.</summary>
        public static IEnumerable<(Type From, Type To)> Scan(Assembly assembly)
        {
            var edges = new HashSet<(Type, Type)>();
            foreach (var type in assembly.GetTypes())
            {
                var from = Outermost(type);
                var found = new HashSet<Type>();
                CollectType(type, found);
                foreach (var to in found)
                {
                    var target = Outermost(to);
                    if (target != from) edges.Add((from, target));
                }
            }
            return edges;
        }

        private static Type Outermost(Type type)
        {
            while (type.DeclaringType != null) type = type.DeclaringType;
            return type;
        }

        private static void CollectType(Type type, HashSet<Type> found)
        {
            Add(type.BaseType, found);
            foreach (var i in type.GetInterfaces()) Add(i, found);
            foreach (var f in type.GetFields(AllDeclared)) Add(f.FieldType, found);
            foreach (var m in type.GetMethods(AllDeclared)) CollectMethod(type, m, found);
            foreach (var c in type.GetConstructors(AllDeclared)) CollectMethod(type, c, found);
        }

        private static void CollectMethod(Type owner, MethodBase method, HashSet<Type> found)
        {
            if (method is MethodInfo info) Add(info.ReturnType, found);
            foreach (var p in method.GetParameters()) Add(p.ParameterType, found);

            MethodBody? body;
            try { body = method.GetMethodBody(); }
            catch (Exception) { return; }
            if (body == null) return;
            foreach (var local in body.LocalVariables) Add(local.LocalType, found);

            byte[]? il = body.GetILAsByteArray();
            if (il == null) return;
            Type[] typeArgs = owner.IsGenericType ? owner.GetGenericArguments() : Type.EmptyTypes;
            Type[] methodArgs = method.IsGenericMethod ? method.GetGenericArguments() : Type.EmptyTypes;
            var module = method.Module;
            foreach (int token in MemberTokens(il))
                AddToken(module, token, typeArgs, methodArgs, found);
        }

        /// <summary>
        /// Every method or constructor in <paramref name="assembly"/> whose IL refers to <paramref name="target"/> (calls it,
        /// or takes it as a delegate), as "<c>Type.Method</c>" with nested types (lambdas, state machines) reported under
        /// their outermost declaring type.
        /// </summary>
        public static List<string> FindCallers(Assembly assembly, MethodBase target)
        {
            var callers = new List<string>();
            foreach (var type in assembly.GetTypes())
            {
                var methods = type.GetMethods(AllDeclared).Cast<MethodBase>().Concat(type.GetConstructors(AllDeclared));
                foreach (var method in methods)
                {
                    byte[]? il;
                    try { il = method.GetMethodBody()?.GetILAsByteArray(); }
                    catch (Exception) { continue; }
                    if (il == null) continue;
                    Type[] typeArgs = type.IsGenericType ? type.GetGenericArguments() : Type.EmptyTypes;
                    Type[] methodArgs = method.IsGenericMethod ? method.GetGenericArguments() : Type.EmptyTypes;
                    foreach (int token in MemberTokens(il))
                    {
                        MemberInfo? member;
                        try { member = method.Module.ResolveMember(token, typeArgs, methodArgs); }
                        catch (Exception) { continue; }
                        if (member is MethodBase m && m.MetadataToken == target.MetadataToken && m.Module == target.Module)
                        {
                            callers.Add($"{Outermost(type).FullName}.{method.Name}");
                            break;
                        }
                    }
                }
            }
            return callers;
        }

        /// <summary>The field, method, type and token operands in a method body's IL.</summary>
        private static IEnumerable<int> MemberTokens(byte[] il)
        {
            int pos = 0;
            while (pos < il.Length)
            {
                OpCode op = il[pos] == 0xFE && pos + 1 < il.Length
                    ? TwoByteOpCodes[il[++pos]]
                    : OneByteOpCodes[il[pos]];
                pos++;
                switch (op.OperandType)
                {
                    case OperandType.InlineField:
                    case OperandType.InlineMethod:
                    case OperandType.InlineTok:
                    case OperandType.InlineType:
                        yield return BitConverter.ToInt32(il, pos);
                        pos += 4;
                        break;
                    case OperandType.InlineNone:
                        break;
                    case OperandType.ShortInlineBrTarget:
                    case OperandType.ShortInlineI:
                    case OperandType.ShortInlineVar:
                        pos += 1;
                        break;
                    case OperandType.InlineVar:
                        pos += 2;
                        break;
                    case OperandType.InlineI8:
                    case OperandType.InlineR:
                        pos += 8;
                        break;
                    case OperandType.InlineSwitch:
                        int count = BitConverter.ToInt32(il, pos);
                        pos += 4 + 4 * count;
                        break;
                    default:
                        pos += 4;
                        break;
                }
            }
        }

        private static void AddToken(Module module, int token, Type[] typeArgs, Type[] methodArgs, HashSet<Type> found)
        {
            MemberInfo? member;
            try { member = module.ResolveMember(token, typeArgs, methodArgs); }
            catch (Exception) { return; }
            switch (member)
            {
                case Type t:
                    Add(t, found);
                    break;
                case FieldInfo f:
                    Add(f.DeclaringType, found);
                    Add(f.FieldType, found);
                    break;
                case MethodBase m:
                    Add(m.DeclaringType, found);
                    if (m is MethodInfo mi)
                    {
                        Add(mi.ReturnType, found);
                        if (mi.IsGenericMethod)
                            foreach (var a in mi.GetGenericArguments()) Add(a, found);
                    }
                    break;
            }
        }

        private static void Add(Type? type, HashSet<Type> found)
        {
            if (type == null || type.IsGenericParameter) return;
            if (type.HasElementType)
            {
                Add(type.GetElementType(), found);
                return;
            }
            if (type.IsGenericType)
            {
                foreach (var a in type.GetGenericArguments()) Add(a, found);
                if (!type.IsGenericTypeDefinition) type = type.GetGenericTypeDefinition();
            }
            found.Add(type);
        }
    }
}
