// Copyright (c) 2026 Marco Parenzan
//
// Licensed under the MIT License. See the LICENSE file in the project
// root for full license information.

using PySharpLib.Runtime;
using System.Numerics;

namespace PySharpLib.Modules;

/// <summary>threading su thread .NET: Thread, Lock, RLock, Condition, Event, Timer.</summary>
public static class ThreadingModule
{
    public static PyModule Create()
    {
        var m = new PyModule("threading");
        var d = m.Dict;

        d["Thread"] = BuildThreadClass();
        d["Lock"] = BuildLockClass("Lock");
        d["RLock"] = BuildLockClass("RLock");
        d["Condition"] = BuildConditionClass();
        d["Event"] = BuildEventClass();
        d["Timer"] = BuildTimerClass();

        d["current_thread"] = new PyBuiltinFunction("current_thread", (_, _, _) =>
        {
            var cls = new PyClass("Thread", new List<PyClass>());
            var inst = new PyInstance(cls);
            inst.Dict["name"] = Thread.CurrentThread.Name ?? "MainThread";
            cls.Dict["name"] = inst.Dict["name"];
            return inst;
        });
        d["get_ident"] = new PyBuiltinFunction("get_ident", (_, _, _) =>
            new BigInteger(Environment.CurrentManagedThreadId));

        return m;
    }

    // ---------------------------------------------------------------- Thread

    private static PyClass BuildThreadClass()
    {
        var cls = new PyClass("Thread", new List<PyClass>());
        const string key = "__thread__";
        void Add(string name, BuiltinFn fn) => cls.Dict[name] = new PyBuiltinFunction($"Thread.{name}", fn);

        Add("__init__", (_, a, kwargs) =>
        {
            var inst = (PyInstance)a[0];
            inst.Dict["_target"] = Get(kwargs, "target") ?? PyNone.Instance;
            inst.Dict["_args"] = Get(kwargs, "args") ?? PyTuple.Empty;
            inst.Dict["_kwargs"] = Get(kwargs, "kwargs") ?? new PyDict();
            inst.Dict["name"] = Get(kwargs, "name") ?? "Thread";
            inst.Dict["daemon"] = Get(kwargs, "daemon") ?? true;
            return PyNone.Instance;
        });

        Add("start", (interp, a, _) =>
        {
            var inst = (PyInstance)a[0];
            var target = inst.Dict["_target"];
            var argsTuple = inst.Dict["_args"] as PyTuple ?? PyTuple.Empty;
            var kwargsDict = inst.Dict["_kwargs"] as PyDict;
            Dictionary<string, object>? kwargs = null;
            if (kwargsDict is { Count: > 0 })
            {
                kwargs = new Dictionary<string, object>();
                foreach (var e in kwargsDict.Entries)
                    kwargs[(string)e.Key] = e.Value;
            }

            object Run()
            {
                if (target is PyNone)
                {
                    // subclass with a run() method
                    return interp.CallMethod(inst, "run", Array.Empty<object>());
                }
                return interp.Call(target, argsTuple.Items, kwargs);
            }

            var thread = new Thread(() =>
            {
                try
                {
                    Run();
                }
                catch (PyRaise ex)
                {
                    interp.Out.Write($"Exception in thread: {PyErr.FormatForClr(ex.Value)}\n");
                }
            })
            {
                IsBackground = PyOps.Truthy(interp, inst.Dict.TryGet("daemon", out var dm) ? dm : true),
                Name = inst.Dict.TryGet("name", out var nm) ? nm as string : "Thread",
            };
            inst.Dict[key] = thread;
            thread.Start();
            return PyNone.Instance;
        });

        Add("join", (_, a, _) =>
        {
            var inst = (PyInstance)a[0];
            if (inst.Dict.TryGet(key, out var t) && t is Thread thread)
            {
                if (a.Length > 1 && a[1] is not PyNone)
                    thread.Join(TimeSpan.FromSeconds(PyOps.AsDouble(a[1])));
                else
                    thread.Join();
            }
            return PyNone.Instance;
        });

        Add("is_alive", (_, a, _) =>
        {
            var inst = (PyInstance)a[0];
            return inst.Dict.TryGet(key, out var t) && t is Thread thread && thread.IsAlive;
        });

        return cls;
    }

    private static object? Get(Dictionary<string, object>? kwargs, string name)
        => kwargs is not null && kwargs.TryGetValue(name, out var v) ? v : null;

    // ---------------------------------------------------------------- Lock / RLock

    private sealed class LockState
    {
        // Semaphore, not Monitor: a Python Lock can be released by a different thread.
        public readonly SemaphoreSlim Sem = new(1, 1);
        public int Depth;
        public int OwnerThread = -1;
    }

    private static PyClass BuildLockClass(string name)
    {
        var cls = new PyClass(name, new List<PyClass>());
        const string key = "__lock__";
        bool reentrant = name == "RLock";
        void Add(string method, BuiltinFn fn) => cls.Dict[method] = new PyBuiltinFunction($"{name}.{method}", fn);

        // State is created in __init__ to avoid races on lazy creation from multiple threads.
        Add("__init__", (_, a, _) =>
        {
            ((PyInstance)a[0]).Dict[key] = new LockState();
            return PyNone.Instance;
        });

        LockState L(object self)
        {
            var inst = (PyInstance)self;
            if (!inst.Dict.TryGet(key, out var v))
            {
                lock (inst)
                {
                    if (!inst.Dict.TryGet(key, out v))
                    {
                        v = new LockState();
                        inst.Dict[key] = v;
                    }
                }
            }
            return (LockState)v;
        }

        Add("acquire", (interp, a, kwargs) =>
        {
            var st = L(a[0]);
            bool blocking = a.Length > 1 ? PyOps.Truthy(interp, a[1])
                : Get(kwargs, "blocking") is { } b2 ? PyOps.Truthy(interp, b2) : true;
            double timeout = a.Length > 2 ? PyOps.AsDouble(a[2])
                : Get(kwargs, "timeout") is { } to && to is not PyNone ? PyOps.AsDouble(to) : -1;

            if (reentrant && st.OwnerThread == Environment.CurrentManagedThreadId)
            {
                st.Depth++;
                return true;
            }

            bool acquired = !blocking
                ? st.Sem.Wait(0)
                : timeout < 0
                    ? AcquireBlocking(st)
                    : st.Sem.Wait(TimeSpan.FromSeconds(timeout));

            if (acquired)
            {
                st.OwnerThread = Environment.CurrentManagedThreadId;
                st.Depth = 1;
            }
            return acquired;
        });

        Add("release", (_, a, _) =>
        {
            var st = L(a[0]);
            if (st.Depth == 0)
                throw PyErr.RuntimeError("release unlocked lock");
            if (reentrant && st.OwnerThread != Environment.CurrentManagedThreadId)
                throw PyErr.RuntimeError("cannot release un-acquired lock");
            st.Depth--;
            if (st.Depth == 0)
            {
                st.OwnerThread = -1;
                st.Sem.Release();
            }
            return PyNone.Instance;
        });

        Add("locked", (_, a, _) => L(a[0]).Depth > 0);
        Add("__enter__", (interp, a, _) =>
        {
            interp.CallMethod(a[0], "acquire", Array.Empty<object>());
            return a[0];
        });
        Add("__exit__", (interp, a, _) =>
        {
            interp.CallMethod(a[0], "release", Array.Empty<object>());
            return false;
        });

        return cls;
    }

    private static bool AcquireBlocking(LockState st)
    {
        st.Sem.Wait();
        return true;
    }

    // ---------------------------------------------------------------- Condition

    private static PyClass BuildConditionClass()
    {
        var cls = new PyClass("Condition", new List<PyClass>());
        const string key = "__cond__";
        void Add(string name, BuiltinFn fn) => cls.Dict[name] = new PyBuiltinFunction($"Condition.{name}", fn);

        object M(object self)
        {
            var inst = (PyInstance)self;
            if (!inst.Dict.TryGet(key, out var v))
            {
                lock (inst)
                {
                    if (!inst.Dict.TryGet(key, out v))
                    {
                        v = new object();
                        inst.Dict[key] = v;
                    }
                }
            }
            return v;
        }

        Add("__init__", (_, a, _) =>
        {
            ((PyInstance)a[0]).Dict[key] = new object();
            return PyNone.Instance;
        });
        Add("acquire", (_, a, _) =>
        {
            System.Threading.Monitor.Enter(M(a[0]));
            return true;
        });
        Add("release", (_, a, _) =>
        {
            System.Threading.Monitor.Exit(M(a[0]));
            return PyNone.Instance;
        });
        Add("wait", (_, a, _) =>
        {
            if (a.Length > 1 && a[1] is not PyNone)
                return System.Threading.Monitor.Wait(M(a[0]), TimeSpan.FromSeconds(PyOps.AsDouble(a[1])));
            return System.Threading.Monitor.Wait(M(a[0]));
        });
        Add("notify", (_, a, _) =>
        {
            System.Threading.Monitor.Pulse(M(a[0]));
            return PyNone.Instance;
        });
        Add("notify_all", (_, a, _) =>
        {
            System.Threading.Monitor.PulseAll(M(a[0]));
            return PyNone.Instance;
        });
        Add("__enter__", (_, a, _) =>
        {
            System.Threading.Monitor.Enter(M(a[0]));
            return a[0];
        });
        Add("__exit__", (_, a, _) =>
        {
            System.Threading.Monitor.Exit(M(a[0]));
            return false;
        });
        return cls;
    }

    // ---------------------------------------------------------------- Event

    private static PyClass BuildEventClass()
    {
        var cls = new PyClass("Event", new List<PyClass>());
        const string key = "__event__";
        void Add(string name, BuiltinFn fn) => cls.Dict[name] = new PyBuiltinFunction($"Event.{name}", fn);

        Add("__init__", (_, a, _) =>
        {
            ((PyInstance)a[0]).Dict[key] = new ManualResetEventSlim(false);
            return PyNone.Instance;
        });

        ManualResetEventSlim E(object self)
        {
            var inst = (PyInstance)self;
            if (!inst.Dict.TryGet(key, out var v))
            {
                lock (inst)
                {
                    if (!inst.Dict.TryGet(key, out v))
                    {
                        v = new ManualResetEventSlim(false);
                        inst.Dict[key] = v;
                    }
                }
            }
            return (ManualResetEventSlim)v;
        }

        Add("set", (_, a, _) =>
        {
            E(a[0]).Set();
            return PyNone.Instance;
        });
        Add("clear", (_, a, _) =>
        {
            E(a[0]).Reset();
            return PyNone.Instance;
        });
        Add("is_set", (_, a, _) => E(a[0]).IsSet);
        Add("wait", (_, a, _) =>
        {
            if (a.Length > 1 && a[1] is not PyNone)
                return E(a[0]).Wait(TimeSpan.FromSeconds(PyOps.AsDouble(a[1])));
            E(a[0]).Wait();
            return true;
        });
        return cls;
    }

    // ---------------------------------------------------------------- Timer

    private static PyClass BuildTimerClass()
    {
        var cls = new PyClass("Timer", new List<PyClass>());
        void Add(string name, BuiltinFn fn) => cls.Dict[name] = new PyBuiltinFunction($"Timer.{name}", fn);

        Add("__init__", (_, a, _) =>
        {
            var inst = (PyInstance)a[0];
            inst.Dict["interval"] = a[1];
            inst.Dict["function"] = a[2];
            inst.Dict["_cancelled"] = false;
            return PyNone.Instance;
        });
        Add("start", (interp, a, _) =>
        {
            var inst = (PyInstance)a[0];
            double interval = PyOps.AsDouble(inst.Dict["interval"]);
            var fn = inst.Dict["function"];
            var thread = new Thread(() =>
            {
                Thread.Sleep(TimeSpan.FromSeconds(interval));
                if (!PyOps.Truthy(interp, inst.Dict["_cancelled"]))
                {
                    try
                    {
                        interp.Call(fn, Array.Empty<object>());
                    }
                    catch (PyRaise)
                    {
                        // ignora
                    }
                }
            })
            { IsBackground = true };
            thread.Start();
            return PyNone.Instance;
        });
        Add("cancel", (_, a, _) =>
        {
            ((PyInstance)a[0]).Dict["_cancelled"] = true;
            return PyNone.Instance;
        });
        return cls;
    }
}
