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

    public Type[]? TypeArguments { get; }

    public ClrMethod(object? target, Type type, string name, Type[]? typeArguments = null)
    {
        Target = target;
        Type = type;
        Name = name;
        TypeArguments = typeArguments;
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
        // any other runtime-native Python type (PySlice, PyRange, PyIterator, generators, ...) is already a Python value
        _ when value.GetType().Namespace is { } ns && ns.StartsWith("PySharpLib", StringComparison.Ordinal) => value,
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

            // A .NET method group passed where a delegate is expected (e.g. Speaker(Speaker.Beep)): the overload
            // whose signature matches the delegate's Invoke, bound to the same instance (or static).
            case ClrMethod method when typeof(Delegate).IsAssignableFrom(target) && target.GetMethod("Invoke") is { } invoke:
            {
                var parameters = invoke.GetParameters().Select(p => p.ParameterType).ToArray();
                var flags = BindingFlags.Public | (method.Target is null ? BindingFlags.Static : BindingFlags.Instance);
                var match = method.Type.GetMethods(flags).FirstOrDefault(m => m.Name == method.Name
                    && m.ReturnType == invoke.ReturnType && m.GetParameters().Select(p => p.ParameterType).SequenceEqual(parameters));
                if (match is null)
                    return false;
                result = method.Target is null ? Delegate.CreateDelegate(target, match) : Delegate.CreateDelegate(target, method.Target, match);
                return true;
            }

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
        // Real, general C# gotcha found live (the same class of bug as a mixed-type switch
        // *expression*, but here via a ternary conditional): `long` has an implicit conversion *to*
        // `BigInteger`, so a bare `cond ? (long)bi : bi` ternary's inferred common type is
        // `BigInteger` for *both* branches — the "fits in long" conversion silently never took
        // effect, and every caller got a raw boxed `BigInteger` back regardless. Confirmed live via
        // a real ASP.NET Core host (`samples/AspNetPySharpHost`) serializing a Python plugin's
        // return value to JSON: a `len(...)` result serialized as `{"isPowerOfTwo":false,...}` —
        // `System.Text.Json`'s reflection-based fallback for an unrecognized struct — instead of a
        // plain JSON number. Each branch is cast to `(object)` explicitly so no such widening
        // happens; the value that gets boxed is each branch's own real type.
        BigInteger bi => bi >= long.MinValue && bi <= long.MaxValue ? (object)(long)bi : bi,
        _ => pyValue, // bool, double, string pass through as their .NET selves
    };

    /// <summary>Recursively converts a Python value (typically a function's return value) into a
    /// plain, JSON-serializable .NET object graph: `dict`/`list`/`tuple`/`set` become `Dictionary
    /// &lt;string, object?&gt;`/`List&lt;object?&gt;` (recursively converting their own contents too),
    /// `None` becomes `null`, `bool`/`str`/`double` pass through, and `int` becomes a `long` when it
    /// fits (falling back to `BigInteger`, matching `Unwrap`'s own choice, for values real .NET
    /// integer types can't hold). Unlike `Unwrap` (a single-level, boxing-only conversion for a value
    /// already known to be a scalar), this is for a host that doesn't know its shape ahead of time —
    /// the common case when a real embedding host (e.g. an ASP.NET Core handler) calls into a Python
    /// plugin function and needs to serialize whatever structured value it returns.</summary>
    public static object? ToPlainObject(object pyValue) => pyValue switch
    {
        PyDict d => d.Entries.ToDictionary(e => Convert.ToString(ToPlainObject(e.Key))!, e => ToPlainObject(e.Value)),
        PyList l => l.Items.Select(ToPlainObject).ToList(),
        PyTuple t => t.Items.Select(ToPlainObject).ToList(),
        PySet s => s.Items.Select(ToPlainObject).ToList(),
        PyFrozenSet s => s.Items.Select(ToPlainObject).ToList(),
        _ => Unwrap(pyValue),
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

    /// <summary>Runs a reflection call; the .NET exception thrown by the callee becomes the matching Python exception.</summary>
    private static object? Clr(Func<object?> call)
    {
        try { return call(); }
        catch (TargetInvocationException e) when (e.InnerException is { } inner) { throw ClrExceptions.ToPython(inner); }
        catch (IndexOutOfRangeException e) { throw ClrExceptions.ToPython(e); } // Array.GetValue/SetValue throw directly
    }

    /// <summary>Attribute access on a CLR object (instance) — property, field, or method group.</summary>
    public static bool TryGetMember(object? target, Type type, string name, bool isStatic, out object value)
    {
        var flags = isStatic ? StaticFlags : InstanceFlags;

        var property = type.GetProperty(name, flags);
        if (property is not null && property.CanRead && property.GetIndexParameters().Length == 0)
        {
            value = ClrMarshal.ToPython(Clr(() => property.GetValue(target)));
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
            Clr(() => { property.SetValue(target, v); return null; });
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
        => ClrMarshal.ToPython(InvokeMethodRaw(method, args));

    /// <summary>Calls the method and returns what it returned, not yet converted to a Python value: the suspendable
    /// evaluator (<c>Interp.RunModuleAsync</c>) awaits a <see cref="Task"/>/<see cref="ValueTask"/> result before converting it.</summary>
    public static object? InvokeMethodRaw(ClrMethod method, object[] args)
    {
        var flags = (method.Target is null ? StaticFlags : InstanceFlags);
        var methods = method.Type.GetMethods(flags).Where(m => m.Name == method.Name);
        var closed = new List<MethodInfo>();
        foreach (var candidate in methods)
        {
            if (method.TypeArguments is null)
            {
                if (!candidate.ContainsGenericParameters) closed.Add(candidate);
            }
            else if (candidate.IsGenericMethodDefinition && candidate.GetGenericArguments().Length == method.TypeArguments.Length)
            {
                try { closed.Add(candidate.MakeGenericMethod(method.TypeArguments)); }
                catch (ArgumentException) { /* This overload's constraints do not accept the explicit types. */ }
            }
        }
        var candidates = closed.ToArray();
        if (candidates.Length == 0)
            throw PyErr.AttributeError($"'{method.Type.Name}' has no method '{method.Name}'");

        if (TryBind(candidates, args, out var chosen, out var marshalled))
            return Clr(() => chosen!.Invoke(method.Target, marshalled));

        throw PyErr.TypeError(
            $"no overload of '{method.Type.Name}.{method.Name}' matches {args.Length} argument(s) of the given types");
    }

    /// <summary>Select explicit generic arguments for a CLR method group using method[Type] or method[TypeA, TypeB].</summary>
    public static ClrMethod SelectGeneric(ClrMethod method, object index)
    {
        var values = index is PyTuple tuple ? tuple.Items.ToArray() : new[] { index };
        if (values.Length == 0 || values.Any(v => v is not ClrType))
            throw PyErr.TypeError("Generic arguments must be CLR types, not numeric values or constructor functions");
        var types = values.Cast<ClrType>().Select(t => t.Type).ToArray();
        if (!method.Type.GetMethods(method.Target is null ? StaticFlags : InstanceFlags)
            .Any(m => m.Name == method.Name && m.IsGenericMethodDefinition && m.GetGenericArguments().Length == types.Length))
            throw PyErr.TypeError("No generic method has this type-argument arity");
        return new ClrMethod(method.Target, method.Type, method.Name, types);
    }
    /// <summary>Close a CLR generic type explicitly; no type inference or numeric conversions.</summary>
    public static ClrType SelectGenericType(ClrType type, object index)
    {
        if(!type.Type.IsGenericTypeDefinition) throw PyErr.TypeError("CLR type is not an open generic definition");
        var values=index is PyTuple tuple?tuple.Items.ToArray():new[]{index};
        if(values.Length!=type.Type.GetGenericArguments().Length || values.Any(v=>v is not ClrType))
            throw PyErr.TypeError("Wrong generic type arity or non-CLR type argument");
        try { return new ClrType(type.Type.MakeGenericType(values.Cast<ClrType>().Select(v=>v.Type).ToArray())); }
        catch(ArgumentException e) { throw PyErr.TypeError("CLR generic constraints failed: "+e.Message); }
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
            return ClrMarshal.ToPython(Clr(() => ((ConstructorInfo)chosen!).Invoke(marshalled)));

        throw PyErr.TypeError($"no constructor of '{type.Name}' matches {args.Length} argument(s) of the given types");
    }

    /// <summary>Indexer arguments: obj[x, y] arrives as a tuple and binds to a two-parameter indexer.</summary>
    private static object[] IndexArguments(object index) => index is PyTuple tuple ? tuple.Items.ToArray() : new[] { index };

    /// <summary>.NET array element positions (one int per dimension), or null if the index does not fit the array.</summary>
    private static int[]? ArrayIndices(Array array, object index)
    {
        var items = IndexArguments(index);
        if (items.Length != array.Rank) return null;
        var indices = new int[items.Length];
        for (int i = 0; i < items.Length; i++)
        {
            if (!ClrMarshal.TryToClr(items[i], typeof(int), out var position)) return null;
            int value = (int)position!;
            indices[i] = value < 0 && array.Rank == 1 ? value + array.Length : value; // a[-1] as in Python
        }
        return indices;
    }

    /// <summary>Indexer read: obj[index] via the type's default indexer (get_Item), or a .NET array element.</summary>
    public static bool TryGetIndex(ClrObject obj, object index, out object value)
    {
        if (obj.Instance is Array array && ArrayIndices(array, index) is { } indices)
        {
            value = ClrMarshal.ToPython(Clr(() => array.GetValue(indices)));
            return true;
        }
        var getter = obj.Type.GetMethods(InstanceFlags)
            .Where(m => m.Name == "get_Item").ToArray();
        if (getter.Length > 0 && TryBind(getter, IndexArguments(index), out var chosen, out var marshalled))
        {
            value = ClrMarshal.ToPython(Clr(() => chosen!.Invoke(obj.Instance, marshalled)));
            return true;
        }
        value = PyNone.Instance;
        return false;
    }

    /// <summary>Indexer write: obj[index] = value via set_Item, or a .NET array element.</summary>
    public static bool TrySetIndex(ClrObject obj, object index, object pyValue)
    {
        if (obj.Instance is Array array && ArrayIndices(array, index) is { } indices)
        {
            if (!ClrMarshal.TryToClr(pyValue, array.GetType().GetElementType()!, out var element))
                throw PyErr.TypeError($"cannot store {PyOps.TypeName(pyValue)} in {array.GetType().Name}");
            Clr(() => { array.SetValue(element, indices); return null; });
            return true;
        }
        var setter = obj.Type.GetMethods(InstanceFlags)
            .Where(m => m.Name == "set_Item").ToArray();
        return TryBind(setter, [.. IndexArguments(index), pyValue], out var chosen, out var marshalled)
            && Set(chosen!, obj.Instance, marshalled);

        static bool Set(MethodBase m, object target, object?[] args)
        {
            Clr(() => m.Invoke(target, args));
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

    /// <summary>Pick the first overload whose parameters accept the marshalled arguments.
    /// Trailing optional parameters (C# <c>= default</c>, <c>= 42</c>, …) may be omitted and take their default value.</summary>
    private static bool TryBind(MethodBase[] candidates, object[] args,
        out MethodBase? chosen, out object?[] marshalled)
    {
        // Prefer exact arity; then the overloads that need the fewest omitted optional parameters.
        foreach (var candidate in candidates.OrderBy(c => c.GetParameters().Length - args.Length))
        {
            if (candidate.ContainsGenericParameters) continue;
            var ps = candidate.GetParameters();
            if (ps.Any(p => p.ParameterType.IsByRefLike || p.ParameterType.IsByRef)) continue;
            if (ps.Length < args.Length || ps.Skip(args.Length).Any(p => !p.IsOptional))
                continue;
            var converted = new object?[ps.Length];
            for (int i = args.Length; i < ps.Length; i++)
                converted[i] = DefaultArgument(ps[i]);
            bool ok = true;
            for (int i = 0; i < args.Length; i++)
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

    /// <summary>Value of an omitted optional parameter: its declared default, or default(T) for <c>= default</c>
    /// on a struct (CancellationToken, TimeSpan…), which reflection reports as missing or null.</summary>
    private static object? DefaultArgument(ParameterInfo parameter)
    {
        object? value = parameter.HasDefaultValue ? parameter.DefaultValue : null;
        if (value is null or DBNull or Missing && parameter.ParameterType.IsValueType && Nullable.GetUnderlyingType(parameter.ParameterType) is null)
            return Activator.CreateInstance(parameter.ParameterType);
        if (value is not null && parameter.ParameterType.IsEnum && value.GetType() != parameter.ParameterType)
            return Enum.ToObject(parameter.ParameterType, value);
        return value is DBNull or Missing ? null : value;
    }
}

/// <summary>Maps an exception thrown by .NET code called from Python to a Python exception, so scripts can catch it.
/// The message is "<.NET type>: <message>" and the original is available as the <c>clr_exception</c> attribute.
/// OperationCanceledException becomes KeyboardInterrupt: cancellation by the host or the user, not caught by
/// <c>except Exception</c>.</summary>
public static class ClrExceptions
{
    public static PyRaise ToPython(Exception error)
    {
        if (error is PyRaise raise) return raise;
        var cls = error switch
        {
            OperationCanceledException => PyErr.KeyboardInterruptClass,
            ArgumentException => PyErr.ValueErrorClass,
            InvalidCastException => PyErr.TypeErrorClass,
            IndexOutOfRangeException => PyErr.IndexErrorClass,
            KeyNotFoundException => PyErr.KeyErrorClass,
            NotImplementedException or NotSupportedException => PyErr.NotImplementedErrorClass,
            FileNotFoundException or DirectoryNotFoundException => PyErr.FileNotFoundErrorClass,
            UnauthorizedAccessException => PyErr.PermissionErrorClass,
            TimeoutException => PyErr.TimeoutErrorClass,
            IOException => PyErr.OSErrorClass,
            OverflowException => PyErr.OverflowErrorClass,
            DivideByZeroException => PyErr.ZeroDivisionErrorClass,
            OutOfMemoryException => PyErr.MemoryErrorClass,
            _ => PyErr.RuntimeErrorClass,
        };
        var result = PyErr.Raise(cls, $"{error.GetType().Name}: {error.Message}");
        result.Value.Dict["clr_exception"] = new ClrObject(error);
        return result;
    }
}
