// Copyright (c) 2026 Marco Parenzan
//
// Licensed under the MIT License. See the LICENSE file in the project
// root for full license information.

using System.Numerics;
using PySharpLib.Parsing;
using PySharpLib.Runtime;

namespace PySharpLib.Interpretation;

/// <summary>
/// The suspendable evaluator behind <see cref="PyEngine.RunAsync"/>: it runs the same AST as the synchronous
/// <c>Exec</c>/<c>Eval</c>, on the caller's thread, and can <b>wait</b> where the synchronous one blocks.
/// It exists for hosts with a single thread (a browser running .NET as WebAssembly), where a Python
/// <c>sleep</c> that blocks would freeze the page.
///
/// <para>Where it can wait: a call to a .NET method that returns a <see cref="Task"/> or <see cref="ValueTask"/>
/// (the host's own <c>async</c> method, seen from Python as <c>host.sleep(0.1)</c>), and a call to a builtin that
/// has an <see cref="PyBuiltinFunction.AsyncFn"/> (<c>time.sleep</c>). The awaited result is converted to a Python
/// value; an exception becomes the Python exception it would have been on the blocking path.</para>
///
/// <para>How it is kept small: it only re-implements what can contain a call (statements that nest statements,
/// and the expressions that nest calls). Everything else — and every statement or expression with no call
/// inside — runs on the synchronous evaluator, through the same helpers (<c>BinaryOp</c>, <c>GetAttr</c>,
/// <c>AssignTo</c>…), so the two cannot drift apart on semantics. A call chain made of Python functions, methods
/// and lambdas is followed all the way down; <b>not</b> followed (they run synchronously, so a wait inside
/// them does not suspend): class bodies, <c>__init__</c> and other dunder methods reached through operators,
/// comprehensions, f-strings, <c>match</c>, callbacks passed to builtins (<c>sorted(key=f)</c>), and generators
/// and coroutines (which use threads, so they are not available where threads are not).</para>
///
/// <para>State: the synchronous evaluator keeps its frame stack and recursion count per thread, which a wait may
/// change; this one keeps them in the frames it passes down and in <see cref="_asyncDepth"/> instead. One
/// asynchronous run at a time per interpreter.</para>
/// </summary>
public sealed partial class Interp
{
    private int _asyncDepth;

    /// <summary>Memo of <see cref="NeedsAsync(Stmt)"/>/<see cref="NeedsAsync(Expr)"/>, by node identity (the AST nodes are records: equal by value).</summary>
    private readonly Dictionary<object, bool> _needsAsync = new(ReferenceEqualityComparer.Instance);

    // ================================================================ esecuzione

    /// <summary>Runs a module like <see cref="RunModule"/>, waiting where a call can wait.</summary>
    public async ValueTask RunModuleAsync(Module ast, PyModule module)
    {
        var env = new Env(module) { IsGlobalScope = true };
        var frame = new Frame(null, env, "<module>", module.FileName, 1);
        Trace?.Invoke(new TraceEvent(TraceEventKind.Call, frame.Name, frame.File, frame.Line, env, null));
        try
        {
            foreach (var stmt in ast.Body)
                await ExecAsync(stmt, env, frame);
        }
        catch (PyRaise ex)
        {
            RecordFrame(ex, frame);
            throw;
        }
    }

    // ================================================================ analisi

    /// <summary>True if running the statement can reach a call: only then does it need the suspendable path.</summary>
    private bool NeedsAsync(Stmt stmt)
    {
        if (_needsAsync.TryGetValue(stmt, out bool known)) return known;
        bool result = stmt switch
        {
            BlockStmt b => NeedsAsync(b.Body),
            ExprStmt e => NeedsAsync(e.Value),
            AssignStmt a => NeedsAsync(a.Value) || a.Targets.Any(NeedsAsync),
            AugAssignStmt a => NeedsAsync(a.Target) || NeedsAsync(a.Value),
            IfStmt i => NeedsAsync(i.Cond) || NeedsAsync(i.Body) || NeedsAsync(i.OrElse),
            WhileStmt w => NeedsAsync(w.Cond) || NeedsAsync(w.Body) || NeedsAsync(w.OrElse),
            ForStmt f when !f.IsAsync => NeedsAsync(f.Iter) || NeedsAsync(f.Body) || NeedsAsync(f.OrElse),
            ReturnStmt r => r.Value is not null && NeedsAsync(r.Value),
            TryStmt t => NeedsAsync(t.Body) || t.Handlers.Any(h => NeedsAsync(h.Body)) || NeedsAsync(t.OrElse) || NeedsAsync(t.Finally),
            WithStmt w when !w.IsAsync => w.Items.Any(i => NeedsAsync(i.Ctx)) || NeedsAsync(w.Body),
            _ => false,
        };
        _needsAsync[stmt] = result;
        return result;
    }

    private bool NeedsAsync(List<Stmt> body)
    {
        foreach (var s in body)
            if (NeedsAsync(s)) return true;
        return false;
    }

    /// <summary>True if evaluating the expression can reach a call. Only the expression kinds the suspendable
    /// path re-implements are looked into; any other kind (lambda, comprehension, f-string…) is evaluated
    /// synchronously, so it counts as having no call.</summary>
    private bool NeedsAsync(Expr expr)
    {
        if (_needsAsync.TryGetValue(expr, out bool known)) return known;
        bool result = expr switch
        {
            CallExpr => true,
            TupleExpr t => t.Items.Any(NeedsAsync),
            ListExpr l => l.Items.Any(NeedsAsync),
            SetExpr s => s.Items.Any(NeedsAsync),
            DictExpr d => d.Items.Any(i => (i.Key is not null && NeedsAsync(i.Key)) || NeedsAsync(i.Value)),
            UnaryExpr u => NeedsAsync(u.Operand),
            BinaryExpr b => NeedsAsync(b.Left) || NeedsAsync(b.Right),
            BoolOpExpr b => b.Values.Any(NeedsAsync),
            CompareExpr c => NeedsAsync(c.Left) || c.Comparators.Any(NeedsAsync),
            AttributeExpr a => NeedsAsync(a.Obj),
            IndexExpr i => NeedsAsync(i.Obj) || NeedsAsync(i.Index),
            SliceExpr s => (s.Start is not null && NeedsAsync(s.Start)) || (s.Stop is not null && NeedsAsync(s.Stop)) || (s.Step is not null && NeedsAsync(s.Step)),
            IfExpExpr i => NeedsAsync(i.Cond) || NeedsAsync(i.Then) || NeedsAsync(i.Else),
            WalrusExpr w => NeedsAsync(w.Value),
            StarExpr s => NeedsAsync(s.Value),
            _ => false,
        };
        _needsAsync[expr] = result;
        return result;
    }

    // ================================================================ istruzioni

    private async ValueTask ExecStmtsAsync(List<Stmt> stmts, Env env, Frame frame)
    {
        foreach (var s in stmts)
            await ExecAsync(s, env, frame);
    }

    private async ValueTask ExecAsync(Stmt stmt, Env env, Frame frame)
    {
        if (stmt.Line > 0)
        {
            frame.Line = stmt.Line;
            Trace?.Invoke(new TraceEvent(TraceEventKind.Line, frame.Name, frame.File, stmt.Line, env, null));
        }

        if (!NeedsAsync(stmt))
        {
            Exec(stmt, env);
            return;
        }

        switch (stmt)
        {
            case BlockStmt b:
                await ExecStmtsAsync(b.Body, env, frame);
                break;

            case ExprStmt e:
                await EvalAsync(e.Value, env, frame);
                break;

            case AssignStmt a:
            {
                var value = await EvalAsync(a.Value, env, frame);
                // Same eager auto() resolution as the synchronous path (see Env.EnumAuto).
                if (env.EnumAuto is { } autoState)
                {
                    if (value is PyInstance ai && ai.Class.Name == "auto")
                        value = autoState.ConsumeAuto();
                    else if (value is BigInteger bi)
                        autoState.ObserveExplicit(bi);
                }
                foreach (var target in a.Targets)
                    AssignTo(target, value, env);
                break;
            }

            case AugAssignStmt a:
            {
                var current = await EvalAsync(a.Target, env, frame);
                var operand = await EvalAsync(a.Value, env, frame);
                ApplyAugAssign(a, env, current, operand);
                break;
            }

            case IfStmt i:
                if (PyOps.Truthy(this, await EvalAsync(i.Cond, env, frame)))
                    await ExecStmtsAsync(i.Body, env, frame);
                else
                    await ExecStmtsAsync(i.OrElse, env, frame);
                break;

            case WhileStmt w:
            {
                bool broke = false;
                while (PyOps.Truthy(this, await EvalAsync(w.Cond, env, frame)))
                {
                    try
                    {
                        await ExecStmtsAsync(w.Body, env, frame);
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
                    await ExecStmtsAsync(w.OrElse, env, frame);
                break;
            }

            case ForStmt f:
            {
                var iterable = await EvalAsync(f.Iter, env, frame);
                bool broke = false;
                foreach (var item in PyOps.Iterate(this, iterable))
                {
                    AssignTo(f.Target, item, env);
                    try
                    {
                        await ExecStmtsAsync(f.Body, env, frame);
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
                    await ExecStmtsAsync(f.OrElse, env, frame);
                break;
            }

            case ReturnStmt r:
                throw new ReturnSignal(r.Value is null ? PyNone.Instance : await EvalAsync(r.Value, env, frame));

            case TryStmt t:
                await ExecTryAsync(t, env, frame);
                break;

            case WithStmt w:
                await ExecWithItemAsync(w, 0, env, frame);
                break;

            default:
                Exec(stmt, env);
                break;
        }
    }

    private async ValueTask ExecTryAsync(TryStmt t, Env env, Frame frame)
    {
        try
        {
            bool completed = false;
            try
            {
                await ExecStmtsAsync(t.Body, env, frame);
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
                        await ExecStmtsAsync(handler.Body, env, frame);
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
                await ExecStmtsAsync(t.OrElse, env, frame);
        }
        finally
        {
            await ExecStmtsAsync(t.Finally, env, frame);
        }
    }

    private async ValueTask ExecWithItemAsync(WithStmt w, int index, Env env, Frame frame)
    {
        if (index >= w.Items.Count)
        {
            await ExecStmtsAsync(w.Body, env, frame);
            return;
        }

        var item = w.Items[index];
        var ctx = await EvalAsync(item.Ctx, env, frame);
        var entered = CallMethod(ctx, "__enter__", Array.Empty<object>());
        if (item.Target is not null)
            AssignTo(item.Target, entered, env);

        PyRaise? pending = null;
        try
        {
            await ExecWithItemAsync(w, index + 1, env, frame);
        }
        catch (PyRaise ex)
        {
            pending = ex;
        }
        catch
        {
            // return/break/continue (or CLR errors): __exit__ must still be called
            CallMethod(ctx, "__exit__", new object[] { PyNone.Instance, PyNone.Instance, PyNone.Instance });
            throw;
        }

        if (pending is null)
        {
            CallMethod(ctx, "__exit__", new object[] { PyNone.Instance, PyNone.Instance, PyNone.Instance });
        }
        else
        {
            var exitResult = CallMethod(ctx, "__exit__", new object[] { pending.Value.Class, pending.Value, PyNone.Instance });
            if (!PyOps.Truthy(this, exitResult))
                throw pending;
        }
    }

    // ================================================================ espressioni

    private async ValueTask<object> EvalAsync(Expr expr, Env env, Frame frame)
    {
        if (!NeedsAsync(expr))
            return Eval(expr, env);

        switch (expr)
        {
            case CallExpr call:
                return await EvalCallAsync(call, env, frame);

            case TupleExpr t:
                return new PyTuple((await EvalItemsAsync(t.Items, env, frame)).ToArray());
            case ListExpr l:
                return new PyList(await EvalItemsAsync(l.Items, env, frame));
            case SetExpr s:
                return new PySet(await EvalItemsAsync(s.Items, env, frame));

            case DictExpr d:
            {
                var dict = new PyDict();
                foreach (var (key, valueExpr) in d.Items)
                {
                    if (key is null)
                    {
                        var mapping = await EvalAsync(valueExpr, env, frame);
                        if (mapping is PyDict pd)
                            dict.Update(pd);
                        else
                            foreach (var k in PyOps.Iterate(this, mapping))
                                dict[k] = GetItem(mapping, k);
                    }
                    else
                    {
                        var k = await EvalAsync(key, env, frame);
                        dict[k] = await EvalAsync(valueExpr, env, frame);
                    }
                }
                return dict;
            }

            case UnaryExpr u:
                return UnaryOp(u.Op, await EvalAsync(u.Operand, env, frame));

            case BinaryExpr b:
            {
                var left = await EvalAsync(b.Left, env, frame);
                return BinaryOp(b.Op, left, await EvalAsync(b.Right, env, frame));
            }

            case BoolOpExpr b:
            {
                object last = PyNone.Instance;
                foreach (var operand in b.Values)
                {
                    last = await EvalAsync(operand, env, frame);
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
                var left = await EvalAsync(c.Left, env, frame);
                if (c.Ops.Count == 1)
                    return CompareRaw(c.Ops[0], left, await EvalAsync(c.Comparators[0], env, frame));
                for (int i = 0; i < c.Ops.Count; i++)
                {
                    var right = await EvalAsync(c.Comparators[i], env, frame);
                    if (!CompareOnce(c.Ops[i], left, right))
                        return false;
                    left = right;
                }
                return true;
            }

            case AttributeExpr a:
                return GetAttr(await EvalAsync(a.Obj, env, frame), MangleAttrName(a.Name));

            case IndexExpr i:
            {
                var obj = await EvalAsync(i.Obj, env, frame);
                return GetItem(obj, await EvalIndexAsync(i.Index, env, frame));
            }

            case SliceExpr s:
                return await EvalSliceAsync(s, env, frame);

            case IfExpExpr i:
                return PyOps.Truthy(this, await EvalAsync(i.Cond, env, frame))
                    ? await EvalAsync(i.Then, env, frame)
                    : await EvalAsync(i.Else, env, frame);

            case WalrusExpr w:
            {
                var value = await EvalAsync(w.Value, env, frame);
                env.Set(w.Name, value);
                return value;
            }

            default:
                return Eval(expr, env);
        }
    }

    private async ValueTask<List<object>> EvalItemsAsync(List<Expr> items, Env env, Frame frame)
    {
        var result = new List<object>(items.Count);
        foreach (var item in items)
        {
            if (item is StarExpr star)
                result.AddRange(PyOps.Iterate(this, await EvalAsync(star.Value, env, frame)));
            else
                result.Add(await EvalAsync(item, env, frame));
        }
        return result;
    }

    private async ValueTask<object> EvalIndexAsync(Expr index, Env env, Frame frame)
    {
        switch (index)
        {
            case SliceExpr s:
                return await EvalSliceAsync(s, env, frame);
            case TupleExpr t:
            {
                var parts = new object[t.Items.Count];
                for (int i = 0; i < parts.Length; i++)
                    parts[i] = await EvalIndexAsync(t.Items[i], env, frame);
                return new PyTuple(parts);
            }
            default:
                return await EvalAsync(index, env, frame);
        }
    }

    private async ValueTask<PySlice> EvalSliceAsync(SliceExpr s, Env env, Frame frame)
        => new(
            s.Start is null ? PyNone.Instance : await EvalAsync(s.Start, env, frame),
            s.Stop is null ? PyNone.Instance : await EvalAsync(s.Stop, env, frame),
            s.Step is null ? PyNone.Instance : await EvalAsync(s.Step, env, frame));

    private async ValueTask<object> EvalCallAsync(CallExpr call, Env env, Frame frame)
    {
        var callee = await EvalAsync(call.Func, env, frame);
        var args = new List<object>();
        Dictionary<string, object>? kwargs = null;

        foreach (var arg in call.Args)
        {
            if (arg.IsStar)
            {
                args.AddRange(PyOps.Iterate(this, await EvalAsync(arg.Value, env, frame)));
            }
            else if (arg.IsDoubleStar)
            {
                var mapping = await EvalAsync(arg.Value, env, frame);
                kwargs ??= new Dictionary<string, object>();
                if (mapping is PyDict d)
                {
                    foreach (var e in d.Entries)
                    {
                        if (e.Key is not string key)
                            throw PyErr.TypeError("keywords must be strings");
                        kwargs[key] = e.Value;
                    }
                }
                else if (PyOps.TryGetMappingItems(this, mapping, out var mapItems))
                {
                    foreach (var (k, v) in mapItems)
                    {
                        if (k is not string key)
                            throw PyErr.TypeError("keywords must be strings");
                        kwargs[key] = v;
                    }
                }
                else
                {
                    throw PyErr.TypeError("argument after ** must be a mapping");
                }
            }
            else if (arg.Name is not null)
            {
                kwargs ??= new Dictionary<string, object>();
                kwargs[arg.Name] = await EvalAsync(arg.Value, env, frame);
            }
            else
            {
                args.Add(await EvalAsync(arg.Value, env, frame));
            }
        }

        return await CallAsync(callee, args.ToArray(), kwargs);
    }

    // ================================================================ chiamate

    /// <summary>Like <see cref="Call"/>, but a Python function/method/lambda in the chain, a .NET method returning a
    /// task and a builtin with an async form can wait. Anything else is the synchronous <see cref="Call"/>.</summary>
    public async ValueTask<object> CallAsync(object callee, object[] args, Dictionary<string, object>? kwargs = null)
    {
        if (++_asyncDepth > MaxCallDepth)
        {
            _asyncDepth--;
            throw new PyRaise(PyErr.MakeInstance(PyErr.RecursionErrorClass, "maximum recursion depth exceeded"));
        }
        try
        {
            switch (callee)
            {
                case PyBuiltinFunction { AsyncFn: { } asyncFn }:
                    return await asyncFn(this, args, kwargs);

                case PyBoundMethod bm:
                {
                    var newArgs = new object[args.Length + 1];
                    newArgs[0] = bm.Self;
                    Array.Copy(args, 0, newArgs, 1, args.Length);
                    return await CallAsync(bm.Function, newArgs, kwargs);
                }

                case PyFunction fn when !fn.IsAsync && !fn.IsGenerator:
                    return await CallFunctionAsync(fn, args, kwargs);

                case PyStaticMethod sm:
                    return await CallAsync(sm.Function, args, kwargs);

                case PyInstance inst when inst.Class.TryLookup("__call__", out var callMethod):
                {
                    var newArgs = new object[args.Length + 1];
                    newArgs[0] = inst;
                    Array.Copy(args, 0, newArgs, 1, args.Length);
                    return await CallAsync(callMethod, newArgs, kwargs);
                }

                case ClrMethod clrMethod:
                    return await ClrResultAsync(ClrBinder.InvokeMethodRaw(clrMethod, args));

                case ClrObject { Instance: Delegate del }:
                    return await ClrResultAsync(ClrBinder.InvokeMethodRaw(new ClrMethod(del, del.GetType(), "Invoke"), args));

                default:
                    return CallCore(callee, args, kwargs);
            }
        }
        finally
        {
            _asyncDepth--;
        }
    }

    private async ValueTask<object> CallFunctionAsync(PyFunction fn, object[] args, Dictionary<string, object>? kwargs)
    {
        var env = BindParameters(fn, args, kwargs);
        if (fn.LambdaBody is not null)
            return await EvalAsync(fn.LambdaBody, env, new Frame(fn, env, fn.Name, fn.Module.FileName, 0));
        return await ExecFunctionBodyAsync(fn, env);
    }

    private async ValueTask<object> ExecFunctionBodyAsync(PyFunction fn, Env env)
    {
        var frame = new Frame(fn, env, fn.Name, fn.Module.FileName, fn.Body is { Count: > 0 } ? fn.Body[0].Line : 0);
        Trace?.Invoke(new TraceEvent(TraceEventKind.Call, frame.Name, frame.File, frame.Line, env, null));
        try
        {
            await ExecStmtsAsync(fn.Body!, env, frame);
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
            Trace?.Invoke(new TraceEvent(TraceEventKind.Return, frame.Name, frame.File, frame.Line, env, null));
        }
    }

    /// <summary>What a .NET method returned, as a Python value: a <see cref="Task"/>/<see cref="ValueTask"/> is awaited first
    /// (its result, if it has one, is the value). A .NET exception thrown by the awaited work becomes the Python
    /// exception, as it does when the method throws directly (an OperationCanceledException is how the host
    /// says "the window was closed").</summary>
    private static async ValueTask<object> ClrResultAsync(object? raw)
    {
        try
        {
            switch (raw)
            {
                case Task task:
                    await task;
                    return ClrMarshal.ToPython(TaskResult(task));
                case ValueTask valueTask:
                    await valueTask;
                    return PyNone.Instance;
                case not null when raw.GetType() is { IsGenericType: true } type && type.GetGenericTypeDefinition() == typeof(ValueTask<>):
                {
                    var task = (Task)type.GetMethod("AsTask")!.Invoke(raw, null)!;
                    await task;
                    return ClrMarshal.ToPython(TaskResult(task));
                }
                default:
                    return ClrMarshal.ToPython(raw);
            }
        }
        catch (PyRaise)
        {
            throw;
        }
        catch (Exception e) when (e is not (BreakSignal or ContinueSignal or ReturnSignal))
        {
            throw ClrExceptions.ToPython(e);
        }
    }

    /// <summary>The result of a completed <c>Task&lt;T&gt;</c>, or null for a plain <see cref="Task"/>.</summary>
    private static object? TaskResult(Task task)
    {
        var type = task.GetType();
        if (!type.IsGenericType) return null;
        var result = type.GetProperty("Result")!;
        return result.PropertyType.FullName == "System.Threading.Tasks.VoidTaskResult" ? null : result.GetValue(task);
    }
}
