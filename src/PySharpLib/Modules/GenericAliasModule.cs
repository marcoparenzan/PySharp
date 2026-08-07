// Copyright (c) 2026 Marco Parenzan
//
// Licensed under the MIT License. See the LICENSE file in the project
// root for full license information.

using PySharpLib.Runtime;

namespace PySharpLib.Modules;

/// <summary>
/// Real generic-alias tracking: `List[int]`, `Dict[str, int]`, `Optional[int]`, `SomeGeneric[T]`
/// now build an object carrying `__origin__`/`__args__` (instead of subscripting being a no-op),
/// so `typing.get_origin`/`get_args` — and anything that relies on them, like pydantic's field-type
/// resolution — work for real. See FASTAPI_PLAN.md Phase 1.9's last entry.
/// </summary>
public static class GenericAliasModule
{
    public static readonly PyClass GenericAliasClass = BuildGenericAliasClass();

    /// <summary>Typing placeholder -> its real-world origin (e.g. typing.List -> the `list`
    /// builtin), so `get_origin(List[int]) is list` matches CPython. Unmapped classes (arbitrary
    /// user generics, or typing names with no natural runtime counterpart like Union) use
    /// themselves as the origin, which is also correct CPython behavior.</summary>
    private static readonly Dictionary<PyClass, object> OriginMap = new();
    private static readonly Dictionary<PyClass, Func<object[], object[]>> ArgsTransform = new();

    public static void MapOrigin(PyClass typingClass, object realOrigin) => OriginMap[typingClass] = realOrigin;

    /// <summary>Rewrites the args tuple after subscripting, for aliases whose args aren't just
    /// "what was between the brackets" — e.g. `Optional[int]` is really `Union[int, NoneType]`.</summary>
    public static void MapArgsTransform(PyClass typingClass, Func<object[], object[]> transform)
        => ArgsTransform[typingClass] = transform;

    /// <summary>`cls[index]`: builds the alias. `index` is a bare value for a single type parameter
    /// (`List[int]`) or a tuple for several (`Dict[str, int]`).</summary>
    public static PyInstance Subscript(PyClass cls, object index)
    {
        var origin = OriginMap.TryGetValue(cls, out var mapped) ? mapped : cls;
        var args = index is PyTuple t ? t.Items : new[] { index };
        if (ArgsTransform.TryGetValue(cls, out var transform))
            args = transform(args);
        return MakeAlias(origin, args);
    }

    public static PyInstance MakeAlias(object origin, IReadOnlyList<object> args)
    {
        var inst = new PyInstance(GenericAliasClass);
        inst.Dict["__origin__"] = origin;
        inst.Dict["__args__"] = new PyTuple(args.ToArray());
        return inst;
    }

    /// <summary>typing.get_origin: the alias's origin, or None if not an alias at all.</summary>
    public static object GetOrigin(object tp) =>
        tp is PyInstance inst && inst.Class == GenericAliasClass && inst.Dict.TryGet("__origin__", out var o)
            ? o
            : PyNone.Instance;

    /// <summary>typing.get_args: the alias's args tuple, or an empty tuple if not an alias.</summary>
    public static PyTuple GetArgs(object tp) =>
        tp is PyInstance inst && inst.Class == GenericAliasClass && inst.Dict.TryGet("__args__", out var a) && a is PyTuple t
            ? t
            : new PyTuple(Array.Empty<object>());

    private static PyClass BuildGenericAliasClass()
    {
        var cls = new PyClass("_GenericAlias", new List<PyClass>());
        void Add(string name, BuiltinFn fn) => cls.Dict[name] = new PyBuiltinFunction($"_GenericAlias.{name}", fn);

        Add("__repr__", (interp, a, _) =>
        {
            var inst = (PyInstance)a[0];
            string originName = PyOps.Repr(interp, inst.Dict["__origin__"]);
            // Builtins/types repr as "<class 'list'>"; real typing aliases show just the bare name.
            if (originName.StartsWith("<class '") && originName.EndsWith("'>"))
                originName = originName[8..^2];
            var argsTuple = (PyTuple)inst.Dict["__args__"];
            var argsRepr = string.Join(", ", argsTuple.Items.Select(x => PyOps.Repr(interp, x)));
            return $"{originName}[{argsRepr}]";
        });
        Add("__eq__", (interp, a, _) =>
        {
            if (a[0] is not PyInstance x || a[1] is not PyInstance y || y.Class != GenericAliasClass)
                return false;
            return interp.RichEquals(x.Dict["__origin__"], y.Dict["__origin__"])
                   && interp.RichEquals(x.Dict["__args__"], y.Dict["__args__"]);
        });
        // Real CPython protocol: a non-class value used as a class base gets __mro_entries__
        // called on it (with the full original bases tuple) to find its real substitute(s) — this
        // is what makes `class Foo(Generic[T]):` work at all, since Generic[T] is this exact alias
        // object, never meant to end up in the MRO itself. See Interp.cs's ExecClassDef.
        Add("__mro_entries__", (_, a, _) =>
        {
            var inst = (PyInstance)a[0];
            return new PyTuple(new object[] { inst.Dict["__origin__"] });
        });

        return cls;
    }
}
