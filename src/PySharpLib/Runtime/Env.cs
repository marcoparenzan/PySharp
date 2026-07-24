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

/// <summary>A Python module: namespace + reference to the builtins.</summary>
public sealed class PyModule
{
    public string Name { get; }
    public PyDict Dict { get; } = new();
    /// <summary>The builtins module (null only for the builtins module itself).</summary>
    public PyModule? Builtins { get; set; }

    public PyModule(string name)
    {
        Name = name;
        Dict["__name__"] = name;
    }
}
