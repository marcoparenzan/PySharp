// Copyright (c) 2026 Marco Parenzan
//
// Licensed under the MIT License. See the LICENSE file in the project
// root for full license information.

using System.Collections;
using System.Numerics;
using System.Reflection;

namespace PySharpLib.Runtime;

// =====================================================================================
//  .NET (CLR) interop — inject host objects into the Python scope and use them idiomatically.
//
//  A host application can expose any .NET object (or Type) to a script via
//  PyEngine.SetVariable("name", obj). Inside Python the object behaves like a normal object:
//  method calls, property/field access, indexing and iteration all work, with automatic
//  marshalling between Python values and .NET types.
//
//  Foreign .NET objects are represented by ClrObject / ClrType wrappers so the interpreter
//  never reflects over its own internal runtime types by accident.
// =====================================================================================

/// <summary>Wraps a foreign .NET instance exposed to Python.</summary>
public sealed class ClrObject
{
    public object Instance { get; }
    public Type Type { get; }

    public ClrObject(object instance)
    {
        Instance = instance;
        Type = instance.GetType();
    }

    public override string ToString() => Instance.ToString() ?? Type.FullName ?? Type.Name;
}

/// <summary>Wraps a .NET <see cref="System.Type"/> for static-member access and construction.</summary>
public sealed class ClrType
{
    public Type Type { get; }
    public ClrType(Type type) => Type = type;
    public override string ToString() => $"<clr class '{Type.FullName}'>";
}

/// <summary>A bound (or static) .NET method group; callable from Python with overload resolution.</summary>
public sealed class ClrMethod
{
    /// <summary>Instance the method is bound to (null for static methods).</summary>
    public object? Target { get; }
    public Type Type { get; }
    public string Name { get; }

    public ClrMethod(object? target, Type type, string name)
    {
        Target = target;
        Type = type;
        Name = name;
    }

    public override string ToString() => $"<clr method '{Type.Name}.{Name}'>";
}

/// <summary>Marshalling between Python values and .NET values.</summary>
public static class ClrMarshal
{
    /// <summary>Convert a .NET value coming from the host into a Python value.</summary>
    public static object ToPython(object? value) => value switch
    {
        null => PyNone.Instance,
        // already a Python-native value: pass through unchanged
        PyNone or bool or BigInteger or double or string
            or PyList or PyDict or PyTuple or PySet or PyFrozenSet or PyBytes or PyByteArray
            or PyInstance or PyClass or PyModule or PyFunction or PyBuiltinFunction or PyBoundMethod
            or ClrObject or ClrType or ClrMethod => value,
        Type t => new ClrType(t),
        char c => c.ToString(),
        sbyte or byte or short or ushort or int or uint or long or ulong
            => (BigInteger)Convert.ToInt64(value),
        float f => (double)f,
        decimal m => (double)m,
        _ => new ClrObject(value),
    };

    /// <summary>Try to convert a Python value into a .NET value assignable to <paramref name="target"/>.</summary>
    public static bool TryToClr(object pyValue, Type target, out object? result)
    {
        result = null;

        // Nullable<T>: unwrap the underlying type.
        var underlying = Nullable.GetUnderlyingType(target);
        if (underlying is not null)
            target = underlying;

        if (target == typeof(object))
        {
            result = Unwrap(pyValue);
            return true;
        }

        switch (pyValue)
        {
            case PyNone:
                if (!target.IsValueType || Nullable.GetUnderlyingType(target) is not null)
                {
                    result = null;
                    return true;
                }
                return false;

            case ClrObject co:
                if (target.IsInstanceOfType(co.Instance))
                {
                    result = co.Instance;
                    return true;
                }
                return false;

            case bool b when target == typeof(bool):
                result = b;
                return true;

            case string s:
                if (target == typeof(string)) { result = s; return true; }
                if (target == typeof(char) && s.Length == 1) { result = s[0]; return true; }
                return false;

            case BigInteger bi:
                return TryConvertNumber(bi, target, out result);

            case double d:
                if (target == typeof(double)) { result = d; return true; }
                if (target == typeof(float)) { result = (float)d; return true; }
                if (target == typeof(decimal)) { result = (decimal)d; return true; }
                return false;

            case PyList list:
                return TryConvertSequence(list.Items, target, out result);

            default:
                if (target.IsInstanceOfType(pyValue))
                {
                    result = pyValue;
                    return true;
                }
                return false;
        }
    }

    /// <summary>Best-effort unwrap of a Python value to a plain .NET value (for `object` targets / boxing).</summary>
    public static object? Unwrap(object pyValue) => pyValue switch
    {
        PyNone => null,
        ClrObject co => co.Instance,
        BigInteger bi => bi >= long.MinValue && bi <= long.MaxValue ? (long)bi : bi,
        _ => pyValue, // bool, double, string pass through as their .NET selves
    };

    private static bool TryConvertNumber(BigInteger value, Type target, out object? result)
    {
        result = null;
        try
        {
            if (target == typeof(BigInteger)) { result = value; return true; }
            if (target == typeof(int)) { result = (int)value; return true; }
            if (target == typeof(long)) { result = (long)value; return true; }
            if (target == typeof(short)) { result = (short)value; return true; }
            if (target == typeof(sbyte)) { result = (sbyte)value; return true; }
            if (target == typeof(byte)) { result = (byte)value; return true; }
            if (target == typeof(ushort)) { result = (ushort)value; return true; }
            if (target == typeof(uint)) { result = (uint)value; return true; }
            if (target == typeof(ulong)) { result = (ulong)value; return true; }
            if (target == typeof(double)) { result = (double)value; return true; }
            if (target == typeof(float)) { result = (float)value; return true; }
            if (target == typeof(decimal)) { result = (decimal)value; return true; }
        }
        catch (OverflowException)
        {
            return false;
        }
        return false;
    }

    private static bool TryConvertSequence(List<object> items, Type target, out object? result)
    {
        result = null;
        Type? elementType = target.IsArray ? target.GetElementType()
            : target.IsGenericType && target.GetGenericTypeDefinition() == typeof(List<>)
                ? target.GetGenericArguments()[0]
                : target.IsGenericType && target.GetGenericTypeDefinition() == typeof(IEnumerable<>)
                    ? target.GetGenericArguments()[0]
                    : null;
        if (elementType is null)
            return false;

        var array = Array.CreateInstance(elementType, items.Count);
        for (int i = 0; i < items.Count; i++)
        {
            if (!TryToClr(items[i], elementType, out var element))
                return false;
            array.SetValue(element, i);
        }

        if (target.IsArray)
        {
            result = array;
            return true;
        }
        // List<T> / IEnumerable<T>
        var list = (IList)Activator.CreateInstance(typeof(List<>).MakeGenericType(elementType))!;
        foreach (var e in array)
            list.Add(e);
        result = list;
        return true;
    }
}

/// <summary>Reflection-based member access and invocation for CLR objects/types.</summary>
public static class ClrBinder
{
    private const BindingFlags InstanceFlags =
        BindingFlags.Public | BindingFlags.Instance | BindingFlags.FlattenHierarchy;
    private const BindingFlags StaticFlags =
        BindingFlags.Public | BindingFlags.Static | BindingFlags.FlattenHierarchy;

    /// <summary>Attribute access on a CLR object (instance) — property, field, or method group.</summary>
    public static bool TryGetMember(object? target, Type type, string name, bool isStatic, out object value)
    {
        var flags = isStatic ? StaticFlags : InstanceFlags;

        var property = type.GetProperty(name, flags);
        if (property is not null && property.CanRead && property.GetIndexParameters().Length == 0)
        {
            value = ClrMarshal.ToPython(property.GetValue(target));
            return true;
        }

        var field = type.GetField(name, flags);
        if (field is not null)
        {
            value = ClrMarshal.ToPython(field.GetValue(target));
            return true;
        }

        if (type.GetMember(name, MemberTypes.Method, flags).Length > 0)
        {
            value = new ClrMethod(target, type, name);
            return true;
        }

        var nested = type.GetNestedType(name, BindingFlags.Public);
        if (nested is not null)
        {
            value = new ClrType(nested);
            return true;
        }

        value = PyNone.Instance;
        return false;
    }

    /// <summary>Set a property or field on a CLR object/type.</summary>
    public static bool TrySetMember(object? target, Type type, string name, object pyValue, bool isStatic)
    {
        var flags = isStatic ? StaticFlags : InstanceFlags;

        var property = type.GetProperty(name, flags);
        if (property is not null && property.CanWrite)
        {
            if (!ClrMarshal.TryToClr(pyValue, property.PropertyType, out var v))
                throw PyErr.TypeError($"cannot assign {PyOps.TypeName(pyValue)} to '{type.Name}.{name}' ({property.PropertyType.Name})");
            property.SetValue(target, v);
            return true;
        }

        var field = type.GetField(name, flags);
        if (field is not null && !field.IsInitOnly && !field.IsLiteral)
        {
            if (!ClrMarshal.TryToClr(pyValue, field.FieldType, out var v))
                throw PyErr.TypeError($"cannot assign {PyOps.TypeName(pyValue)} to '{type.Name}.{name}' ({field.FieldType.Name})");
            field.SetValue(target, v);
            return true;
        }

        return false;
    }

    /// <summary>Invoke a CLR method group, resolving overloads by arity and marshalled argument types.</summary>
    public static object InvokeMethod(ClrMethod method, object[] args)
    {
        var flags = (method.Target is null ? StaticFlags : InstanceFlags);
        var candidates = method.Type.GetMethods(flags).Where(m => m.Name == method.Name).ToArray();
        if (candidates.Length == 0)
            throw PyErr.AttributeError($"'{method.Type.Name}' has no method '{method.Name}'");

        if (TryBind(candidates, args, out var chosen, out var marshalled))
            return ClrMarshal.ToPython(chosen!.Invoke(method.Target, marshalled));

        throw PyErr.TypeError(
            $"no overload of '{method.Type.Name}.{method.Name}' matches {args.Length} argument(s) of the given types");
    }

    /// <summary>Construct a CLR instance (ClrType called like a function).</summary>
    public static object Construct(Type type, object[] args)
    {
        if (type.IsAbstract || type.IsInterface)
            throw PyErr.TypeError($"cannot instantiate '{type.Name}'");
        var candidates = type.GetConstructors(BindingFlags.Public | BindingFlags.Instance);
        if (args.Length == 0 && type.IsValueType && candidates.All(c => c.GetParameters().Length != 0))
            return ClrMarshal.ToPython(Activator.CreateInstance(type));

        if (TryBind(candidates, args, out var chosen, out var marshalled))
            return ClrMarshal.ToPython(((ConstructorInfo)chosen!).Invoke(marshalled));

        throw PyErr.TypeError($"no constructor of '{type.Name}' matches {args.Length} argument(s) of the given types");
    }

    /// <summary>Indexer read: obj[index] via the type's default indexer (get_Item).</summary>
    public static bool TryGetIndex(ClrObject obj, object index, out object value)
    {
        var getter = obj.Type.GetMethods(InstanceFlags)
            .Where(m => m.Name == "get_Item").ToArray();
        if (getter.Length > 0 && TryBind(getter, new[] { index }, out var chosen, out var marshalled))
        {
            value = ClrMarshal.ToPython(chosen!.Invoke(obj.Instance, marshalled));
            return true;
        }
        value = PyNone.Instance;
        return false;
    }

    /// <summary>Indexer write: obj[index] = value via set_Item.</summary>
    public static bool TrySetIndex(ClrObject obj, object index, object pyValue)
    {
        var setter = obj.Type.GetMethods(InstanceFlags)
            .Where(m => m.Name == "set_Item").ToArray();
        return TryBind(setter, new[] { index, pyValue }, out var chosen, out var marshalled)
            && Set(chosen!, obj.Instance, marshalled);

        static bool Set(MethodBase m, object target, object?[] args)
        {
            m.Invoke(target, args);
            return true;
        }
    }

    /// <summary>Iterate a CLR object that implements IEnumerable, marshalling each element.</summary>
    public static IEnumerable<object>? TryEnumerate(ClrObject obj)
    {
        if (obj.Instance is IEnumerable enumerable)
            return Enumerate(enumerable);
        return null;

        static IEnumerable<object> Enumerate(IEnumerable e)
        {
            foreach (var item in e)
                yield return ClrMarshal.ToPython(item);
        }
    }

    /// <summary>Pick the first overload whose parameters accept the marshalled arguments.</summary>
    private static bool TryBind(MethodBase[] candidates, object[] args,
        out MethodBase? chosen, out object?[] marshalled)
    {
        // Prefer exact arity; among those, the first that marshals cleanly.
        foreach (var candidate in candidates.OrderBy(c => c.GetParameters().Length == args.Length ? 0 : 1))
        {
            var ps = candidate.GetParameters();
            if (ps.Length != args.Length)
                continue;
            var converted = new object?[ps.Length];
            bool ok = true;
            for (int i = 0; i < ps.Length; i++)
            {
                if (!ClrMarshal.TryToClr(args[i], ps[i].ParameterType, out converted[i]))
                {
                    ok = false;
                    break;
                }
            }
            if (ok)
            {
                chosen = candidate;
                marshalled = converted;
                return true;
            }
        }
        chosen = null;
        marshalled = Array.Empty<object?>();
        return false;
    }
}
