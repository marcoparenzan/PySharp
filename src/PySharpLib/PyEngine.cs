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

    public PyEngine(TextWriter? stdout = null)
    {
        BuiltinsModule = BuiltinsFactory.Create();
        Interp = new Interp(BuiltinsModule, stdout);
        Importer = new Importer(BuiltinsModule);
        Interp.ImportHook = Importer.Import;
        Modules.StdlibModules.RegisterAll(Importer);
    }

    /// <summary>Runs the source in a new __main__ module and returns it.</summary>
    public PyModule Run(string source, string fileName = "<string>")
    {
        var module = CreateModule("__main__");
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
