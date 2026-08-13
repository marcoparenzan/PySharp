// Copyright (c) 2026 Marco Parenzan
//
// Licensed under the MIT License. See the LICENSE file in the project
// root for full license information.

namespace PySharpLib.Runtime;

/// <summary>
/// Lexical environment: local variables with a chain toward the enclosing scopes.
/// Name resolution follows LEGB: local → enclosing → globals → builtins.
/// </summary>
public sealed class Env
{
    private readonly Dictionary<string, object> _vars = new();
    private HashSet<string>? _globalNames;
    private HashSet<string>? _nonlocalNames;

    public Env? Parent { get; }
    /// <summary>The module namespace (globals) this scope belongs to.</summary>
    public PyModule Module { get; }
    /// <summary>True for a module's top-level scope: assignments go straight into globals.</summary>
    public bool IsGlobalScope { get; init; }

    /// <summary>True for a class body's scope: excluded from the closure chain.</summary>
    public bool IsClassScope { get; init; }

    /// <summary>Non-null only for a class body's own scope: the class's own name (as written in
    /// source, before any name mangling), used to mangle a `__name`-shaped binding made *directly*
    /// in the class body (a `def __foo(self): ...` or a plain `__x = 1`) to `_ClassName__name` —
    /// see <see cref="Interp.MangleAttrName"/>'s own doc comment for the read/write side of the
    /// same real CPython rule. Without this half too, a mangled *read* (`self.__foo`) would look for
    /// a binding that was never actually stored under the mangled key.</summary>
    public string? MangleClassName { get; init; }

    /// <summary>Non-null only for an `enum`/`Flag`/etc. class body's own scope — real CPython
    /// resolves each `NAME = auto()` to its actual int value *immediately* at class-body assignment
    /// time (via `_EnumDict.__setitem__`), not in a later pass once the whole body has executed, so
    /// that a later same-body expression referencing an earlier member by name (e.g. real
    /// sqlalchemy's own `engine/reflection.py`: `ANY_VIEW = VIEW | MATERIALIZED_VIEW`, combining two
    /// already-assigned `auto()` members with `|`) sees a plain resolved int, not the raw `auto()`
    /// sentinel. See the `AssignStmt` case in Interp.Exec and Interp.ConvertToEnum (which still does
    /// its own final pass, now mostly a no-op for already-resolved values, but still needed for
    /// non-eager assignments and the class-decorator-driven `IntEnum`/functional-syntax paths).
    /// </summary>
    public EnumAutoState? EnumAuto { get; init; }

    /// <summary>The first scope usable as a closure (skips class scopes).</summary>
    public Env EffectiveClosure
    {
        get
        {
            var e = this;
            while (e.IsClassScope && e.Parent is not null)
                e = e.Parent;
            return e;
        }
    }

    public Env(PyModule module, Env? parent = null)
    {
        Module = module;
        Parent = parent;
    }

    public void DeclareGlobal(string name)
        => (_globalNames ??= new HashSet<string>()).Add(name);

    public void DeclareNonlocal(string name)
        => (_nonlocalNames ??= new HashSet<string>()).Add(name);

    public bool TryGet(string name, out object value)
    {
        // 'global name' → resolve only on module + builtins, ignoring the enclosing scopes
        if (_globalNames is not null && _globalNames.Contains(name))
            return TryGetGlobalOrBuiltin(name, out value);

        // 'nonlocal name' → resolve only in the enclosing scopes (not this one, not globals)
        if (_nonlocalNames is not null && _nonlocalNames.Contains(name))
        {
            for (var e = Parent; e is not null; e = e.Parent)
                if (e._vars.TryGetValue(name, out value!))
                    return true;
            value = PyNone.Instance;
            return false;
        }

        for (var e = this; e is not null; e = e.Parent)
        {
            if (e._vars.TryGetValue(name, out value!))
                return true;
        }
        return TryGetGlobalOrBuiltin(name, out value);
    }

    private bool TryGetGlobalOrBuiltin(string name, out object value)
    {
        if (Module.Dict.TryGet(name, out value!))
            return true;
        return Module.Builtins is not null && Module.Builtins.Dict.TryGet(name, out value!);
    }

    public void Set(string name, object value)
    {
        if (IsGlobalScope)
        {
            Module.Dict[name] = value;
            return;
        }
        if (_globalNames is not null && _globalNames.Contains(name))
        {
            Module.Dict[name] = value;
            return;
        }
        if (_nonlocalNames is not null && _nonlocalNames.Contains(name))
        {
            for (var e = Parent; e is not null; e = e.Parent)
            {
                if (e._vars.ContainsKey(name))
                {
                    e._vars[name] = value;
                    return;
                }
            }
            throw PyErr.SyntaxLike($"no binding for nonlocal '{name}' found");
        }
        _vars[name] = value;
    }

    public bool Delete(string name)
        => _vars.Remove(name) || Module.Dict.Remove(name);

    public bool HasLocal(string name) => _vars.ContainsKey(name);

    /// <summary>The local variables of this scope (to build the class dict).</summary>
    public IEnumerable<KeyValuePair<string, object>> Locals => _vars;
}

/// <summary>Mutable per-enum-class-body `auto()` sequence counter — see <see cref="Env.EnumAuto"/>.
/// Plain `Enum`/`IntEnum` bodies generate successive integers (1, 2, 3, ...); `Flag`/`IntFlag`
/// bodies generate successive powers of two (1, 2, 4, 8, ...) instead, matching real CPython's
/// distinct `_generate_next_value_` for each family.</summary>
public sealed class EnumAutoState
{
    public bool IsFlag { get; init; }
    private System.Numerics.BigInteger _nextSequential = System.Numerics.BigInteger.One;
    private System.Numerics.BigInteger _nextFlagBit = System.Numerics.BigInteger.One;

    public System.Numerics.BigInteger ConsumeAuto()
    {
        if (IsFlag)
        {
            var v = _nextFlagBit;
            _nextFlagBit *= 2;
            return v;
        }
        var seq = _nextSequential;
        _nextSequential += 1;
        return seq;
    }

    /// <summary>Real CPython: a plain Enum's `auto()` continues from one past the highest explicit
    /// int the body has assigned so far. Not attempted for Flag's own power-of-two sequence — nothing
    /// reachable so far mixes an explicit non-power-of-two value with a later auto() call in a Flag
    /// body, and doing so correctly needs real CPython's "next unused single bit" search, not a
    /// simple increment.</summary>
    public void ObserveExplicit(System.Numerics.BigInteger v)
    {
        if (!IsFlag)
            _nextSequential = v + 1;
    }
}

/// <summary>A Python module: namespace + reference to the builtins.</summary>
public sealed class PyModule
{
    public string Name { get; }
    public PyDict Dict { get; } = new();
    /// <summary>The builtins module (null only for the builtins module itself).</summary>
    public PyModule? Builtins { get; set; }
    /// <summary>Source file this module was loaded from (for tracebacks). Default "&lt;string&gt;".</summary>
    public string FileName { get; set; } = "<string>";

    public PyModule(string name)
    {
        Name = name;
        Dict["__name__"] = name;
        // Real CPython: every module has a real `__doc__` global (its docstring, or None when
        // absent) — found via real requests' own `models.py` module-level `_init` referencing the
        // bare name `__doc__`, reachable from `import requests`. Defaulting to None here (rather
        // than leaving it unbound, which raised NameError) matches the same "not a byte-identical
        // docstring capture, just a real always-present default" simplification already accepted
        // for class __doc__ elsewhere in this project.
        Dict["__doc__"] = PyNone.Instance;
    }

    /// <summary>Wraps an *existing* dict as this module's namespace (instead of a fresh one) —
    /// used by eval()'s globals argument, so evaluating an expression against a real dict the
    /// caller passed in shares that same object (mutations from the expression, e.g. a walrus
    /// assignment, are visible to the caller afterward), matching real CPython's `eval(expr,
    /// globals_dict)` exactly.</summary>
    public PyModule(string name, PyDict dict)
    {
        Name = name;
        Dict = dict;
    }
}
