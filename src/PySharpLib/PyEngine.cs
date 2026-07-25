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
        Interp.RunModule(ast, module);
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
