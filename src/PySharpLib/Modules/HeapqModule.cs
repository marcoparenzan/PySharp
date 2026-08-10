// Copyright (c) 2026 Marco Parenzan
//
// Licensed under the MIT License. See the LICENSE file in the project
// root for full license information.

using PySharpLib.Interpretation;
using PySharpLib.Runtime;

namespace PySharpLib.Modules;

/// <summary>heapq: a real binary min-heap over a real Python list, in place — the exact
/// sift-up/sift-down algorithm real CPython's own `Lib/heapq.py` uses, ported directly (not a
/// wrapper over a .NET priority-queue type, so element ordering/tie-breaking matches real
/// CPython exactly), using `interp.Compare` for `&lt;` so heap elements can be arbitrary
/// `__lt__`-comparable objects, not just numbers. Found via real `pika`'s own
/// `adapters/select_connection.py` (a real min-heap of connection timeouts, ordered by
/// deadline). See ROADMAP.md scenario 7.</summary>
public static class HeapqModule
{
    public static PyModule Create()
    {
        var m = new PyModule("heapq");
        var d = m.Dict;

        d["heappush"] = new PyBuiltinFunction("heappush", (interp, a, _) =>
        {
            var heap = (PyList)a[0];
            heap.Items.Add(a[1]);
            SiftDown(interp, heap, 0, heap.Items.Count - 1);
            return PyNone.Instance;
        });

        d["heappop"] = new PyBuiltinFunction("heappop", (interp, a, _) =>
        {
            var heap = (PyList)a[0];
            var lastItem = heap.Items[^1];
            heap.Items.RemoveAt(heap.Items.Count - 1);
            if (heap.Items.Count == 0)
                return lastItem;
            var returnItem = heap.Items[0];
            heap.Items[0] = lastItem;
            SiftUp(interp, heap, 0);
            return returnItem;
        });

        d["heapify"] = new PyBuiltinFunction("heapify", (interp, a, _) =>
        {
            var heap = (PyList)a[0];
            for (int i = heap.Items.Count / 2 - 1; i >= 0; i--)
                SiftUp(interp, heap, i);
            return PyNone.Instance;
        });

        d["heappushpop"] = new PyBuiltinFunction("heappushpop", (interp, a, _) =>
        {
            var heap = (PyList)a[0];
            var item = a[1];
            if (heap.Items.Count > 0 && interp.Compare(heap.Items[0], item) < 0)
            {
                (item, heap.Items[0]) = (heap.Items[0], item);
                SiftUp(interp, heap, 0);
            }
            return item;
        });

        d["heapreplace"] = new PyBuiltinFunction("heapreplace", (interp, a, _) =>
        {
            var heap = (PyList)a[0];
            var returnItem = heap.Items[0];
            heap.Items[0] = a[1];
            SiftUp(interp, heap, 0);
            return returnItem;
        });

        return m;
    }

    private static void SiftDown(Interp interp, PyList heap, int startPos, int pos)
    {
        var newItem = heap.Items[pos];
        while (pos > startPos)
        {
            int parentPos = (pos - 1) >> 1;
            var parent = heap.Items[parentPos];
            if (interp.Compare(newItem, parent) < 0)
            {
                heap.Items[pos] = parent;
                pos = parentPos;
                continue;
            }
            break;
        }
        heap.Items[pos] = newItem;
    }

    private static void SiftUp(Interp interp, PyList heap, int pos)
    {
        int endPos = heap.Items.Count;
        int startPos = pos;
        var newItem = heap.Items[pos];
        int childPos = 2 * pos + 1;
        while (childPos < endPos)
        {
            int rightPos = childPos + 1;
            if (rightPos < endPos && !(interp.Compare(heap.Items[childPos], heap.Items[rightPos]) < 0))
                childPos = rightPos;
            heap.Items[pos] = heap.Items[childPos];
            pos = childPos;
            childPos = 2 * pos + 1;
        }
        heap.Items[pos] = newItem;
        SiftDown(interp, heap, startPos, pos);
    }
}
