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

    private static object BuildNamedTuple(Interpretation.Interp interp, object[] a)
    {
        string typeName = (string)a[0];
        List<string> fields = a[1] switch
        {
            string s => s.Split(new[] { ' ', ',' }, StringSplitOptions.RemoveEmptyEntries).ToList(),
            _ => PyOps.Iterate(interp, a[1]).Select(x => (string)x).ToList(),
        };

        var cls = new PyClass(typeName, new List<PyClass>());
        cls.Dict["_fields"] = new PyTuple(fields.Select(f => (object)f).ToArray());
        cls.Dict["__init__"] = new PyBuiltinFunction($"{typeName}.__init__", (_, args, kwargs) =>
        {
            var inst = (PyInstance)args[0];
            for (int i = 0; i < fields.Count; i++)
            {
                if (i + 1 < args.Length)
                    inst.Dict[fields[i]] = args[i + 1];
                else if (kwargs is not null && kwargs.TryGetValue(fields[i], out var kv))
                    inst.Dict[fields[i]] = kv;
                else
                    throw PyErr.TypeError($"{typeName}() missing argument '{fields[i]}'");
            }
            return PyNone.Instance;
        });
        cls.Dict["__repr__"] = new PyBuiltinFunction($"{typeName}.__repr__", (interp2, args, _) =>
        {
            var inst = (PyInstance)args[0];
            var parts = fields.Select(f => $"{f}={PyOps.Repr(interp2, inst.Dict[f])}");
            return $"{typeName}({string.Join(", ", parts)})";
        });
        cls.Dict["__getitem__"] = new PyBuiltinFunction($"{typeName}.__getitem__", (_, args, _) =>
        {
            var inst = (PyInstance)args[0];
            int i = PyOps.SeqIndex(args[1], fields.Count, typeName);
            return inst.Dict[fields[i]];
        });
        cls.Dict["__len__"] = new PyBuiltinFunction($"{typeName}.__len__", (_, _, _) =>
            new BigInteger(fields.Count));
        cls.Dict["__iter__"] = new PyBuiltinFunction($"{typeName}.__iter__", (_, args, _) =>
        {
            var inst = (PyInstance)args[0];
            return new PyIterator(fields.Select(f => inst.Dict[f]).GetEnumerator());
        });
        return cls;
    }

    /// <summary>
    /// collections.abc: plain placeholder classes, like the equivalent names already stubbed in
    /// `typing` (see MiscModules.CreateTyping) — just need to exist and be importable/subclassable.
    /// No isinstance/subclass-hook duck-typing (e.g. isinstance({}, Mapping) is not True here) unless
    /// a real scenario needs it.
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

        // Real Mapping mixin method: `get(key, default=None)` via `self[key]`, catching KeyError.
        // Found via starlette's real `Headers(Mapping[str, str])` (datastructures.py) — Headers
        // overrides __getitem__/keys/values/items/__contains__/__eq__ itself, but relies on this one
        // mixin method from the real ABC for `headers.get("content-type")`-style lookups
        // (responses.py's FileResponse, reached serving a real static asset).
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
        d["Mapping"] = mapping;
        // Real CPython: MutableMapping derives from Mapping (inheriting the same mixin methods) —
        // must be built with Mapping already in its bases at construction time, since PyClass
        // computes its MRO once in the constructor (mutating .Bases afterward wouldn't update it).
        d["MutableMapping"] = new PyClass("MutableMapping", new List<PyClass> { mapping });
        return m;
    }
}
