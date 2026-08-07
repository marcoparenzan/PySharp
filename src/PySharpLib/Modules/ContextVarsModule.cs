// Copyright (c) 2026 Marco Parenzan
//
// Licensed under the MIT License. See the LICENSE file in the project
// root for full license information.

using System.Linq;
using PySharpLib.Runtime;

namespace PySharpLib.Modules;

/// <summary>
/// contextvars: real get/set/reset/Context/copy_context semantics, scoped to a single current value
/// per ContextVar rather than true per-task context isolation (PySharp's coroutines already run
/// cooperatively one at a time — see Async.cs — so nothing observed so far needs real forked-context
/// propagation across concurrent tasks; this is the same kind of v1 descoping as pickle's own binary
/// format or datetime's strftime-without-strptime elsewhere in this plan). Found via anyio's real
/// `from contextvars import Token`/`Context` usage (lowlevel.py, abc/_tasks.py, _core/_eventloop.py),
/// itself a real dependency of starlette. See FASTAPI_PLAN.md.
/// </summary>
public static class ContextVarsModule
{
    private const string ValueKey = "__value__";
    private const string HasValueKey = "__has_value__";
    private const string NameKey = "__name__";
    private const string DefaultKey = "__default__";
    private const string HasDefaultKey = "__has_default__";

    public static readonly PyClass TokenClass = BuildTokenClass();
    public static readonly PyClass ContextVarClass = BuildContextVarClass();
    public static readonly PyClass ContextClass = BuildContextClass();
    public static readonly PyInstance MissingSentinel = new(new PyClass("Token.MISSING", new List<PyClass>()));

    public static PyModule Create()
    {
        var m = new PyModule("contextvars");
        m.Dict["ContextVar"] = ContextVarClass;
        m.Dict["Token"] = TokenClass;
        m.Dict["Context"] = ContextClass;
        m.Dict["copy_context"] = new PyBuiltinFunction("copy_context", (_, _, _) => new PyInstance(ContextClass));
        return m;
    }

    private static PyClass BuildTokenClass()
    {
        var cls = new PyClass("Token", new List<PyClass>());
        cls.Dict["MISSING"] = MissingSentinel;
        cls.Dict["var"] = new PyProperty
        {
            Getter = new PyBuiltinFunction("Token.var", (_, a, _) => ((PyInstance)a[0]).Dict["var"]),
        };
        cls.Dict["old_value"] = new PyProperty
        {
            Getter = new PyBuiltinFunction("Token.old_value", (_, a, _) => ((PyInstance)a[0]).Dict["old_value"]),
        };
        return cls;
    }

    private static PyClass BuildContextVarClass()
    {
        var cls = new PyClass("ContextVar", new List<PyClass>());
        void Add(string n, BuiltinFn fn) => cls.Dict[n] = new PyBuiltinFunction($"ContextVar.{n}", fn);

        Add("__init__", (_, a, kwargs) =>
        {
            var inst = (PyInstance)a[0];
            inst.Dict[NameKey] = a.Length > 1 ? a[1] : "";
            inst.Dict[HasValueKey] = false;
            if (kwargs is not null && kwargs.TryGetValue("default", out var def))
            {
                inst.Dict[DefaultKey] = def;
                inst.Dict[HasDefaultKey] = true;
            }
            else
            {
                inst.Dict[HasDefaultKey] = false;
            }
            return PyNone.Instance;
        });
        Add("__repr__", (_, a, _) => $"<ContextVar name='{((PyInstance)a[0]).Dict[NameKey]}'>");
        Add("get", (_, a, _) =>
        {
            var inst = (PyInstance)a[0];
            if ((bool)inst.Dict[HasValueKey])
                return inst.Dict[ValueKey];
            if (a.Length > 1)
                return a[1];
            if ((bool)inst.Dict[HasDefaultKey])
                return inst.Dict[DefaultKey];
            throw new PyRaise(PyErr.MakeInstance(PyErr.LookupError, inst.Dict[NameKey]));
        });
        Add("set", (_, a, _) =>
        {
            var inst = (PyInstance)a[0];
            var token = new PyInstance(TokenClass);
            token.Dict["var"] = inst;
            token.Dict["old_value"] = (bool)inst.Dict[HasValueKey] ? inst.Dict[ValueKey] : MissingSentinel;
            inst.Dict[ValueKey] = a[1];
            inst.Dict[HasValueKey] = true;
            return token;
        });
        Add("reset", (_, a, _) =>
        {
            var inst = (PyInstance)a[0];
            var token = (PyInstance)a[1];
            var old = token.Dict["old_value"];
            if (ReferenceEquals(old, MissingSentinel))
                inst.Dict[HasValueKey] = false;
            else
            {
                inst.Dict[ValueKey] = old;
                inst.Dict[HasValueKey] = true;
            }
            return PyNone.Instance;
        });

        return cls;
    }

    private static PyClass BuildContextClass()
    {
        var cls = new PyClass("Context", new List<PyClass>());
        void Add(string n, BuiltinFn fn) => cls.Dict[n] = new PyBuiltinFunction($"Context.{n}", fn);

        // Simplified: doesn't fork/restore ContextVar values around the call (no concurrent task
        // needs real isolation yet — see the module's own doc comment) — just runs the callable.
        Add("run", (interp, a, kwargs) => interp.Call(a[1], a.Skip(2).ToArray(), kwargs));
        Add("copy", (_, a, _) => new PyInstance(ContextClass));
        Add("get", (interp, a, _) =>
        {
            var v = (PyInstance)a[1];
            return (bool)v.Dict[HasValueKey] ? v.Dict[ValueKey]
                : a.Length > 2 ? a[2]
                : (bool)v.Dict[HasDefaultKey] ? v.Dict[DefaultKey]
                : PyNone.Instance;
        });
        Add("__getitem__", (_, a, _) =>
        {
            var v = (PyInstance)a[1];
            return (bool)v.Dict[HasValueKey] ? v.Dict[ValueKey]
                : (bool)v.Dict[HasDefaultKey] ? v.Dict[DefaultKey]
                : throw PyErr.KeyError(a[1]);
        });

        return cls;
    }
}
