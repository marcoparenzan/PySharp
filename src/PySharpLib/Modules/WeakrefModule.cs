// Copyright (c) 2026 Marco Parenzan
//
// Licensed under the MIT License. See the LICENSE file in the project
// root for full license information.

using PySharpLib.Runtime;

namespace PySharpLib.Modules;

/// <summary>
/// weakref: v1 scope is "not actually weak". PySharp has no exposed GC hooks to make entries
/// disappear when their referent is collected (and .NET's GC semantics differ from CPython's
/// refcounting enough that faithfully replicating "when does this go away" isn't worth chasing).
/// WeakKeyDictionary/WeakValueDictionary/WeakSet are real dicts/sets that just never evict —
/// correct for every normal operation, the only difference is they don't free memory early.
/// ref(obj) returns a plain callable that always returns obj (real weakref.ref returns None once
/// the referent is gone). See FASTAPI_PLAN.md Phase 1.9.
/// </summary>
public static class WeakrefModule
{
    public static readonly PyClass RefClass = BuildRefClass();

    public static PyModule Create()
    {
        var m = new PyModule("weakref");
        var d = m.Dict;

        d["ref"] = RefClass;
        d["WeakKeyDictionary"] = new PyBuiltinFunction("WeakKeyDictionary", (_, _, _) => new PyDict());
        d["WeakValueDictionary"] = new PyBuiltinFunction("WeakValueDictionary", (_, _, _) => new PyDict());
        d["WeakSet"] = new PyBuiltinFunction("WeakSet", (interp, a, _) =>
            a.Length > 0 ? new PySet(PyOps.Iterate(interp, a[0])) : new PySet());
        d["proxy"] = new PyBuiltinFunction("proxy", (_, a, _) => a[0]);
        d["finalize"] = new PyBuiltinFunction("finalize", (_, _, _) => PyNone.Instance);

        return m;
    }

    private static PyClass BuildRefClass()
    {
        var cls = new PyClass("ref", new List<PyClass>());
        cls.Dict["__init__"] = new PyBuiltinFunction("ref.__init__", (_, a, _) =>
        {
            ((PyInstance)a[0]).Dict["__referent__"] = a[1];
            return PyNone.Instance;
        });
        cls.Dict["__call__"] = new PyBuiltinFunction("ref.__call__", (_, a, _) =>
            ((PyInstance)a[0]).Dict["__referent__"]);
        return cls;
    }
}
