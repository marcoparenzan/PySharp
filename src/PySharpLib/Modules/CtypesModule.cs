using System.Numerics;
using System.Reflection.Emit;
using System.Runtime.InteropServices;
using PySharpLib.Runtime;

namespace PySharpLib.Modules;

/// <summary>
/// ctypes-lite: caricamento di DLL native via NativeLibrary e chiamata di funzioni
/// with DynamicMethod + calli based marshalling. Supports scalar types and char*
/// strings; pointers to structs and callbacks are out of scope for v1.
/// </summary>
public static class CtypesModule
{
    private const string HandleKey = "__dllhandle__";
    private const string NameKey = "__dllname__";
    private const string FnPtrKey = "__fnptr__";
    private const string CTypeKey = "__ctype__";

    /// <summary>Cache of calli thunks per signature.</summary>
    private static readonly Dictionary<string, Func<IntPtr, object?[], object?>> ThunkCache = new();

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
            var cls = new PyClass(name, new List<PyClass>());
            cls.Dict[CTypeKey] = code;
            cls.Dict["__init__"] = new PyBuiltinFunction($"{name}.__init__", (_, a, _) =>
            {
                ((PyInstance)a[0]).Dict["value"] = a.Length > 1 ? a[1] : PyNone.Instance;
                return PyNone.Instance;
            });
            d[name] = cls;
        }

        var funcClass = BuildFuncClass();
        var dllClass = BuildDllClass(funcClass);
        d["CDLL"] = dllClass;
        d["WinDLL"] = dllClass;
        d["windll"] = MakeLoaderNamespace(dllClass);
        d["cdll"] = MakeLoaderNamespace(dllClass);

        d["sizeof"] = new PyBuiltinFunction("sizeof", (_, a, _) =>
        {
            string code = CTypeCode(a[0]) ?? throw PyErr.TypeError("sizeof() argument must be a ctypes type");
            return new BigInteger(code switch
            {
                "i1" or "u1" => 1,
                "i2" or "u2" => 2,
                "i4" or "u4" or "f4" => 4,
                _ => 8,
            });
        });

        d["get_last_error"] = new PyBuiltinFunction("get_last_error", (_, _, _) =>
            new BigInteger(Marshal.GetLastWin32Error()));

        return m;
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

    private static PyClass BuildFuncClass()
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
                argCodes = pyArgs.Select(InferCode).ToArray();
            }

            string retCode = fn.Dict.TryGet("restype", out var rt) && rt is not PyNone
                ? rt is PyNone ? "i4" : CTypeCode(rt) ?? "i4"
                : "i4";

            // marshalling in ingresso
            var natives = new object?[pyArgs.Length];
            var toFree = new List<IntPtr>();
            try
            {
                for (int i = 0; i < pyArgs.Length; i++)
                    natives[i] = MarshalIn(pyArgs[i], argCodes[i], toFree);

                var thunk = GetThunk(argCodes, retCode);
                var result = thunk(fnPtr, natives);
                return MarshalOut(result, retCode);
            }
            finally
            {
                foreach (var ptr in toFree)
                    Marshal.FreeHGlobal(ptr);
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

    private static string InferCode(object arg) => arg switch
    {
        BigInteger or bool => "i4",
        double => "f8",
        string or PyBytes => "s",
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

    private static object? MarshalIn(object arg, string code, List<IntPtr> toFree)
    {
        // value from a c_* instance
        if (arg is PyInstance inst && CTypeCode(inst) is not null)
            arg = inst.Dict.TryGet("value", out var v) ? v : PyNone.Instance;

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
                IntPtr ptr = Marshal.StringToHGlobalUni(
                    arg as string ?? throw PyErr.TypeError("c_wchar_p expects str"));
                toFree.Add(ptr);
                return ptr;
            }
            case "p":
                return arg is PyNone ? IntPtr.Zero : (IntPtr)(long)PyOps.AsBigInt(arg, "ctypes");
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
