// Copyright (c) 2026 Marco Parenzan
//
// Licensed under the MIT License. See the LICENSE file in the project
// root for full license information.

using System.Numerics;
using PySharpLib.Runtime;

namespace PySharpLib.Modules;

/// <summary>collections: deque (used by paho for the packet queue), OrderedDict, defaultdict.</summary>
public static class CollectionsModule
{
    public static PyModule Create()
    {
        var m = new PyModule("collections");
        var d = m.Dict;
        d["deque"] = BuildDequeClass();
        d["OrderedDict"] = new PyBuiltinFunction("OrderedDict", (interp, a, kwargs) =>
        {
            var dict = new PyDict();
            if (a.Length > 0)
            {
                if (a[0] is PyDict src)
                    dict.Update(src);
                else
                    foreach (var pair in PyOps.Iterate(interp, a[0]))
                    {
                        var kv = PyOps.Iterate(interp, pair).ToList();
                        dict[kv[0]] = kv[1];
                    }
            }
            if (kwargs is not null)
                foreach (var kv in kwargs)
                    dict[kv.Key] = kv.Value;
            return dict;
        });
        d["defaultdict"] = BuildDefaultDictClass();
        // Counter(iterable_or_mapping): a real dict of counts. Simplification (probe-driven, see
        // FASTAPI_PLAN.md): missing keys raise KeyError like a plain dict rather than returning 0,
        // and there's no .most_common() yet — add both if/when a real run needs them.
        d["Counter"] = new PyBuiltinFunction("Counter", (interp, a, kwargs) =>
        {
            var counts = new PyDict();
            void Bump(object key)
            {
                var current = counts.TryGet(key, out var v) ? PyOps.AsBigInt(v, "count") : BigInteger.Zero;
                counts[key] = current + 1;
            }
            if (a.Length > 0)
            {
                if (a[0] is PyDict src)
                    foreach (var e in src.Entries)
                        counts[e.Key] = e.Value;
                else
                    foreach (var item in PyOps.Iterate(interp, a[0]))
                        Bump(item);
            }
            if (kwargs is not null)
                foreach (var kv in kwargs)
                    counts[kv.Key] = kv.Value;
            return counts;
        });
        // ChainMap(*maps): real multi-map lookup, first map wins on key collisions. Simplification:
        // returns a merged snapshot rather than a live view over independently-mutable maps.
        d["ChainMap"] = new PyBuiltinFunction("ChainMap", (_, a, _) =>
        {
            var merged = new PyDict();
            for (int i = a.Length - 1; i >= 0; i--)
                if (a[i] is PyDict src)
                    merged.Update(src);
            return merged;
        });
        d["namedtuple"] = new PyBuiltinFunction("namedtuple", (interp, a, _) => BuildNamedTuple(interp, a));
        return m;
    }

    // ---------------------------------------------------------------- deque

    private static PyClass BuildDequeClass()
    {
        var cls = new PyClass("deque", new List<PyClass>());
        const string key = "__deque__";

        LinkedList<object> Q(object self) => (LinkedList<object>)((PyInstance)self).Dict[key];
        void Add(string name, BuiltinFn fn) => cls.Dict[name] = new PyBuiltinFunction($"deque.{name}", fn);

        Add("__init__", (interp, a, _) =>
        {
            var q = new LinkedList<object>();
            if (a.Length > 1 && a[1] is not PyNone)
                foreach (var x in PyOps.Iterate(interp, a[1]))
                    q.AddLast(x);
            ((PyInstance)a[0]).Dict[key] = q;
            return PyNone.Instance;
        });
        Add("append", (_, a, _) =>
        {
            Q(a[0]).AddLast(a[1]);
            return PyNone.Instance;
        });
        Add("appendleft", (_, a, _) =>
        {
            Q(a[0]).AddFirst(a[1]);
            return PyNone.Instance;
        });
        Add("pop", (_, a, _) =>
        {
            var q = Q(a[0]);
            if (q.Count == 0)
                throw PyErr.IndexError("pop from an empty deque");
            var v = q.Last!.Value;
            q.RemoveLast();
            return v;
        });
        Add("popleft", (_, a, _) =>
        {
            var q = Q(a[0]);
            if (q.Count == 0)
                throw PyErr.IndexError("pop from an empty deque");
            var v = q.First!.Value;
            q.RemoveFirst();
            return v;
        });
        Add("clear", (_, a, _) =>
        {
            Q(a[0]).Clear();
            return PyNone.Instance;
        });
        Add("extend", (interp, a, _) =>
        {
            foreach (var x in PyOps.Iterate(interp, a[1]))
                Q(a[0]).AddLast(x);
            return PyNone.Instance;
        });
        Add("remove", (interp, a, _) =>
        {
            var q = Q(a[0]);
            for (var node = q.First; node is not null; node = node.Next)
            {
                if (interp.RichEquals(node.Value, a[1]))
                {
                    q.Remove(node);
                    return PyNone.Instance;
                }
            }
            throw PyErr.ValueError("deque.remove(x): x not in deque");
        });
        Add("__len__", (_, a, _) => new BigInteger(Q(a[0]).Count));
        Add("__iter__", (_, a, _) => new PyIterator(Q(a[0]).ToList().GetEnumerator()));
        Add("__contains__", (interp, a, _) => Q(a[0]).Any(x => interp.RichEquals(x, a[1])));
        Add("__bool__", (_, a, _) => Q(a[0]).Count > 0);
        Add("__repr__", (interp, a, _) =>
            $"deque([{string.Join(", ", Q(a[0]).Select(x => PyOps.Repr(interp, x)))}])");
        Add("__getitem__", (_, a, _) =>
        {
            var q = Q(a[0]);
            int i = PyOps.SeqIndex(a[1], q.Count, "deque");
            return q.ElementAt(i);
        });
        return cls;
    }

    // ---------------------------------------------------------------- defaultdict

    private static PyClass BuildDefaultDictClass()
    {
        var cls = new PyClass("defaultdict", new List<PyClass>());
        const string dataKey = "__data__";
        const string factoryKey = "default_factory";

        PyDict D(object self) => (PyDict)((PyInstance)self).Dict[dataKey];
        void Add(string name, BuiltinFn fn) => cls.Dict[name] = new PyBuiltinFunction($"defaultdict.{name}", fn);

        Add("__init__", (_, a, _) =>
        {
            var inst = (PyInstance)a[0];
            inst.Dict[dataKey] = new PyDict();
            inst.Dict[factoryKey] = a.Length > 1 ? a[1] : PyNone.Instance;
            return PyNone.Instance;
        });
        Add("__getitem__", (interp, a, _) =>
        {
            var d = D(a[0]);
            if (d.TryGet(a[1], out var v))
                return v;
            var factory = ((PyInstance)a[0]).Dict[factoryKey];
            if (factory is PyNone)
                throw PyErr.KeyError(a[1]);
            var value = interp.Call(factory, Array.Empty<object>());
            d[a[1]] = value;
            return value;
        });
        Add("__setitem__", (_, a, _) =>
        {
            D(a[0])[a[1]] = a[2];
            return PyNone.Instance;
        });
        Add("__contains__", (_, a, _) => D(a[0]).ContainsKey(a[1]));
        Add("__len__", (_, a, _) => new BigInteger(D(a[0]).Count));
        Add("__iter__", (_, a, _) => new PyIterator(D(a[0]).Keys.ToList().GetEnumerator()));
        Add("keys", (_, a, _) => new PyList(D(a[0]).Keys));
        Add("values", (_, a, _) => new PyList(D(a[0]).Values));
        Add("items", (_, a, _) =>
            new PyList(D(a[0]).Entries.Select(e => (object)new PyTuple(new[] { e.Key, e.Value }))));
        Add("get", (_, a, _) => D(a[0]).TryGet(a[1], out var v) ? v : a.Length > 2 ? a[2] : PyNone.Instance);
        return cls;
    }

    // ---------------------------------------------------------------- namedtuple

    // Delegates to Interp.ConvertToNamedTuple (the same generator `class Foo(NamedTuple):`/
    // functional `typing.NamedTuple(...)` already use) rather than a second, hand-maintained
    // implementation — the two had already drifted (this one was missing `_asdict` entirely, and
    // both were missing `_replace`, found via real rfc3986's own `uri.py`:
    // `class URIReference(namedtuple("URIReference", misc.URI_COMPONENTS), URIMixin):`).
    // ConvertToNamedTuple only ever reads `__annotations__`'s *keys* (field names), never the
    // annotation values, so a plain `namedtuple(name, fields)` call (no real type info) can just
    // fill them with a placeholder.
    private static object BuildNamedTuple(Interpretation.Interp interp, object[] a)
    {
        string typeName = (string)a[0];
        List<string> fields = a[1] switch
        {
            string s => s.Split(new[] { ' ', ',' }, StringSplitOptions.RemoveEmptyEntries).ToList(),
            _ => PyOps.Iterate(interp, a[1]).Select(x => (string)x).ToList(),
        };

        var cls = new PyClass(typeName, new List<PyClass>());
        var ann = new PyDict();
        foreach (var f in fields)
            ann[f] = PyNone.Instance;
        cls.Dict["__annotations__"] = ann;
        interp.ConvertToNamedTuple(cls);
        return cls;
    }

    /// <summary>Real Mapping/MutableMapping mixin classes, shared by identity between
    /// `collections.abc` and `typing` (real CPython: `typing.Mapping`/`typing.MutableMapping` are
    /// the exact same classes as their `collections.abc` counterparts, just generic-subscriptable —
    /// found the hard way via real httpx's own `Headers(typing.MutableMapping[str, str])`
    /// (`_models.py`): it subclasses the `typing` spelling, not `collections.abc`'s, so these two
    /// modules must hand out the *same* class objects or a class built against one sees none of the
    /// other's mixin methods). Stateless (the mixin methods only ever touch the instance they're
    /// called on via `__getitem__`/`__setitem__`/`__delitem__`/iteration), so — unlike this
    /// project's other, script-varying shared state — one process-wide instance is safe, the same
    /// category as `EnumModule.EnumClass`/`GenericAliasModule.GenericAliasClass`.</summary>
    public static readonly PyClass MappingClass = BuildMappingClass();
    public static readonly PyClass MutableMappingClass = BuildMutableMappingClass(MappingClass);

    /// <summary>
    /// collections.abc: plain placeholder classes, like the equivalent names already stubbed in
    /// `typing` (see MiscModules.CreateTyping) — just need to exist and be importable/subclassable.
    /// No isinstance/subclass-hook duck-typing (e.g. isinstance({}, Mapping) is not True here) unless
    /// a real scenario needs it. Mapping/MutableMapping are the real, shared classes above instead.
    /// </summary>
    public static PyModule CreateAbc()
    {
        var m = new PyModule("collections.abc");
        var d = m.Dict;
        foreach (var name in new[]
        {
            "Callable", "Hashable", "Iterable", "Iterator", "Reversible", "Generator",
            "Sized", "Container", "Collection", "Set", "MutableSet",
            "MappingView", "KeysView", "ItemsView", "ValuesView",
            "Sequence", "MutableSequence", "ByteString", "Awaitable", "Coroutine",
            "AsyncIterable", "AsyncIterator", "AsyncGenerator", "Buffer",
        })
        {
            d[name] = new PyClass(name, new List<PyClass>());
        }
        d["Mapping"] = MappingClass;
        d["MutableMapping"] = MutableMappingClass;
        return m;
    }

    // Real Mapping mixin method: `get(key, default=None)` via `self[key]`, catching KeyError.
    // Found via starlette's real `Headers(Mapping[str, str])` (datastructures.py) — Headers
    // overrides __getitem__/keys/values/items/__contains__/__eq__ itself, but relies on this one
    // mixin method from the real ABC for `headers.get("content-type")`-style lookups
    // (responses.py's FileResponse, reached serving a real static asset).
    private static PyClass BuildMappingClass()
    {
        var mapping = new PyClass("Mapping", new List<PyClass>());
        mapping.Dict["get"] = new PyBuiltinFunction("Mapping.get", (interp, a, kwargs) =>
        {
            object? def = a.Length > 2 ? a[2] : kwargs is not null && kwargs.TryGetValue("default", out var d2) ? d2 : PyNone.Instance;
            try
            {
                return interp.GetItem(a[0], a[1]);
            }
            catch (PyRaise ex) when (ex.Value.Class.IsSubclassOf(PyErr.KeyErrorClass))
            {
                return def!;
            }
        });
        return mapping;
    }

    // Real MutableMapping.pop/popitem/setdefault/clear — CPython's own mixin algorithms, built on
    // __getitem__/__setitem__/__delitem__/iteration. Found via real httpx's own `Headers.update()`
    // (`_models.py`): overrides `update` itself but relies on the inherited mixin for
    // `self.pop(key)`.
    private static PyClass BuildMutableMappingClass(PyClass mapping)
    {
        // Real CPython: MutableMapping derives from Mapping (inheriting the same mixin methods) —
        // must be built with Mapping already in its bases at construction time, since PyClass
        // computes its MRO once in the constructor (mutating .Bases afterward wouldn't update it).
        var mutableMapping = new PyClass("MutableMapping", new List<PyClass> { mapping });

        object popMarker = new();
        mutableMapping.Dict["pop"] = new PyBuiltinFunction("MutableMapping.pop", (interp, a, kwargs) =>
        {
            object def = a.Length > 2 ? a[2] : kwargs is not null && kwargs.TryGetValue("default", out var d2) ? d2 : popMarker;
            object value;
            try
            {
                value = interp.GetItem(a[0], a[1]);
            }
            catch (PyRaise ex) when (ex.Value.Class.IsSubclassOf(PyErr.KeyErrorClass))
            {
                if (ReferenceEquals(def, popMarker))
                    throw;
                return def;
            }
            interp.DelItem(a[0], a[1]);
            return value;
        });
        // Real MutableMapping.popitem(): pops an arbitrary (the first, via iteration order) item.
        mutableMapping.Dict["popitem"] = new PyBuiltinFunction("MutableMapping.popitem", (interp, a, _) =>
        {
            object key;
            try
            {
                key = PyOps.Iterate(interp, a[0]).First();
            }
            catch (InvalidOperationException)
            {
                throw PyErr.KeyError("popitem(): dictionary is empty");
            }
            object value = interp.GetItem(a[0], key);
            interp.DelItem(a[0], key);
            return new PyTuple(new[] { key, value });
        });
        // Real MutableMapping.setdefault(key, default=None): returns the existing value, or sets
        // and returns `default` if the key is missing.
        mutableMapping.Dict["setdefault"] = new PyBuiltinFunction("MutableMapping.setdefault", (interp, a, kwargs) =>
        {
            object def = a.Length > 2 ? a[2] : kwargs is not null && kwargs.TryGetValue("default", out var d2) ? d2 : PyNone.Instance;
            try
            {
                return interp.GetItem(a[0], a[1]);
            }
            catch (PyRaise ex) when (ex.Value.Class.IsSubclassOf(PyErr.KeyErrorClass))
            {
                interp.SetItem(a[0], a[1], def);
                return def;
            }
        });
        // Real MutableMapping.clear(): repeatedly popitem() until empty.
        mutableMapping.Dict["clear"] = new PyBuiltinFunction("MutableMapping.clear", (interp, a, _) =>
        {
            while (true)
            {
                try
                {
                    interp.CallMethod(a[0], "popitem", Array.Empty<object>());
                }
                catch (PyRaise ex) when (ex.Value.Class.IsSubclassOf(PyErr.KeyErrorClass))
                {
                    return PyNone.Instance;
                }
            }
        });
        return mutableMapping;
    }
}
