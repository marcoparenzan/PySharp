// Copyright (c) 2026 Marco Parenzan
//
// Licensed under the MIT License. See the LICENSE file in the project
// root for full license information.

using System.Numerics;
using PySharpLib.Interpretation;
using PySharpLib.Runtime;

namespace PySharpLib.Modules;

/// <summary>itertools: v1 scope is just what real-world scenario scripts have needed so far (see
/// FASTAPI_PLAN.md) — chain, islice, zip_longest.</summary>
public static class ItertoolsModule
{
    public static PyModule Create()
    {
        var m = new PyModule("itertools");
        var d = m.Dict;

        d["chain"] = new PyBuiltinFunction("chain", (interp, a, _) =>
            new PyIterator(a.SelectMany(x => PyOps.Iterate(interp, x)).GetEnumerator()));

        d["islice"] = new PyBuiltinFunction("islice", (interp, a, _) =>
        {
            var src = PyOps.Iterate(interp, a[0]);
            int start = 0, step = 1;
            int stop = a.Length == 2
                ? (int)PyOps.AsBigInt(a[1], "stop")
                : int.MaxValue;
            if (a.Length > 2)
            {
                start = (int)PyOps.AsBigInt(a[1], "start");
                stop = a[2] is PyNone ? int.MaxValue : (int)PyOps.AsBigInt(a[2], "stop");
                if (a.Length > 3)
                    step = (int)PyOps.AsBigInt(a[3], "step");
            }
            return new PyIterator(Sliced(src, start, stop, step).GetEnumerator());
        });

        d["zip_longest"] = new PyBuiltinFunction("zip_longest", (interp, a, kwargs) =>
        {
            object fill = kwargs is not null && kwargs.TryGetValue("fillvalue", out var fv) ? fv : PyNone.Instance;
            var iters = a.Select(x => PyOps.Iterate(interp, x).GetEnumerator()).ToArray();
            return new PyIterator(ZipLongest(iters, fill).GetEnumerator());
        });

        d["takewhile"] = new PyBuiltinFunction("takewhile", (interp, a, _) =>
        {
            object pred = a[0];
            var src = PyOps.Iterate(interp, a[1]);
            return new PyIterator(TakeWhile(interp, pred, src).GetEnumerator());
        });

        d["dropwhile"] = new PyBuiltinFunction("dropwhile", (interp, a, _) =>
        {
            object pred = a[0];
            var src = PyOps.Iterate(interp, a[1]);
            return new PyIterator(DropWhile(interp, pred, src).GetEnumerator());
        });

        // Real itertools.filterfalse: the inverse of the `filter()` builtin — yields items where the
        // predicate is falsy, or (predicate is `None`) items that are themselves falsy. Found via
        // real sqlalchemy's own `util/_collections.py`.
        d["filterfalse"] = new PyBuiltinFunction("filterfalse", (interp, a, _) =>
        {
            object? pred = a[0] is PyNone ? null : a[0];
            var src = PyOps.Iterate(interp, a[1]);
            return new PyIterator(FilterFalse(interp, pred, src).GetEnumerator());
        });

        // Real itertools.groupby(iterable, key=None): groups *consecutive* equal-key items. Each
        // group here is eagerly collected into its own buffer rather than staying lazily coupled to
        // outer-iterator advancement (real CPython invalidates a not-fully-consumed group once you
        // advance past it) — a simplification that matches every real call site seen so far
        // (`for key, group in groupby(...): ... list(group) ...`, consuming each group immediately).
        // Found via real sqlalchemy's own `orm/persistence.py`.
        d["groupby"] = new PyBuiltinFunction("groupby", (interp, a, kwargs) =>
        {
            object? keyFn = a.Length > 1 && a[1] is not PyNone ? a[1]
                : kwargs is not null && kwargs.TryGetValue("key", out var k) && k is not PyNone ? k : null;
            var src = PyOps.Iterate(interp, a[0]);
            return new PyIterator(GroupBy(interp, keyFn, src).GetEnumerator());
        });

        // Real itertools.count(start=0, step=1): an infinite arithmetic sequence. Found via real
        // sqlalchemy's own `util/langhelpers.py` `counter()` (a threadsafe counter built on
        // `itertools.count(1)`).
        d["count"] = new PyBuiltinFunction("count", (_, a, kwargs) =>
        {
            BigInteger start = a.Length > 0 ? PyOps.AsBigInt(a[0], "start")
                : kwargs is not null && kwargs.TryGetValue("start", out var s) ? PyOps.AsBigInt(s, "start")
                : BigInteger.Zero;
            BigInteger step = a.Length > 1 ? PyOps.AsBigInt(a[1], "step")
                : kwargs is not null && kwargs.TryGetValue("step", out var st) ? PyOps.AsBigInt(st, "step")
                : BigInteger.One;
            return new PyIterator(Count(start, step).GetEnumerator());
        });

        return m;
    }

    private static IEnumerable<object> Count(BigInteger start, BigInteger step)
    {
        for (var i = start; ; i += step)
            yield return i;
    }

    private static IEnumerable<object> GroupBy(Interp interp, object? keyFn, IEnumerable<object> src)
    {
        object? currentKey = null;
        List<object>? currentGroup = null;
        bool have = false;
        foreach (var item in src)
        {
            object k = keyFn is null ? item : interp.Call(keyFn, new[] { item });
            if (have && interp.RichEquals(k, currentKey!))
            {
                currentGroup!.Add(item);
                continue;
            }
            if (have)
                yield return new PyTuple(new object[] { currentKey!, new PyIterator(currentGroup!.GetEnumerator()) });
            currentKey = k;
            currentGroup = new List<object> { item };
            have = true;
        }
        if (have)
            yield return new PyTuple(new object[] { currentKey!, new PyIterator(currentGroup!.GetEnumerator()) });
    }

    private static IEnumerable<object> FilterFalse(Interp interp, object? pred, IEnumerable<object> src)
    {
        foreach (var item in src)
        {
            bool truthy = pred is null ? PyOps.Truthy(interp, item) : PyOps.Truthy(interp, interp.Call(pred, new[] { item }));
            if (!truthy)
                yield return item;
        }
    }

    private static IEnumerable<object> TakeWhile(Interp interp, object pred, IEnumerable<object> src)
    {
        foreach (var item in src)
        {
            if (!PyOps.Truthy(interp, interp.Call(pred, new[] { item })))
                yield break;
            yield return item;
        }
    }

    private static IEnumerable<object> DropWhile(Interp interp, object pred, IEnumerable<object> src)
    {
        bool dropping = true;
        foreach (var item in src)
        {
            if (dropping && PyOps.Truthy(interp, interp.Call(pred, new[] { item })))
                continue;
            dropping = false;
            yield return item;
        }
    }

    private static IEnumerable<object> Sliced(IEnumerable<object> src, int start, int stop, int step)
    {
        int i = 0;
        foreach (var item in src)
        {
            if (i >= stop)
                yield break;
            if (i >= start && (i - start) % step == 0)
                yield return item;
            i++;
        }
    }

    private static IEnumerable<object> ZipLongest(IEnumerator<object>[] iters, object fill)
    {
        while (true)
        {
            bool any = false;
            var row = new object[iters.Length];
            for (int i = 0; i < iters.Length; i++)
            {
                if (iters[i].MoveNext())
                {
                    row[i] = iters[i].Current;
                    any = true;
                }
                else
                {
                    row[i] = fill;
                }
            }
            if (!any)
                yield break;
            yield return new PyTuple(row);
        }
    }
}
