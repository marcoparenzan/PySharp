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

        // Real CPython `functools.singledispatch`: a generic function that dispatches on the
        // runtime type of its first argument. `.register` supports all three real forms —
        // `@f.register(SomeType)` (explicit type, returns a decorator), `@f.register` bare (reads
        // the wrapped function's own first-parameter type annotation, a bare `None` annotation
        // meaning NoneType per real `typing.get_type_hints`'s own conversion), and the direct
        // `f.register(SomeType, impl)` two-arg form. Found via real pg8000's own `converters.py`
        // (`@singledispatch def array_out(val): ...` + several `@array_out.register`-decorated
        // per-type implementations, including two stacked `.register(bytes)`/`.register(bytearray)`
        // decorators on the same function) — reachable once installed as the pure-Python SQLAlchemy
        // Postgres dialect driver (ORM_PLAN.md).
        d["singledispatch"] = new PyBuiltinFunction("singledispatch", (interp, a, _) =>
        {
            var dispatcher = new PyInstance(SingleDispatchClass);
            dispatcher.Dict["__default__"] = a[0];
            dispatcher.Dict["__registry__"] = new List<(object TypeKey, object Impl)>();
            UpdateWrapper(dispatcher, a[0]);
            return dispatcher;
        });

        return m;
    }

    private static readonly PyClass SingleDispatchClass = BuildSingleDispatchClass();

    private static PyClass BuildSingleDispatchClass()
    {
        var cls = new PyClass("singledispatch_function", new List<PyClass>());
        void Add(string name, BuiltinFn fn) => cls.Dict[name] = new PyBuiltinFunction($"singledispatch.{name}", fn);

        Add("__call__", (interp, a, kwargs) =>
        {
            var self = (PyInstance)a[0];
            var callArgs = a.Skip(1).ToArray();
            if (callArgs.Length == 0)
                throw PyErr.TypeError("singledispatch function requires at least 1 positional argument");
            var registry = (List<(object TypeKey, object Impl)>)self.Dict["__registry__"];
            object impl = self.Dict["__default__"];
            // Real CPython walks the runtime type's full MRO to find the most specific registered
            // implementation; this practical subset checks registrations in registration order and
            // takes the first `isinstance`-style match, which is exact for every real caller found
            // so far (each argument matches at most one of a set of unrelated concrete types, e.g.
            // pg8000's own list/tuple/None/dict/bytes/bytearray/str dispatch — no diamond
            // inheritance between the registered types themselves).
            foreach (var (typeKey, candidateImpl) in registry)
            {
                if (Builtins.BuiltinsFactory.IsInstance(callArgs[0], typeKey))
                {
                    impl = candidateImpl;
                    break;
                }
            }
            return interp.Call(impl, callArgs, kwargs);
        });

        Add("register", (interp, a, _) =>
        {
            var self = (PyInstance)a[0];
            var registry = (List<(object TypeKey, object Impl)>)self.Dict["__registry__"];
            // Direct two-arg form: register(cls, func).
            if (a.Length >= 3)
            {
                registry.Add((a[1], a[2]));
                return a[2];
            }
            // Bare decorator form (`@f.register`, no call): the argument is the function itself —
            // infer the dispatch type from its own first parameter's annotation.
            if (a[1] is PyFunction bareFn)
            {
                var firstParam = bareFn.Params.Positional.FirstOrDefault();
                if (firstParam?.Annotation is null)
                    throw PyErr.TypeError(
                        $"Invalid first argument to `register()`: {bareFn.Name}. Use either `@register(some_class)` or plain `@register` on an annotated function.");
                object annValue = interp.Eval(firstParam.Annotation, bareFn.Closure);
                object typeKey = annValue is PyNone ? MiscModules.NoneTypeClass : annValue;
                registry.Add((typeKey, bareFn));
                return bareFn;
            }
            // Explicit-type decorator form (`@f.register(SomeType)`): return a decorator that
            // registers whatever function it's applied to next.
            object explicitTypeKey = a[1];
            return new PyBuiltinFunction("singledispatch.register_impl", (_, b, _) =>
            {
                registry.Add((explicitTypeKey, b[0]));
                return b[0];
            });
        });

        return cls;
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
