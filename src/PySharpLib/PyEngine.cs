// Copyright (c) 2026 Marco Parenzan
//
// Licensed under the MIT License. See the LICENSE file in the project
// root for full license information.

using PySharpLib.Builtins;
using PySharpLib.Importing;
using PySharpLib.Interpretation;
using PySharpLib.Parsing;
using PySharpLib.Runtime;

namespace PySharpLib;

/// <summary>
/// Public facade of the PySharp interpreter: builds a complete environment
/// (builtins + interpreter + import system) and lets you run Python sources.
/// </summary>
public sealed class PyEngine
{
    /// <summary>The CPython language level PySharp targets (it implements a subset of this version).</summary>
    public const string PythonCompatibility = "3.12.0";

    public Interp Interp { get; }
    public PyModule BuiltinsModule { get; }
    public Importer Importer { get; }

    /// <summary>
    /// Host-injected globals: names made available in the script's scope. Values are
    /// marshalled to Python on <see cref="Run(string, string)"/> — .NET primitives become
    /// their Python equivalents, any other object/Type becomes a usable CLR wrapper
    /// (method calls, property/field access, indexing, iteration). See <see cref="SetVariable"/>.
    /// </summary>
    public Dictionary<string, object?> Globals { get; } = new();

    public PyEngine(TextWriter? stdout = null)
    {
        BuiltinsModule = BuiltinsFactory.Create();
        Interp = new Interp(BuiltinsModule, stdout);
        Importer = new Importer(BuiltinsModule);
        Interp.ImportHook = Importer.Import;
        Modules.StdlibModules.RegisterAll(Importer);
    }

    /// <summary>
    /// Inject a .NET object (or <see cref="System.Type"/>) into the script scope under
    /// <paramref name="name"/>. From Python the value is used idiomatically:
    /// <c>obj.Method(args)</c>, <c>obj.Property</c>, <c>obj[i]</c>, iteration, etc.
    /// Fluent: returns this engine.
    /// </summary>
    public PyEngine SetVariable(string name, object? value)
    {
        Globals[name] = value;
        return this;
    }

    /// <summary>Runs the source in a new __main__ module (seeded with the injected globals) and returns it.</summary>
    public PyModule Run(string source, string fileName = "<string>")
    {
        var module = CreateModule("__main__");
        module.FileName = fileName;
        foreach (var (name, value) in Globals)
            module.Dict[name] = Runtime.ClrMarshal.ToPython(value);
        var ast = Parser.Parse(source, fileName);
        // Real, deep-but-legitimate Python recursion (close to CPython's own default 1000-frame
        // limit) needs more C# stack than the OS default thread stack (1MB) gives this tree-walking
        // interpreter — each Python-level call is several nested C# frames (Call/CallCore/
        // CallFunction/ExecFunctionBody/ExecStmts/Exec/Eval). Found the hard way: Interp.Call's new
        // recursion-depth guard (see its own doc comment) correctly turned one specific runaway-
        // recursion corpus test into a catchable RecursionError, but plain recursive functions were
        // *still* overflowing the real CLR stack well before reaching that depth counter's limit —
        // a real, pre-existing constraint of running on the default thread stack, not specific to
        // that one fix. Matches the same technique this codebase already uses for coroutine/
        // generator bodies (their own dedicated threads in Async.cs/PyGenerator.cs).
        Runtime.BigStack.Run(() => Interp.RunModule(ast, module));
        return module;
    }

    /// <summary>Runs the source and returns what was written to stdout (for tests).</summary>
    public static string CaptureOutput(string source, string fileName = "<test>")
    {
        var writer = new StringWriter();
        var engine = new PyEngine(writer);
        engine.Run(source, fileName);
        return writer.ToString();
    }

    public PyModule CreateModule(string name)
        => new(name) { Builtins = BuiltinsModule };
}
