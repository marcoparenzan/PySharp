// Copyright (c) 2026 Marco Parenzan
//
// Licensed under the MIT License. See the LICENSE file in the project
// root for full license information.

using System.Collections.Concurrent;
using System.Numerics;
using PySharpLib.Runtime;

namespace PySharpLib.Modules;

/// <summary>queue: a real, thread-safe FIFO queue for cross-OS-thread producer/consumer use —
/// backed by System.Collections.Concurrent.BlockingCollection (real blocking put/get with real
/// timeouts), not PySharp's asyncio.Queue (which is for cooperative single-threaded coroutines on
/// one thread — a genuinely different primitive for a genuinely different use case). Found via
/// anyio's real `from queue import Queue` (_backends/_asyncio.py, for its worker-thread pool
/// result handoff), reachable from `import starlette`. See FASTAPI_PLAN.md Phase 3.</summary>
public static class QueueModule
{
    public static readonly PyClass EmptyClass = new("Empty", new List<PyClass> { PyErr.Exception });
    public static readonly PyClass FullClass = new("Full", new List<PyClass> { PyErr.Exception });

    private const string CollectionKey = "__coll__";
    private const string UnfinishedKey = "__unfinished__";
    private const string JoinLockKey = "__joinlock__";

    public static PyModule Create()
    {
        var m = new PyModule("queue");
        var d = m.Dict;
        d["Empty"] = EmptyClass;
        d["Full"] = FullClass;
        d["Queue"] = BuildQueueClass();
        d["SimpleQueue"] = BuildQueueClass();
        d["LifoQueue"] = BuildQueueClass();
        return m;
    }

    private static PyClass BuildQueueClass()
    {
        var cls = new PyClass("Queue", new List<PyClass>());
        void Add(string name, BuiltinFn fn) => cls.Dict[name] = new PyBuiltinFunction($"Queue.{name}", fn);

        BlockingCollection<object> Coll(object self) => (BlockingCollection<object>)((PyInstance)self).Dict[CollectionKey];

        Add("__init__", (_, a, kwargs) =>
        {
            var inst = (PyInstance)a[0];
            int maxsize = a.Length > 1 ? (int)PyOps.AsBigInt(a[1], "maxsize")
                : kwargs is not null && kwargs.TryGetValue("maxsize", out var ms) ? (int)PyOps.AsBigInt(ms, "maxsize")
                : 0;
            inst.Dict[CollectionKey] = maxsize > 0
                ? new BlockingCollection<object>(maxsize)
                : new BlockingCollection<object>();
            inst.Dict[UnfinishedKey] = (BigInteger)0;
            inst.Dict[JoinLockKey] = new object();
            return PyNone.Instance;
        });

        Add("put", (_, a, kwargs) =>
        {
            var inst = (PyInstance)a[0];
            bool block = a.Length > 2 ? a[2] is true : !(kwargs is not null && kwargs.TryGetValue("block", out var b) && b is false);
            double? timeout = TimeoutArg(a, kwargs, 3);
            bool ok;
            if (!block)
            {
                ok = Coll(inst).TryAdd(a[1]);
            }
            else if (timeout is null)
            {
                Coll(inst).Add(a[1]);
                ok = true;
            }
            else
            {
                ok = Coll(inst).TryAdd(a[1], (int)(timeout.Value * 1000));
            }
            if (!ok)
                throw new PyRaise(PyErr.MakeInstance(FullClass));
            lock (inst.Dict[JoinLockKey]!)
                inst.Dict[UnfinishedKey] = (BigInteger)inst.Dict[UnfinishedKey] + 1;
            return PyNone.Instance;
        });
        Add("put_nowait", (interp, a, _) => interp.CallMethod(a[0], "put", new[] { a[1], (object)false }));

        Add("get", (_, a, kwargs) =>
        {
            var inst = (PyInstance)a[0];
            bool block = a.Length > 1 ? a[1] is true : !(kwargs is not null && kwargs.TryGetValue("block", out var b) && b is false);
            double? timeout = TimeoutArg(a, kwargs, 2);
            object item;
            bool ok;
            if (!block)
            {
                ok = Coll(inst).TryTake(out item!);
            }
            else if (timeout is null)
            {
                item = Coll(inst).Take();
                ok = true;
            }
            else
            {
                ok = Coll(inst).TryTake(out item!, (int)(timeout.Value * 1000));
            }
            if (!ok)
                throw new PyRaise(PyErr.MakeInstance(EmptyClass));
            return item;
        });
        Add("get_nowait", (interp, a, _) => interp.CallMethod(a[0], "get", new object[] { false }));

        Add("qsize", (_, a, _) => (BigInteger)Coll(a[0]).Count);
        Add("empty", (_, a, _) => Coll(a[0]).Count == 0);
        Add("full", (_, a, _) => Coll(a[0]).BoundedCapacity > 0 && Coll(a[0]).Count >= Coll(a[0]).BoundedCapacity);

        Add("task_done", (_, a, _) =>
        {
            var inst = (PyInstance)a[0];
            lock (inst.Dict[JoinLockKey]!)
            {
                var remaining = (BigInteger)inst.Dict[UnfinishedKey] - 1;
                inst.Dict[UnfinishedKey] = remaining;
                if (remaining <= 0)
                    System.Threading.Monitor.PulseAll(inst.Dict[JoinLockKey]!);
            }
            return PyNone.Instance;
        });
        Add("join", (_, a, _) =>
        {
            var inst = (PyInstance)a[0];
            lock (inst.Dict[JoinLockKey]!)
            {
                while ((BigInteger)inst.Dict[UnfinishedKey] > 0)
                    System.Threading.Monitor.Wait(inst.Dict[JoinLockKey]!);
            }
            return PyNone.Instance;
        });

        return cls;
    }

    private static double? TimeoutArg(object[] a, Dictionary<string, object>? kwargs, int posIndex)
    {
        object? t = a.Length > posIndex ? a[posIndex] : (kwargs is not null && kwargs.TryGetValue("timeout", out var v) ? v : null);
        return t switch
        {
            null or PyNone => null,
            double d => d,
            long l => l,
            int i => i,
            BigInteger bi => (double)bi,
            _ => null,
        };
    }
}
