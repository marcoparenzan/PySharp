// Copyright (c) 2026 Marco Parenzan
//
// Licensed under the MIT License. See the LICENSE file in the project
// root for full license information.

namespace PySharpLib.Runtime;

// =====================================================================================
//  Frames, tracebacks and the execution trace hook — observability for the host.
//
//  Every executing scope (the module top-level and each function call) has a Frame that
//  remembers the line currently running. When a Python exception (PyRaise) unwinds, each
//  frame it passes through is recorded into the exception's Traceback, so the host can see
//  *where* the error happened and inspect the variables in scope at each level. A host can
//  also observe execution live through Interp.Trace (the foundation for step-debugging).
// =====================================================================================

/// <summary>A live execution frame: the module top-level or a function call in progress.</summary>
public sealed class Frame
{
    /// <summary>The function running in this frame; null for the module top-level.</summary>
    public PyFunction? Fn { get; }
    public Env Env { get; }
    public string Name { get; }
    public string File { get; }
    /// <summary>The source line currently executing in this frame (updated per statement).</summary>
    public int Line { get; set; }

    public Frame(PyFunction? fn, Env env, string name, string file, int line)
    {
        Fn = fn;
        Env = env;
        Name = name;
        File = file;
        Line = line;
    }

    /// <summary>Snapshot this live frame into an immutable traceback entry.</summary>
    public PyFrameInfo Snapshot() => new(Name, File, Line, Env, Fn is null);
}

/// <summary>One entry of a traceback: where a frame was when the exception passed through it.</summary>
public sealed class PyFrameInfo
{
    public string Function { get; }
    public string File { get; }
    public int Line { get; }
    /// <summary>The scope in effect at this frame — read its variables for post-mortem inspection.</summary>
    public Env Scope { get; }
    public bool IsModule { get; }

    public PyFrameInfo(string function, string file, int line, Env scope, bool isModule)
    {
        Function = function;
        File = file;
        Line = line;
        Scope = scope;
        IsModule = isModule;
    }

    /// <summary>The variables visible in this frame (module globals for the top-level frame).</summary>
    public PyDict Locals()
    {
        if (IsModule)
            return Scope.Module.Dict;
        var d = new PyDict();
        foreach (var kv in Scope.Locals)
            d[kv.Key] = kv.Value;
        return d;
    }

    public override string ToString() => $"  File \"{File}\", line {Line}, in {Function}";
}

/// <summary>Kind of a <see cref="TraceEvent"/> reported to <c>Interp.Trace</c>.</summary>
public enum TraceEventKind
{
    /// <summary>A source line is about to execute.</summary>
    Line,
    /// <summary>A Python function is being entered.</summary>
    Call,
    /// <summary>A Python function is returning.</summary>
    Return,
    /// <summary>An exception is unwinding through a frame.</summary>
    Exception,
}

/// <summary>
/// An execution event delivered to the host's <c>Interp.Trace</c> callback. The callback runs
/// synchronously on the interpreter thread, so a debugger may block inside it to implement
/// breakpoints / stepping. This is the intended foundation for a Debug Adapter (e.g. VS Code).
/// </summary>
public readonly record struct TraceEvent(
    TraceEventKind Kind,
    string Function,
    string File,
    int Line,
    Env Scope,
    PyInstance? Exception);
