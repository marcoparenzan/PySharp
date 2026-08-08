// Copyright (c) 2026 Marco Parenzan
//
// Licensed under the MIT License. See the LICENSE file in the project
// root for full license information.

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
    public static PyRaise StopIteration() => new(MakeInstance(StopIterationClass));

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
