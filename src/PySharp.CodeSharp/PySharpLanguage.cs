using CodeSharp;
using PySharpLib;
using PySharpLib.Parsing;
using PySharpLib.Runtime;

namespace PySharp.CodeSharp;

/// <summary>
/// PySharp (Python 3.12 subset) as a CodeSharp language.
/// <see cref="PyEngine.Run(string, string)"/> starts a fresh <c>__main__</c> on every call, so this session keeps one module
/// of its own and runs every source in it: that is what makes variables and definitions survive between calls.
/// Not supported by the engine, so not claimed: cancellation of a running script, input redirection, and a separate error stream.
/// </summary>
public sealed class PySharpLanguage : ILanguage
{
    public string Id => "pysharp";
    public string DisplayName => "PySharp";
    public string Version => PyEngine.PythonCompatibility;
    public IReadOnlyList<string> FileExtensions { get; } = [".py"];
    public LanguageCapabilities Capabilities => LanguageCapabilities.Check | LanguageCapabilities.Evaluate | LanguageCapabilities.Variables;
    public ILanguageSession CreateSession(SessionOptions? options = null) => new PySharpLanguageSession(this, options ?? new());
}

internal sealed class PySharpLanguageSession : ILanguageSession
{
    private const string ResultName = "__codesharp_result__";
    private readonly PyEngine engine;
    private readonly PyModule module;

    public PySharpLanguageSession(PySharpLanguage language, SessionOptions options)
    {
        Language = language;
        engine = new PyEngine(options.Output ?? Console.Out);
        module = engine.CreateModule("__main__");
        foreach (var (name, value) in options.Variables ?? new Dictionary<string, object?>()) SetVariable(name, value);
    }

    public ILanguage Language { get; }
    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    public IReadOnlyDictionary<string, object?> Variables => module.Dict.Entries
        .Where(e => e.Key is string name && !name.StartsWith("__", StringComparison.Ordinal))
        .ToDictionary(e => (string)e.Key, e => ClrMarshal.ToPlainObject(e.Value));

    public void SetVariable(string name, object? value) => module.Dict[name] = ClrMarshal.ToPython(value);

    public ValueTask<IReadOnlyList<Diagnostic>> CheckAsync(SourceText source, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        try { Parser.Parse(source.Text, source.FileName); return ValueTask.FromResult<IReadOnlyList<Diagnostic>>([]); }
        catch (PySyntaxError e) { return ValueTask.FromResult<IReadOnlyList<Diagnostic>>([Map(e)]); }
    }

    public async ValueTask<ExecutionResult> RunAsync(SourceText source, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        try { await Task.Run(() => Execute(source.Text, source.FileName), CancellationToken.None).ConfigureAwait(false); return ExecutionResult.Ok(); }
        catch (Exception e) { return ExecutionResult.Failed(Map(e, source.FileName)); }
    }

    public async ValueTask<ExecutionResult> EvaluateAsync(SourceText expression, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        try
        {
            return await Task.Run(() =>
            {
                Execute($"{ResultName} = (\n{expression.Text}\n)", expression.FileName);
                try { return ExecutionResult.Ok(module.Dict.TryGet(ResultName, out var value) ? ClrMarshal.ToPlainObject(value) : null); }
                finally { module.Dict.Remove(ResultName); }
            }, CancellationToken.None).ConfigureAwait(false);
        }
        catch (Exception e) { return ExecutionResult.Failed(Map(e, expression.FileName)); }
    }

    private void Execute(string text, string fileName)
    {
        module.FileName = fileName;
        var ast = Parser.Parse(text, fileName);
        BigStack.Run(() => engine.Interp.RunModule(ast, module));
    }

    private static Diagnostic Map(PySyntaxError e) => new("PY-SYNTAX", e.Message, DiagnosticSeverity.Error, new SourceSpan(e.FileName, e.Line, e.Column));

    private static Diagnostic Map(Exception e, string file) => e switch
    {
        PySyntaxError s => Map(s),
        PyRaise raise => new Diagnostic("PY-" + raise.Value.Class.Name.ToUpperInvariant(), raise.Message, DiagnosticSeverity.Error,
            raise.Traceback is { Count: > 0 } frames ? new SourceSpan(frames[0].File, frames[0].Line, 1) : null),
        _ => new Diagnostic("PY-HOST", e.Message),
    };
}
