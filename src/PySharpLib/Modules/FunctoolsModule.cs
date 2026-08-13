// Copyright (c) 2026 Marco Parenzan
//
// Licensed under the MIT License. See the LICENSE file in the project
// root for full license information.

using PySharpLib.Runtime;

namespace PySharpLib.Modules;

public static class FunctoolsModule
{
    public static PyModule Create()
    {
        var m = new PyModule("functools");
        var d = m.Dict;

        d["reduce"] = new PyBuiltinFunction("reduce", (interp, a, _) =>
        {
            var items = PyOps.Iterate(interp, a[1]).ToList();
            object acc;
            int start;
            if (a.Length > 2)
            {
                acc = a[2];
                start = 0;
            }
            else
            {
                if (items.Count == 0)
                    throw PyErr.TypeError("reduce() of empty iterable with no initial value");
                acc = items[0];
                start = 1;
            }
            for (int i = start; i < items.Count; i++)
                acc = interp.Call(a[0], new[] { acc, items[i] });
            return acc;
        });

        var partialClass = new PyClass("partial", new List<PyClass>());
        partialClass.Dict["__init__"] = new PyBuiltinFunction("partial.__init__", (_, a, kwargs) =>
        {
            var inst = (PyInstance)a[0];
            inst.Dict["func"] = a[1];
            inst.Dict["args"] = new PyTuple(a.Skip(2).ToArray());
            var kw = new PyDict();
            if (kwargs is not null)
                foreach (var e in kwargs)
                    kw[e.Key] = e.Value;
            inst.Dict["keywords"] = kw;
            return PyNone.Instance;
        });
        partialClass.Dict["__call__"] = new PyBuiltinFunction("partial.__call__", (interp, a, kwargs) =>
        {
            var inst = (PyInstance)a[0];
            var fixedArgs = ((PyTuple)inst.Dict["args"]).Items;
            var callArgs = fixedArgs.Concat(a.Skip(1)).ToArray();
            var kw = new Dictionary<string, object>();
            foreach (var e in ((PyDict)inst.Dict["keywords"]).Entries)
                kw[(string)e.Key] = e.Value;
            if (kwargs is not null)
                foreach (var e in kwargs)
                    kw[e.Key] = e.Value;
            return interp.Call(inst.Dict["func"], callArgs, kw.Count > 0 ? kw : null);
        });
        d["partial"] = partialClass;

        // partialmethod: same shape as partial, but a descriptor — accessed through an instance it
        // binds that instance as the first argument (like an unbound method), then applies the
        // fixed args/kwargs on top.
        var partialMethodClass = new PyClass("partialmethod", new List<PyClass>());
        partialMethodClass.Dict["__init__"] = partialClass.Dict["__init__"];
        partialMethodClass.Dict["__get__"] = new PyBuiltinFunction("partialmethod.__get__", (interp, a, _) =>
        {
            var inst = (PyInstance)a[0];
            object? obj = a.Length > 1 ? a[1] : null;
            var fixedArgs = ((PyTuple)inst.Dict["args"]).Items;
            var kw = ((PyDict)inst.Dict["keywords"]).Copy();
            return new PyBuiltinFunction("partialmethod.<locals>.bound", (interp2, callArgs, callKwargs) =>
            {
                var allArgs = (obj is null ? Array.Empty<object>() : new[] { obj })
                    .Concat(fixedArgs).Concat(callArgs).ToArray();
                var kwDict = new Dictionary<string, object>();
                foreach (var e in kw.Entries)
                    kwDict[(string)e.Key] = e.Value;
                if (callKwargs is not null)
                    foreach (var e in callKwargs)
                        kwDict[e.Key] = e.Value;
                return interp2.Call(inst.Dict["func"], allArgs, kwDict.Count > 0 ? kwDict : null);
            });
        });
        d["partialmethod"] = partialMethodClass;

        // Real CPython: `wraps(wrapped)` returns `partial(update_wrapper, wrapped=wrapped)` — both
        // exposed here (found via real sqlalchemy's own `import functools` reaching for
        // `update_wrapper` directly, not just the `@wraps` decorator form).
        d["update_wrapper"] = new PyBuiltinFunction("update_wrapper", (_, a, _) =>
        {
            UpdateWrapper(a[0], a[1]);
            return a[0];
        });
        d["wraps"] = new PyBuiltinFunction("wraps", (interp, a, _) =>
        {
            var wrapped = a[0];
            return new PyBuiltinFunction("wraps_decorator", (interp2, b, _) =>
            {
                UpdateWrapper(b[0], wrapped);
                return b[0];
            });
        });

        d["lru_cache"] = new PyBuiltinFunction("lru_cache", (_, a, _) =>
        {
            // usable as @lru_cache or @lru_cache(maxsize=...): no cache in v1
            if (a.Length == 1 && a[0] is PyFunction or PyBuiltinFunction)
                return a[0];
            return new PyBuiltinFunction("lru_cache_deco", (_, b, _) => b[0]);
        });

        d["cache"] = new PyBuiltinFunction("cache", (_, a, _) => a[0]);
        d["total_ordering"] = new PyBuiltinFunction("total_ordering", (_, a, _) => a[0]);

        d["cmp_to_key"] = new PyBuiltinFunction("cmp_to_key", (interp, a, _) =>
        {
            var cmpFn = a[0];
            var keyClass = new PyClass("K", new List<PyClass>());
            keyClass.Dict["__init__"] = new PyBuiltinFunction("K.__init__", (_, b, _) =>
            {
                ((PyInstance)b[0]).Dict["obj"] = b[1];
                return PyNone.Instance;
            });
            keyClass.Dict["__lt__"] = new PyBuiltinFunction("K.__lt__", (interp2, b, _) =>
            {
                var r = interp2.Call(cmpFn, new[]
                {
                    ((PyInstance)b[0]).Dict["obj"], ((PyInstance)b[1]).Dict["obj"],
                });
                return interp2.Compare(r, System.Numerics.BigInteger.Zero) < 0;
            });
            return keyClass;
        });

        return m;
    }

    /// <summary>Real CPython `functools.update_wrapper`: copies `__module__`/`__name__`/
    /// `__qualname__`/`__doc__` from `wrapped` onto `wrapper` and sets `wrapper.__wrapped__ =
    /// wrapped` — works for both a real `PyFunction` and a `PyBuiltinFunction` on either side (real
    /// CPython copies `__dict__` too via `updated=`; not needed by anything reachable so far).</summary>
    private static void UpdateWrapper(object wrapper, object wrapped)
    {
        string? name = wrapped switch { PyFunction f => f.Name, PyBuiltinFunction b => b.Name, _ => null };
        PyDict? wrappedAttrs = wrapped switch { PyFunction f => f.Attributes, PyBuiltinFunction b => b.Attributes, _ => null };
        PyDict? wrapperAttrs = wrapper switch { PyFunction f => f.Attributes, PyBuiltinFunction b => b.Attributes, _ => null };
        if (wrapperAttrs is null)
            return;
        if (name is not null)
        {
            wrapperAttrs["__name__"] = name;
            wrapperAttrs["__qualname__"] = name;
        }
        wrapperAttrs["__wrapped__"] = wrapped;
        if (wrappedAttrs is null)
            return;
        if (wrappedAttrs.TryGet("__doc__", out var doc))
            wrapperAttrs["__doc__"] = doc;
        if (wrappedAttrs.TryGet("__module__", out var mod))
            wrapperAttrs["__module__"] = mod;
        if (wrappedAttrs.TryGet("__qualname__", out var qn))
            wrapperAttrs["__qualname__"] = qn;
    }
}
