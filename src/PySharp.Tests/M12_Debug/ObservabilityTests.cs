// Copyright (c) 2026 Marco Parenzan
//
// Licensed under the MIT License. See the LICENSE file in the project
// root for full license information.

using System.Numerics;
using PySharpLib;
using PySharpLib.Runtime;

namespace PySharp.Tests.M12_Debug;

/// <summary>
/// Observability for embedding: on an exception the host can see the line, the full call
/// stack (traceback) and the variables in scope; and it can watch execution live via a hook.
/// </summary>
public class ObservabilityTests
{
    private static PyRaise RunExpectingRaise(string source, string file = "test.py")
    {
        var engine = new PyEngine(TextWriter.Null);
        return Assert.Throws<PyRaise>(() => engine.Run(source, file));
    }

    [Fact]
    public void Traceback_reports_the_error_line_innermost_first()
    {
        var ex = RunExpectingRaise(
            "def level_two(x):\n" +   // 1
            "    return x / 0\n" +     // 2  <- error
            "def level_one(n):\n" +   // 3
            "    return level_two(n)\n" + // 4
            "level_one(10)\n");        // 5

        Assert.NotNull(ex.Traceback);
        // innermost first: level_two (line 2) -> level_one (line 4) -> <module> (line 5)
        Assert.Equal("level_two", ex.Traceback![0].Function);
        Assert.Equal(2, ex.Traceback[0].Line);
        Assert.Equal("level_one", ex.Traceback[1].Function);
        Assert.Equal(4, ex.Traceback[1].Line);
        Assert.Equal("<module>", ex.Traceback[2].Function);
        Assert.Equal(5, ex.Traceback[2].Line);
    }

    [Fact]
    public void Traceback_carries_the_source_file()
    {
        var ex = RunExpectingRaise("raise ValueError('x')\n", "myscript.py");
        Assert.Equal("myscript.py", ex.Traceback![0].File);
    }

    [Fact]
    public void Frame_exposes_local_variables_at_the_error()
    {
        var ex = RunExpectingRaise(
            "def compute():\n" +
            "    a = 5\n" +
            "    b = 'hi'\n" +
            "    raise RuntimeError('boom')\n" +
            "compute()\n");

        var locals = ex.Traceback![0].Locals();     // innermost frame = compute()
        Assert.True(locals.TryGet("a", out var a));
        Assert.Equal(new BigInteger(5), a);
        Assert.True(locals.TryGet("b", out var b));
        Assert.Equal("hi", b);
    }

    [Fact]
    public void Module_frame_locals_are_the_globals()
    {
        var ex = RunExpectingRaise(
            "widget = 42\n" +
            "raise ValueError('x')\n");

        var moduleFrame = ex.Traceback![^1];         // outermost = <module>
        Assert.True(moduleFrame.IsModule);
        Assert.True(moduleFrame.Locals().TryGet("widget", out var w));
        Assert.Equal(new BigInteger(42), w);
    }

    [Fact]
    public void FormatTraceback_is_cpython_shaped()
    {
        var ex = RunExpectingRaise(
            "def f():\n    raise ValueError('boom')\nf()\n", "s.py");
        var text = PyErr.FormatTraceback(ex);
        Assert.StartsWith("Traceback (most recent call last):", text);
        Assert.Contains("File \"s.py\", line 3, in <module>", text);
        Assert.Contains("File \"s.py\", line 2, in f", text);
        Assert.EndsWith("ValueError: boom", text);
    }

    [Fact]
    public void Caught_exceptions_do_not_leak_a_traceback_to_the_host()
    {
        // An exception handled inside Python must not surface as an uncaught PyRaise.
        var engine = new PyEngine(TextWriter.Null);
        var module = engine.Run(
            "ok = False\n" +
            "try:\n" +
            "    raise ValueError('x')\n" +
            "except ValueError:\n" +
            "    ok = True\n");
        Assert.True(module.Dict.TryGet("ok", out var ok) && ok is true);
    }

    [Fact]
    public void Trace_hook_observes_lines_and_calls()
    {
        var engine = new PyEngine(TextWriter.Null);
        var events = new List<TraceEvent>();
        engine.Interp.Trace = e => events.Add(e);

        engine.Run(
            "def f(x):\n" +
            "    return x + 1\n" +
            "y = f(10)\n");

        Assert.Contains(events, e => e.Kind == TraceEventKind.Call && e.Function == "<module>");
        Assert.Contains(events, e => e.Kind == TraceEventKind.Call && e.Function == "f");
        Assert.Contains(events, e => e.Kind == TraceEventKind.Return && e.Function == "f");
        Assert.Contains(events, e => e.Kind == TraceEventKind.Line && e.Function == "f" && e.Line == 2);
    }

    [Fact]
    public void Trace_hook_reports_exception_events()
    {
        var engine = new PyEngine(TextWriter.Null);
        var exceptions = new List<TraceEvent>();
        engine.Interp.Trace = e => { if (e.Kind == TraceEventKind.Exception) exceptions.Add(e); };

        Assert.Throws<PyRaise>(() => engine.Run(
            "def f():\n    raise ValueError('boom')\nf()\n"));

        Assert.NotEmpty(exceptions);
        Assert.Contains(exceptions, e => e.Function == "f");
        Assert.All(exceptions, e => Assert.NotNull(e.Exception));
    }

    [Fact]
    public void No_hook_means_no_overhead_path_still_works()
    {
        var engine = new PyEngine(TextWriter.Null);   // Trace stays null
        var module = engine.Run("x = sum(range(10))\n");
        Assert.True(module.Dict.TryGet("x", out var x));
        Assert.Equal(new BigInteger(45), x);
    }
}
