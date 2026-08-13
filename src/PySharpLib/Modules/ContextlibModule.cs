// Copyright (c) 2026 Marco Parenzan
//
// Licensed under the MIT License. See the LICENSE file in the project
// root for full license information.

using PySharpLib.Interpretation;
using PySharpLib.Runtime;

namespace PySharpLib.Modules;

/// <summary>
/// contextlib: contextmanager (generator-backed context managers, driven via
/// PyGenerator.MoveNext/ThrowInto) and suppress. v1 scope — only what real-world
/// scenario scripts have been seen to use (see AIOMQTT_PLAN.md).
/// </summary>
public static class ContextlibModule
{
    private const string GenKey = "__gen__";
    private const string ExcTypesKey = "__exc_types__";

    private const string AsyncFnKey = "__async_fn__";
    private const string AsyncArgsKey = "__async_args__";
    private const string AsyncKwargsKey = "__async_kwargs__";

    public static readonly PyClass GeneratorContextManagerClass = BuildGeneratorContextManagerClass();
    public static readonly PyClass AsyncGeneratorContextManagerClass = BuildAsyncGeneratorContextManagerClass();
    public static readonly PyClass SuppressClass = BuildSuppressClass();
    public static readonly PyClass AbstractContextManagerClass = BuildAbstractContextManagerClass("AbstractContextManager", "__enter__", "__exit__");
    public static readonly PyClass AbstractAsyncContextManagerClass = BuildAbstractContextManagerClass("AbstractAsyncContextManager", "__aenter__", "__aexit__");
    public static readonly PyClass ExitStackClass = BuildExitStackClass(isAsync: false);
    public static readonly PyClass AsyncExitStackClass = BuildExitStackClass(isAsync: true);

    public static PyModule Create()
    {
        var m = new PyModule("contextlib");
        var d = m.Dict;

        d["AbstractContextManager"] = AbstractContextManagerClass;
        d["AbstractAsyncContextManager"] = AbstractAsyncContextManagerClass;
        d["ExitStack"] = ExitStackClass;
        d["AsyncExitStack"] = AsyncExitStackClass;

        d["contextmanager"] = new PyBuiltinFunction("contextmanager", (interp, a, _) =>
        {
            if (a.Length < 1)
                throw PyErr.TypeError("contextmanager() missing 1 required positional argument: 'func'");
            var genFn = a[0];

            return new PyBuiltinFunction("contextmanager_wrapper", (interp2, callArgs, callKwargs) =>
            {
                if (interp2.Call(genFn, callArgs, callKwargs) is not PyGenerator gen)
                    throw PyErr.TypeError("@contextmanager function must be a generator function (use 'yield')");
                var inst = new PyInstance(GeneratorContextManagerClass);
                inst.Dict[GenKey] = gen;
                return inst;
            });
        });

        // Real CPython's async counterpart of @contextmanager, wrapping an async-generator
        // function. Applying the decorator (module-definition time — what `import starlette`
        // actually exercises, via starlette._utils's real `@asynccontextmanager async def
        // create_collapsing_task_group(): ... yield tg ...`) works for real: it just wraps the
        // function reference. Actually *entering* the resulting context manager needs to drive a
        // real async generator, which PySharp doesn't support yet (see ROADMAP.md's Axis A gap
        // list) — __aenter__/__aexit__ raise a clear NotImplementedError instead of hanging or
        // silently misbehaving, the same honest-limitation shape as AsyncExitStack's suspending-
        // coroutine case.
        d["asynccontextmanager"] = new PyBuiltinFunction("asynccontextmanager", (interp, a, _) =>
        {
            if (a.Length < 1)
                throw PyErr.TypeError("asynccontextmanager() missing 1 required positional argument: 'func'");
            var genFn = a[0];

            return new PyBuiltinFunction("asynccontextmanager_wrapper", (_, callArgs, callKwargs) =>
            {
                var inst = new PyInstance(AsyncGeneratorContextManagerClass);
                inst.Dict[AsyncFnKey] = genFn;
                inst.Dict[AsyncArgsKey] = callArgs;
                inst.Dict[AsyncKwargsKey] = (object?)callKwargs ?? PyNone.Instance;
                return inst;
            });
        });

        d["suppress"] = new PyBuiltinFunction("suppress", (_, a, _) =>
        {
            var inst = new PyInstance(SuppressClass);
            inst.Dict[ExcTypesKey] = a;
            return inst;
        });

        // Real CPython `contextlib.nullcontext(enter_result=None)`: a real do-nothing context
        // manager (sync *and* async, since Python 3.10) — `__enter__`/`__aenter__` just hand back
        // `enter_result`. Found via real sqlalchemy's own async-engine plumbing, which uses it as a
        // placeholder context when no real lock/transaction is needed.
        var nullContextClass = new PyClass("nullcontext", new List<PyClass>());
        nullContextClass.Dict["__init__"] = new PyBuiltinFunction("nullcontext.__init__", (_, a, _) =>
        {
            ((PyInstance)a[0]).Dict["enter_result"] = a.Length > 1 ? a[1] : PyNone.Instance;
            return PyNone.Instance;
        });
        nullContextClass.Dict["__enter__"] = new PyBuiltinFunction("nullcontext.__enter__",
            (_, a, _) => ((PyInstance)a[0]).Dict["enter_result"]);
        nullContextClass.Dict["__exit__"] = new PyBuiltinFunction("nullcontext.__exit__", (_, _, _) => false);
        nullContextClass.Dict["__aenter__"] = new PyBuiltinFunction("nullcontext.__aenter__",
            (_, a, _) => ((PyInstance)a[0]).Dict["enter_result"]);
        nullContextClass.Dict["__aexit__"] = new PyBuiltinFunction("nullcontext.__aexit__", (_, _, _) => false);
        d["nullcontext"] = nullContextClass;

        return m;
    }

    private static PyClass BuildGeneratorContextManagerClass()
    {
        var cls = new PyClass("_GeneratorContextManager", new List<PyClass>());
        void Add(string name, BuiltinFn fn) => cls.Dict[name] = new PyBuiltinFunction($"_GeneratorContextManager.{name}", fn);

        Add("__enter__", (interp, a, _) =>
        {
            var gen = Gen(a[0]);
            if (!gen.MoveNext(interp, out var value))
                throw PyErr.RuntimeError("generator didn't yield");
            return value;
        });

        Add("__exit__", (interp, a, _kwargs) =>
        {
            var gen = Gen(a[0]);

            if (a[1] is PyNone)
            {
                if (gen.MoveNext(interp, out _))
                    throw PyErr.RuntimeError("generator didn't stop");
                return false;
            }

            var excInstance = (PyInstance)a[2];
            try
            {
                if (gen.ThrowInto(interp, excInstance, out _))
                    throw PyErr.RuntimeError("generator didn't stop after throw()");
                // The generator caught the exception and returned normally: suppress it,
                // exactly like a bare `except:` around the `yield` would in CPython.
                return true;
            }
            catch (PyRaise ex) when (ReferenceEquals(ex.Value, excInstance))
            {
                // The generator let the same exception propagate unchanged: don't suppress it,
                // the `with` statement re-raises the original.
                return false;
            }
            // A *different* exception raised out of the generator propagates from here,
            // replacing the original with-block exception (matches CPython).
        });

        return cls;
    }

    private static PyGenerator Gen(object self) =>
        (PyGenerator)((PyInstance)self).Dict[GenKey];

    private const string AsyncGenKey = "__agen__";

    /// <summary>
    /// Real __aenter__/__aexit__, driving a real PyAsyncGenerator (Runtime/Async.cs) — mirrors
    /// BuildGeneratorContextManagerClass's sync __enter__/__exit__ exactly (MoveNext/ThrowInto
    /// there ↔ ANext/AThrow here), just wrapped in the Future-continuation shape every async
    /// builtin here uses instead of blocking the calling thread. Previously __aenter__/__aexit__
    /// raised NotImplementedError since PySharp had no real async generators to drive — now it
    /// does (see AsyncGeneratorTests.cs), so this is real, not a stub.
    /// </summary>
    private static PyClass BuildAsyncGeneratorContextManagerClass()
    {
        var cls = new PyClass("_AsyncGeneratorContextManager", new List<PyClass>());
        void Add(string name, BuiltinFn fn) => cls.Dict[name] = new PyBuiltinFunction($"_AsyncGeneratorContextManager.{name}", fn);

        Add("__aenter__", (interp, a, _) =>
        {
            var inst = (PyInstance)a[0];
            var genFn = inst.Dict[AsyncFnKey];
            var args = (object[])inst.Dict[AsyncArgsKey];
            var callKwargs = inst.Dict[AsyncKwargsKey] is Dictionary<string, object> kw ? kw : null;
            var agen = (PyAsyncGenerator)interp.Call(genFn, args, callKwargs);
            inst.Dict[AsyncGenKey] = agen;

            var inner = agen.ANext(interp);
            var outer = new PyFuture { Loop = PyEventLoop.Running };
            inner.AddNativeCallback(() =>
            {
                if (inner.Exception is { } stop && PyErr.Matches(stop.Value, PyErr.StopAsyncIterationClass))
                    outer.SetException(new PyRaise(PyErr.MakeInstance(PyErr.RuntimeErrorClass, "generator didn't yield")));
                else if (inner.Exception is { } ex)
                    outer.SetException(ex);
                else
                    outer.SetResult(inner.GetResult());
            });
            return outer;
        });

        Add("__aexit__", (interp, a, _) =>
        {
            var inst = (PyInstance)a[0];
            var agen = (PyAsyncGenerator)inst.Dict[AsyncGenKey];
            var outer = new PyFuture { Loop = PyEventLoop.Running };

            if (a[1] is PyNone)
            {
                var inner = agen.ANext(interp);
                inner.AddNativeCallback(() =>
                {
                    if (inner.Exception is { } stop && PyErr.Matches(stop.Value, PyErr.StopAsyncIterationClass))
                        outer.SetResult(false);
                    else if (inner.Exception is { } ex)
                        outer.SetException(ex);
                    else
                        outer.SetException(new PyRaise(PyErr.MakeInstance(PyErr.RuntimeErrorClass, "generator didn't stop")));
                });
                return outer;
            }

            var excInstance = (PyInstance)a[2];
            var thrown = agen.AThrow(interp, new PyRaise(excInstance));
            thrown.AddNativeCallback(() =>
            {
                if (thrown.Exception is { } stop && PyErr.Matches(stop.Value, PyErr.StopAsyncIterationClass))
                    outer.SetResult(true); // caught inside the body, which then returned: suppress
                else if (thrown.Exception is { } same && ReferenceEquals(same.Value, excInstance))
                    outer.SetResult(false); // let the same exception propagate unchanged: don't suppress
                else if (thrown.Exception is { } different)
                    outer.SetException(different); // a different exception replaces the original
                else
                    outer.SetException(new PyRaise(PyErr.MakeInstance(PyErr.RuntimeErrorClass, "generator didn't stop after athrow()")));
            });
            return outer;
        });

        return cls;
    }

    private static PyClass BuildSuppressClass()
    {
        var cls = new PyClass("suppress", new List<PyClass>());
        void Add(string name, BuiltinFn fn) => cls.Dict[name] = new PyBuiltinFunction($"suppress.{name}", fn);

        Add("__enter__", (_, _, _) => PyNone.Instance);

        Add("__exit__", (_, a, _) =>
        {
            if (a[1] is PyNone)
                return false;
            var excInstance = (PyInstance)a[2];
            var types = (object[])((PyInstance)a[0]).Dict[ExcTypesKey];
            foreach (var t in types)
            {
                if (t is PyClass excClass && PyErr.Matches(excInstance, excClass))
                    return true;
            }
            return false;
        });

        return cls;
    }

    /// <summary>
    /// contextlib.AbstractContextManager / AbstractAsyncContextManager: a base to subclass and
    /// override, not something used directly — default __enter__/__exit__ (or __aenter__/__aexit__)
    /// match CPython's (enter returns self, exit is a no-op that doesn't suppress). The async pair
    /// return an already-resolved Future so `await mgr.__aenter__()` works without a real event loop
    /// registering a callback.
    /// </summary>
    private static PyClass BuildAbstractContextManagerClass(string className, string enterName, string exitName)
    {
        var cls = new PyClass(className, new List<PyClass>());
        bool isAsync = enterName == "__aenter__";

        object Wrap(object value) => isAsync
            ? MakeResolvedFuture(value)
            : value;

        cls.Dict[enterName] = new PyBuiltinFunction($"{className}.{enterName}", (_, a, _) => Wrap(a[0]));
        cls.Dict[exitName] = new PyBuiltinFunction($"{className}.{exitName}", (_, _, _) => Wrap(false));
        return cls;
    }

    private static PyFuture MakeResolvedFuture(object result)
    {
        var fut = new PyFuture { Loop = PyEventLoop.Running };
        fut.SetResult(result);
        return fut;
    }

    private const string CallbacksKey = "__callbacks__";

    /// <summary>
    /// contextlib.ExitStack / AsyncExitStack: real callback-stack semantics (enter_context/push/
    /// callback/pop_all/close, unwound in LIFO order on __exit__, matching real CPython) — not a
    /// stub. The async variant's async-specific entry points (enter_async_context/push_async_exit/
    /// push_async_callback/aclose) support context managers whose __aenter__/__aexit__ resolve
    /// immediately (an already-resolved Future, or a plain value — the shape every async context
    /// manager written in this codebase so far actually has); one whose __aenter__/__aexit__ is a
    /// real *suspending* coroutine raises NotImplementedError rather than silently hanging or
    /// misbehaving, since driving an arbitrary inner coroutine to completion from a plain builtin
    /// function (outside the calling coroutine's own suspension loop) isn't supported yet — a real,
    /// clearly-scoped limitation, not attempted blind. Found via anyio's real `AsyncExitStack()`
    /// usage (abc/_sockets.py) — referenced but not yet exercised beyond import, so this is ahead of
    /// what's been observed to actually run; kept honest about that gap rather than guessed at.
    /// See FASTAPI_PLAN.md.
    /// </summary>
    private static PyClass BuildExitStackClass(bool isAsync)
    {
        string className = isAsync ? "AsyncExitStack" : "ExitStack";
        var cls = new PyClass(className, new List<PyClass>());
        void Add(string n, BuiltinFn fn) => cls.Dict[n] = new PyBuiltinFunction($"{className}.{n}", fn);

        static PyList Callbacks(PyInstance inst)
        {
            if (!inst.Dict.TryGet(CallbacksKey, out var v) || v is not PyList list)
            {
                list = new PyList();
                inst.Dict[CallbacksKey] = list;
            }
            return list;
        }

        object UnwrapAsync(object awaited)
        {
            switch (awaited)
            {
                case PyFuture { IsDone: true } f:
                    return f.GetResult();
                case PyFuture:
                case PyCoroutine:
                    throw PyErr.NotImplementedError(
                        $"{className}: only already-resolved async context managers/callbacks are supported so far");
                default:
                    return awaited;
            }
        }

        Add("__init__", (_, a, _) =>
        {
            Callbacks((PyInstance)a[0]).Items.Clear();
            return PyNone.Instance;
        });
        Add(isAsync ? "__aenter__" : "__enter__", (_, a, _) =>
            isAsync ? MakeResolvedFuture(a[0]) : a[0]);

        object RunExit(Interp interp, PyInstance inst, object excType, object exc, object tb)
        {
            var callbacks = Callbacks(inst).Items;
            bool suppressed = false;
            while (callbacks.Count > 0)
            {
                var cb = callbacks[^1];
                callbacks.RemoveAt(callbacks.Count - 1);
                var result = interp.Call(cb, new[] { excType, exc, tb });
                if (isAsync)
                    result = UnwrapAsync(result);
                if (PyOps.Truthy(interp, result))
                    suppressed = true;
            }
            return suppressed;
        }

        Add(isAsync ? "__aexit__" : "__exit__", (interp, a, _) =>
        {
            var inst = (PyInstance)a[0];
            object excType = a.Length > 1 ? a[1] : PyNone.Instance;
            object exc = a.Length > 2 ? a[2] : PyNone.Instance;
            object tb = a.Length > 3 ? a[3] : PyNone.Instance;
            var suppressed = RunExit(interp, inst, excType, exc, tb);
            return isAsync ? MakeResolvedFuture(suppressed) : suppressed;
        });
        Add(isAsync ? "aclose" : "close", (interp, a, _) =>
        {
            var inst = (PyInstance)a[0];
            RunExit(interp, inst, PyNone.Instance, PyNone.Instance, PyNone.Instance);
            return isAsync ? MakeResolvedFuture(PyNone.Instance) : PyNone.Instance;
        });

        Add("enter_context", (interp, a, _) =>
        {
            var inst = (PyInstance)a[0];
            var cm = a[1];
            var entered = interp.CallMethod(cm, "__enter__", Array.Empty<object>());
            Callbacks(inst).Items.Add(new PyBuiltinFunction("_exit_wrapper", (interp2, exitArgs, _) =>
                interp2.CallMethod(cm, "__exit__", exitArgs)));
            return entered;
        });
        Add("push", (interp, a, _) =>
        {
            var inst = (PyInstance)a[0];
            var exitObj = a[1];
            if (interp.TryGetAttr(exitObj, "__exit__", out var unusedExitAttr))
                Callbacks(inst).Items.Add(new PyBuiltinFunction("_exit_wrapper", (interp2, exitArgs, _) =>
                    interp2.CallMethod(exitObj, "__exit__", exitArgs)));
            else
                Callbacks(inst).Items.Add(exitObj);
            return exitObj;
        });
        Add("callback", (interp, a, kwargs) =>
        {
            var inst = (PyInstance)a[0];
            var fn = a[1];
            var extraArgs = a.Skip(2).ToArray();
            Callbacks(inst).Items.Add(new PyBuiltinFunction("_callback_wrapper", (interp2, _, _) =>
                interp2.Call(fn, extraArgs, kwargs)));
            return fn;
        });
        Add("pop_all", (_, a, _) =>
        {
            var inst = (PyInstance)a[0];
            var newInst = new PyInstance(cls);
            var newList = new PyList(Callbacks(inst).Items);
            newInst.Dict[CallbacksKey] = newList;
            Callbacks(inst).Items.Clear();
            return newInst;
        });

        if (isAsync)
        {
            Add("enter_async_context", (interp, a, _) =>
            {
                var inst = (PyInstance)a[0];
                var cm = a[1];
                var entered = UnwrapAsync(interp.CallMethod(cm, "__aenter__", Array.Empty<object>()));
                Callbacks(inst).Items.Add(new PyBuiltinFunction("_aexit_wrapper", (interp2, exitArgs, _) =>
                    interp2.CallMethod(cm, "__aexit__", exitArgs)));
                return MakeResolvedFuture(entered);
            });
            Add("push_async_exit", (interp, a, _) =>
            {
                var inst = (PyInstance)a[0];
                var exitObj = a[1];
                if (interp.TryGetAttr(exitObj, "__aexit__", out var unusedAexitAttr))
                    Callbacks(inst).Items.Add(new PyBuiltinFunction("_aexit_wrapper", (interp2, exitArgs, _) =>
                        interp2.CallMethod(exitObj, "__aexit__", exitArgs)));
                else
                    Callbacks(inst).Items.Add(exitObj);
                return exitObj;
            });
            Add("push_async_callback", (interp, a, kwargs) =>
            {
                var inst = (PyInstance)a[0];
                var fn = a[1];
                var extraArgs = a.Skip(2).ToArray();
                Callbacks(inst).Items.Add(new PyBuiltinFunction("_acallback_wrapper", (interp2, _, _) =>
                    interp2.Call(fn, extraArgs, kwargs)));
                return fn;
            });
        }

        return cls;
    }
}
