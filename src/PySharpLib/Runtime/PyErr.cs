// Copyright (c) 2026 Marco Parenzan
//
// Licensed under the MIT License. See the LICENSE file in the project
// root for full license information.

using PySharpLib.Builtins;

namespace PySharpLib.Runtime;

/// <summary>
/// Hierarchy of builtin exceptions (shared, immutable PyClass) and factories
/// to raise them from C# code.
/// </summary>
public static class PyErr
{
    public static readonly PyClass BaseException = new("BaseException", new List<PyClass>());
    public static readonly PyClass Exception = Derive("Exception", BaseException);
    public static readonly PyClass ArithmeticError = Derive("ArithmeticError", Exception);
    public static readonly PyClass ZeroDivisionErrorClass = Derive("ZeroDivisionError", ArithmeticError);
    public static readonly PyClass OverflowErrorClass = Derive("OverflowError", ArithmeticError);
    public static readonly PyClass AssertionErrorClass = Derive("AssertionError", Exception);
    public static readonly PyClass AttributeErrorClass = Derive("AttributeError", Exception);
    public static readonly PyClass BufferErrorClass = Derive("BufferError", Exception);
    public static readonly PyClass EOFErrorClass = Derive("EOFError", Exception);
    public static readonly PyClass ImportErrorClass = Derive("ImportError", Exception);
    public static readonly PyClass ModuleNotFoundErrorClass = Derive("ModuleNotFoundError", ImportErrorClass);
    public static readonly PyClass LookupError = Derive("LookupError", Exception);
    public static readonly PyClass IndexErrorClass = Derive("IndexError", LookupError);
    public static readonly PyClass KeyErrorClass = Derive("KeyError", LookupError);
    public static readonly PyClass MemoryErrorClass = Derive("MemoryError", Exception);
    public static readonly PyClass NameErrorClass = Derive("NameError", Exception);
    public static readonly PyClass UnboundLocalErrorClass = Derive("UnboundLocalError", NameErrorClass);
    public static readonly PyClass OSErrorClass = Derive("OSError", Exception);
    public static readonly PyClass BlockingIOErrorClass = Derive("BlockingIOError", OSErrorClass);
    public static readonly PyClass ConnectionErrorClass = Derive("ConnectionError", OSErrorClass);
    public static readonly PyClass BrokenPipeErrorClass = Derive("BrokenPipeError", ConnectionErrorClass);
    public static readonly PyClass ConnectionAbortedErrorClass = Derive("ConnectionAbortedError", ConnectionErrorClass);
    public static readonly PyClass ConnectionRefusedErrorClass = Derive("ConnectionRefusedError", ConnectionErrorClass);
    public static readonly PyClass ConnectionResetErrorClass = Derive("ConnectionResetError", ConnectionErrorClass);
    public static readonly PyClass FileExistsErrorClass = Derive("FileExistsError", OSErrorClass);
    public static readonly PyClass FileNotFoundErrorClass = Derive("FileNotFoundError", OSErrorClass);
    public static readonly PyClass IsADirectoryErrorClass = Derive("IsADirectoryError", OSErrorClass);
    public static readonly PyClass NotADirectoryErrorClass = Derive("NotADirectoryError", OSErrorClass);
    public static readonly PyClass InterruptedErrorClass = Derive("InterruptedError", OSErrorClass);
    public static readonly PyClass PermissionErrorClass = Derive("PermissionError", OSErrorClass);
    public static readonly PyClass TimeoutErrorClass = Derive("TimeoutError", OSErrorClass);
    public static readonly PyClass ReferenceErrorClass = Derive("ReferenceError", Exception);
    public static readonly PyClass RuntimeErrorClass = Derive("RuntimeError", Exception);
    public static readonly PyClass NotImplementedErrorClass = Derive("NotImplementedError", RuntimeErrorClass);
    public static readonly PyClass RecursionErrorClass = Derive("RecursionError", RuntimeErrorClass);
    public static readonly PyClass StopIterationClass = Derive("StopIteration", Exception);
    public static readonly PyClass StopAsyncIterationClass = Derive("StopAsyncIteration", Exception);
    public static readonly PyClass SyntaxErrorClass = Derive("SyntaxError", Exception);
    public static readonly PyClass IndentationErrorClass = Derive("IndentationError", SyntaxErrorClass);
    public static readonly PyClass SystemErrorClass = Derive("SystemError", Exception);
    public static readonly PyClass SystemExitClass = Derive("SystemExit", BaseException);
    public static readonly PyClass KeyboardInterruptClass = Derive("KeyboardInterrupt", BaseException);
    public static readonly PyClass TypeErrorClass = Derive("TypeError", Exception);
    public static readonly PyClass ValueErrorClass = Derive("ValueError", Exception);
    public static readonly PyClass UnicodeErrorClass = Derive("UnicodeError", ValueErrorClass);
    public static readonly PyClass UnicodeDecodeErrorClass = Derive("UnicodeDecodeError", UnicodeErrorClass);
    public static readonly PyClass UnicodeEncodeErrorClass = Derive("UnicodeEncodeError", UnicodeErrorClass);
    public static readonly PyClass Warning = Derive("Warning", Exception);
    public static readonly PyClass DeprecationWarningClass = Derive("DeprecationWarning", Warning);
    public static readonly PyClass UserWarningClass = Derive("UserWarning", Warning);
    public static readonly PyClass RuntimeWarningClass = Derive("RuntimeWarning", Warning);
    // Real CPython's remaining stdlib Warning subclasses — added together (found via real
    // sqlalchemy needing `PendingDeprecationWarning`; the rest are one-line-cheap and equally likely
    // to be the next gap for any other real package).
    public static readonly PyClass PendingDeprecationWarningClass = Derive("PendingDeprecationWarning", Warning);
    public static readonly PyClass SyntaxWarningClass = Derive("SyntaxWarning", Warning);
    public static readonly PyClass FutureWarningClass = Derive("FutureWarning", Warning);
    public static readonly PyClass ImportWarningClass = Derive("ImportWarning", Warning);
    public static readonly PyClass UnicodeWarningClass = Derive("UnicodeWarning", Warning);
    public static readonly PyClass BytesWarningClass = Derive("BytesWarning", Warning);
    public static readonly PyClass ResourceWarningClass = Derive("ResourceWarning", Warning);
    public static readonly PyClass EncodingWarningClass = Derive("EncodingWarning", Warning);

    // PEP 654 (real CPython 3.11+, matching this project's declared 3.12 compatibility):
    // BaseExceptionGroup(BaseException), ExceptionGroup(BaseExceptionGroup, Exception). Found via
    // real anyio's own `_backends/_asyncio.py` (an httpx transitive dependency's cancel-scope
    // teardown: `raise BaseExceptionGroup("unhandled errors in a TaskGroup", self._exceptions)`,
    // and `exc_val.split(...)` to separate anyio's own cancellation exceptions from genuine errors).
    public static readonly PyClass BaseExceptionGroupClass = Derive("BaseExceptionGroup", BaseException);
    public static readonly PyClass ExceptionGroupClass =
        new("ExceptionGroup", new List<PyClass> { BaseExceptionGroupClass, Exception });

    static PyErr()
    {
        // default __init__/__str__/__repr__ for all exceptions.
        BaseException.Dict["__init__"] = new PyBuiltinFunction("BaseException.__init__", (_, a, _) =>
        {
            ((PyInstance)a[0]).Dict["args"] = new PyTuple(a.Skip(1).ToArray());
            return PyNone.Instance;
        });
        BaseException.Dict["__str__"] = new PyBuiltinFunction("BaseException.__str__", (interp, a, _) =>
        {
            var inst = (PyInstance)a[0];
            if (inst.Dict.TryGet("args", out var argsObj) && argsObj is PyTuple t)
            {
                return t.Items.Length switch
                {
                    0 => "",
                    1 => PyOps.Str(interp, t.Items[0]),
                    _ => PyOps.Repr(interp, argsObj),
                };
            }
            return "";
        });
        BaseException.Dict["__repr__"] = new PyBuiltinFunction("BaseException.__repr__", (interp, a, _) =>
        {
            var inst = (PyInstance)a[0];
            string args = inst.Dict.TryGet("args", out var argsObj) && argsObj is PyTuple t
                ? string.Join(", ", t.Items.Select(x => PyOps.Repr(interp, x)))
                : "";
            return $"{inst.Class.Name}({args})";
        });

        // Real CPython OSError.__str__: when a real errno/strerror pair is set, formats as
        // "[Errno N] strerror" (plus ": 'filename'" when a filename is set too) instead of the
        // generic args-tuple formatting every other exception uses — found via real pika's own
        // logging of a caught FileNotFoundError/BlockingIOError needing the familiar
        // "[Errno 2] No such file or directory: '...'" shape. Falls back to the ordinary
        // args-based formatting for an OSError built without a real errno (e.g. `OSError("msg")`).
        OSErrorClass.Dict["__str__"] = new PyBuiltinFunction("OSError.__str__", (interp, a, _) =>
        {
            var inst = (PyInstance)a[0];
            bool hasErrno = inst.Dict.TryGet("errno", out var errnoObj) && errnoObj is not PyNone;
            bool hasStrerror = inst.Dict.TryGet("strerror", out var strerrorObj) && strerrorObj is not PyNone;
            if (hasErrno && hasStrerror)
            {
                string head = $"[Errno {PyOps.Str(interp, errnoObj)}] {PyOps.Str(interp, strerrorObj)}";
                return inst.Dict.TryGet("filename", out var filenameObj) && filenameObj is not PyNone
                    ? $"{head}: {PyOps.Repr(interp, filenameObj)}"
                    : head;
            }
            if (inst.Dict.TryGet("args", out var argsObj) && argsObj is PyTuple t)
                return t.Items.Length switch
                {
                    0 => "",
                    1 => PyOps.Str(interp, t.Items[0]),
                    _ => PyOps.Repr(interp, argsObj),
                };
            return "";
        });

        // Real CPython BaseExceptionGroup.__new__: auto-upgrades to a real ExceptionGroup instance
        // when every given exception is an Exception (not just BaseException) — matching real
        // CPython's own actual type-selection rule — and rejects mixing a bare BaseException into
        // an explicitly-requested ExceptionGroup.
        BaseExceptionGroupClass.Dict["__new__"] = new PyBuiltinFunction("BaseExceptionGroup.__new__", (interp, a, _) =>
        {
            var requestedCls = (PyClass)a[0];
            var excs = PyOps.Iterate(interp, a[2]).ToList();
            if (excs.Count == 0)
                throw ValueError("exception group must have at least one exception");
            bool allAreExceptions = excs.All(e => e is PyInstance pi && pi.Class.IsSubclassOf(Exception));
            var chosenCls = requestedCls == BaseExceptionGroupClass && allAreExceptions
                ? ExceptionGroupClass
                : requestedCls;
            if (chosenCls.IsSubclassOf(ExceptionGroupClass) && !allAreExceptions)
                throw new PyRaise(MakeInstance(TypeErrorClass, "Cannot nest BaseExceptions in an ExceptionGroup"));
            return new PyInstance(chosenCls);
        });
        BaseExceptionGroupClass.Dict["__init__"] = new PyBuiltinFunction("BaseExceptionGroup.__init__", (_, a, _) =>
        {
            var inst = (PyInstance)a[0];
            object message = a[1];
            var excTuple = new PyTuple(a[2] is PyTuple pt ? pt.Items : ((PyList)a[2]).Items.ToArray());
            inst.Dict["message"] = message;
            inst.Dict["exceptions"] = excTuple;
            inst.Dict["args"] = new PyTuple(new[] { message, excTuple });
            return PyNone.Instance;
        });
        BaseExceptionGroupClass.Dict["__str__"] = new PyBuiltinFunction("BaseExceptionGroup.__str__", (interp, a, _) =>
        {
            var inst = (PyInstance)a[0];
            int n = ((PyTuple)inst.Dict["exceptions"]).Items.Length;
            return $"{PyOps.Str(interp, inst.Dict["message"])} ({n} sub-exception{(n == 1 ? "" : "s")})";
        });
        // Real BaseExceptionGroup.split(condition): partitions .exceptions into (matching,
        // non-matching) sub-groups (either None if empty), preserving .message — scoped to a flat
        // (non-nested) group, since nothing reachable nests exception groups within exception
        // groups. `condition` may be an exception type/tuple of types (isinstance-style) or a
        // callable predicate, matching real CPython.
        BaseExceptionGroupClass.Dict["split"] = new PyBuiltinFunction("BaseExceptionGroup.split", (interp, a, _) =>
        {
            var inst = (PyInstance)a[0];
            object condition = a[1];
            bool Matches(object exc) => condition is PyClass or PyTuple
                ? BuiltinsFactory.IsInstance(exc, condition)
                : PyOps.Truthy(interp, interp.Call(condition, new[] { exc }));

            var matched = new List<object>();
            var unmatched = new List<object>();
            foreach (var exc in ((PyTuple)inst.Dict["exceptions"]).Items)
                (Matches(exc) ? matched : unmatched).Add(exc);

            object Derive(List<object> items) => items.Count == 0
                ? PyNone.Instance
                : SetGroupFields(new PyInstance(inst.Class), inst.Dict["message"], items);

            return new PyTuple(new[] { Derive(matched), Derive(unmatched) });
        });
    }

    private static PyInstance SetGroupFields(PyInstance inst, object message, List<object> exceptions)
    {
        var excTuple = new PyTuple(exceptions.ToArray());
        inst.Dict["message"] = message;
        inst.Dict["exceptions"] = excTuple;
        inst.Dict["args"] = new PyTuple(new[] { message, (object)excTuple });
        return inst;
    }

    /// <summary>Tutte le classi eccezione da pubblicare nei builtins.</summary>
    public static IEnumerable<PyClass> AllClasses()
    {
        yield return BaseException;
        yield return Exception;
        yield return ArithmeticError;
        yield return ZeroDivisionErrorClass;
        yield return OverflowErrorClass;
        yield return AssertionErrorClass;
        yield return AttributeErrorClass;
        yield return BufferErrorClass;
        yield return EOFErrorClass;
        yield return ImportErrorClass;
        yield return ModuleNotFoundErrorClass;
        yield return LookupError;
        yield return IndexErrorClass;
        yield return KeyErrorClass;
        yield return MemoryErrorClass;
        yield return NameErrorClass;
        yield return UnboundLocalErrorClass;
        yield return OSErrorClass;
        yield return BlockingIOErrorClass;
        yield return ConnectionErrorClass;
        yield return BrokenPipeErrorClass;
        yield return ConnectionAbortedErrorClass;
        yield return ConnectionRefusedErrorClass;
        yield return ConnectionResetErrorClass;
        yield return FileExistsErrorClass;
        yield return FileNotFoundErrorClass;
        yield return IsADirectoryErrorClass;
        yield return NotADirectoryErrorClass;
        yield return InterruptedErrorClass;
        yield return PermissionErrorClass;
        yield return TimeoutErrorClass;
        yield return ReferenceErrorClass;
        yield return RuntimeErrorClass;
        yield return NotImplementedErrorClass;
        yield return RecursionErrorClass;
        yield return StopIterationClass;
        yield return StopAsyncIterationClass;
        yield return SyntaxErrorClass;
        yield return IndentationErrorClass;
        yield return SystemErrorClass;
        yield return SystemExitClass;
        yield return KeyboardInterruptClass;
        yield return TypeErrorClass;
        yield return ValueErrorClass;
        yield return UnicodeErrorClass;
        yield return UnicodeDecodeErrorClass;
        yield return UnicodeEncodeErrorClass;
        yield return Warning;
        yield return DeprecationWarningClass;
        yield return UserWarningClass;
        yield return RuntimeWarningClass;
        yield return PendingDeprecationWarningClass;
        yield return SyntaxWarningClass;
        yield return FutureWarningClass;
        yield return ImportWarningClass;
        yield return UnicodeWarningClass;
        yield return BytesWarningClass;
        yield return ResourceWarningClass;
        yield return EncodingWarningClass;
        yield return BaseExceptionGroupClass;
        yield return ExceptionGroupClass;
    }

    private static PyClass Derive(string name, PyClass baseClass)
        => new(name, new List<PyClass> { baseClass });

    // ---------------------------------------------------------------- factory

    public static PyInstance MakeInstance(PyClass cls, params object[] args)
    {
        var inst = new PyInstance(cls);
        inst.Dict["args"] = new PyTuple(args);
        return inst;
    }

    public static PyRaise Raise(PyClass cls, string message)
        => new(MakeInstance(cls, message));

    /// <summary>A real OSError-family construction: sets `.errno`/`.strerror` (and `.filename`,
    /// when given) as real attributes, not just `.args` entries — matching real CPython's own
    /// `OSError.__new__` unpacking of a 2/3-arg call, and needed because `MakeInstance` (the
    /// generic path) deliberately never runs `__init__`. `.args` itself mirrors real CPython too:
    /// a `filename` is folded into the `.filename` attribute but excluded from `.args`.</summary>
    public static PyInstance MakeOSError(PyClass cls, System.Numerics.BigInteger errno, string strerror, object? filename = null)
    {
        var inst = new PyInstance(cls);
        inst.Dict["args"] = new PyTuple(new object[] { errno, strerror });
        inst.Dict["errno"] = errno;
        inst.Dict["strerror"] = strerror;
        inst.Dict["filename"] = filename ?? (object)PyNone.Instance;
        return inst;
    }

    public static PyRaise TypeError(string msg) => Raise(TypeErrorClass, msg);
    public static PyRaise ValueError(string msg) => Raise(ValueErrorClass, msg);
    public static PyRaise IndexError(string msg) => Raise(IndexErrorClass, msg);
    public static PyRaise NameError(string msg) => Raise(NameErrorClass, msg);
    public static PyRaise AttributeError(string msg) => Raise(AttributeErrorClass, msg);
    public static PyRaise ZeroDivisionError(string msg) => Raise(ZeroDivisionErrorClass, msg);
    public static PyRaise RuntimeError(string msg) => Raise(RuntimeErrorClass, msg);
    public static PyRaise NotImplementedError(string msg) => Raise(NotImplementedErrorClass, msg);
    public static PyRaise OSError(string msg) => Raise(OSErrorClass, msg);
    public static PyRaise ImportError(string msg) => Raise(ImportErrorClass, msg);
    public static PyRaise ModuleNotFoundError(string msg) => Raise(ModuleNotFoundErrorClass, msg);
    public static PyRaise OverflowError(string msg) => Raise(OverflowErrorClass, msg);
    public static PyRaise SyntaxLike(string msg) => Raise(SyntaxErrorClass, msg);
    /// <summary>Real CPython: a generator's `return value` becomes StopIteration(value)'s `.value`
    /// attribute (None, with empty .args, for a bare `return`/falling off the end) — found via
    /// httpx's real sync auth flow catching `StopIteration` around `auth_flow.send(...)`.</summary>
    public static PyRaise StopIteration(object? value = null)
    {
        // Real CPython: a None return value (explicit `return None`, bare `return`, or falling off
        // the end) raises a bare StopIteration() with empty .args — only a genuinely non-None value
        // becomes .args == (value,). Both cases still expose the real value via .value.
        bool hasValue = value is not null and not PyNone;
        var inst = MakeInstance(StopIterationClass, hasValue ? new[] { value! } : Array.Empty<object>());
        inst.Dict["value"] = value ?? PyNone.Instance;
        return new PyRaise(inst);
    }

    public static PyRaise KeyError(object key)
        => new(MakeInstance(KeyErrorClass, key));

    /// <summary>Human-readable message for the CLR side (logs, InnerException).</summary>
    public static string FormatForClr(PyInstance value)
    {
        string args = value.Dict.TryGet("args", out var a) && a is PyTuple t && t.Items.Length > 0
            ? string.Join(", ", t.Items.Select(x => x?.ToString() ?? ""))
            : "";
        return args.Length > 0 ? $"{value.Class.Name}: {args}" : value.Class.Name;
    }

    /// <summary>True if the instance is of class cls (or a subclass).</summary>
    public static bool Matches(PyInstance exc, PyClass cls) => exc.Class.IsSubclassOf(cls);

    /// <summary>
    /// A CPython-style traceback string for an unwound exception:
    /// <c>Traceback (most recent call last): ... ClassName: message</c>. The traceback carried by
    /// <see cref="PyRaise"/> is innermost-first, so it is reversed here for display.
    /// </summary>
    public static string FormatTraceback(PyRaise ex)
    {
        var sb = new System.Text.StringBuilder();
        if (ex.Traceback is { Count: > 0 } frames)
        {
            sb.AppendLine("Traceback (most recent call last):");
            for (int i = frames.Count - 1; i >= 0; i--)
                sb.AppendLine(frames[i].ToString());
        }
        sb.Append(FormatForClr(ex.Value));
        return sb.ToString();
    }
}
