// Copyright (c) 2026 Marco Parenzan
//
// Licensed under the MIT License. See the LICENSE file in the project
// root for full license information.

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

    public static readonly PyClass GeneratorContextManagerClass = BuildGeneratorContextManagerClass();
    public static readonly PyClass SuppressClass = BuildSuppressClass();
    public static readonly PyClass AbstractContextManagerClass = BuildAbstractContextManagerClass("AbstractContextManager", "__enter__", "__exit__");
    public static readonly PyClass AbstractAsyncContextManagerClass = BuildAbstractContextManagerClass("AbstractAsyncContextManager", "__aenter__", "__aexit__");

    public static PyModule Create()
    {
        var m = new PyModule("contextlib");
        var d = m.Dict;

        d["AbstractContextManager"] = AbstractContextManagerClass;
        d["AbstractAsyncContextManager"] = AbstractAsyncContextManagerClass;

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

        d["suppress"] = new PyBuiltinFunction("suppress", (_, a, _) =>
        {
            var inst = new PyInstance(SuppressClass);
            inst.Dict[ExcTypesKey] = a;
            return inst;
        });

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
}
