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
    public static readonly PyClass ForwardRefClass = BuildForwardRefClass();

    /// <summary>Typing placeholder -> its real-world origin (e.g. typing.List -> the `list`
    /// builtin), so `get_origin(List[int]) is list` matches CPython. Unmapped classes (arbitrary
    /// user generics, or typing names with no natural runtime counterpart like Union) use
    /// themselves as the origin, which is also correct CPython behavior.</summary>
    // ConcurrentDictionary, not plain Dictionary: these are static/shared across every Interp
    // instance, written on every `import typing` (MiscModules.CreateTyping) and read on every
    // `issubclass`/subscript call — under real concurrent test execution (xUnit parallelizes across
    // test classes, each with its own PyEngine), a plain unsynchronized Dictionary being written by
    // one thread's `import typing` while another thread reads it via `issubclass` can corrupt its
    // internal bucket structure, which for .NET's Dictionary can manifest as a genuine infinite loop
    // — found the hard way as a real test-suite hang once TryGetOrigin (below) started giving
    // `issubclass` a reason to read this far more often than the old, narrower Subscript-only path.
    private static readonly System.Collections.Concurrent.ConcurrentDictionary<PyClass, object> OriginMap = new();
    private static readonly System.Collections.Concurrent.ConcurrentDictionary<PyClass, Func<object[], object[]>> ArgsTransform = new();

    public static void MapOrigin(PyClass typingClass, object realOrigin) => OriginMap[typingClass] = realOrigin;

    /// <summary>The real-world origin a typing placeholder (e.g. `typing.List`) maps to, if any —
    /// lets `issubclass`/`isinstance` treat `issubclass(list, typing.List)` as real CPython's own
    /// `_SpecialGenericAlias.__subclasscheck__` does (delegating to the origin), instead of a flat
    /// MRO check against an unrelated placeholder class with no real relationship to `list`.</summary>
    public static bool TryGetOrigin(PyClass typingClass, out object origin) =>
        OriginMap.TryGetValue(typingClass, out origin!);

    // [ThreadStatic], not a plain static: MiscModules.CreateTyping builds a *fresh* "Generic"
    // PyClass on every `import typing` (one per Interp instance, since each test/script gets its
    // own typing module) — a single shared static here meant whichever test's `import typing` ran
    // *last* under real parallel test execution silently overwrote every other concurrently-running
    // test's own Generic identity, so their later `class Foo(Generic[T]):` de-duplication check
    // (below) compared against the wrong (some other test's) Generic class and could leave a
    // genuine duplicate `Generic` in the resolved bases, breaking MRO computation outright — a real,
    // intermittent flaky-suite bug, not a hang or a wrong-answer-every-time bug, which is why it
    // took several full-suite reruns to catch. Each PyEngine.Run() executes its script on its own
    // dedicated OS thread (see BigStack.Run), so [ThreadStatic] correctly scopes this per test/
    // script the same way PyGenerator.Current/PyCoroutine.Current already do for analogous state.
    [ThreadStatic]
    private static PyClass? _genericPlaceholder;

    /// <summary>The `typing.Generic` placeholder class, set once by MiscModules.CreateTyping —
    /// __mro_entries__ needs to recognize it by identity to de-duplicate redundant `Generic[T]`
    /// bases (see below).</summary>
    public static PyClass? GenericPlaceholder
    {
        get => _genericPlaceholder;
        set => _genericPlaceholder = value;
    }

    /// <summary>`typing.TypeVar("T")`/`ParamSpec`/`TypeVarTuple` each build a fresh, uniquely-named
    /// `PyClass` (real CPython gives each call a distinct object too) — marked with this key so
    /// `Resubscript` can recognize one by presence of the marker rather than by a shared class
    /// identity that doesn't exist.</summary>
    private const string TypeVarMarker = "__is_typevar__";

    public static PyClass MakeTypeVarLike(string name)
    {
        var cls = new PyClass(name, new List<PyClass>());
        cls.Dict[TypeVarMarker] = true;
        return cls;
    }

    private static bool IsTypeVarLike(object o) => o is PyClass pc && pc.Dict.ContainsKey(TypeVarMarker);

    /// <summary>Does resolving base <paramref name="b"/> bring `Generic` into the MRO on its own
    /// (a real class that already derives Generic, or a generic alias over one)?</summary>
    private static bool OriginBringsInGeneric(object b)
    {
        if (GenericPlaceholder is null)
            return false;
        var cls = b switch
        {
            PyClass c => c,
            PyInstance i when i.Class == GenericAliasClass && i.Dict.TryGet("__origin__", out var o) && o is PyClass oc => oc,
            _ => null,
        };
        return cls is not null && cls.Mro.Contains(GenericPlaceholder);
    }

    /// <summary>Rewrites the args tuple after subscripting, for aliases whose args aren't just
    /// "what was between the brackets" — e.g. `Optional[int]` is really `Union[int, NoneType]`.</summary>
    public static void MapArgsTransform(PyClass typingClass, Func<object[], object[]> transform)
        => ArgsTransform[typingClass] = transform;

    /// <summary>`cls[index]`: builds the alias. `index` is a bare value for a single type parameter
    /// (`List[int]`) or a tuple for several (`Dict[str, int]`).</summary>
    public static PyInstance Subscript(PyClass cls, object index)
    {
        var origin = OriginMap.TryGetValue(cls, out var mapped) ? mapped : cls;
        var rawArgs = index is PyTuple t ? t.Items : new[] { index };
        // Real CPython: `Literal[...]`'s own arguments are literal *values* (str/int/bool/None/enum
        // members) — unlike every other generic subscript, they're never forward-referenced type
        // names, so they must never get ForwardRef-wrapped. Found via real sqlalchemy's own
        // `orm/session.py` `JoinTransactionMode = Literal["conditional_savepoint", ...]`, whose own
        // `.__args__` needs to stay the literal strings for `x in JoinTransactionMode.__args__` (a
        // real validation check against Session's own default value) to work at all.
        var args = cls.Name == "Literal" ? rawArgs : rawArgs.Select(WrapForwardRef).ToArray();
        if (ArgsTransform.TryGetValue(cls, out var transform))
            args = transform(args);
        return MakeAlias(origin, args);
    }

    /// <summary>Real CPython's `_type_check` auto-wraps a bare string type argument into a real
    /// `ForwardRef` (e.g. `Optional["SomeType"]`) — PySharp evaluates annotations eagerly (no
    /// deferred string mode), but a string used *as* a type argument still needs this real wrapping,
    /// or downstream code that specifically checks `isinstance(x, ForwardRef)` (real pydantic v1's
    /// own `update_field_forward_refs`) never recognizes it as something to resolve later. Found via
    /// fastapi's real `openapi/models.py`: `Optional["SchemaOrBool"]`-shaped genuinely
    /// self-referential fields, resolved via `Schema.update_forward_refs()` after the class body
    /// (once `SchemaOrBool` is actually defined). See FASTAPI_PLAN.md Phase 4.</summary>
    private static object WrapForwardRef(object arg)
    {
        if (arg is not string s)
            return arg;
        var inst = new PyInstance(ForwardRefClass);
        inst.Dict["__forward_arg__"] = s;
        inst.Dict["__forward_evaluated__"] = false;
        inst.Dict["__forward_value__"] = PyNone.Instance;
        inst.Dict["__forward_is_argument__"] = true;
        inst.Dict["__forward_module__"] = PyNone.Instance;
        inst.Dict["__forward_is_class__"] = false;
        return inst;
    }

    /// <summary>
    /// `alias[index]` where `alias` is itself already a built generic alias (not a bare class) —
    /// e.g. `Dict[str, T][int]`, or a `Union` of parameterized aliases like starlette's real
    /// `Lifespan = StatelessLifespan[AppType] | StatefulLifespan[AppType]` then `Lifespan[AppType]`.
    /// Real CPython substitutes each free TypeVar found (recursively) in `__args__`, positionally,
    /// with the new subscript's value(s) — matching real `_GenericAlias.__getitem__`/
    /// `UnionType.__getitem__`. Found via starlette's real `applications.py` (itself imported by
    /// `import starlette`): a `Lifespan[AppType]` function-parameter annotation, eagerly evaluated
    /// here despite `from __future__ import annotations` being present (PySharp's standing,
    /// documented gap around deferred annotations — real CPython would never evaluate this
    /// particular expression at all).
    /// </summary>
    public static PyInstance Resubscript(PyInstance alias, object index)
    {
        var parameters = new List<object>();
        CollectTypeVars(alias, parameters, new HashSet<object>());
        var subs = index is PyTuple t ? t.Items : new object[] { index };
        var map = new Dictionary<object, object>();
        for (int i = 0; i < parameters.Count && i < subs.Length; i++)
            map[parameters[i]] = subs[i];
        return (PyInstance)Substitute(alias, map);
    }

    private static void CollectTypeVars(object node, List<object> found, HashSet<object> seen)
    {
        if (IsTypeVarLike(node))
        {
            if (seen.Add(node))
                found.Add(node);
            return;
        }
        if (node is PyInstance inst && inst.Class == GenericAliasClass
            && inst.Dict.TryGet("__args__", out var argsObj) && argsObj is PyTuple args)
        {
            foreach (var a in args.Items)
                CollectTypeVars(a, found, seen);
        }
        // Callable[[A, B], R]'s parameter list is a PyList, not directly nested aliases/TypeVars —
        // real CPython's own _CallableGenericAlias flattens through it the same way.
        else if (node is PyList list)
        {
            foreach (var a in list.Items)
                CollectTypeVars(a, found, seen);
        }
    }

    private static object Substitute(object node, Dictionary<object, object> map)
    {
        if (node is PyInstance inst && inst.Class == GenericAliasClass
            && inst.Dict.TryGet("__origin__", out var origin) && inst.Dict.TryGet("__args__", out var argsObj) && argsObj is PyTuple args)
        {
            var newArgs = args.Items.Select(a => map.TryGetValue(a, out var sub) ? sub : Substitute(a, map)).ToArray();
            return MakeAlias(origin, newArgs);
        }
        if (node is PyList list)
            return new PyList(list.Items.Select(a => map.TryGetValue(a, out var sub) ? sub : Substitute(a, map)));
        return map.TryGetValue(node, out var replaced) ? replaced : node;
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
        // Real CPython: calling a subscripted generic (`asyncio.Future[int]()`) just constructs
        // the origin, same as calling the bare class — `_GenericAlias.__call__` forwards straight
        // through. Found via anyio's real `_backends/_asyncio.py`: `asyncio.Future[T_Retval]()`.
        Add("__call__", (interp, a, kwargs) =>
        {
            var inst = (PyInstance)a[0];
            var origin = inst.Dict["__origin__"];
            return interp.Call(origin, a.Skip(1).ToArray(), kwargs);
        });
        Add("__mro_entries__", (_, a, _) =>
        {
            var inst = (PyInstance)a[0];
            var origin = inst.Dict["__origin__"];
            // `Generic[T]` specifically contributes NOTHING when another base in the same class
            // statement already brings `Generic` into the MRO transitively (e.g. `class Foo(
            // Generic[T], SomeOtherGeneric[T])`, where SomeOtherGeneric already derives Generic) —
            // otherwise bare `Generic` would appear twice in the resolved bases, an inconsistent
            // MRO. Matches real CPython's typing.py de-duplication. Found via anyio's real
            // `class StapledObjectStream(Generic[T_Item], ObjectStream[T_Item])` and the identical
            // pattern throughout anyio/abc/_streams.py's whole stream-class hierarchy.
            if (ReferenceEquals(origin, GenericPlaceholder) && a[1] is PyTuple rawBases)
            {
                bool impliedElsewhere = rawBases.Items.Any(b =>
                    !ReferenceEquals(b, inst) && OriginBringsInGeneric(b));
                if (impliedElsewhere)
                    return new PyTuple(Array.Empty<object>());
            }
            return new PyTuple(new object[] { origin });
        });

        return cls;
    }

    /// <summary>
    /// Real CPython's typing.ForwardRef: a real `__init__` (storing the forward-ref string plus the
    /// real bookkeeping fields real code inspects directly — pydantic v1's own
    /// `update_field_forward_refs` checks `field.type_.__class__ == ForwardRef`), and a real
    /// `_evaluate(globalns, localns, recursive_guard=None)` that resolves the string via the real
    /// `eval()` builtin (Builtins.cs) — genuinely evaluating it as a Python expression against the
    /// given namespaces, caching the result exactly like real CPython. `__eq__`/`__hash__` compare
    /// by the forward-ref string, matching real CPython (two ForwardRef('X') instances are equal).
    /// v1 scope: no `recursive_guard`-based infinite-recursion protection (nothing in the reachable
    /// path recurses through the same forward ref twice) — the parameter is accepted for real
    /// call-signature compatibility but not consulted.
    /// </summary>
    private static PyClass BuildForwardRefClass()
    {
        var cls = new PyClass("ForwardRef", new List<PyClass>());
        cls.Dict["__slots__"] = new PyTuple(new object[]
        {
            "__forward_arg__", "__forward_code__", "__forward_evaluated__", "__forward_value__",
            "__forward_is_argument__", "__forward_is_class__", "__forward_module__",
        });
        void Add(string name, BuiltinFn fn) => cls.Dict[name] = new PyBuiltinFunction($"ForwardRef.{name}", fn);

        Add("__init__", (_, a, kwargs) =>
        {
            var inst = (PyInstance)a[0];
            inst.Dict["__forward_arg__"] = a.Length > 1
                ? a[1]
                : throw PyErr.TypeError("ForwardRef() missing required argument: 'arg'");
            inst.Dict["__forward_evaluated__"] = false;
            inst.Dict["__forward_value__"] = PyNone.Instance;
            inst.Dict["__forward_is_argument__"] = a.Length > 2 ? a[2]
                : kwargs is not null && kwargs.TryGetValue("is_argument", out var ia) ? ia : true;
            inst.Dict["__forward_module__"] =
                kwargs is not null && kwargs.TryGetValue("module", out var mod) ? mod : PyNone.Instance;
            inst.Dict["__forward_is_class__"] =
                kwargs is not null && kwargs.TryGetValue("is_class", out var ic) ? ic : false;
            return PyNone.Instance;
        });

        Add("_evaluate", (interp, a, _) =>
        {
            var inst = (PyInstance)a[0];
            if (!(inst.Dict.TryGet("__forward_evaluated__", out var already) && already is true))
            {
                object globalns = a.Length > 1 ? a[1] : PyNone.Instance;
                object localns = a.Length > 2 ? a[2] : PyNone.Instance;
                if (globalns is PyNone && localns is PyNone)
                    globalns = localns = new PyDict();
                else if (globalns is PyNone)
                    globalns = localns;
                else if (localns is PyNone)
                    localns = globalns;

                string source = (string)inst.Dict["__forward_arg__"];
                var evalFn = interp.BuiltinsModule.Dict["eval"];
                var value = interp.Call(evalFn, new object[] { source, globalns, localns });
                inst.Dict["__forward_value__"] = value;
                inst.Dict["__forward_evaluated__"] = true;
            }
            return inst.Dict["__forward_value__"];
        });

        Add("__repr__", (interp, a, _) =>
            $"ForwardRef('{((PyInstance)a[0]).Dict["__forward_arg__"]}')");
        Add("__eq__", (_, a, _) =>
            a[1] is PyInstance y && y.Class == ForwardRefClass
            && Equals(((PyInstance)a[0]).Dict["__forward_arg__"], y.Dict["__forward_arg__"]));
        Add("__hash__", (_, a, _) =>
            new System.Numerics.BigInteger(((string)((PyInstance)a[0]).Dict["__forward_arg__"]).GetHashCode()));

        return cls;
    }
}
