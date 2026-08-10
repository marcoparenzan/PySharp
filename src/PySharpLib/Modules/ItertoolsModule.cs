// Copyright (c) 2026 Marco Parenzan
//
// Licensed under the MIT License. See the LICENSE file in the project
// root for full license information.

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

        return m;
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
