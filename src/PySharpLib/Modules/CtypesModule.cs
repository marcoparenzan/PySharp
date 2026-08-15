// Copyright (c) 2026 Marco Parenzan
//
// Licensed under the MIT License. See the LICENSE file in the project
// root for full license information.

using System.Numerics;
using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.InteropServices;
using System.Text;
using PySharpLib.Interpretation;
using PySharpLib.Runtime;

namespace PySharpLib.Modules;

/// <summary>
/// ctypes-lite: loading native DLLs via NativeLibrary and calling functions with a DynamicMethod +
/// calli-based thunk. Supports scalar types, real `Structure`/`byref`/`POINTER` (see CTYPES_PLAN.md
/// Phase 1), `char*`/`wchar*` strings, and real `CFUNCTYPE`/`WINFUNCTYPE` callbacks (Phase 2) — native
/// code calling back into a Python function via a dynamically-built native trampoline.
///
/// Design: every non-string ctypes value (`c_int`, `c_ulong`, a `Structure` subclass, ...) is backed
/// by a real C# `byte[]` buffer, not `Marshal.AllocHGlobal` — reading/writing a field decodes/encodes
/// bytes at a computed offset, and `byref()` pins that same managed array
/// (`GCHandle.Alloc(..., Pinned)`) for the duration of one native call. A native function's writes
/// land directly in the pinned managed buffer, so there is no separate "marshal back" step.
/// </summary>
public static class CtypesModule
{
    private const string HandleKey = "__dllhandle__";
    private const string NameKey = "__dllname__";
    private const string FnPtrKey = "__fnptr__";
    private const string CTypeKey = "__ctype__";
    private const string BufferKey = "__buffer__";
    private const string WideKey = "__wide__";
    private const string ByRefTargetKey = "__byreftarget__";
    private const string LayoutCacheKey = "__layout_cache__";
    private const string CallbackDelegateKey = "__callback_delegate__";

    /// <summary>Cache of calli thunks per signature.</summary>
    private static readonly Dictionary<string, Func<IntPtr, object?[], object?>> ThunkCache = new();

    /// <summary>Every ctypes-specific `PyClass` (`Structure`, `_CArgObject`/`byref`, the char-buffer
    /// `Array` class) is a real LOCAL variable inside `Create()`, threaded through as a parameter to
    /// every static method that needs it — deliberately *not* a static field. `Create()` runs once
    /// per `import ctypes` (once per `PyEngine`), and xUnit runs tests in parallel: a static field
    /// here would let one test's concurrent `Create()` call silently overwrite the `PyClass` object
    /// another test's in-flight script had already captured (e.g. inside a `Structure` subclass's own
    /// `Bases`), breaking identity checks like `cls.Mro.Contains(structureClass)` — a real bug found
    /// and fixed this same way surfaces repeatedly across this project's own history (see
    /// FASTAPI_PLAN.md's `GenericAliasModule.OriginMap`/`GenericPlaceholder` races).</summary>
    public static PyModule Create()
    {
        var m = new PyModule("ctypes");
        var d = m.Dict;

        // c_* classes: each is a PyClass with the type code in __ctype__
        foreach (var (name, code) in new[]
        {
            ("c_bool", "i1"), ("c_byte", "i1"), ("c_ubyte", "u1"),
            ("c_short", "i2"), ("c_ushort", "u2"),
            ("c_int", "i4"), ("c_uint", "u4"), ("c_long", "i4"), ("c_ulong", "u4"),
            ("c_longlong", "i8"), ("c_ulonglong", "u8"), ("c_size_t", "u8"), ("c_ssize_t", "i8"),
            ("c_float", "f4"), ("c_double", "f8"),
            ("c_char_p", "s"), ("c_wchar_p", "w"), ("c_void_p", "p"),
        })
        {
            d[name] = code is "s" or "w" ? BuildStringPointerClass(name, code) : BuildBufferBackedClass(name, code);
        }

        PyClass structureClass = BuildStructureClass();
        d["Structure"] = structureClass;

        PyClass byRefClass = new("_CArgObject", new List<PyClass>());
        d["byref"] = new PyBuiltinFunction("byref", (_, a, _) =>
        {
            var inst = new PyInstance(byRefClass);
            inst.Dict[ByRefTargetKey] = a[0];
            return inst;
        });

        d["POINTER"] = new PyBuiltinFunction("POINTER", (_, a, _) => MakePointerType(a[0]));

        PyClass charBufferClass = BuildCharBufferClass();
        d["create_string_buffer"] = new PyBuiltinFunction("create_string_buffer",
            (_, a, _) => CreateCharBuffer(a, wide: false, charBufferClass));
        d["create_unicode_buffer"] = new PyBuiltinFunction("create_unicode_buffer",
            (_, a, _) => CreateCharBuffer(a, wide: true, charBufferClass));

        var funcClass = BuildFuncClass(byRefClass, charBufferClass);
        var dllClass = BuildDllClass(funcClass);
        d["CDLL"] = dllClass;
        d["WinDLL"] = dllClass;
        d["windll"] = MakeLoaderNamespace(dllClass);
        d["cdll"] = MakeLoaderNamespace(dllClass);

        d["sizeof"] = new PyBuiltinFunction("sizeof", (_, a, _) => new BigInteger(SizeOfArg(a[0], structureClass)));

        // Real ctypes: CFUNCTYPE(restype, *argtypes)/WINFUNCTYPE(restype, *argtypes) both return a
        // callable "prototype" type; calling *that* with a Python callable wraps it as a real native
        // function pointer callbacks can be passed through. Real CPython distinguishes cdecl
        // (CFUNCTYPE) from stdcall (WINFUNCTYPE) on 32-bit Windows; on x64 (this project's only
        // target) there is a single unified calling convention, so both are aliases here — same
        // practical-subset choice this module's forward-call path already makes (always `Winapi`).
        var callbackFn = new PyBuiltinFunction("CFUNCTYPE", (_, a, _) => MakeCallbackType(a));
        d["CFUNCTYPE"] = callbackFn;
        d["WINFUNCTYPE"] = callbackFn;

        d["get_last_error"] = new PyBuiltinFunction("get_last_error", (_, _, _) =>
            new BigInteger(Marshal.GetLastWin32Error()));

        return m;
    }

    // ---------------------------------------------------------------- buffer-backed scalar types

    /// <summary>A numeric or pointer-sized ctype (`c_int`, `c_ulong`, `c_void_p`, ...): a real 1-16
    /// byte buffer backs `.value`, so the same instance can be passed to `byref()` and have a native
    /// function's write show up on the next `.value` read — no separate marshal-back step.</summary>
    private static PyClass BuildBufferBackedClass(string name, string code)
    {
        var cls = new PyClass(name, new List<PyClass>());
        cls.Dict[CTypeKey] = code;
        cls.Dict["__init__"] = new PyBuiltinFunction($"{name}.__init__", (_, a, _) =>
        {
            var inst = (PyInstance)a[0];
            var buf = new byte[SizeOfCode(code)];
            inst.Dict[BufferKey] = buf;
            if (a.Length > 1 && a[1] is not PyNone)
                WriteField(buf, 0, code, a[1]);
            return PyNone.Instance;
        });
        cls.Dict["value"] = new PyProperty
        {
            Getter = new PyBuiltinFunction($"{name}.value.getter", (_, a, _) =>
                ReadField((byte[])((PyInstance)a[0]).Dict[BufferKey], 0, code)),
            Setter = new PyBuiltinFunction($"{name}.value.setter", (_, a, _) =>
            {
                WriteField((byte[])((PyInstance)a[0]).Dict[BufferKey], 0, code, a[1]);
                return PyNone.Instance;
            }),
        };
        cls.Dict["__repr__"] = new PyBuiltinFunction($"{name}.__repr__", (_, a, _) =>
            $"{name}({ReadField((byte[])((PyInstance)a[0]).Dict[BufferKey], 0, code)})");
        return cls;
    }

    /// <summary>`c_char_p`/`c_wchar_p`: unchanged from before this round — used almost exclusively as
    /// direct call arguments (`fn(b'hello')`/`fn('hello')`), where the existing `MarshalIn` string
    /// handling already does the right thing. Not buffer-backed (a real per-call native string
    /// allocation is freed right after the call, same as always); `.value` still works, just isn't
    /// `byref`-able. Real out-parameter string buffers are `create_string_buffer`/
    /// `create_unicode_buffer` instead (see `BuildCharBufferClass`), which real ctypes also expects.</summary>
    private static PyClass BuildStringPointerClass(string name, string code)
    {
        var cls = new PyClass(name, new List<PyClass>());
        cls.Dict[CTypeKey] = code;
        cls.Dict["__init__"] = new PyBuiltinFunction($"{name}.__init__", (_, a, _) =>
        {
            ((PyInstance)a[0]).Dict["value"] = a.Length > 1 ? a[1] : PyNone.Instance;
            return PyNone.Instance;
        });
        return cls;
    }

    // ---------------------------------------------------------------- Structure

    private static PyClass BuildStructureClass()
    {
        var cls = new PyClass("Structure", new List<PyClass>());
        cls.Dict["__init__"] = new PyBuiltinFunction("Structure.__init__", (_, a, kwargs) =>
        {
            var inst = (PyInstance)a[0];
            var (fields, size) = GetLayout(inst.Class);
            var buf = new byte[size];
            inst.Dict[BufferKey] = buf;
            for (int i = 1; i < a.Length && i - 1 < fields.Count; i++)
                WriteField(buf, fields[i - 1].Offset, fields[i - 1].Code, a[i]);
            if (kwargs is not null)
                foreach (var (key, value) in kwargs)
                {
                    var field = fields.FirstOrDefault(f => f.Name == key);
                    if (field.Name is null)
                        throw PyErr.TypeError($"'{key}' is an invalid keyword argument for {inst.Class.Name}()");
                    WriteField(buf, field.Offset, field.Code, value);
                }
            return PyNone.Instance;
        });
        cls.Dict["__getattr__"] = new PyBuiltinFunction("Structure.__getattr__", (_, a, _) =>
        {
            var inst = (PyInstance)a[0];
            string name = (string)a[1];
            var (fields, _) = GetLayout(inst.Class);
            foreach (var f in fields)
                if (f.Name == name)
                    return ReadField((byte[])inst.Dict[BufferKey], f.Offset, f.Code);
            throw PyErr.AttributeError($"'{inst.Class.Name}' object has no attribute '{name}'");
        });
        cls.Dict["__setattr__"] = new PyBuiltinFunction("Structure.__setattr__", (_, a, _) =>
        {
            var inst = (PyInstance)a[0];
            string name = (string)a[1];
            var (fields, _) = GetLayout(inst.Class);
            foreach (var f in fields)
                if (f.Name == name)
                {
                    WriteField((byte[])inst.Dict[BufferKey], f.Offset, f.Code, a[2]);
                    return PyNone.Instance;
                }
            inst.Dict[name] = a[2];
            return PyNone.Instance;
        });
        return cls;
    }

    private static bool IsStructureSubclass(PyClass cls, PyClass structureClass) => cls != structureClass && cls.Mro.Contains(structureClass);

    /// <summary>Real C struct layout, natural alignment (no explicit packing pragma support — not
    /// needed for any Windows API this shim targets so far): each field is aligned to its own size,
    /// and the struct's total size is padded up to its largest member's alignment. Cached on the
    /// class itself since `_fields_` never changes after the class body runs.</summary>
    private static (List<(string Name, int Offset, string Code)> Fields, int Size) GetLayout(PyClass structClass)
    {
        if (structClass.Dict.TryGet(LayoutCacheKey, out var cached))
            return ((List<(string, int, string)>, int))cached;

        if (!structClass.Dict.TryGet("_fields_", out var fieldsObj))
            throw PyErr.AttributeError($"class '{structClass.Name}' has no _fields_");
        var items = fieldsObj switch
        {
            PyList l => (IReadOnlyList<object>)l.Items,
            PyTuple t => t.Items,
            _ => throw PyErr.TypeError("_fields_ must be a list of (name, ctype) pairs"),
        };
        var fields = new List<(string Name, int Offset, string Code)>();
        int offset = 0, maxAlign = 1;
        foreach (var item in items)
        {
            var pair = (PyTuple)item;
            string fname = (string)pair.Items[0];
            string code = CTypeCode(pair.Items[1])
                ?? throw PyErr.TypeError($"_fields_ entry '{fname}' must be a ctypes type");
            int size = SizeOfCode(code);
            offset = AlignUp(offset, size);
            fields.Add((fname, offset, code));
            offset += size;
            maxAlign = Math.Max(maxAlign, size);
        }
        int total = Math.Max(AlignUp(offset, maxAlign), 1);
        var result = (fields, total);
        structClass.Dict[LayoutCacheKey] = result;
        return result;
    }

    private static int AlignUp(int offset, int align) => align <= 1 ? offset : (offset + align - 1) / align * align;

    // ---------------------------------------------------------------- POINTER / byref helpers

    private static PyClass MakePointerType(object pointeeType)
    {
        string pointeeName = pointeeType switch
        {
            PyClass c => c.Name,
            _ => "void",
        };
        var cls = new PyClass($"LP_{pointeeName}", new List<PyClass>());
        cls.Dict[CTypeKey] = "p";
        return cls;
    }

    // ---------------------------------------------------------------- CFUNCTYPE / WINFUNCTYPE callbacks

    /// <summary>Per-callback-instance context a generated native trampoline looks up by an integer
    /// key embedded directly in its own IL (a `DynamicMethod` can't close over C# objects the way an
    /// ordinary lambda can) — the interpreter to call back into, the wrapped Python callable, and the
    /// type codes needed to marshal native args in / the Python return value out.</summary>
    private sealed record CallbackContext(Interp Interp, object Callable, string[] ArgCodes, string RetCode);

    private static readonly Dictionary<int, CallbackContext> CallbackContexts = new();
    private static int _nextCallbackKey = 1;

    private static readonly MethodInfo InvokeCallbackTrampolineMethod =
        typeof(CtypesModule).GetMethod(nameof(InvokeCallbackTrampoline), BindingFlags.NonPublic | BindingFlags.Static)!;

    /// <summary>`CFUNCTYPE(restype, *argtypes)`: returns a real "prototype" class — calling *that*
    /// with a Python callable (`CMPFUNC(py_func)`) is what actually builds the native trampoline
    /// (see `BuildCallbackThunk`). Scoped to scalar/pointer-sized argument and return types (matching
    /// this module's existing scope decisions elsewhere — no by-value struct arguments), which covers
    /// every common real Windows callback shape (`EnumWindows`, `qsort` comparators, `WNDPROC`, …).
    /// </summary>
    private static PyClass MakeCallbackType(object[] a)
    {
        string retCode = a.Length > 0 ? (CTypeCode(a[0]) ?? "v") : "v";
        var argCodes = a.Skip(1)
            .Select(t => CTypeCode(t) ?? throw PyErr.TypeError("CFUNCTYPE/WINFUNCTYPE argtypes must be ctypes types"))
            .ToArray();

        var cls = new PyClass("CFunctionType", new List<PyClass>());
        // Real ctypes: an instance of a CFUNCTYPE-built prototype is itself pointer-shaped (it can be
        // passed anywhere a `c_void_p`-like argument is expected) — reuse the "p" ctype code so
        // `InferCode`'s existing fallback already does the right thing when no explicit `argtypes` is
        // declared on the call site.
        cls.Dict[CTypeKey] = "p";
        cls.Dict["__init__"] = new PyBuiltinFunction("CFunctionType.__init__", (interp, ia, _) =>
        {
            var inst = (PyInstance)ia[0];
            object callable = ia[1];
            var (ptr, del) = BuildCallbackThunk(interp, callable, argCodes, retCode);
            inst.Dict[FnPtrKey] = ptr;
            // Kept alive for as long as this wrapper instance is: the delegate object is what
            // actually roots the JIT-generated native trampoline in memory — once it's collectible,
            // native code calling through `ptr` would crash. Real ctypes has the identical real
            // constraint ("the callback must be kept alive as long as it may be called").
            inst.Dict[CallbackDelegateKey] = del;
            return PyNone.Instance;
        });
        return cls;
    }

    /// <summary>Builds a real native function pointer that, when called from native code, marshals
    /// its arguments to Python values, calls `callable`, marshals the Python return value back to a
    /// native value, and returns it — the reverse direction of the existing calli-based forward-call
    /// thunk. Uses a generic `Func&lt;...&gt;`/`Action&lt;...&gt;` delegate type (rather than
    /// `System.Reflection.Emit.TypeBuilder`-defining a brand-new delegate type per signature) since
    /// every supported argument/return type here is a blittable value type (ints, floats, `IntPtr`) —
    /// .NET can generate a native-callable P/Invoke stub for those generic delegates directly.</summary>
    private static (IntPtr Ptr, Delegate Del) BuildCallbackThunk(Interp interp, object callable, string[] argCodes, string retCode)
    {
        var argNetTypes = argCodes.Select(NativeType).ToArray();
        var retNetType = NativeType(retCode);
        bool isVoid = retNetType == typeof(void);
        // Real .NET requirement: `Marshal.GetFunctionPointerForDelegate` rejects any delegate type
        // constructed from a generic definition (confirmed live: "The specified Type must not be a
        // generic type" — even a fully *closed* `Func<IntPtr, IntPtr, int>` counts, not just an open
        // one) — the delegate type must be a real, non-generic type with `UnmanagedFunctionPointer`
        // semantics. `GetOrBuildDelegateType` builds (and caches) exactly that per unique signature.
        Type delegateType = GetOrBuildDelegateType(argNetTypes, isVoid ? typeof(void) : retNetType);

        int key;
        lock (CallbackContexts)
        {
            key = _nextCallbackKey++;
            CallbackContexts[key] = new CallbackContext(interp, callable, argCodes, retCode);
        }

        var dm = new DynamicMethod(
            "ctypes_callback_" + key,
            isVoid ? typeof(void) : retNetType,
            argNetTypes,
            typeof(CtypesModule).Module,
            skipVisibility: true);
        var il = dm.GetILGenerator();

        var argsLocal = il.DeclareLocal(typeof(object[]));
        il.Emit(OpCodes.Ldc_I4, argNetTypes.Length);
        il.Emit(OpCodes.Newarr, typeof(object));
        il.Emit(OpCodes.Stloc, argsLocal);
        for (int i = 0; i < argNetTypes.Length; i++)
        {
            il.Emit(OpCodes.Ldloc, argsLocal);
            il.Emit(OpCodes.Ldc_I4, i);
            il.Emit(OpCodes.Ldarg, i);
            il.Emit(OpCodes.Box, argNetTypes[i]);
            il.Emit(OpCodes.Stelem_Ref);
        }
        il.Emit(OpCodes.Ldc_I4, key);
        il.Emit(OpCodes.Ldloc, argsLocal);
        il.Emit(OpCodes.Call, InvokeCallbackTrampolineMethod);
        if (isVoid)
            il.Emit(OpCodes.Pop);
        else
            il.Emit(OpCodes.Unbox_Any, retNetType);
        il.Emit(OpCodes.Ret);

        var del = dm.CreateDelegate(delegateType);
        var ptr = Marshal.GetFunctionPointerForDelegate(del);
        return (ptr, del);
    }

    /// <summary>Called from generated native-callback trampolines only (see `BuildCallbackThunk`) —
    /// looks up the real interpreter/callable/type-codes context by key, marshals each native
    /// argument to a Python value the same way a function's own return value is marshalled
    /// (`MarshalOut`), calls the wrapped Python callable, and marshals its return value back to a
    /// boxed native value.</summary>
    private static object? InvokeCallbackTrampoline(int key, object[] nativeArgs)
    {
        CallbackContext ctx;
        lock (CallbackContexts)
            ctx = CallbackContexts[key];

        var pyArgs = new object[nativeArgs.Length];
        for (int i = 0; i < nativeArgs.Length; i++)
            pyArgs[i] = MarshalOut(nativeArgs[i], ctx.ArgCodes[i]);

        object pyResult = ctx.Interp.Call(ctx.Callable, pyArgs);
        return ctx.RetCode == "v" ? null : PyToNativeScalar(pyResult, ctx.RetCode);
    }

    // Real, general C# gotcha found live: a switch *expression* whose arms are different numeric
    // types (sbyte, int, double, ...) infers a common type across ALL arms via the standard implicit
    // numeric conversion hierarchy (here, `double`) and converts every arm to it *before* the method's
    // own `object` return type ever comes into play — so `Unbox_Any` back in the generated trampoline
    // saw a boxed `System.Double` for an "i4"-coded return value instead of the intended boxed
    // `System.Int32`, and crashed. Each arm is cast to `(object)` explicitly so no such arm-to-arm
    // widening happens; the value that gets boxed is each arm's own real type.
    private static object PyToNativeScalar(object pyValue, string code) => code switch
    {
        "i1" => (object)(sbyte)(long)PyOps.AsBigInt(pyValue, "ctypes"),
        "u1" => (object)(byte)(long)PyOps.AsBigInt(pyValue, "ctypes"),
        "i2" => (object)(short)(long)PyOps.AsBigInt(pyValue, "ctypes"),
        "u2" => (object)(ushort)(long)PyOps.AsBigInt(pyValue, "ctypes"),
        "i4" => (object)(int)(long)PyOps.AsBigInt(pyValue, "ctypes"),
        "u4" => (object)(uint)(long)PyOps.AsBigInt(pyValue, "ctypes"),
        "i8" => (object)(long)PyOps.AsBigInt(pyValue, "ctypes"),
        "u8" => (object)(ulong)(long)PyOps.AsBigInt(pyValue, "ctypes"),
        "f4" => (object)(float)PyOps.AsDouble(pyValue),
        "f8" => (object)PyOps.AsDouble(pyValue),
        "p" => (object)(IntPtr)(long)PyOps.AsBigInt(pyValue, "ctypes"),
        _ => throw PyErr.TypeError($"unsupported callback return type code {code}"),
    };

    private static readonly ModuleBuilder CallbackDelegateModule = AssemblyBuilder
        .DefineDynamicAssembly(new AssemblyName("PySharpCtypesCallbacks"), AssemblyBuilderAccess.Run)
        .DefineDynamicModule("PySharpCtypesCallbacksModule");

    private static readonly Dictionary<string, Type> DelegateTypeCache = new();

    /// <summary>Builds (or reuses) a real, non-generic delegate type for the given native signature —
    /// the standard runtime recipe for a P/Invoke-callable delegate type built dynamically:
    /// `MulticastDelegate`-derived, a `.ctor(object, IntPtr)` and an `Invoke(...)` method both marked
    /// `Runtime | Managed` (the CLR supplies their actual implementations), decorated with
    /// `[UnmanagedFunctionPointer]` so `Marshal.GetFunctionPointerForDelegate` accepts it.</summary>
    private static Type GetOrBuildDelegateType(Type[] argTypes, Type retType)
    {
        string key = string.Join(",", argTypes.Select(t => t.Name)) + "->" + retType.Name;
        lock (DelegateTypeCache)
        {
            if (DelegateTypeCache.TryGetValue(key, out var cached))
                return cached;

            var tb = CallbackDelegateModule.DefineType(
                "CtypesCallback_" + key.GetHashCode(),
                TypeAttributes.Public | TypeAttributes.Sealed | TypeAttributes.AnsiClass
                    | TypeAttributes.AutoClass,
                typeof(MulticastDelegate));

            var attrCtor = typeof(UnmanagedFunctionPointerAttribute).GetConstructor(new[] { typeof(CallingConvention) })!;
            tb.SetCustomAttribute(new CustomAttributeBuilder(attrCtor, new object[] { CallingConvention.Winapi }));

            var ctor = tb.DefineConstructor(
                MethodAttributes.RTSpecialName | MethodAttributes.HideBySig | MethodAttributes.Public,
                CallingConventions.Standard,
                new[] { typeof(object), typeof(IntPtr) });
            ctor.SetImplementationFlags(MethodImplAttributes.Runtime | MethodImplAttributes.Managed);

            var invoke = tb.DefineMethod(
                "Invoke",
                MethodAttributes.Public | MethodAttributes.HideBySig | MethodAttributes.NewSlot | MethodAttributes.Virtual,
                retType,
                argTypes);
            invoke.SetImplementationFlags(MethodImplAttributes.Runtime | MethodImplAttributes.Managed);

            var built = tb.CreateType()!;
            DelegateTypeCache[key] = built;
            return built;
        }
    }

    // ---------------------------------------------------------------- create_string_buffer / create_unicode_buffer

    /// <summary>A real mutable native buffer (real ctypes' idiomatic way to receive a string a native
    /// function writes into) — one shared class for both narrow/wide, distinguished by `__wide__`.
    /// `.value` reads/writes the null-terminated string; the underlying `byte[]` is what `byref`-style
    /// passing (here: direct passing, since arrays decay to pointers like in C) pins for the call.</summary>
    private static PyClass BuildCharBufferClass()
    {
        var cls = new PyClass("Array", new List<PyClass>());
        cls.Dict["value"] = new PyProperty
        {
            // Real ctypes: `create_string_buffer(...).value` is real `bytes` (the ANSI/narrow one);
            // `create_unicode_buffer(...).value` is a real `str` — two different Python types, not
            // just an encoding detail, matching what real scripts actually branch on.
            Getter = new PyBuiltinFunction("Array.value.getter", (_, a, _) =>
            {
                var inst = (PyInstance)a[0];
                var buf = (byte[])inst.Dict[BufferKey];
                if ((bool)inst.Dict[WideKey])
                    return ReadWideNullTerminated(buf);
                int len = Array.IndexOf(buf, (byte)0);
                return new PyBytes(buf[..(len < 0 ? buf.Length : len)]);
            }),
            Setter = new PyBuiltinFunction("Array.value.setter", (_, a, _) =>
            {
                var inst = (PyInstance)a[0];
                var buf = (byte[])inst.Dict[BufferKey];
                bool wide = (bool)inst.Dict[WideKey];
                if (wide)
                    WriteStringIntoBuffer(buf, (string)a[1], wide: true);
                else if (a[1] is PyBytes pb)
                    WriteBytesIntoBuffer(buf, pb.Data);
                else
                    WriteStringIntoBuffer(buf, (string)a[1], wide: false);
                return PyNone.Instance;
            }),
        };
        return cls;
    }

    private static PyInstance CreateCharBuffer(object[] a, bool wide, PyClass charBufferClass)
    {
        int size;
        string? initialText = null;
        byte[]? initialBytes = null;
        if (a.Length > 0 && a[0] is BigInteger n)
        {
            size = (int)n;
        }
        else if (a.Length > 0 && a[0] is string s)
        {
            initialText = s;
            size = wide ? s.Length + 1 : Encoding.ASCII.GetByteCount(s) + 1;
        }
        else if (a.Length > 0 && a[0] is PyBytes b)
        {
            // real ctypes: create_string_buffer accepts bytes directly (the dominant real usage —
            // create_unicode_buffer is the str-accepting one instead).
            initialBytes = b.Data;
            size = initialBytes.Length + 1;
        }
        else
        {
            throw PyErr.TypeError("create_string_buffer/create_unicode_buffer expects an int size, a str, or bytes");
        }
        if (a.Length > 1)
            size = (int)(BigInteger)a[1];
        var inst = new PyInstance(charBufferClass);
        var buf = new byte[wide ? size * 2 : size];
        inst.Dict[BufferKey] = buf;
        inst.Dict[WideKey] = wide;
        if (initialText is not null)
            WriteStringIntoBuffer(buf, initialText, wide);
        else if (initialBytes is not null)
            initialBytes.CopyTo(buf, 0);
        return inst;
    }

    private static string ReadWideNullTerminated(byte[] buf)
    {
        int charCount = buf.Length / 2;
        int len = 0;
        while (len < charCount && !(buf[len * 2] == 0 && buf[len * 2 + 1] == 0))
            len++;
        return Encoding.Unicode.GetString(buf, 0, len * 2);
    }

    private static void WriteStringIntoBuffer(byte[] buf, string value, bool wide)
    {
        var encoded = wide ? Encoding.Unicode.GetBytes(value) : Encoding.ASCII.GetBytes(value);
        WriteBytesIntoBuffer(buf, encoded, reserve: wide ? 2 : 1);
    }

    private static void WriteBytesIntoBuffer(byte[] buf, byte[] data, int reserve = 1)
    {
        if (data.Length > buf.Length - reserve)
            throw PyErr.ValueError("bytes/string too long for the buffer");
        Array.Clear(buf, 0, buf.Length);
        data.CopyTo(buf, 0);
    }

    /// <summary>windll.kernel32 → CDLL("kernel32") via __getattr__.</summary>
    private static PyInstance MakeLoaderNamespace(PyClass dllClass)
    {
        var loaderClass = new PyClass("LibraryLoader", new List<PyClass>());
        loaderClass.Dict["__getattr__"] = new PyBuiltinFunction("LibraryLoader.__getattr__",
            (interp, a, _) => interp.Call(dllClass, new[] { a[1] }));
        return new PyInstance(loaderClass);
    }

    private static PyClass BuildDllClass(PyClass funcClass)
    {
        var cls = new PyClass("CDLL", new List<PyClass>());

        cls.Dict["__init__"] = new PyBuiltinFunction("CDLL.__init__", (_, a, _) =>
        {
            var inst = (PyInstance)a[0];
            string name = (string)a[1];
            if (!NativeLibrary.TryLoad(name, out var handle))
                throw PyErr.OSError($"cannot load library '{name}'");
            inst.Dict[HandleKey] = handle;
            inst.Dict[NameKey] = name;
            return PyNone.Instance;
        });

        cls.Dict["__getattr__"] = new PyBuiltinFunction("CDLL.__getattr__", (_, a, _) =>
        {
            var inst = (PyInstance)a[0];
            string fnName = (string)a[1];
            var handle = (IntPtr)inst.Dict[HandleKey];
            if (!NativeLibrary.TryGetExport(handle, fnName, out var fnPtr))
                throw PyErr.AttributeError(
                    $"function '{fnName}' not found in {inst.Dict[NameKey]}");
            var fn = new PyInstance(funcClass);
            fn.Dict[FnPtrKey] = fnPtr;
            fn.Dict["__name__"] = fnName;
            fn.Dict["restype"] = PyNone.Instance;  // default: c_int
            fn.Dict["argtypes"] = PyNone.Instance;
            // cache on the dll instance, so any restype/argtypes set persist
            inst.Dict[fnName] = fn;
            return fn;
        });

        cls.Dict["__repr__"] = new PyBuiltinFunction("CDLL.__repr__", (_, a, _) =>
            $"<CDLL '{((PyInstance)a[0]).Dict[NameKey]}'>");

        return cls;
    }

    private static PyClass BuildFuncClass(PyClass byRefClass, PyClass charBufferClass)
    {
        var cls = new PyClass("_FuncPtr", new List<PyClass>());
        cls.Dict["__call__"] = new PyBuiltinFunction("_FuncPtr.__call__", (interp, a, _) =>
        {
            var fn = (PyInstance)a[0];
            var fnPtr = (IntPtr)fn.Dict[FnPtrKey];
            var pyArgs = a.Skip(1).ToArray();

            // type codes for the arguments
            string[] argCodes;
            if (fn.Dict.TryGet("argtypes", out var at) && at is not PyNone)
            {
                var declared = PyOps.Iterate(interp, at)
                    .Select(t => CTypeCode(t) ?? throw PyErr.TypeError("argtypes must contain ctypes types"))
                    .ToArray();
                if (declared.Length != pyArgs.Length)
                    throw PyErr.TypeError(
                        $"{fn.Dict["__name__"]}() expects {declared.Length} arguments ({pyArgs.Length} given)");
                argCodes = declared;
            }
            else
            {
                argCodes = pyArgs.Select(x => InferCode(x, byRefClass, charBufferClass)).ToArray();
            }

            string retCode = fn.Dict.TryGet("restype", out var rt) && rt is not PyNone
                ? rt is PyNone ? "i4" : CTypeCode(rt) ?? "i4"
                : "i4";

            // marshalling in ingresso
            var natives = new object?[pyArgs.Length];
            var toFree = new List<IntPtr>();
            var handlesToFree = new List<GCHandle>();
            try
            {
                for (int i = 0; i < pyArgs.Length; i++)
                    natives[i] = MarshalIn(pyArgs[i], argCodes[i], toFree, handlesToFree, byRefClass, charBufferClass);

                var thunk = GetThunk(argCodes, retCode);
                var result = thunk(fnPtr, natives);
                return MarshalOut(result, retCode);
            }
            finally
            {
                foreach (var ptr in toFree)
                    Marshal.FreeHGlobal(ptr);
                foreach (var handle in handlesToFree)
                    handle.Free();
            }
        });
        return cls;
    }

    private static string? CTypeCode(object o) => o switch
    {
        PyClass cls when cls.Dict.TryGet(CTypeKey, out var c) => (string)c,
        PyInstance inst when inst.Class.Dict.TryGet(CTypeKey, out var c) => (string)c,
        PyNone => null,
        _ => null,
    };

    private static string InferCode(object arg, PyClass byRefClass, PyClass charBufferClass) => arg switch
    {
        BigInteger or bool => "i4",
        double => "f8",
        string or PyBytes => "s",
        PyInstance inst when inst.Class == byRefClass => "p",
        PyInstance inst when inst.Class == charBufferClass => (bool)inst.Dict[WideKey] ? "w" : "s",
        PyInstance inst when CTypeCode(inst) is { } code => code,
        _ => throw PyErr.TypeError($"don't know how to pass {PyOps.TypeName(arg)} to a C function"),
    };

    private static Type NativeType(string code) => code switch
    {
        "i1" => typeof(sbyte),
        "u1" => typeof(byte),
        "i2" => typeof(short),
        "u2" => typeof(ushort),
        "i4" => typeof(int),
        "u4" => typeof(uint),
        "i8" => typeof(long),
        "u8" => typeof(ulong),
        "f4" => typeof(float),
        "f8" => typeof(double),
        "s" or "w" or "p" => typeof(IntPtr),
        "v" => typeof(void),
        _ => throw PyErr.TypeError($"unknown ctype code {code}"),
    };

    private static object? MarshalIn(
        object arg, string code, List<IntPtr> toFree, List<GCHandle> handlesToFree, PyClass byRefClass, PyClass charBufferClass)
    {
        // value from a c_* instance: buffer-backed types (numeric/pointer) read through ReadField;
        // string-pointer types (c_char_p/c_wchar_p) still keep their value in `.Dict["value"]`
        // directly (see BuildStringPointerClass) — not a real Python-level property, so a plain dict
        // lookup (not attribute lookup) is correct and sufficient for both cases here. Excludes any
        // instance already carrying its own native pointer (`FnPtrKey`) — a CFUNCTYPE-built callback
        // wrapper — which the "p" case below extracts directly instead.
        if (arg is PyInstance simple && simple.Class != byRefClass && simple.Class != charBufferClass
            && !simple.Dict.ContainsKey(FnPtrKey) && CTypeCode(simple) is { } instCode)
        {
            arg = simple.Dict.TryGet(BufferKey, out var bufObj) ? ReadField((byte[])bufObj, 0, instCode)
                : simple.Dict.TryGet("value", out var v) ? v : PyNone.Instance;
        }

        switch (code)
        {
            case "i1": return (sbyte)(long)PyOps.AsBigInt(arg, "ctypes");
            case "u1": return (byte)(long)PyOps.AsBigInt(arg, "ctypes");
            case "i2": return (short)(long)PyOps.AsBigInt(arg, "ctypes");
            case "u2": return (ushort)(long)PyOps.AsBigInt(arg, "ctypes");
            case "i4": return (int)(long)PyOps.AsBigInt(arg, "ctypes");
            case "u4": return (uint)(long)PyOps.AsBigInt(arg, "ctypes");
            case "i8": return (long)PyOps.AsBigInt(arg, "ctypes");
            case "u8": return (ulong)(long)PyOps.AsBigInt(arg, "ctypes");
            case "f4": return (float)PyOps.AsDouble(arg);
            case "f8": return PyOps.AsDouble(arg);
            case "s":
            {
                if (arg is PyNone)
                    return IntPtr.Zero;
                if (arg is PyInstance charBuf && charBuf.Class == charBufferClass)
                {
                    var handle = GCHandle.Alloc((byte[])charBuf.Dict[BufferKey], GCHandleType.Pinned);
                    handlesToFree.Add(handle);
                    return handle.AddrOfPinnedObject();
                }
                IntPtr ptr = arg switch
                {
                    string s => Marshal.StringToHGlobalAnsi(s),
                    PyBytes b => BytesToHGlobal(b.Data),
                    _ => throw PyErr.TypeError("c_char_p expects str, bytes or None"),
                };
                toFree.Add(ptr);
                return ptr;
            }
            case "w":
            {
                if (arg is PyNone)
                    return IntPtr.Zero;
                if (arg is PyInstance wcharBuf && wcharBuf.Class == charBufferClass)
                {
                    var handle = GCHandle.Alloc((byte[])wcharBuf.Dict[BufferKey], GCHandleType.Pinned);
                    handlesToFree.Add(handle);
                    return handle.AddrOfPinnedObject();
                }
                IntPtr ptr = Marshal.StringToHGlobalUni(
                    arg as string ?? throw PyErr.TypeError("c_wchar_p expects str"));
                toFree.Add(ptr);
                return ptr;
            }
            case "p":
            {
                if (arg is PyNone)
                    return IntPtr.Zero;
                if (arg is PyInstance byRef && byRef.Class == byRefClass)
                {
                    var target = byRef.Dict[ByRefTargetKey];
                    byte[] buf = target switch
                    {
                        PyInstance ti when ti.Dict.TryGet(BufferKey, out var b) => (byte[])b,
                        _ => throw PyErr.TypeError("byref() target has no addressable buffer"),
                    };
                    var handle = GCHandle.Alloc(buf, GCHandleType.Pinned);
                    handlesToFree.Add(handle);
                    return handle.AddrOfPinnedObject();
                }
                // A CFUNCTYPE-built callback wrapper: pass its own real native function pointer
                // directly.
                if (arg is PyInstance cbInst && cbInst.Dict.TryGet(FnPtrKey, out var cbPtr))
                    return (IntPtr)cbPtr;
                return (IntPtr)(long)PyOps.AsBigInt(arg, "ctypes");
            }
            default:
                throw PyErr.TypeError($"unknown ctype code {code}");
        }
    }

    private static IntPtr BytesToHGlobal(byte[] data)
    {
        // null-terminated as ctypes does with bytes for c_char_p
        IntPtr ptr = Marshal.AllocHGlobal(data.Length + 1);
        Marshal.Copy(data, 0, ptr, data.Length);
        Marshal.WriteByte(ptr, data.Length, 0);
        return ptr;
    }

    private static object MarshalOut(object? nativeResult, string code) => code switch
    {
        "v" => PyNone.Instance,
        "i1" => new BigInteger((sbyte)nativeResult!),
        "u1" => new BigInteger((byte)nativeResult!),
        "i2" => new BigInteger((short)nativeResult!),
        "u2" => new BigInteger((ushort)nativeResult!),
        "i4" => new BigInteger((int)nativeResult!),
        "u4" => new BigInteger((uint)nativeResult!),
        "i8" => new BigInteger((long)nativeResult!),
        "u8" => new BigInteger((ulong)nativeResult!),
        "f4" => (double)(float)nativeResult!,
        "f8" => (double)nativeResult!,
        "s" => (IntPtr)nativeResult! == IntPtr.Zero
            ? PyNone.Instance
            : Marshal.PtrToStringAnsi((IntPtr)nativeResult!)!,
        "w" => (IntPtr)nativeResult! == IntPtr.Zero
            ? PyNone.Instance
            : Marshal.PtrToStringUni((IntPtr)nativeResult!)!,
        "p" => new BigInteger((long)(IntPtr)nativeResult!),
        _ => throw PyErr.TypeError($"unknown ctype code {code}"),
    };

    // ---------------------------------------------------------------- dtype-generic field access

    private static int SizeOfCode(string code) => code switch
    {
        "i1" or "u1" => 1,
        "i2" or "u2" => 2,
        "i4" or "u4" or "f4" => 4,
        "i8" or "u8" or "f8" or "p" => 8,
        _ => throw PyErr.TypeError($"unsupported field type code {code}"),
    };

    private static int SizeOfArg(object arg, PyClass structureClass) => arg switch
    {
        PyClass cls when IsStructureSubclass(cls, structureClass) => GetLayout(cls).Size,
        PyClass cls when cls.Dict.TryGet(CTypeKey, out var c) => SizeOfCode((string)c),
        PyInstance inst when IsStructureSubclass(inst.Class, structureClass) => GetLayout(inst.Class).Size,
        PyInstance inst when inst.Class.Dict.TryGet(CTypeKey, out var c) => SizeOfCode((string)c),
        _ => throw PyErr.TypeError("sizeof() argument must be a ctypes type or instance"),
    };

    private static object ReadField(byte[] buf, int offset, string code) => code switch
    {
        "i1" => new BigInteger((sbyte)buf[offset]),
        "u1" => new BigInteger(buf[offset]),
        "i2" => new BigInteger(BitConverter.ToInt16(buf, offset)),
        "u2" => new BigInteger(BitConverter.ToUInt16(buf, offset)),
        "i4" => new BigInteger(BitConverter.ToInt32(buf, offset)),
        "u4" => new BigInteger(BitConverter.ToUInt32(buf, offset)),
        "i8" => new BigInteger(BitConverter.ToInt64(buf, offset)),
        "u8" => new BigInteger(BitConverter.ToUInt64(buf, offset)),
        "f4" => (double)BitConverter.ToSingle(buf, offset),
        "f8" => BitConverter.ToDouble(buf, offset),
        "p" => new BigInteger(BitConverter.ToUInt64(buf, offset)),
        _ => throw PyErr.TypeError($"unsupported field type code {code}"),
    };

    private static void WriteField(byte[] buf, int offset, string code, object value)
    {
        switch (code)
        {
            case "i1": buf[offset] = unchecked((byte)(sbyte)(long)PyOps.AsBigInt(value, "ctypes")); break;
            case "u1": buf[offset] = (byte)(long)PyOps.AsBigInt(value, "ctypes"); break;
            case "i2": BitConverter.GetBytes((short)(long)PyOps.AsBigInt(value, "ctypes")).CopyTo(buf, offset); break;
            case "u2": BitConverter.GetBytes((ushort)(long)PyOps.AsBigInt(value, "ctypes")).CopyTo(buf, offset); break;
            case "i4": BitConverter.GetBytes((int)(long)PyOps.AsBigInt(value, "ctypes")).CopyTo(buf, offset); break;
            case "u4": BitConverter.GetBytes((uint)(long)PyOps.AsBigInt(value, "ctypes")).CopyTo(buf, offset); break;
            case "i8": BitConverter.GetBytes((long)PyOps.AsBigInt(value, "ctypes")).CopyTo(buf, offset); break;
            case "u8": BitConverter.GetBytes((ulong)(long)PyOps.AsBigInt(value, "ctypes")).CopyTo(buf, offset); break;
            case "f4": BitConverter.GetBytes((float)PyOps.AsDouble(value)).CopyTo(buf, offset); break;
            case "f8": BitConverter.GetBytes(PyOps.AsDouble(value)).CopyTo(buf, offset); break;
            case "p": BitConverter.GetBytes((ulong)(long)PyOps.AsBigInt(value, "ctypes")).CopyTo(buf, offset); break;
            default: throw PyErr.TypeError($"unsupported field type code {code}");
        }
    }

    /// <summary>Builds (or reuses) a calli thunk for the requested signature.</summary>
    private static Func<IntPtr, object?[], object?> GetThunk(string[] argCodes, string retCode)
    {
        string key = string.Join(",", argCodes) + "->" + retCode;
        lock (ThunkCache)
        {
            if (ThunkCache.TryGetValue(key, out var cached))
                return cached;

            var argTypes = argCodes.Select(NativeType).ToArray();
            var retType = NativeType(retCode == "v" ? "v" : retCode);

            var dm = new DynamicMethod(
                "ffi_" + key.GetHashCode(),
                typeof(object),
                new[] { typeof(IntPtr), typeof(object?[]) },
                typeof(CtypesModule).Module,
                skipVisibility: true);
            var il = dm.GetILGenerator();

            for (int i = 0; i < argTypes.Length; i++)
            {
                il.Emit(OpCodes.Ldarg_1);
                il.Emit(OpCodes.Ldc_I4, i);
                il.Emit(OpCodes.Ldelem_Ref);
                il.Emit(OpCodes.Unbox_Any, argTypes[i]);
            }
            il.Emit(OpCodes.Ldarg_0); // function pointer in cima allo stack
            il.EmitCalli(OpCodes.Calli, CallingConvention.Winapi, retType, argTypes);
            if (retType == typeof(void))
                il.Emit(OpCodes.Ldnull);
            else
                il.Emit(OpCodes.Box, retType);
            il.Emit(OpCodes.Ret);

            var thunk = (Func<IntPtr, object?[], object?>)dm.CreateDelegate(
                typeof(Func<IntPtr, object?[], object?>));
            ThunkCache[key] = thunk;
            return thunk;
        }
    }
}
