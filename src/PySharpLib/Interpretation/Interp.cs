// Copyright (c) 2026 Marco Parenzan
//
// Licensed under the MIT License. See the LICENSE file in the project
// root for full license information.

using System.Numerics;
using System.Text;
using PySharpLib.Parsing;
using PySharpLib.Runtime;

namespace PySharpLib.Interpretation;

/// <summary>Interprete tree-walking di PySharp.</summary>
public sealed class Interp
{
    private static readonly Dictionary<string, (string Dunder, string Reflected)> BinDunders = new()
    {
        ["+"] = ("__add__", "__radd__"),
        ["-"] = ("__sub__", "__rsub__"),
        ["*"] = ("__mul__", "__rmul__"),
        ["/"] = ("__truediv__", "__rtruediv__"),
        ["//"] = ("__floordiv__", "__rfloordiv__"),
        ["%"] = ("__mod__", "__rmod__"),
        ["**"] = ("__pow__", "__rpow__"),
        ["<<"] = ("__lshift__", "__rlshift__"),
        [">>"] = ("__rshift__", "__rrshift__"),
        ["&"] = ("__and__", "__rand__"),
        ["|"] = ("__or__", "__ror__"),
        ["^"] = ("__xor__", "__rxor__"),
        ["@"] = ("__matmul__", "__rmatmul__"),
    };

    private static readonly Dictionary<string, string> CmpDunders = new()
    {
        ["<"] = "__lt__",
        ["<="] = "__le__",
        [">"] = "__gt__",
        [">="] = "__ge__",
        ["=="] = "__eq__",
        ["!="] = "__ne__",
    };

    /// <summary>Stack of exceptions being handled (for bare raise, and for sys.exc_info()/
    /// traceback.format_exc() to see the real currently-handled exception + its traceback).</summary>
    private readonly Stack<PyRaise> _handling = new();

    /// <summary>The exception currently being handled (innermost active `except:` block), or null
    /// outside of one — real CPython's `sys.exc_info()`/`traceback.format_exc()` semantics.</summary>
    public PyRaise? CurrentHandledException => _handling.Count > 0 ? _handling.Peek() : null;

    /// <summary>Call stack (module frame + function frames). Per-thread (generators/coroutines run on their own threads).</summary>
    [ThreadStatic]
    private static Stack<Frame>? _frames;

    /// <summary>The current function frame (skips the module frame); used by super()/locals()/globals().</summary>
    public static Frame? CurrentFrame
        => _frames?.FirstOrDefault(f => f.Fn is not null);

    /// <summary>The innermost live frame, module frame included (unlike <see cref="CurrentFrame"/>).
    /// Used by locals()/globals() to find the right module when called at true top level (no
    /// function call active) — the fix for a real bug where they fell back to whichever module
    /// happened to be the enclosing C# closure's `module` variable (the builtins module) instead of
    /// the actual currently-executing one.</summary>
    public static Frame? InnermostFrame
        => _frames is { Count: > 0 } ? _frames.Peek() : null;

    /// <summary>
    /// Optional host hook invoked on every executed line, function call/return and unwinding
    /// exception. Runs synchronously on the interpreter thread — a debugger may block inside it
    /// to implement breakpoints/stepping (the intended basis for a VS Code Debug Adapter).
    /// Leave null for zero overhead.
    /// </summary>
    public Action<TraceEvent>? Trace { get; set; }

    public PyModule BuiltinsModule { get; }
    public TextWriter Out { get; set; }
    /// <summary>argv for sys.argv (argv[0] = script).</summary>
    public List<string> Argv { get; } = new() { "" };
    /// <summary>Import hook, set by the Importer (M5). Receives (name, relative level, current module).</summary>
    public Func<Interp, string, int, PyModule, PyModule>? ImportHook { get; set; }

    public Interp(PyModule builtinsModule, TextWriter? stdout = null)
    {
        BuiltinsModule = builtinsModule;
        Out = stdout ?? Console.Out;
    }

    // ================================================================ esecuzione

    public void RunModule(Module ast, PyModule module)
    {
        var env = new Env(module) { IsGlobalScope = true };
        var frame = new Frame(null, env, "<module>", module.FileName, 1);
        (_frames ??= new Stack<Frame>()).Push(frame);
        Trace?.Invoke(new TraceEvent(TraceEventKind.Call, frame.Name, frame.File, frame.Line, env, null));
        try
        {
            foreach (var stmt in ast.Body)
                Exec(stmt, env);
        }
        catch (PyRaise ex)
        {
            RecordFrame(ex, frame);
            throw;
        }
        finally
        {
            _frames.Pop();
        }
    }

    /// <summary>Append a frame to an unwinding exception's traceback (control-flow exceptions excepted).</summary>
    private void RecordFrame(PyRaise ex, Frame frame)
    {
        // StopIteration/StopAsyncIteration are used as control flow — don't pay for their tracebacks.
        if (PyErr.Matches(ex.Value, PyErr.StopIterationClass)
            || PyErr.Matches(ex.Value, PyErr.StopAsyncIterationClass))
            return;
        (ex.Traceback ??= new List<PyFrameInfo>()).Add(frame.Snapshot());
        if (Trace is not null)
            Trace(new TraceEvent(TraceEventKind.Exception, frame.Name, frame.File, frame.Line, frame.Env, ex.Value));
    }

    public void ExecStmts(List<Stmt> stmts, Env env)
    {
        foreach (var s in stmts)
            Exec(s, env);
    }

    public void Exec(Stmt stmt, Env env)
    {
        if (stmt.Line > 0 && _frames is { Count: > 0 })
        {
            var top = _frames.Peek();
            top.Line = stmt.Line;
            if (Trace is not null)
                Trace(new TraceEvent(TraceEventKind.Line, top.Name, top.File, stmt.Line, env, null));
        }

        switch (stmt)
        {
            case BlockStmt b:
                ExecStmts(b.Body, env);
                break;

            case ExprStmt e:
                Eval(e.Value, env);
                break;

            case AssignStmt a:
            {
                var value = Eval(a.Value, env);
                foreach (var target in a.Targets)
                    AssignTo(target, value, env);
                break;
            }

            case AugAssignStmt a:
                ExecAugAssign(a, env);
                break;

            case AnnAssignStmt a:
                if (a.Value is not null)
                    AssignTo(a.Target, Eval(a.Value, env), env);
                // Record the annotated name in __annotations__, evaluated the same way function
                // parameter annotations already are (best-effort: an unresolvable forward
                // reference just falls back to None rather than failing the assignment). Needed by
                // NamedTuple (only cares about the keys) and by typing.get_type_hints on classes
                // (cares about the values too — e.g. a pydantic BaseModel's `x: int` field).
                if (a.Target is NameExpr annName)
                {
                    if (!env.TryGet("__annotations__", out var annObj) || annObj is not PyDict ann
                        || !env.HasLocal("__annotations__"))
                    {
                        ann = new PyDict();
                        env.Set("__annotations__", ann);
                    }
                    object annValue;
                    try
                    {
                        annValue = Eval(a.Annotation, env);
                    }
                    catch (PyRaise)
                    {
                        annValue = PyNone.Instance;
                    }
                    ann[annName.Id] = annValue;
                }
                break;

            case IfStmt i:
                if (PyOps.Truthy(this, Eval(i.Cond, env)))
                    ExecStmts(i.Body, env);
                else
                    ExecStmts(i.OrElse, env);
                break;

            case WhileStmt w:
            {
                bool broke = false;
                while (PyOps.Truthy(this, Eval(w.Cond, env)))
                {
                    try
                    {
                        ExecStmts(w.Body, env);
                    }
                    catch (BreakSignal)
                    {
                        broke = true;
                        break;
                    }
                    catch (ContinueSignal)
                    {
                    }
                }
                if (!broke)
                    ExecStmts(w.OrElse, env);
                break;
            }

            case ForStmt f when f.IsAsync:
                ExecAsyncFor(f, env);
                break;

            case ForStmt f:
            {
                var iterable = Eval(f.Iter, env);
                bool broke = false;
                foreach (var item in PyOps.Iterate(this, iterable))
                {
                    AssignTo(f.Target, item, env);
                    try
                    {
                        ExecStmts(f.Body, env);
                    }
                    catch (BreakSignal)
                    {
                        broke = true;
                        break;
                    }
                    catch (ContinueSignal)
                    {
                    }
                }
                if (!broke)
                    ExecStmts(f.OrElse, env);
                break;
            }

            case FuncDef d:
            {
                var fn = MakeFunction(d.Name, d.Params, d.Body, null, d.IsGenerator, env, d.Returns, d.IsAsync);
                object result = fn;
                for (int i = d.Decorators.Count - 1; i >= 0; i--)
                    result = Call(Eval(d.Decorators[i], env), new[] { result });
                env.Set(d.Name, result);
                break;
            }

            case ClassDef c:
                ExecClassDef(c, env);
                break;

            case ReturnStmt r:
                throw new ReturnSignal(r.Value is null ? PyNone.Instance : Eval(r.Value, env));

            case PassStmt:
                break;
            case BreakStmt:
                throw BreakSignal.Instance;
            case ContinueStmt:
                throw ContinueSignal.Instance;

            case RaiseStmt r:
                ExecRaise(r, env);
                break;

            case TryStmt t:
                ExecTry(t, env);
                break;

            case WithStmt w when w.IsAsync:
                ExecAsyncWith(w, 0, env);
                break;

            case WithStmt w:
                ExecWith(w, env);
                break;

            case ImportStmt imp:
                foreach (var alias in imp.Names)
                {
                    // Import loads the entire a.b.c chain and returns the exact module
                    var module = Import(alias.DottedName, 0, env.Module);
                    if (alias.AsName is not null)
                    {
                        env.Set(alias.AsName, module);
                    }
                    else
                    {
                        // import a.b → binding of the top-level 'a'
                        string top = alias.DottedName.Split('.')[0];
                        env.Set(top, Import(top, 0, env.Module));
                    }
                }
                break;

            case FromImportStmt fi:
            {
                var resolved = Import(fi.Module, fi.Level, env.Module);
                if (fi.Star)
                {
                    if (resolved.Dict.TryGet("__all__", out var allObj) && allObj is PyList allList)
                    {
                        foreach (var nameObj in allList.Items)
                        {
                            if (nameObj is string key && resolved.Dict.TryGet(key, out var val))
                                env.Set(key, val);
                        }
                    }
                    else
                    {
                        foreach (var e in resolved.Dict.Entries)
                        {
                            if (e.Key is string key && !key.StartsWith('_'))
                                env.Set(key, e.Value);
                        }
                    }
                }
                else
                {
                    foreach (var alias in fi.Names)
                    {
                        object value;
                        if (resolved.Dict.TryGet(alias.DottedName, out var v))
                        {
                            value = v;
                        }
                        else
                        {
                            // from pkg import submodule
                            string submoduleAbsolute =
                                (fi.Module.Length > 0 ? fi.Module + "." : "") + alias.DottedName;
                            try
                            {
                                value = Import(submoduleAbsolute, fi.Level, env.Module);
                            }
                            catch (PyRaise ex) when (IsMissingExactly(ex, submoduleAbsolute))
                            {
                                // Only when *this* submodule itself doesn't exist do we report the
                                // friendlier "cannot import name" — matching CPython. Any other
                                // failure (the submodule exists but *it* failed to import, e.g. one
                                // of its own dependencies is missing) must propagate unchanged, or
                                // the real cause is lost behind a misleading message.
                                throw PyErr.ImportError(
                                    $"cannot import name '{alias.DottedName}' from '{resolved.Name}'");
                            }
                        }
                        env.Set(alias.AsName ?? alias.DottedName, value);
                    }
                }
                break;
            }

            case GlobalStmt g:
                foreach (var name in g.Names)
                    env.DeclareGlobal(name);
                break;

            case NonlocalStmt n:
                foreach (var name in n.Names)
                    env.DeclareNonlocal(name);
                break;

            case DelStmt d:
                foreach (var target in d.Targets)
                    ExecDelete(target, env);
                break;

            case AssertStmt a:
                if (!PyOps.Truthy(this, Eval(a.Test, env)))
                {
                    string msg = a.Msg is null ? "" : PyOps.Str(this, Eval(a.Msg, env));
                    throw PyErr.Raise(PyErr.AssertionErrorClass, msg);
                }
                break;

            case MatchStmt m:
                ExecMatch(m, env);
                break;

            default:
                throw PyErr.RuntimeError($"statement not supported: {stmt.GetType().Name}");
        }
    }

    private PyModule Import(string name, int level, PyModule current)
    {
        if (ImportHook is null)
            throw PyErr.ModuleNotFoundError($"No module named '{name}' (import system not configured)");
        return ImportHook(this, name, level, current);
    }

    private void ExecAugAssign(AugAssignStmt a, Env env)
    {
        var current = Eval(a.Target, env);
        var operand = Eval(a.Value, env);

        // In-place for mutable types
        if (current is PyList list && a.Op == "+")
        {
            list.Items.AddRange(PyOps.Iterate(this, operand));
            return;
        }
        if (current is PySet set && SetItems(operand) is { } otherItems)
        {
            switch (a.Op)
            {
                case "|": set.Items.UnionWith(otherItems); return;
                case "&": set.Items.IntersectWith(otherItems); return;
                case "-": set.Items.ExceptWith(otherItems); return;
                case "^": set.Items.SymmetricExceptWith(otherItems); return;
            }
        }
        if (current is PyDict dict && a.Op == "|" && operand is PyDict otherDict)
        {
            dict.Update(otherDict);
            return;
        }
        if (current is PyInstance inst)
        {
            string iname = "__i" + BinDunders[a.Op].Dunder[2..];
            if (TryCallMethod(inst, iname, new[] { operand }, out var res))
            {
                if (res is not PyNotImplemented)
                {
                    AssignTo(a.Target, res, env);
                    return;
                }
            }
        }

        AssignTo(a.Target, BinaryOp(a.Op, current, operand), env);
    }

    private void ExecClassDef(ClassDef c, Env env)
    {
        // Real CPython semantics: evaluate every base first (need the full tuple below), then for
        // any base that isn't already a class, call its __mro_entries__(original_bases) to find the
        // real substitute(s) — this is the general protocol behind `class Foo(Generic[T]):`,
        // `class Foo(SomeGenericAlias):`, `class Foo(SomeSpecialForm):` (e.g. TypedDict) all
        // working: none of those objects were ever meant to end up in the MRO themselves.
        var rawBases = new List<object>();
        object? explicitMetaclass = null;
        foreach (var b in c.Bases)
        {
            if (b.Name == "metaclass")
            {
                explicitMetaclass = Eval(b.Value, env);
                continue;
            }
            if (b.Name is not null || b.IsStar || b.IsDoubleStar)
                continue; // other class keywords (__init_subclass__ kwargs and the like): ignored in v1
            rawBases.Add(Eval(b.Value, env));
        }
        var rawBasesTuple = new PyTuple(rawBases.ToArray());

        var bases = new List<PyClass>();
        foreach (var baseVal0 in rawBases)
        {
            var baseVal = baseVal0;
            if (baseVal is PyInstance mroInst
                && TryCallMethod(mroInst, "__mro_entries__", new object[] { rawBasesTuple }, out var entriesObj))
            {
                if (entriesObj is not PyTuple entries)
                    throw PyErr.TypeError("__mro_entries__ must return a tuple");
                foreach (var entry in entries.Items)
                {
                    if (entry is PyClass ec)
                        bases.Add(ec);
                    else if (entry is PyBuiltinFunction ebf)
                        bases.Add(GetPseudoBaseClass(ebf.Name));
                    else
                        throw PyErr.TypeError($"__mro_entries__ must return classes, got {PyOps.TypeName(entry)}");
                }
                continue;
            }
            switch (baseVal)
            {
                case PyClass pc:
                    bases.Add(pc);
                    break;
                case PyBuiltinFunction bf:
                    // "builtin type" base (type, int, str, ...): placeholder pseudo-class
                    bases.Add(GetPseudoBaseClass(bf.Name));
                    break;
                default:
                    throw PyErr.TypeError($"class base must be a class, got {PyOps.TypeName(baseVal)}");
            }
        }

        // Winning metaclass (simplified — see PyClass.Metaclass's doc comment): the explicit
        // `metaclass=` kwarg if given (and it's a real class, not e.g. the `type` builtin, which
        // means "no custom metaclass"), else the first base that itself carries one.
        var metaclass = explicitMetaclass as PyClass ?? bases.Select(b => b.Metaclass).FirstOrDefault(m => m is not null);

        var classEnv = new Env(env.Module, env) { IsClassScope = true };
        ExecStmts(c.Body, classEnv);

        PyClass cls;
        if (metaclass is not null)
        {
            // Real metaclass protocol (simplified to what's been observed — e.g. pydantic's
            // ModelMetaclass): calling the metaclass is what `type.__call__(mcs, name, bases,
            // namespace)` does for a normal `class X(Y, metaclass=M): ...` statement in real
            // CPython. Calls the metaclass's own __new__ (real, interpreted Python, e.g.
            // ModelMetaclass.__new__ building __config__/__fields__/validators) instead of just
            // allocating a plain PyClass — the metaclass's `super().__new__(...)` chain bottoms out
            // at the real class-building fallback in the `case PySuper sup:` __new__ handling below.
            // Metaclass __init__ is deliberately not dispatched: no metaclass in scope so far
            // defines one (ModelMetaclass/ABCMeta don't) — a real gap if that ever changes.
            var namespaceDict = new PyDict();
            foreach (var kv in classEnv.Locals)
                namespaceDict[kv.Key] = kv.Value;
            namespaceDict["__qualname__"] = c.Name;
            namespaceDict["__module__"] = env.Module.Name;
            var basesTuple = new PyTuple(bases.Cast<object>().ToArray());
            object built = metaclass.TryLookup("__new__", out var newFn)
                ? Call(newFn, new object[] { metaclass, c.Name, basesTuple, namespaceDict })
                : TypeConstructorMethods.BuildClass(c.Name, basesTuple, namespaceDict);
            cls = built as PyClass
                ?? throw PyErr.TypeError($"metaclass.__new__() must return a class, not {PyOps.TypeName(built)}");
            cls.Metaclass = metaclass;
            foreach (var kv in namespaceDict.Entries)
                foreach (var inner in InnerFunctions(kv.Value))
                    inner.DefiningClass = cls;
        }
        else
        {
            cls = new PyClass(c.Name, bases);
            foreach (var kv in classEnv.Locals)
            {
                cls.Dict[kv.Key] = kv.Value;
                // Record the defining class for zero-arg super().
                foreach (var inner in InnerFunctions(kv.Value))
                    inner.DefiningClass = cls;
            }
            cls.Dict["__qualname__"] = c.Name;
        }

        // Enum transformation: plain attributes become members (PyInstance with name/value).
        if (bases.Any(b => b.TryLookup("__is_enum__", out _)))
            ConvertToEnum(cls);

        // typing.NamedTuple: genera __init__ e i protocolli tuple dai campi annotati.
        if (bases.Any(b => b.Name == "NamedTuple"))
            ConvertToNamedTuple(cls);

        object result = cls;
        for (int i = c.Decorators.Count - 1; i >= 0; i--)
            result = Call(Eval(c.Decorators[i], env), new[] { result });
        env.Set(c.Name, result);
    }

    // object's default __new__/__init__ — shared by both direct class attribute access
    // (`case PyClass cls:`) and `super().__new__`/`super().__init__` (`case PySuper sup:`), since a
    // class that doesn't override them should behave identically whichever way they're reached.
    private static readonly PyBuiltinFunction ObjectInitFallback =
        new("object.__init__", (_, _, _) => PyNone.Instance);

    /// <summary>Real CPython: `obj.__dict__ = newdict` (equivalently `object.__setattr__(obj,
    /// '__dict__', newdict)`) replaces the instance's whole namespace — pydantic's real
    /// `BaseModel.__init__` uses exactly this (`object_setattr(self, '__dict__', values)`) to set
    /// every validated field at once instead of one attribute at a time.</summary>
    private static void ObjectSetAttrImpl(PyInstance inst, string name, object value)
    {
        if (name == "__dict__")
        {
            inst.Dict.Clear();
            if (value is PyDict newDict)
                foreach (var e in newDict.Entries)
                    inst.Dict[e.Key] = e.Value;
            return;
        }
        inst.Dict[name] = value;
    }

    // object.__setattr__ accessed directly on a class (not via super()) — e.g. pydantic's real
    // `object_setattr = object.__setattr__` module-level alias. Unbound-method shaped: the instance
    // is an explicit first argument, matching ObjectNewFallback's calling convention.
    private static readonly PyBuiltinFunction ObjectSetattrFallback = new("object.__setattr__", (_, a, _) =>
    {
        if (a[0] is not PyInstance inst)
            throw PyErr.AttributeError($"'{PyOps.TypeName(a[0])}' object has no attribute '{a[1]}'");
        ObjectSetAttrImpl(inst, (string)a[1], a[2]);
        return PyNone.Instance;
    });

    private static readonly PyBuiltinFunction ObjectNewFallback = new("object.__new__", (_, a, _) =>
        // type.__new__-shaped call (mcs, name, bases, namespace) — a custom metaclass's own __new__
        // calling `super().__new__(...)` or a stub base's `.__new__` directly (e.g. typing_extensions'
        // real `_ProtocolMeta.__new__` calls `abc.ABCMeta.__new__(mcls, name, bases, namespace,
        // **kwargs)` directly rather than via super()) bottoms out here: build the real class,
        // exactly like real CPython's type.__new__ does.
        a.Length >= 3 && a[0] is PyClass && a[1] is string clsName && a[2] is PyTuple
            ? TypeConstructorMethods.BuildClass(clsName, a[2], a.Length > 3 ? a[3] : PyNone.Instance)
            // object.__new__(cls, ...): a blank instance — the shape used when nothing overrides
            // __new__ for a regular (non-metaclass) class.
            : a.Length > 0 && a[0] is PyClass cls0 ? new PyInstance(cls0) : PyNone.Instance);

    private static readonly Dictionary<string, PyClass> PseudoBases = new();

    /// <summary>The same singleton "class base for a builtin type" pseudo-class `class Foo(int):`
    /// uses for `int`/`str`/etc. — shared (not internal-only) so issubclass()'s builtin-type-as-arg-1
    /// handling compares against the identical objects, not a lookalike copy.</summary>
    internal static PyClass GetPseudoBaseClass(string name)
    {
        lock (PseudoBases)
        {
            if (!PseudoBases.TryGetValue(name, out var cls))
            {
                cls = new PyClass(name, new List<PyClass>());
                PseudoBases[name] = cls;
            }
            return cls;
        }
    }

    /// <summary>Generates __init__/__repr__/__getitem__/__iter__/__len__ for a typing.NamedTuple.</summary>
    private void ConvertToNamedTuple(PyClass cls)
    {
        if (!cls.Dict.TryGet("__annotations__", out var annObj) || annObj is not PyDict ann)
            return;
        var fields = ann.Keys.OfType<string>().ToList();
        var defaults = new Dictionary<string, object>();
        foreach (var f in fields)
        {
            if (cls.Dict.TryGet(f, out var def))
                defaults[f] = def;
        }

        cls.Dict["_fields"] = new PyTuple(fields.Select(f => (object)f).ToArray());
        cls.Dict["__init__"] = new PyBuiltinFunction($"{cls.Name}.__init__", (_, a, kwargs) =>
        {
            var inst = (PyInstance)a[0];
            for (int i = 0; i < fields.Count; i++)
            {
                if (i + 1 < a.Length)
                    inst.Dict[fields[i]] = a[i + 1];
                else if (kwargs is not null && kwargs.TryGetValue(fields[i], out var kv))
                    inst.Dict[fields[i]] = kv;
                else if (defaults.TryGetValue(fields[i], out var def))
                    inst.Dict[fields[i]] = def;
                else
                    throw PyErr.TypeError($"{cls.Name}() missing argument '{fields[i]}'");
            }
            return PyNone.Instance;
        });
        cls.Dict["__repr__"] = new PyBuiltinFunction($"{cls.Name}.__repr__", (interp, a, _) =>
        {
            var inst = (PyInstance)a[0];
            return $"{cls.Name}({string.Join(", ", fields.Select(f => $"{f}={PyOps.Repr(interp, inst.Dict[f])}"))})";
        });
        cls.Dict["__getitem__"] = new PyBuiltinFunction($"{cls.Name}.__getitem__", (_, a, _) =>
        {
            var inst = (PyInstance)a[0];
            int i = PyOps.SeqIndex(a[1], fields.Count, cls.Name);
            return inst.Dict[fields[i]];
        });
        cls.Dict["__len__"] = new PyBuiltinFunction($"{cls.Name}.__len__", (_, _, _) =>
            new BigInteger(fields.Count));
        cls.Dict["__iter__"] = new PyBuiltinFunction($"{cls.Name}.__iter__", (_, a, _) =>
        {
            var inst = (PyInstance)a[0];
            return new PyIterator(fields.Select(f => inst.Dict[f]).GetEnumerator());
        });
        cls.Dict["_asdict"] = new PyBuiltinFunction($"{cls.Name}._asdict", (_, a, _) =>
        {
            var inst = (PyInstance)a[0];
            var result = new PyDict();
            foreach (var f in fields)
                result[f] = inst.Dict[f];
            return result;
        });
    }

    /// <summary>Converts the value attributes of an enum class into singleton members.</summary>
    private void ConvertToEnum(PyClass cls)
    {
        var members = new PyDict();
        var nextAuto = BigInteger.One;
        foreach (var key in cls.Dict.Keys.OfType<string>().ToList())
        {
            if (key.StartsWith("__", StringComparison.Ordinal))
                continue;
            var value = cls.Dict[key];
            if (value is PyFunction or PyBuiltinFunction or PyStaticMethod or PyClassMethod or PyProperty or PyClass)
                continue;

            if (value is PyInstance autoInst && autoInst.Class.Name == "auto")
                value = nextAuto;
            if (value is BigInteger bi)
                nextAuto = bi + 1;

            var member = new PyInstance(cls);
            member.Dict["name"] = key;
            member.Dict["_name_"] = key;
            member.Dict["value"] = value;
            member.Dict["_value_"] = value;
            cls.Dict[key] = member;
            members[key] = member;
        }
        cls.Dict["__members__"] = members;
    }

    private static IEnumerable<PyFunction> InnerFunctions(object classAttr)
    {
        switch (classAttr)
        {
            case PyFunction f:
                yield return f;
                break;
            case PyStaticMethod s when s.Function is PyFunction f:
                yield return f;
                break;
            case PyClassMethod c when c.Function is PyFunction f:
                yield return f;
                break;
            case PyProperty p:
                if (p.Getter is PyFunction g) yield return g;
                if (p.Setter is PyFunction st) yield return st;
                if (p.Deleter is PyFunction dl) yield return dl;
                break;
        }
    }

    private void ExecRaise(RaiseStmt r, Env env)
    {
        if (r.Exc is null)
        {
            if (_handling.Count == 0)
                throw PyErr.RuntimeError("No active exception to re-raise");
            throw _handling.Peek();
        }

        var excValue = Eval(r.Exc, env);
        PyInstance instance = excValue switch
        {
            PyInstance i => i,
            PyClass cls when cls.IsSubclassOf(PyErr.BaseException) => (PyInstance)Call(cls, Array.Empty<object>()),
            _ => throw PyErr.TypeError("exceptions must derive from BaseException"),
        };

        if (r.Cause is not null)
        {
            var cause = Eval(r.Cause, env);
            instance.Dict["__cause__"] = cause;
        }
        throw new PyRaise(instance);
    }

    private void ExecTry(TryStmt t, Env env)
    {
        try
        {
            bool completed = false;
            try
            {
                ExecStmts(t.Body, env);
                completed = true;
            }
            catch (PyRaise ex)
            {
                bool handled = false;
                foreach (var handler in t.Handlers)
                {
                    if (!HandlerMatches(handler, ex.Value, env))
                        continue;
                    handled = true;
                    if (handler.Name is not null)
                        env.Set(handler.Name, ex.Value);
                    _handling.Push(ex);
                    try
                    {
                        ExecStmts(handler.Body, env);
                    }
                    finally
                    {
                        _handling.Pop();
                    }
                    break;
                }
                if (!handled)
                    throw;
            }
            if (completed)
                ExecStmts(t.OrElse, env);
        }
        finally
        {
            ExecStmts(t.Finally, env);
        }
    }

    // ---------------------------------------------------------------- match/case (PEP 634)

    /// <summary>
    /// Real (not stubbed) structural pattern matching: literal/capture/wildcard/value/sequence/
    /// mapping/class/or/as patterns, with guards. Bindings from a case's pattern are collected into
    /// a scratch dict and only applied to <paramref name="env"/> once the whole pattern (not just a
    /// prefix of it) matches — matching real CPython, which never leaves a partial binding behind
    /// from a case whose structural match failed partway through. A guard that evaluates falsy still
    /// leaves that case's bindings in place (matching real CPython) and moves on to the next case.
    /// </summary>
    private void ExecMatch(MatchStmt m, Env env)
    {
        var subject = Eval(m.Subject, env);
        foreach (var c in m.Cases)
        {
            var bindings = new Dictionary<string, object>();
            if (!TryMatchPattern(c.Pattern, subject, env, bindings))
                continue;
            foreach (var kv in bindings)
                env.Set(kv.Key, kv.Value);
            if (c.Guard is not null && !PyOps.Truthy(this, Eval(c.Guard, env)))
                continue;
            ExecStmts(c.Body, env);
            return;
        }
    }

    private bool TryMatchPattern(Pattern pattern, object subject, Env env, Dictionary<string, object> bindings)
    {
        switch (pattern)
        {
            case CapturePattern cap:
                if (cap.Name is not null)
                    bindings[cap.Name] = subject;
                return true;

            case LiteralPattern lit:
            {
                var value = Eval(lit.Value, env);
                // True/False/None compare by identity, matching real CPython's singleton patterns;
                // everything else (numbers/strings/bytes) compares by ==.
                return lit.Value is BoolLit or NoneLit ? IsIdentical(subject, value) : RichEquals(subject, value);
            }

            case ValuePattern vp:
                return RichEquals(subject, Eval(vp.Value, env));

            case OrPattern orPat:
            {
                foreach (var alt in orPat.Alternatives)
                {
                    var altBindings = new Dictionary<string, object>();
                    if (!TryMatchPattern(alt, subject, env, altBindings))
                        continue;
                    foreach (var kv in altBindings)
                        bindings[kv.Key] = kv.Value;
                    return true;
                }
                return false;
            }

            case AsPattern asPat:
                if (!TryMatchPattern(asPat.Inner, subject, env, bindings))
                    return false;
                bindings[asPat.Name] = subject;
                return true;

            case SequencePattern seq:
                return TryMatchSequence(seq, subject, env, bindings);

            case MappingPattern map:
                return TryMatchMapping(map, subject, env, bindings);

            case ClassPattern cls:
                return TryMatchClass(cls, subject, env, bindings);

            default:
                throw PyErr.RuntimeError($"pattern not supported: {pattern.GetType().Name}");
        }
    }

    private bool TryMatchSequence(SequencePattern seq, object subject, Env env, Dictionary<string, object> bindings)
    {
        // Real CPython: matches list/tuple/range-shaped sequences, explicitly excluding
        // str/bytes/bytearray (which are iterable but not "sequence patterns" under PEP 634).
        List<object>? items = subject switch
        {
            PyList l => l.Items,
            PyTuple t => t.Items.ToList(),
            PyRange r => r.Enumerate().ToList(),
            _ => null,
        };
        if (items is null)
            return false;

        int starIndex = seq.Items.FindIndex(p => p is StarPattern);
        if (starIndex < 0)
        {
            if (items.Count != seq.Items.Count)
                return false;
            for (int i = 0; i < items.Count; i++)
                if (!TryMatchPattern(seq.Items[i], items[i], env, bindings))
                    return false;
            return true;
        }

        int after = seq.Items.Count - starIndex - 1;
        if (items.Count < seq.Items.Count - 1)
            return false;
        for (int i = 0; i < starIndex; i++)
            if (!TryMatchPattern(seq.Items[i], items[i], env, bindings))
                return false;
        var star = (StarPattern)seq.Items[starIndex];
        if (star.Name is not null)
            bindings[star.Name] = new PyList(items.Skip(starIndex).Take(items.Count - starIndex - after));
        for (int i = 0; i < after; i++)
            if (!TryMatchPattern(seq.Items[starIndex + 1 + i], items[items.Count - after + i], env, bindings))
                return false;
        return true;
    }

    private bool TryMatchMapping(MappingPattern map, object subject, Env env, Dictionary<string, object> bindings)
    {
        // v1 scope: a real dict only (not the full Mapping protocol on an arbitrary PyInstance) —
        // nothing observed so far has needed a custom-Mapping subject.
        if (subject is not PyDict dict)
            return false;
        var matchedKeys = new HashSet<object>(PyEqualityComparer.Instance);
        foreach (var (keyExpr, valuePat) in map.Items)
        {
            var key = Eval(keyExpr, env);
            if (!dict.TryGet(key, out var value))
                return false;
            if (!TryMatchPattern(valuePat, value, env, bindings))
                return false;
            matchedKeys.Add(key);
        }
        if (map.RestName is not null)
        {
            var rest = new PyDict();
            foreach (var e in dict.Entries)
                if (!matchedKeys.Contains(e.Key))
                    rest[e.Key] = e.Value;
            bindings[map.RestName] = rest;
        }
        return true;
    }

    private bool TryMatchClass(ClassPattern cp, object subject, Env env, Dictionary<string, object> bindings)
    {
        var clsObj = Eval(cp.Cls, env);
        if (!Builtins.BuiltinsFactory.IsInstance(subject, clsObj))
            return false;

        if (cp.Positional.Count > 0)
        {
            // A handful of builtin types have no real __match_args__ — PEP 634 special-cases them:
            // a single positional sub-pattern matches the whole subject value directly.
            if (clsObj is PyBuiltinFunction bf && Builtins.BuiltinsFactory.BuiltinTypeNames.Contains(bf.Name))
            {
                if (cp.Positional.Count != 1)
                    throw PyErr.TypeError($"{bf.Name}() accepts 1 positional sub-pattern ({cp.Positional.Count} given)");
                if (!TryMatchPattern(cp.Positional[0], subject, env, bindings))
                    return false;
            }
            else
            {
                List<string>? matchArgs = clsObj is PyClass realCls
                    && realCls.TryLookup("__match_args__", out var maObj) && maObj is PyTuple maTuple
                    ? maTuple.Items.OfType<string>().ToList()
                    : null;
                string clsName = (clsObj as PyClass)?.Name ?? PyOps.TypeName(clsObj);
                if (matchArgs is null)
                    throw PyErr.TypeError($"{clsName}() accepts 0 positional sub-patterns");
                if (cp.Positional.Count > matchArgs.Count)
                    throw PyErr.TypeError(
                        $"{clsName}() accepts {matchArgs.Count} positional sub-patterns ({cp.Positional.Count} given)");
                for (int i = 0; i < cp.Positional.Count; i++)
                {
                    if (!TryGetAttr(subject, matchArgs[i], out var attrVal))
                        return false;
                    if (!TryMatchPattern(cp.Positional[i], attrVal, env, bindings))
                        return false;
                }
            }
        }

        foreach (var (name, pat) in cp.Keyword)
        {
            if (!TryGetAttr(subject, name, out var attrVal))
                return false;
            if (!TryMatchPattern(pat, attrVal, env, bindings))
                return false;
        }
        return true;
    }

    private bool HandlerMatches(ExceptHandler handler, PyInstance exc, Env env)
    {
        if (handler.Type is null)
            return true;
        var type = Eval(handler.Type, env);
        return TypeMatchesException(type, exc);
    }

    private bool TypeMatchesException(object type, PyInstance exc) => type switch
    {
        PyClass cls => PyErr.Matches(exc, cls),
        PyTuple tuple => tuple.Items.Any(x => TypeMatchesException(x, exc)),
        _ => throw PyErr.TypeError("catching classes that do not inherit from BaseException is not allowed"),
    };

    /// <summary>True if <paramref name="ex"/> is exactly "No module named '&lt;absolute&gt;'" —
    /// i.e. the submodule a `from pkg import name` fallback tried simply doesn't exist, as opposed
    /// to existing but failing to import for some other reason (a missing dependency of its own,
    /// a syntax/feature gap, etc.), which must propagate unchanged.</summary>
    private static bool IsMissingExactly(PyRaise ex, string absolute)
        => PyErr.Matches(ex.Value, PyErr.ModuleNotFoundErrorClass)
           && ex.Value.Dict.TryGet("args", out var argsObj)
           && argsObj is PyTuple { Items: [string msg] }
           && msg == $"No module named '{absolute}'";

    private void ExecWith(WithStmt w, Env env)
    {
        ExecWithItem(w, 0, env);
    }

    /// <summary><c>await expr</c> from within a C#-driven statement (async for/with).</summary>
    private object Await(object awaitable)
    {
        var coro = PyCoroutine.Current
                   ?? throw PyErr.SyntaxLike("'async for'/'async with' outside async function");
        return coro.RunAwait(this, awaitable);
    }

    private void ExecAsyncFor(ForStmt f, Env env)
    {
        var iterable = Eval(f.Iter, env);
        var iterator = TryCallMethod(iterable, "__aiter__", Array.Empty<object>(), out var ait)
            ? ait
            : iterable;
        bool broke = false;
        while (true)
        {
            object item;
            try
            {
                item = Await(CallMethod(iterator, "__anext__", Array.Empty<object>()));
            }
            catch (PyRaise ex) when (PyErr.Matches(ex.Value, PyErr.StopAsyncIterationClass))
            {
                break;
            }
            AssignTo(f.Target, item, env);
            try
            {
                ExecStmts(f.Body, env);
            }
            catch (BreakSignal)
            {
                broke = true;
                break;
            }
            catch (ContinueSignal)
            {
            }
        }
        if (!broke)
            ExecStmts(f.OrElse, env);
    }

    private void ExecAsyncWith(WithStmt w, int index, Env env)
    {
        if (index >= w.Items.Count)
        {
            ExecStmts(w.Body, env);
            return;
        }

        var item = w.Items[index];
        var ctx = Eval(item.Ctx, env);
        var entered = Await(CallMethod(ctx, "__aenter__", Array.Empty<object>()));
        if (item.Target is not null)
            AssignTo(item.Target, entered, env);

        PyRaise? pending = null;
        try
        {
            ExecAsyncWith(w, index + 1, env);
        }
        catch (PyRaise ex)
        {
            pending = ex;
        }
        catch
        {
            Await(CallMethod(ctx, "__aexit__",
                new object[] { PyNone.Instance, PyNone.Instance, PyNone.Instance }));
            throw;
        }

        if (pending is null)
        {
            Await(CallMethod(ctx, "__aexit__",
                new object[] { PyNone.Instance, PyNone.Instance, PyNone.Instance }));
        }
        else
        {
            var exitResult = Await(CallMethod(ctx, "__aexit__",
                new object[] { pending.Value.Class, pending.Value, PyNone.Instance }));
            if (!PyOps.Truthy(this, exitResult))
                throw pending;
        }
    }

    private void ExecWithItem(WithStmt w, int index, Env env)
    {
        if (index >= w.Items.Count)
        {
            ExecStmts(w.Body, env);
            return;
        }

        var item = w.Items[index];
        var ctx = Eval(item.Ctx, env);
        var entered = CallMethod(ctx, "__enter__", Array.Empty<object>());
        if (item.Target is not null)
            AssignTo(item.Target, entered, env);

        PyRaise? pending = null;
        try
        {
            ExecWithItem(w, index + 1, env);
        }
        catch (PyRaise ex)
        {
            pending = ex;
        }
        catch
        {
            // return/break/continue (or CLR errors): __exit__ must still be called
            CallMethod(ctx, "__exit__",
                new object[] { PyNone.Instance, PyNone.Instance, PyNone.Instance });
            throw;
        }

        object exitResult;
        if (pending is null)
        {
            exitResult = CallMethod(ctx, "__exit__",
                new object[] { PyNone.Instance, PyNone.Instance, PyNone.Instance });
        }
        else
        {
            exitResult = CallMethod(ctx, "__exit__",
                new object[] { pending.Value.Class, pending.Value, PyNone.Instance });
            if (!PyOps.Truthy(this, exitResult))
                throw pending;
        }
    }

    private void ExecDelete(Expr target, Env env)
    {
        switch (target)
        {
            case NameExpr n:
                if (!env.Delete(n.Id))
                    throw PyErr.NameError($"name '{n.Id}' is not defined");
                break;
            case AttributeExpr a:
            {
                var obj = Eval(a.Obj, env);
                DelAttr(obj, a.Name);
                break;
            }
            case IndexExpr i:
            {
                var obj = Eval(i.Obj, env);
                var index = EvalIndex(i.Index, env);
                DelItem(obj, index);
                break;
            }
            case TupleExpr t:
                foreach (var item in t.Items)
                    ExecDelete(item, env);
                break;
            case ListExpr l:
                foreach (var item in l.Items)
                    ExecDelete(item, env);
                break;
            default:
                throw PyErr.SyntaxLike("cannot delete this expression");
        }
    }

    // ================================================================ assegnazione

    public void AssignTo(Expr target, object value, Env env)
    {
        switch (target)
        {
            case NameExpr n:
                env.Set(n.Id, value);
                break;
            case AttributeExpr a:
                SetAttr(Eval(a.Obj, env), a.Name, value);
                break;
            case IndexExpr i:
                SetItem(Eval(i.Obj, env), EvalIndex(i.Index, env), value);
                break;
            case TupleExpr t:
                UnpackInto(t.Items, value, env);
                break;
            case ListExpr l:
                UnpackInto(l.Items, value, env);
                break;
            case StarExpr:
                throw PyErr.SyntaxLike("starred assignment target must be in a list or tuple");
            default:
                throw PyErr.SyntaxLike("cannot assign to expression");
        }
    }

    private void UnpackInto(List<Expr> targets, object value, Env env)
    {
        var values = PyOps.Iterate(this, value).ToList();
        int starIndex = targets.FindIndex(t => t is StarExpr);

        if (starIndex < 0)
        {
            if (values.Count != targets.Count)
                throw PyErr.ValueError(
                    values.Count < targets.Count
                        ? $"not enough values to unpack (expected {targets.Count}, got {values.Count})"
                        : $"too many values to unpack (expected {targets.Count})");
            for (int i = 0; i < targets.Count; i++)
                AssignTo(targets[i], values[i], env);
            return;
        }

        int after = targets.Count - starIndex - 1;
        if (values.Count < targets.Count - 1)
            throw PyErr.ValueError($"not enough values to unpack (expected at least {targets.Count - 1}, got {values.Count})");
        for (int i = 0; i < starIndex; i++)
            AssignTo(targets[i], values[i], env);
        var middle = new PyList(values.Skip(starIndex).Take(values.Count - starIndex - after));
        AssignTo(((StarExpr)targets[starIndex]).Value, middle, env);
        for (int i = 0; i < after; i++)
            AssignTo(targets[starIndex + 1 + i], values[values.Count - after + i], env);
    }

    // ================================================================ evaluation

    public object Eval(Expr expr, Env env)
    {
        switch (expr)
        {
            case IntLit i: return i.Value;
            case FloatLit f: return f.Value;
            case StrLit s: return s.Value;
            case BytesLit b: return new PyBytes(b.Value);
            case BoolLit b: return b.Value;
            case NoneLit: return PyNone.Instance;
            case EllipsisLit: return PyEllipsis.Instance;

            case NameExpr n:
                if (env.TryGet(n.Id, out var v))
                    return v;
                throw PyErr.NameError($"name '{n.Id}' is not defined");

            case TupleExpr t:
                return new PyTuple(EvalItems(t.Items, env).ToArray());
            case ListExpr l:
                return new PyList(EvalItems(l.Items, env));
            case SetExpr s:
                return new PySet(EvalItems(s.Items, env));
            case DictExpr d:
            {
                var dict = new PyDict();
                foreach (var (key, valueExpr) in d.Items)
                {
                    if (key is null)
                    {
                        var mapping = Eval(valueExpr, env);
                        if (mapping is PyDict pd)
                            dict.Update(pd);
                        else
                            foreach (var k in PyOps.Iterate(this, mapping))
                                dict[k] = GetItem(mapping, k);
                    }
                    else
                    {
                        dict[Eval(key, env)] = Eval(valueExpr, env);
                    }
                }
                return dict;
            }

            case UnaryExpr u:
                return UnaryOp(u.Op, Eval(u.Operand, env));

            case BinaryExpr b:
                return BinaryOp(b.Op, Eval(b.Left, env), Eval(b.Right, env));

            case BoolOpExpr b:
            {
                object last = PyNone.Instance;
                foreach (var operand in b.Values)
                {
                    last = Eval(operand, env);
                    bool truthy = PyOps.Truthy(this, last);
                    if (b.Op == "and" && !truthy)
                        return last;
                    if (b.Op == "or" && truthy)
                        return last;
                }
                return last;
            }

            case CompareExpr c:
            {
                var left = Eval(c.Left, env);
                for (int i = 0; i < c.Ops.Count; i++)
                {
                    var right = Eval(c.Comparators[i], env);
                    if (!CompareOnce(c.Ops[i], left, right))
                        return false;
                    left = right;
                }
                return true;
            }

            case CallExpr call:
                return EvalCall(call, env);

            case AttributeExpr a:
                return GetAttr(Eval(a.Obj, env), a.Name);

            case IndexExpr i:
                return GetItem(Eval(i.Obj, env), EvalIndex(i.Index, env));

            case SliceExpr s:
                return EvalSlice(s, env);

            case IfExpExpr i:
                return PyOps.Truthy(this, Eval(i.Cond, env)) ? Eval(i.Then, env) : Eval(i.Else, env);

            case LambdaExpr l:
                return MakeFunction("<lambda>", l.Params, null, l.Body, isGenerator: false, env);

            case WalrusExpr w:
            {
                var value = Eval(w.Value, env);
                env.Set(w.Name, value);
                return value;
            }

            case YieldExpr y:
            {
                var gen = PyGenerator.Current
                          ?? throw PyErr.SyntaxLike("'yield' outside function");
                if (y.IsFrom)
                {
                    var sub = Eval(y.Value!, env);
                    foreach (var item in PyOps.Iterate(this, sub))
                        gen.Yield(item);
                    return PyNone.Instance;
                }
                var val = y.Value is null ? PyNone.Instance : Eval(y.Value, env);
                return gen.Yield(val);
            }

            case AwaitExpr aw:
            {
                var coro = PyCoroutine.Current
                           ?? throw PyErr.SyntaxLike("'await' outside async function");
                return coro.RunAwait(this, Eval(aw.Value, env));
            }

            case FStringExpr f:
            {
                var sb = new StringBuilder();
                foreach (var part in f.Parts)
                    AppendFStringPart(sb, part, env);
                return sb.ToString();
            }

            case ComprehensionExpr comp:
                return EvalComprehension(comp, env);

            case StarExpr:
                throw PyErr.SyntaxLike("can't use starred expression here");

            default:
                throw PyErr.RuntimeError($"expression not supported: {expr.GetType().Name}");
        }
    }

    private IEnumerable<object> EvalItems(List<Expr> items, Env env)
    {
        foreach (var item in items)
        {
            if (item is StarExpr star)
            {
                foreach (var x in PyOps.Iterate(this, Eval(star.Value, env)))
                    yield return x;
            }
            else
            {
                yield return Eval(item, env);
            }
        }
    }

    private object EvalIndex(Expr index, Env env)
        => index switch
        {
            SliceExpr s => EvalSlice(s, env),
            TupleExpr t => new PyTuple(t.Items.Select(i => EvalIndex(i, env)).ToArray()),
            _ => Eval(index, env),
        };

    private PySlice EvalSlice(SliceExpr s, Env env)
        => new(
            s.Start is null ? PyNone.Instance : Eval(s.Start, env),
            s.Stop is null ? PyNone.Instance : Eval(s.Stop, env),
            s.Step is null ? PyNone.Instance : Eval(s.Step, env));

    private void AppendFStringPart(StringBuilder sb, FStringPart part, Env env)
    {
        switch (part)
        {
            case FStringText t:
                sb.Append(t.Text);
                break;
            case FStringValue v:
            {
                var value = Eval(v.Value, env);
                value = v.Conversion switch
                {
                    'r' => PyOps.Repr(this, value),
                    's' => PyOps.Str(this, value),
                    'a' => PyOps.Repr(this, value),
                    _ => value,
                };
                string spec = "";
                if (v.FormatSpec is not null)
                {
                    var specSb = new StringBuilder();
                    foreach (var sp in v.FormatSpec)
                        AppendFStringPart(specSb, sp, env);
                    spec = specSb.ToString();
                }
                sb.Append(FormatValue(value, spec));
                break;
            }
        }
    }

    public string FormatValue(object value, string spec)
    {
        if (value is PyInstance inst && TryCallMethod(inst, "__format__", new object[] { spec }, out var r))
            return r as string ?? throw PyErr.TypeError("__format__ must return a str");
        return PyFormat.Format(this, value, spec);
    }

    private object EvalComprehension(ComprehensionExpr comp, Env env)
    {
        switch (comp.Kind)
        {
            case ComprehensionKind.List:
                return new PyList(ComprehensionValues(comp, env));
            case ComprehensionKind.Set:
                return new PySet(ComprehensionValues(comp, env));
            case ComprehensionKind.Dict:
            {
                var dict = new PyDict();
                var compEnv = new Env(env.Module, env);
                foreach (var _ in RunCompFors(comp.Fors, 0, compEnv))
                    dict[Eval(comp.Key!, compEnv)] = Eval(comp.Value!, compEnv);
                return dict;
            }
            case ComprehensionKind.Generator:
                return new PyIterator(ComprehensionValues(comp, env).GetEnumerator());
            default:
                throw PyErr.RuntimeError("unknown comprehension kind");
        }
    }

    private IEnumerable<object> ComprehensionValues(ComprehensionExpr comp, Env env)
    {
        var compEnv = new Env(env.Module, env);
        foreach (var _ in RunCompFors(comp.Fors, 0, compEnv))
            yield return Eval(comp.Element!, compEnv);
    }

    private IEnumerable<bool> RunCompFors(List<CompFor> fors, int index, Env compEnv)
    {
        if (index >= fors.Count)
        {
            yield return true;
            yield break;
        }
        var f = fors[index];
        foreach (var item in PyOps.Iterate(this, Eval(f.Iter, compEnv)))
        {
            AssignTo(f.Target, item, compEnv);
            if (f.Ifs.All(cond => PyOps.Truthy(this, Eval(cond, compEnv))))
            {
                foreach (var x in RunCompFors(fors, index + 1, compEnv))
                    yield return x;
            }
        }
    }

    // ================================================================ calls

    private object EvalCall(CallExpr call, Env env)
    {
        var callee = Eval(call.Func, env);
        var args = new List<object>();
        Dictionary<string, object>? kwargs = null;

        foreach (var arg in call.Args)
        {
            if (arg.IsStar)
            {
                args.AddRange(PyOps.Iterate(this, Eval(arg.Value, env)));
            }
            else if (arg.IsDoubleStar)
            {
                var mapping = Eval(arg.Value, env);
                if (mapping is not PyDict d)
                    throw PyErr.TypeError("argument after ** must be a mapping");
                kwargs ??= new Dictionary<string, object>();
                foreach (var e in d.Entries)
                {
                    if (e.Key is not string key)
                        throw PyErr.TypeError("keywords must be strings");
                    kwargs[key] = e.Value;
                }
            }
            else if (arg.Name is not null)
            {
                kwargs ??= new Dictionary<string, object>();
                kwargs[arg.Name] = Eval(arg.Value, env);
            }
            else
            {
                args.Add(Eval(arg.Value, env));
            }
        }

        return Call(callee, args.ToArray(), kwargs);
    }

    // Real CPython's sys.getrecursionlimit() default (1000), guarding every call — not just
    // Python-level function calls but builtin/dunder dispatch too, since those recurse through
    // this exact same entry point. Thread-static like _frames (coroutines run on their own
    // threads). Found the hard way: object.__str__'s new default (calling __repr__) combined
    // with a real corpus test's `Foo.__repr__ = Foo.__str__` (recursion.py, already Xfail-listed
    // for exactly this) turned what real CPython raises as a catchable RecursionError into an
    // actual unbounded C# stack overflow — a real, pre-existing gap (no call path in this
    // interpreter enforced any recursion limit before), not specific to that one dunder pair.
    [ThreadStatic]
    private static int _callDepth;
    private const int MaxCallDepth = 1000;

    public object Call(object callee, object[] args, Dictionary<string, object>? kwargs = null)
    {
        if (++_callDepth > MaxCallDepth)
        {
            _callDepth--;
            throw new PyRaise(PyErr.MakeInstance(PyErr.RecursionErrorClass, "maximum recursion depth exceeded"));
        }
        try
        {
            return CallCore(callee, args, kwargs);
        }
        finally
        {
            _callDepth--;
        }
    }

    private object CallCore(object callee, object[] args, Dictionary<string, object>? kwargs)
    {
        switch (callee)
        {
            case PyBuiltinFunction bf:
                try
                {
                    return bf.Fn(this, args, kwargs);
                }
                catch (IndexOutOfRangeException)
                {
                    // args[i] out-of-range access in a builtin → not enough arguments
                    throw PyErr.TypeError($"{bf.Name}() missing required argument");
                }
                catch (ArgumentOutOfRangeException)
                {
                    throw PyErr.TypeError($"{bf.Name}() missing required argument");
                }
                catch (InvalidCastException)
                {
                    throw PyErr.TypeError($"{bf.Name}(): invalid argument type");
                }

            case PyBoundMethod bm:
            {
                var newArgs = new object[args.Length + 1];
                newArgs[0] = bm.Self;
                Array.Copy(args, 0, newArgs, 1, args.Length);
                return Call(bm.Function, newArgs, kwargs);
            }

            case PyFunction fn:
                return CallFunction(fn, args, kwargs);

            case PyClass cls:
                return Instantiate(cls, args, kwargs);

            case PyInstance inst:
                if (inst.Class.TryLookup("__call__", out var callMethod))
                {
                    var newArgs = new object[args.Length + 1];
                    newArgs[0] = inst;
                    Array.Copy(args, 0, newArgs, 1, args.Length);
                    return Call(callMethod, newArgs, kwargs);
                }
                throw PyErr.TypeError($"'{inst.Class.Name}' object is not callable");

            case ClrMethod clrMethod:
                return ClrBinder.InvokeMethod(clrMethod, args);

            case ClrType clrType:
                return ClrBinder.Construct(clrType.Type, args);

            case ClrObject clrObj when clrObj.Instance is Delegate del:
                return ClrBinder.InvokeMethod(
                    new ClrMethod(del, del.GetType(), "Invoke"), args);

            default:
                throw PyErr.TypeError($"'{PyOps.TypeName(callee)}' object is not callable");
        }
    }

    public object CallFunction(PyFunction fn, object[] args, Dictionary<string, object>? kwargs = null)
    {
        var env = BindParameters(fn, args, kwargs);
        if (fn.IsAsync)
            return new PyCoroutine(fn, env);
        if (fn.IsGenerator)
            return new PyGenerator(fn, env);
        if (fn.LambdaBody is not null)
            return Eval(fn.LambdaBody, env);
        return ExecFunctionBody(fn, env);
    }

    /// <summary>Runs the body of a def function (also used by the generator/coroutine threads).</summary>
    public object ExecFunctionBody(PyFunction fn, Env env)
    {
        var frame = new Frame(fn, env, fn.Name, fn.Module.FileName, fn.Body is { Count: > 0 } ? fn.Body[0].Line : 0);
        (_frames ??= new Stack<Frame>()).Push(frame);
        if (Trace is not null)
            Trace(new TraceEvent(TraceEventKind.Call, frame.Name, frame.File, frame.Line, env, null));
        try
        {
            ExecStmts(fn.Body!, env);
            return PyNone.Instance;
        }
        catch (ReturnSignal r)
        {
            return r.Value;
        }
        catch (PyRaise ex)
        {
            RecordFrame(ex, frame);
            throw;
        }
        finally
        {
            if (Trace is not null)
                Trace(new TraceEvent(TraceEventKind.Return, frame.Name, frame.File, frame.Line, env, null));
            _frames.Pop();
        }
    }

    private Env BindParameters(PyFunction fn, object[] args, Dictionary<string, object>? kwargs)
    {
        var env = new Env(fn.Module, fn.Closure);
        var p = fn.Params;
        var remainingKwargs = kwargs is null ? null : new Dictionary<string, object>(kwargs);

        int i = 0;
        foreach (var param in p.Positional)
        {
            if (i < args.Length)
            {
                if (remainingKwargs is not null && remainingKwargs.ContainsKey(param.Name))
                    throw PyErr.TypeError($"{fn.Name}() got multiple values for argument '{param.Name}'");
                env.Set(param.Name, args[i++]);
            }
            else if (remainingKwargs is not null && remainingKwargs.Remove(param.Name, out var kv))
            {
                env.Set(param.Name, kv);
            }
            else if (fn.Defaults.TryGetValue(param.Name, out var def))
            {
                env.Set(param.Name, def);
            }
            else
            {
                throw PyErr.TypeError($"{fn.Name}() missing required argument: '{param.Name}'");
            }
        }

        if (p.StarArgs is not null)
        {
            if (p.StarArgs.Length > 0)
                env.Set(p.StarArgs, new PyTuple(args.Skip(i).ToArray()));
            else if (i < args.Length)
                throw PyErr.TypeError($"{fn.Name}() takes {p.Positional.Count} positional arguments but {args.Length} were given");
            i = args.Length;
        }
        else if (i < args.Length)
        {
            throw PyErr.TypeError($"{fn.Name}() takes {p.Positional.Count} positional arguments but {args.Length} were given");
        }

        foreach (var param in p.KwOnly)
        {
            if (remainingKwargs is not null && remainingKwargs.Remove(param.Name, out var kv))
                env.Set(param.Name, kv);
            else if (fn.Defaults.TryGetValue(param.Name, out var def))
                env.Set(param.Name, def);
            else
                throw PyErr.TypeError($"{fn.Name}() missing required keyword-only argument: '{param.Name}'");
        }

        if (p.KwArgs is not null)
        {
            var extra = new PyDict();
            if (remainingKwargs is not null)
            {
                foreach (var kv in remainingKwargs)
                    extra[kv.Key] = kv.Value;
            }
            env.Set(p.KwArgs, extra);
        }
        else if (remainingKwargs is { Count: > 0 })
        {
            throw PyErr.TypeError(
                $"{fn.Name}() got an unexpected keyword argument '{remainingKwargs.Keys.First()}'");
        }

        return env;
    }

    private object Instantiate(PyClass cls, object[] args, Dictionary<string, object>? kwargs)
    {
        // Call to an enum class: member lookup by value
        if (cls.Dict.TryGet("__members__", out var membersObj) && membersObj is PyDict members)
        {
            if (args.Length != 1)
                throw PyErr.TypeError($"{cls.Name}() takes exactly one value argument");
            var lookup = args[0] is PyInstance mi && mi.Dict.TryGet("value", out var mv) ? mv : args[0];
            foreach (var e in members.Entries)
            {
                if (e.Value is PyInstance member && RichEquals(member.Dict["value"], lookup))
                    return member;
            }
            throw PyErr.ValueError($"{PyOps.Repr(this, lookup)} is not a valid {cls.Name}");
        }

        // Real CPython's type.__call__ protocol: call the class's own __new__ if it defines one
        // (only reachable here via a REAL user/parsed-Python `def __new__(cls, ...):` in the MRO —
        // cls.TryLookup is a raw dict scan, so it never picks up the synthetic ObjectNewFallback
        // GetAttr exposes for classes that DON'T define one, keeping this a no-op for the vast
        // majority of classes exactly as before). __init__ is only called if the __new__ result is
        // actually an instance of cls — real Python skips it entirely otherwise (the common pattern
        // this unblocks: `def __new__(cls, ...): return really_different_object`, e.g.
        // typing_extensions' real backported TypeVar, which returns a real typing.TypeVar instance
        // rather than an instance of its own wrapper class). Found via typing_extensions' real
        // `class TypeVar(metaclass=_TypeVarLikeMeta): def __new__(cls, name, ...): ...`.
        object built;
        bool hasCustomNew = cls.TryLookup("__new__", out var newMethod) && newMethod is PyFunction or PyBuiltinFunction;
        if (hasCustomNew)
        {
            var newArgs = new object[args.Length + 1];
            newArgs[0] = cls;
            Array.Copy(args, 0, newArgs, 1, args.Length);
            built = Call(newMethod!, newArgs, kwargs);
        }
        else
        {
            built = new PyInstance(cls);
        }

        if (built is not PyInstance instance || !instance.Class.IsSubclassOf(cls))
            return built;

        if (cls.TryLookup("__init__", out var init))
        {
            var newArgs = new object[args.Length + 1];
            newArgs[0] = instance;
            Array.Copy(args, 0, newArgs, 1, args.Length);
            var result = Call(init, newArgs, kwargs);
            if (result is not PyNone)
                throw PyErr.TypeError("__init__() should return None");
        }
        else if (cls.IsSubclassOf(PyErr.BaseException))
        {
            instance.Dict["args"] = new PyTuple(args);
        }
        else if (!hasCustomNew && (args.Length > 0 || kwargs is { Count: > 0 }))
        {
            throw PyErr.TypeError($"{cls.Name}() takes no arguments");
        }
        return instance;
    }

    public PyFunction MakeFunction(string name, Parameters parameters, List<Stmt>? body,
        Expr? lambdaBody, bool isGenerator, Env env, Expr? returns = null, bool isAsync = false)
    {
        var defaults = new Dictionary<string, object>();
        foreach (var param in parameters.Positional.Concat(parameters.KwOnly))
        {
            if (param.Default is not null)
                defaults[param.Name] = Eval(param.Default, env);
        }
        // The class scope is not part of the closure chain (Python semantics).
        return new PyFunction(name, parameters, body, lambdaBody, env.EffectiveClosure, env.Module,
            isGenerator, defaults) { Returns = returns, IsAsync = isAsync };
    }

    // ================================================================ metodi helper

    public object CallMethod(object obj, string name, object[] args, Dictionary<string, object>? kwargs = null)
        => Call(GetAttr(obj, name), args, kwargs);

    public bool TryCallMethod(object obj, string name, object[] args, out object result)
    {
        if (obj is PyInstance inst)
        {
            // dunders are looked up on the class, not the instance
            if (inst.Class.TryLookup(name, out var method))
            {
                result = Call(new PyBoundMethod(inst, Unwrap(method, inst)), args);
                return true;
            }
            result = PyNone.Instance;
            return false;
        }
        if (TryGetAttr(obj, name, out var attr))
        {
            result = Call(attr, args);
            return true;
        }
        result = PyNone.Instance;
        return false;
    }

    private static object Unwrap(object classAttr, PyInstance _)
        => classAttr switch
        {
            PyStaticMethod s => s.Function,
            PyClassMethod c => c.Function,
            _ => classAttr,
        };

    // ================================================================ operatori

    public bool RichEquals(object a, object b)
    {
        if (a is PyInstance ia && TryCallMethod(ia, "__eq__", new[] { b }, out var r1) && r1 is not PyNotImplemented)
            return PyOps.Truthy(this, r1);
        if (b is PyInstance ib && TryCallMethod(ib, "__eq__", new[] { a }, out var r2) && r2 is not PyNotImplemented)
            return PyOps.Truthy(this, r2);
        return PyOps.PyEquals(a, b);
    }

    private bool CompareOnce(string op, object left, object right)
    {
        switch (op)
        {
            case "is":
                return IsIdentical(left, right);
            case "is not":
                return !IsIdentical(left, right);
            case "in":
                return PyOps.Contains(this, right, left);
            case "not in":
                return !PyOps.Contains(this, right, left);
            case "==":
                return RichEquals(left, right);
            case "!=":
                return !RichEquals(left, right);
            default:
                return OrderCompare(op, left, right);
        }
    }

    private static bool IsIdentical(object a, object b)
        => ReferenceEquals(a, b)
           || (a is bool ba && b is bool bb && ba == bb)
           || (a is BigInteger ia && b is BigInteger ib && ia == ib) // small int caching emulato
           || (a is string sa && b is string sb && ReferenceEquals(sa, sb));

    private bool OrderCompare(string op, object left, object right)
    {
        // set/frozenset: <,<=,>,>= are subset/superset (not a total ordering)
        if (SetItems(left) is { } ls && SetItems(right) is { } rs)
        {
            return op switch
            {
                "<=" => ls.IsSubsetOf(rs),
                "<" => ls.IsProperSubsetOf(rs),
                ">=" => ls.IsSupersetOf(rs),
                ">" => ls.IsProperSupersetOf(rs),
                _ => throw PyErr.RuntimeError($"unknown comparison {op}"),
            };
        }

        int? cmp = TryOrder(left, right);
        if (cmp is int c)
        {
            return op switch
            {
                "<" => c < 0,
                "<=" => c <= 0,
                ">" => c > 0,
                ">=" => c >= 0,
                _ => throw PyErr.RuntimeError($"unknown comparison {op}"),
            };
        }

        if (left is PyInstance li && TryCallMethod(li, CmpDunders[op], new[] { right }, out var r)
            && r is not PyNotImplemented)
            return PyOps.Truthy(this, r);
        // riflesso
        string reflected = op switch
        {
            "<" => ">",
            ">" => "<",
            "<=" => ">=",
            ">=" => "<=",
            _ => op,
        };
        if (right is PyInstance ri && TryCallMethod(ri, CmpDunders[reflected], new[] { left }, out var rr)
            && rr is not PyNotImplemented)
            return PyOps.Truthy(this, rr);

        throw PyErr.TypeError(
            $"'{op}' not supported between instances of '{PyOps.TypeName(left)}' and '{PyOps.TypeName(right)}'");
    }

    private static HashSet<object>? SetItems(object o) => o switch
    {
        PySet s => s.Items,
        PyFrozenSet f => f.Items,
        // dict.keys() is already-unique by construction, so this is exact, not an approximation —
        // matches real CPython's dict_keys supporting the set operators directly.
        PyDictKeysView v => new HashSet<object>(v.Source.Keys, PyEqualityComparer.Instance),
        // A plain dict is set-like over its own keys for &/-/^ and for | when the other side isn't
        // also a dict (dict|dict merges instead — handled explicitly, and first, in the "|" case).
        // Real CPython: `some_dict | some_dict.keys()` dispatches to dict_keys.__ror__, which treats
        // the dict as its keys. Found via pydantic's real `fields | private_attributes.keys() |
        // {'__slots__'}` (ModelMetaclass.__new__).
        PyDict d => new HashSet<object>(d.Keys, PyEqualityComparer.Instance),
        _ => null,
    };

    /// <summary>Orders two builtin values: -1/0/1, null if not orderable here.</summary>
    private int? TryOrder(object a, object b)
    {
        if (PyOps.IsNumber(a) && PyOps.IsNumber(b))
        {
            if (a is double || b is double)
                return PyOps.AsDouble(a).CompareTo(PyOps.AsDouble(b));
            return PyOps.AsBigInt(a, "cmp").CompareTo(PyOps.AsBigInt(b, "cmp"));
        }
        if (a is string sa && b is string sb)
            return string.CompareOrdinal(sa, sb);
        if (a is PyBytes ba && b is PyBytes bb)
            return ba.Data.AsSpan().SequenceCompareTo(bb.Data);
        if (a is PyList la && b is PyList lb)
            return SequenceOrder(la.Items, lb.Items);
        if (a is PyTuple ta && b is PyTuple tb)
            return SequenceOrder(ta.Items, tb.Items);
        return null;
    }

    private int SequenceOrder(IReadOnlyList<object> a, IReadOnlyList<object> b)
    {
        int n = Math.Min(a.Count, b.Count);
        for (int i = 0; i < n; i++)
        {
            if (RichEquals(a[i], b[i]))
                continue;
            var cmp = TryOrder(a[i], b[i])
                      ?? throw PyErr.TypeError("elements are not orderable");
            return cmp;
        }
        return a.Count.CompareTo(b.Count);
    }

    public int Compare(object a, object b)
    {
        if (RichEquals(a, b))
            return 0;
        var cmp = TryOrder(a, b);
        if (cmp is int c)
            return c;
        return OrderCompare("<", a, b) ? -1 : 1;
    }

    public object UnaryOp(string op, object operand)
    {
        switch (op)
        {
            case "not":
                return !PyOps.Truthy(this, operand);
            case "-":
                return operand switch
                {
                    BigInteger i => -i,
                    double d => -d,
                    bool b => new BigInteger(b ? -1 : 0),
                    PyInstance inst when TryCallMethod(inst, "__neg__", Array.Empty<object>(), out var r) => r,
                    _ => throw PyErr.TypeError($"bad operand type for unary -: '{PyOps.TypeName(operand)}'"),
                };
            case "+":
                return operand switch
                {
                    BigInteger or double => operand,
                    bool b => new BigInteger(b ? 1 : 0),
                    PyInstance inst when TryCallMethod(inst, "__pos__", Array.Empty<object>(), out var r) => r,
                    _ => throw PyErr.TypeError($"bad operand type for unary +: '{PyOps.TypeName(operand)}'"),
                };
            case "~":
                return operand switch
                {
                    BigInteger i => -(i + 1),
                    bool b => new BigInteger(b ? -2 : -1),
                    PyInstance inst when TryCallMethod(inst, "__invert__", Array.Empty<object>(), out var r) => r,
                    _ => throw PyErr.TypeError($"bad operand type for unary ~: '{PyOps.TypeName(operand)}'"),
                };
            default:
                throw PyErr.RuntimeError($"unknown unary operator {op}");
        }
    }

    public object BinaryOp(string op, object a, object b)
    {
        // numeri
        if (PyOps.IsNumber(a) && PyOps.IsNumber(b))
            return NumericOp(op, a, b);

        switch (op)
        {
            case "+":
                if (a is string s1 && b is string s2)
                    return s1 + s2;
                if (a is PyList l1 && b is PyList l2)
                    return new PyList(l1.Items.Concat(l2.Items));
                if (a is PyTuple t1 && b is PyTuple t2)
                    return new PyTuple(t1.Items.Concat(t2.Items).ToArray());
                if (a is PyBytes b1 && b is PyBytes b2)
                    return new PyBytes(b1.Data.Concat(b2.Data).ToArray());
                if (a is PyByteArray ba1)
                    return new PyByteArray(ba1.Data.Concat(BytesOf(b)));
                break;

            case "*":
            {
                if (a is string sa && b is BigInteger nb)
                    return Repeat(sa, nb);
                if (a is BigInteger na && b is string sb)
                    return Repeat(sb, na);
                if (a is PyList la && b is BigInteger nlb)
                    return new PyList(RepeatItems(la.Items, nlb));
                if (a is BigInteger nla && b is PyList lb)
                    return new PyList(RepeatItems(lb.Items, nla));
                if (a is PyTuple ta && b is BigInteger ntb)
                    return new PyTuple(RepeatItems(ta.Items, ntb).ToArray());
                if (a is BigInteger nta && b is PyTuple tb2)
                    return new PyTuple(RepeatItems(tb2.Items, nta).ToArray());
                if (a is PyBytes bta && b is BigInteger nbb)
                    return new PyBytes(RepeatBytes(bta.Data, nbb));
                if (a is BigInteger nba && b is PyBytes btb)
                    return new PyBytes(RepeatBytes(btb.Data, nba));
                break;
            }

            case "%":
                if (a is string fmt)
                    return StrModules.PercentFormat(this, fmt, b);
                break;

            case "|":
                // dict | dict merges (checked first: both operands satisfy SetItems below too,
                // via dict-as-its-own-keys, but merge is dict's own real __or__, taking priority).
                if (a is PyDict d1 && b is PyDict d2)
                {
                    var merged = d1.Copy();
                    merged.Update(d2);
                    return merged;
                }
                if (SetItems(a) is { } su1 && SetItems(b) is { } su2)
                    return MakeSetLike(a, su1.Union(su2));
                // PEP 604: `X | Y` between two type-like objects (real classes, builtin type
                // constructors, None, or an existing union/generic alias for chaining `X | Y | Z`)
                // builds a real union — matching real CPython's `types.UnionType`, not a crash. Real
                // CPython gates this on `type(X)` supporting `__or__`, which for our simplified model
                // means "is this a type" rather than mirroring the full internal type-check; nothing
                // in scope needs the distinction. Found via anyio's real `str | bytes | PathLike[str]
                // | PathLike[bytes]` module-level type alias (abc/_eventloop.py), itself evaluated
                // eagerly (PySharp doesn't defer annotations under `from __future__ import
                // annotations` the way real CPython does — see FASTAPI_PLAN.md).
                if (IsTypeLike(a) && IsTypeLike(b))
                    return Modules.GenericAliasModule.MakeAlias(Modules.MiscModules.UnionTypeClass, new[] { a, b });
                break;
            case "&":
                if (SetItems(a) is { } si1 && SetItems(b) is { } si2)
                    return MakeSetLike(a, si1.Intersect(si2));
                break;
            case "-":
                if (SetItems(a) is { } sd1 && SetItems(b) is { } sd2)
                    return MakeSetLike(a, sd1.Except(sd2));
                break;
            case "^":
                if (SetItems(a) is { } sx1 && SetItems(b) is { } sx2)
                {
                    var symDiff = new HashSet<object>(sx1, PyEqualityComparer.Instance);
                    symDiff.SymmetricExceptWith(sx2);
                    return MakeSetLike(a, symDiff);
                }
                break;
        }

        // dunder su istanze
        if (BinDunders.TryGetValue(op, out var dunders))
        {
            if (a is PyInstance ia && TryCallMethod(ia, dunders.Dunder, new[] { b }, out var r1)
                && r1 is not PyNotImplemented)
                return r1;
            if (b is PyInstance ib && TryCallMethod(ib, dunders.Reflected, new[] { a }, out var r2)
                && r2 is not PyNotImplemented)
                return r2;
        }

        throw PyErr.TypeError(
            $"unsupported operand type(s) for {op}: '{PyOps.TypeName(a)}' and '{PyOps.TypeName(b)}'");
    }

    /// <summary>Result type follows the left operand's type, matching CPython (frozenset | set ->
    /// frozenset, set | frozenset -> set).</summary>
    private static object MakeSetLike(object template, IEnumerable<object> items)
        => template is PyFrozenSet ? new PyFrozenSet(items) : new PySet(items);

    /// <summary>Is `o` something PEP 604's `|` operator accepts: a real class, a builtin type
    /// constructor (int/str/list/...), None (special-cased to NoneType in a union, same as real
    /// CPython), or an existing generic-alias/union instance (so `X | Y | Z` chains left-to-right).</summary>
    private static bool IsTypeLike(object o) => o switch
    {
        PyClass => true,
        PyNone => true,
        PyBuiltinFunction bf => Builtins.BuiltinsFactory.BuiltinTypeNames.Contains(bf.Name),
        PyInstance inst => inst.Class == Modules.GenericAliasModule.GenericAliasClass,
        _ => false,
    };

    private static IEnumerable<byte> BytesOf(object o) => o switch
    {
        PyBytes b => b.Data,
        PyByteArray b => b.Data,
        _ => throw PyErr.TypeError($"can't concat {PyOps.TypeName(o)} to bytearray"),
    };

    private static string Repeat(string s, BigInteger n)
    {
        if (n <= 0)
            return "";
        var sb = new StringBuilder(s.Length * (int)n);
        for (int i = 0; i < (int)n; i++)
            sb.Append(s);
        return sb.ToString();
    }

    private static IEnumerable<object> RepeatItems(IEnumerable<object> items, BigInteger n)
    {
        var list = items.ToList();
        for (int i = 0; i < (int)n; i++)
            foreach (var x in list)
                yield return x;
    }

    private static byte[] RepeatBytes(byte[] data, BigInteger n)
    {
        if (n <= 0)
            return Array.Empty<byte>();
        var result = new byte[data.Length * (int)n];
        for (int i = 0; i < (int)n; i++)
            Array.Copy(data, 0, result, i * data.Length, data.Length);
        return result;
    }

    private object NumericOp(string op, object a, object b)
    {
        bool isFloat = a is double || b is double;
        switch (op)
        {
            case "/":
            {
                double da = PyOps.AsDouble(a);
                double db = PyOps.AsDouble(b);
                if (db == 0)
                    throw PyErr.ZeroDivisionError("division by zero");
                // int too large converted to float → overflow (like CPython)
                if (a is BigInteger && double.IsInfinity(da))
                    throw PyErr.OverflowError("integer division result too large for a float");
                double result = da / db;
                if (double.IsInfinity(result) && !double.IsInfinity(da) && !double.IsInfinity(db))
                    throw PyErr.OverflowError("integer division result too large for a float");
                return result;
            }
            case "+" or "-" or "*" when isFloat:
            {
                double x = PyOps.AsDouble(a), y = PyOps.AsDouble(b);
                return op switch { "+" => x + y, "-" => x - y, _ => x * y };
            }
            case "+" or "-" or "*":
            {
                var x = PyOps.AsBigInt(a, op);
                var y = PyOps.AsBigInt(b, op);
                return op switch { "+" => x + y, "-" => x - y, _ => x * y };
            }
            case "//" when isFloat:
            {
                double y = PyOps.AsDouble(b);
                if (y == 0)
                    throw PyErr.ZeroDivisionError("float floor division by zero");
                return Math.Floor(PyOps.AsDouble(a) / y);
            }
            case "//":
            {
                var x = PyOps.AsBigInt(a, op);
                var y = PyOps.AsBigInt(b, op);
                if (y.IsZero)
                    throw PyErr.ZeroDivisionError("integer division or modulo by zero");
                return FloorDiv(x, y);
            }
            case "%" when isFloat:
            {
                double x = PyOps.AsDouble(a), y = PyOps.AsDouble(b);
                if (y == 0)
                    throw PyErr.ZeroDivisionError("float modulo");
                double r = x - Math.Floor(x / y) * y;
                return r;
            }
            case "%":
            {
                var x = PyOps.AsBigInt(a, op);
                var y = PyOps.AsBigInt(b, op);
                if (y.IsZero)
                    throw PyErr.ZeroDivisionError("integer division or modulo by zero");
                return x - FloorDiv(x, y) * y;
            }
            case "**":
            {
                if (!isFloat)
                {
                    var x = PyOps.AsBigInt(a, op);
                    var y = PyOps.AsBigInt(b, op);
                    if (y >= 0)
                    {
                        // shortcut for bases -1/0/1: they handle astronomical exponents (10**1000)
                        if (x.IsZero) return y.IsZero ? BigInteger.One : BigInteger.Zero;
                        if (x.IsOne) return BigInteger.One;
                        if (x == BigInteger.MinusOne) return y.IsEven ? BigInteger.One : BigInteger.MinusOne;
                        if (y > int.MaxValue)
                            throw PyErr.OverflowError("exponent too large to compute");
                        return BigInteger.Pow(x, (int)y);
                    }
                    // esponente negativo: risultato float (es. 2 ** -1)
                }
                double baseD = PyOps.AsDouble(a), expD = PyOps.AsDouble(b);
                double r = Math.Pow(baseD, expD);
                if (double.IsNaN(r) && baseD < 0)
                    throw PyErr.ValueError("math domain error");
                if (double.IsInfinity(r) && double.IsFinite(baseD) && double.IsFinite(expD))
                    throw PyErr.OverflowError("(34, 'Result too large')");
                return r;
            }
            case "<<" or ">>" or "&" or "|" or "^":
            {
                if (isFloat)
                    throw PyErr.TypeError($"unsupported operand type(s) for {op}: 'float'");
                var x = PyOps.AsBigInt(a, op);
                var y = PyOps.AsBigInt(b, op);
                return op switch
                {
                    "<<" => x << (int)y,
                    ">>" => x >> (int)y,
                    "&" => x & y,
                    "|" => x | y,
                    _ => x ^ y,
                };
            }
            default:
                throw PyErr.TypeError($"unsupported operand type(s) for {op}");
        }
    }

    private static BigInteger FloorDiv(BigInteger x, BigInteger y)
    {
        var q = BigInteger.DivRem(x, y, out var r);
        if (!r.IsZero && (r.Sign != y.Sign))
            q -= 1;
        return q;
    }

    // ================================================================ item access

    public object GetItem(object obj, object index)
    {
        switch (obj)
        {
            case PyDict d:
                return d[index];
            case PyList l when index is PySlice slice:
            {
                var (start, _, step, count) = slice.Indices(l.Items.Count);
                var result = new PyList();
                for (int k = 0, idx = start; k < count; k++, idx += step)
                    result.Items.Add(l.Items[idx]);
                return result;
            }
            case PyList l:
                return l.Items[PyOps.SeqIndex(index, l.Items.Count, "list")];
            case PyTuple t when index is PySlice slice:
            {
                var (start, _, step, count) = slice.Indices(t.Items.Length);
                var items = new object[count];
                for (int k = 0, idx = start; k < count; k++, idx += step)
                    items[k] = t.Items[idx];
                return new PyTuple(items);
            }
            case PyTuple t:
                return t.Items[PyOps.SeqIndex(index, t.Items.Length, "tuple")];
            case string s when index is PySlice slice:
            {
                var (start, _, step, count) = slice.Indices(s.Length);
                var sb = new StringBuilder(count);
                for (int k = 0, idx = start; k < count; k++, idx += step)
                    sb.Append(s[idx]);
                return sb.ToString();
            }
            case string s:
                return s[PyOps.SeqIndex(index, s.Length, "string")].ToString();
            case PyBytes b when index is PySlice slice:
            {
                var (start, _, step, count) = slice.Indices(b.Length);
                var data = new byte[count];
                for (int k = 0, idx = start; k < count; k++, idx += step)
                    data[k] = b.Data[idx];
                return new PyBytes(data);
            }
            case PyBytes b:
                return new BigInteger(b.Data[PyOps.SeqIndex(index, b.Length, "bytes")]);
            case PyByteArray ba when index is PySlice slice:
            {
                var (start, _, step, count) = slice.Indices(ba.Data.Count);
                var data = new List<byte>(count);
                for (int k = 0, idx = start; k < count; k++, idx += step)
                    data.Add(ba.Data[idx]);
                return new PyByteArray(data);
            }
            case PyByteArray ba:
                return new BigInteger(ba.Data[PyOps.SeqIndex(index, ba.Data.Count, "bytearray")]);
            case PyRange r when index is PySlice slice:
            {
                var items = r.Enumerate().ToList();
                var (start, _, step, cnt) = slice.Indices(items.Count);
                if (cnt == 0)
                    return new PyRange(0, 0, 1);
                var first = (BigInteger)items[start];
                var newStep = r.Step * step;
                var last = (BigInteger)items[start + (cnt - 1) * step];
                return new PyRange(first, last + (newStep.Sign > 0 ? 1 : -1), newStep);
            }
            case PyRange r when index is BigInteger or bool:
            {
                var i = PyOps.AsBigInt(index, "range index");
                var count = r.Count;
                var idx = i < 0 ? i + count : i;
                if (idx < 0 || idx >= count)
                    throw PyErr.IndexError("range object index out of range");
                return r.Start + idx * r.Step;
            }
            case PyInstance inst:
            {
                if (TryCallMethod(inst, "__getitem__", new[] { index }, out var r))
                    return r;
                if (inst.Class == Modules.GenericAliasModule.GenericAliasClass)
                    return Modules.GenericAliasModule.Resubscript(inst, index);
                throw PyErr.TypeError($"'{inst.Class.Name}' object is not subscriptable");
            }
            case PyClass pc:
                // List[int], Dict[str, int], SomeGeneric[T], ecc.: builds a real generic alias
                // (__origin__/__args__) instead of a no-op, so typing.get_origin/get_args work.
                return Modules.GenericAliasModule.Subscript(pc, index);
            // PEP 585 (Python 3.9+): subscripting a builtin type directly — `list[int]`,
            // `tuple[int, str]`, `dict[str, int]`, ... — not just `typing.List[int]`. Real CPython
            // returns a `types.GenericAlias`; here it's the same real GenericAliasModule alias
            // `List[int]` etc. already build, with the builtin function itself as `__origin__`
            // (matching real `get_origin(tuple[int, str]) is tuple`). Found via real modern
            // (`from __future__ import annotations`-era) type hints in typing_extensions/anyio using
            // this syntax directly instead of the `typing.Tuple`-style spelling.
            case PyBuiltinFunction bf when Builtins.BuiltinsFactory.BuiltinTypeNames.Contains(bf.Name):
                return Modules.GenericAliasModule.MakeAlias(bf, index is PyTuple bt ? bt.Items : new[] { index });
            case ClrObject clr:
                if (ClrBinder.TryGetIndex(clr, index, out var indexed))
                    return indexed;
                throw PyErr.TypeError($"'{clr.Type.Name}' object is not subscriptable");
            default:
                throw PyErr.TypeError($"'{PyOps.TypeName(obj)}' object is not subscriptable");
        }
    }

    public void SetItem(object obj, object index, object value)
    {
        switch (obj)
        {
            case PyDict d:
                d[index] = value;
                break;
            case PyList l when index is PySlice slice:
            {
                var (start, _, step, count) = slice.Indices(l.Items.Count);
                if (step != 1)
                    throw PyErr.ValueError("extended slice assignment not supported");
                var newItems = PyOps.Iterate(this, value).ToList();
                l.Items.RemoveRange(start, count);
                l.Items.InsertRange(start, newItems);
                break;
            }
            case PyList l:
                l.Items[PyOps.SeqIndex(index, l.Items.Count, "list")] = value;
                break;
            case PyByteArray ba:
                ba.Data[PyOps.SeqIndex(index, ba.Data.Count, "bytearray")] =
                    (byte)PyOps.AsBigInt(value, "bytearray item");
                break;
            case PyInstance inst:
                CallMethod(inst, "__setitem__", new[] { index, value });
                break;
            case ClrObject clr:
                if (!ClrBinder.TrySetIndex(clr, index, value))
                    throw PyErr.TypeError($"'{clr.Type.Name}' object does not support item assignment");
                break;
            default:
                throw PyErr.TypeError($"'{PyOps.TypeName(obj)}' object does not support item assignment");
        }
    }

    public void DelItem(object obj, object index)
    {
        switch (obj)
        {
            case PyDict d:
                if (!d.Remove(index))
                    throw PyErr.KeyError(index);
                break;
            case PyList l when index is PySlice slice:
            {
                var (start, _, step, count) = slice.Indices(l.Items.Count);
                if (step != 1)
                    throw PyErr.ValueError("extended slice deletion not supported");
                l.Items.RemoveRange(start, count);
                break;
            }
            case PyList l:
                l.Items.RemoveAt(PyOps.SeqIndex(index, l.Items.Count, "list"));
                break;
            case PyInstance inst:
                CallMethod(inst, "__delitem__", new[] { index });
                break;
            default:
                throw PyErr.TypeError($"'{PyOps.TypeName(obj)}' object doesn't support item deletion");
        }
    }

    // ================================================================ attributi

    public object GetAttr(object obj, string name)
    {
        if (TryGetAttr(obj, name, out var value))
            return value;
        throw PyErr.AttributeError($"'{PyOps.TypeName(obj)}' object has no attribute '{name}'");
    }

    public bool TryGetAttr(object obj, string name, out object value)
    {
        switch (obj)
        {
            case PyInstance inst:
            {
                if (inst.Dict.TryGet(name, out value!))
                    return true;
                if (inst.Class.TryLookup(name, out var classAttr))
                {
                    value = BindClassAttr(classAttr, inst);
                    return true;
                }
                if (name == "__class__")
                {
                    value = inst.Class;
                    return true;
                }
                if (name == "__dict__")
                {
                    value = inst.Dict;
                    return true;
                }
                if (inst.Class.TryLookup("__getattr__", out var getattr))
                {
                    value = Call(new PyBoundMethod(inst, getattr), new object[] { name });
                    return true;
                }
                // standard exception attributes, None by default
                if (name is "__cause__" or "__context__" or "__traceback__"
                    && inst.Class.IsSubclassOf(PyErr.BaseException))
                {
                    value = PyNone.Instance;
                    return true;
                }
                value = PyNone.Instance;
                return false;
            }

            case PyClass cls:
            {
                if (cls.TryLookup(name, out var attr))
                {
                    value = attr switch
                    {
                        PyStaticMethod s => s.Function,
                        PyClassMethod c => new PyBoundMethod(cls, c.Function),
                        _ => attr,
                    };
                    return true;
                }
                switch (name)
                {
                    case "__name__" or "__qualname__":
                        value = cls.Name;
                        return true;
                    case "__module__":
                        value = "builtins";
                        return true;
                    // Real CPython: `SomeClass.__class__` is its metaclass (`type` by default).
                    // Found via pydantic's real `ModelField.prepare()` (`self.outer_type_.__class__`
                    // idiom checking a field's declared type for GenericAlias-ness).
                    case "__class__":
                        value = (object?)cls.Metaclass ?? Builtins.BuiltinsFactory.TypeNamePseudoClass(this, cls);
                        return true;
                    case "__mro__":
                        value = new PyTuple(cls.Mro.Cast<object>().ToArray());
                        return true;
                    case "__bases__":
                        value = new PyTuple(cls.Bases.Cast<object>().ToArray());
                        return true;
                    case "__dict__":
                        value = cls.Dict;
                        return true;
                    case "__doc__":
                        // No docstring-capture at class-definition time yet (nothing has needed the
                        // real text so far) — None matches CPython for an undocumented class and is
                        // what real code checking `SomeClass.__doc__ or default` expects to see.
                        value = PyNone.Instance;
                        return true;
                    // object's default __new__/__init__, accessed directly on a class that doesn't
                    // override them (not via super() — that's the `case PySuper` branch below).
                    // Real pattern this unblocks: a stub base class like our bare ABCMeta being used
                    // as `SomeRealMetaclass.__new__ = ...; return abc.ABCMeta.__new__(mcls, name,
                    // bases, namespace, **kwargs)` the way typing_extensions' real `_ProtocolMeta`
                    // does — it calls `abc.ABCMeta.__new__` directly rather than via super() (its own
                    // comment explains why: avoiding slow real-CPython ABCMeta machinery on old
                    // versions), so it needs the same real class-building fallback super() gets.
                    case "__new__":
                        value = ObjectNewFallback;
                        return true;
                    case "__init__":
                        value = ObjectInitFallback;
                        return true;
                    // Real pattern this unblocks: pydantic's real `object_setattr = object.__setattr__`
                    // module-level alias (BaseModel.__init__ uses it to bulk-set `__dict__`).
                    case "__setattr__":
                        value = ObjectSetattrFallback;
                        return true;
                }
                value = PyNone.Instance;
                return false;
            }

            case PyModule module:
                if (module.Dict.TryGet(name, out value!))
                    return true;
                // Real CPython: a module's own namespace, e.g. pydantic's real
                // `sys.modules[model.__module__].__dict__` idiom (update_model_forward_refs)
                // resolving forward-ref annotations against the defining module's globals.
                if (name == "__dict__")
                {
                    value = module.Dict;
                    return true;
                }
                value = PyNone.Instance;
                return false;

            case PyBuiltinFunction bfn:
                switch (name)
                {
                    case "__name__" or "__qualname__":
                        value = bfn.Name;
                        return true;
                    case "__module__":
                        value = "builtins";
                        return true;
                    // Real CPython: any callable's `.__call__` is itself callable (a bound
                    // method-wrapper around the same underlying call). Found via starlette's real
                    // `is_async_callable`'s fallback branch `iscoroutinefunction(obj.__call__)`
                    // (_utils.py), reached for a bound method (e.g. the default 404 handler).
                    case "__call__":
                        value = obj;
                        return true;
                }
                // other attributes (e.g. builtin type methods like str.upper): normal path
                return TypeMethods.TryGetBuiltinAttr(this, obj, name, out value);

            case PyFunction fn:
                switch (name)
                {
                    case "__name__" or "__qualname__":
                        value = fn.Attributes.TryGet(name, out var n) ? n : fn.Name;
                        return true;
                    case "__doc__":
                        value = fn.Attributes.TryGet("__doc__", out var doc) ? doc : (object)PyNone.Instance;
                        return true;
                    case "__module__":
                        value = fn.Module.Name;
                        return true;
                    case "__defaults__":
                    {
                        var posDefaults = fn.Params.Positional
                            .Where(p => fn.Defaults.ContainsKey(p.Name))
                            .Select(p => fn.Defaults[p.Name])
                            .ToArray();
                        value = posDefaults.Length == 0 ? PyNone.Instance : new PyTuple(posDefaults);
                        return true;
                    }
                    case "__kwdefaults__":
                    {
                        var kw = new PyDict();
                        foreach (var p in fn.Params.KwOnly)
                            if (fn.Defaults.TryGetValue(p.Name, out var dv))
                                kw[p.Name] = dv;
                        value = kw.Count == 0 ? PyNone.Instance : kw;
                        return true;
                    }
                    case "__annotations__":
                    {
                        // If the user assigned __annotations__ by hand, that wins.
                        if (fn.Attributes.TryGet("__annotations__", out var ann))
                        {
                            value = ann;
                            return true;
                        }
                        // Otherwise evaluate the parameter annotations (best-effort, lazy):
                        // forward refs or unresolvable names are simply skipped.
                        var annots = new PyDict();
                        foreach (var p in fn.Params.Positional.Concat(fn.Params.KwOnly))
                        {
                            if (p.Annotation is null)
                                continue;
                            try
                            {
                                annots[p.Name] = Eval(p.Annotation, fn.Closure);
                            }
                            catch (PyRaise)
                            {
                                // annotation not resolvable at runtime: skip it
                            }
                        }
                        if (fn.Returns is not null)
                        {
                            try
                            {
                                annots["return"] = Eval(fn.Returns, fn.Closure);
                            }
                            catch (PyRaise)
                            {
                                // return annotation not resolvable: skip it
                            }
                        }
                        fn.Attributes["__annotations__"] = annots;
                        value = annots;
                        return true;
                    }
                    case "__code__":
                        value = fn.Code;
                        return true;
                    case "__dict__":
                        value = fn.Attributes;
                        return true;
                    case "__call__":
                        value = obj;
                        return true;
                    // Universal fallback (matches the same case for PyBuiltinFunction/other builtin
                    // values, see TypeMethods.TryGetBuiltinAttr): `v.__class__` for a real (non-
                    // builtin) function. Found via pydantic's real `v.__class__.__name__ ==
                    // 'cython_function_or_method'` idiom (ModelMetaclass.__new__'s is_untouched()).
                    case "__class__":
                        value = Builtins.BuiltinsFactory.TypeNamePseudoClass(this, obj);
                        return true;
                    default:
                        if (fn.Attributes.TryGet(name, out value!))
                            return true;
                        value = PyNone.Instance;
                        return false;
                }

            case PyCode code:
                switch (name)
                {
                    case "co_varnames":
                        value = new PyTuple(code.VarNames.Cast<object>().ToArray());
                        return true;
                    case "co_argcount":
                        value = new BigInteger(code.ArgCount);
                        return true;
                    case "co_kwonlyargcount":
                        value = new BigInteger(code.KwOnlyArgCount);
                        return true;
                    case "co_posonlyargcount":
                        value = BigInteger.Zero;
                        return true;
                    case "co_name":
                        value = code.Name;
                        return true;
                }
                value = PyNone.Instance;
                return false;

            case PyBoundMethod bm:
                switch (name)
                {
                    case "__self__":
                        value = bm.Self;
                        return true;
                    case "__func__":
                        value = bm.Function;
                        return true;
                    case "__name__" when bm.Function is PyFunction f:
                        value = f.Name;
                        return true;
                    case "__name__" when bm.Function is PyBuiltinFunction bf:
                        value = bf.Name;
                        return true;
                    case "__call__":
                        value = obj;
                        return true;
                }
                value = PyNone.Instance;
                return false;

            case PySuper sup:
                if (sup.TryLookup(name, out var superAttr))
                {
                    value = superAttr switch
                    {
                        PyFunction f => new PyBoundMethod(sup.Self, f),
                        PyBuiltinFunction bf => new PyBoundMethod(sup.Self, bf),
                        PyStaticMethod s => s.Function,
                        PyClassMethod c => new PyBoundMethod(
                            sup.Self is PyInstance si ? si.Class : sup.Self, c.Function),
                        PyProperty p when p.Getter is not null
                            => Call(new PyBoundMethod(sup.Self, p.Getter), Array.Empty<object>()),
                        _ => superAttr,
                    };
                    return true;
                }
                // No class in the MRO overrides these — fall back to object's default behavior,
                // the same way CPython's super() does when nothing shadows __setattr__/__delattr__.
                // Common pattern this unblocks: `def __setattr__(self, ...): ...; super().__setattr__(...)`.
                if (name == "__setattr__")
                {
                    var target = sup.Self;
                    value = new PyBuiltinFunction("object.__setattr__", (_, a, _) =>
                    {
                        if (target is not PyInstance inst)
                            throw PyErr.AttributeError($"'{PyOps.TypeName(target)}' object has no attribute '{a[0]}'");
                        ObjectSetAttrImpl(inst, (string)a[0], a[1]);
                        return PyNone.Instance;
                    });
                    return true;
                }
                if (name == "__delattr__")
                {
                    var target = sup.Self;
                    value = new PyBuiltinFunction("object.__delattr__", (_, a, _) =>
                    {
                        if (target is not PyInstance inst || !inst.Dict.Remove((string)a[0]))
                            throw PyErr.AttributeError($"'{PyOps.TypeName(target)}' object has no attribute '{a[0]}'");
                        return PyNone.Instance;
                    });
                    return true;
                }
                if (name == "__init__")
                {
                    // object's default: a no-op. Common pattern this unblocks: a class whose base
                    // is (effectively) object still calls `super().__init__(...)` defensively.
                    value = ObjectInitFallback;
                    return true;
                }
                if (name == "__new__")
                {
                    value = ObjectNewFallback;
                    return true;
                }
                value = PyNone.Instance;
                return false;

            case PyProperty prop:
                switch (name)
                {
                    case "setter":
                        value = new PyBuiltinFunction("property.setter", (_, a, _) =>
                            new PyProperty { Getter = prop.Getter, Setter = a[0], Deleter = prop.Deleter });
                        return true;
                    case "getter":
                        value = new PyBuiltinFunction("property.getter", (_, a, _) =>
                            new PyProperty { Getter = a[0], Setter = prop.Setter, Deleter = prop.Deleter });
                        return true;
                    case "deleter":
                        value = new PyBuiltinFunction("property.deleter", (_, a, _) =>
                            new PyProperty { Getter = prop.Getter, Setter = prop.Setter, Deleter = a[0] });
                        return true;
                }
                value = PyNone.Instance;
                return false;

            case ClrObject clr:
                if (ClrBinder.TryGetMember(clr.Instance, clr.Type, name, isStatic: false, out value))
                    return true;
                throw PyErr.AttributeError($"'{clr.Type.Name}' object has no attribute '{name}'");

            case ClrType ct:
                if (ClrBinder.TryGetMember(null, ct.Type, name, isStatic: true, out value))
                    return true;
                throw PyErr.AttributeError($"type '{ct.Type.Name}' has no attribute '{name}'");

            default:
                return TypeMethods.TryGetBuiltinAttr(this, obj, name, out value);
        }
    }

    private object BindClassAttr(object classAttr, PyInstance inst)
        => classAttr switch
        {
            PyFunction fn => new PyBoundMethod(inst, fn),
            PyBuiltinFunction bf => new PyBoundMethod(inst, bf),
            PyStaticMethod s => s.Function,
            PyClassMethod c => new PyBoundMethod(inst.Class, c.Function),
            PyProperty prop => prop.Getter is not null
                ? Call(new PyBoundMethod(inst, prop.Getter), Array.Empty<object>())
                : throw PyErr.AttributeError("unreadable attribute"),
            _ => classAttr,
        };

    public void SetAttr(object obj, string name, object value)
    {
        switch (obj)
        {
            case PyInstance inst:
            {
                if (inst.Class.TryLookup(name, out var classAttr) && classAttr is PyProperty prop)
                {
                    if (prop.Setter is null)
                        throw PyErr.AttributeError($"can't set attribute '{name}'");
                    Call(new PyBoundMethod(inst, prop.Setter), new[] { value });
                    return;
                }
                if (inst.Class.TryLookup("__setattr__", out var setattr)
                    && setattr is PyFunction or PyBuiltinFunction)
                {
                    Call(new PyBoundMethod(inst, setattr), new object[] { name, value });
                    return;
                }
                inst.Dict[name] = value;
                return;
            }
            case PyClass cls:
                cls.Dict[name] = value;
                return;
            case PyModule module:
                module.Dict[name] = value;
                return;
            case PyFunction fn:
                fn.Attributes[name] = value;
                return;
            case ClrObject clr:
                if (!ClrBinder.TrySetMember(clr.Instance, clr.Type, name, value, isStatic: false))
                    throw PyErr.AttributeError($"'{clr.Type.Name}' object has no settable attribute '{name}'");
                return;
            case ClrType ct:
                if (!ClrBinder.TrySetMember(null, ct.Type, name, value, isStatic: true))
                    throw PyErr.AttributeError($"type '{ct.Type.Name}' has no settable attribute '{name}'");
                return;
            default:
                throw PyErr.AttributeError(
                    $"'{PyOps.TypeName(obj)}' object has no settable attribute '{name}'");
        }
    }

    public void DelAttr(object obj, string name)
    {
        switch (obj)
        {
            case PyInstance inst:
                if (!inst.Dict.Remove(name))
                    throw PyErr.AttributeError($"'{inst.Class.Name}' object has no attribute '{name}'");
                return;
            case PyClass cls:
                if (!cls.Dict.Remove(name))
                    throw PyErr.AttributeError(name);
                return;
            case PyModule module:
                if (!module.Dict.Remove(name))
                    throw PyErr.AttributeError(name);
                return;
            default:
                throw PyErr.AttributeError($"'{PyOps.TypeName(obj)}' object has no deletable attributes");
        }
    }
}
