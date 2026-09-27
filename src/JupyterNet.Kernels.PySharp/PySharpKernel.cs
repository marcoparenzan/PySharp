using JupyterNet.Kernels.Abstractions;
using PySharpLib;
using PySharpLib.Runtime;

namespace JupyterNet.Kernels.PySharp;

/// <summary>
/// Runs Python cells on PySharp's own from-scratch interpreter (<see cref="PyEngine"/>).
/// <see cref="PyEngine.Run"/> always starts a fresh <c>__main__</c> module seeded only from
/// <see cref="PyEngine.Globals"/>, so cross-cell state ("a variable from a previous cell is still
/// there") is implemented here by copying every non-dunder name out of the finished module's
/// <see cref="PyModule.Dict"/> back into <see cref="PyEngine.Globals"/> before the next cell runs
/// — entirely through PyEngine's public, documented surface.
/// </summary>
public sealed class PySharpKernel : IKernel
{
    public string Id => "pysharp";

    private readonly PyEngine _engine;
    private readonly StringWriter _stdout = new();

    public PySharpKernel()
    {
        _engine = new PyEngine(_stdout);
    }

    public Task ExecuteAsync(string code, IKernelOutputSink sink, CancellationToken cancellationToken)
    {
        // Re-injected every cell: cheap, and keeps the callback current if the sink changes.
        _engine.SetVariable("display_html", (Action<string>)sink.WriteHtml);
        _stdout.GetStringBuilder().Clear();

        try
        {
            var module = _engine.Run(code, "<cell>");
            CarryGlobalsForward(module);

            var text = _stdout.ToString();
            if (text.Length > 0) sink.WriteText(text);
        }
        catch (PyRaise ex)
        {
            sink.WriteError(PyErr.FormatTraceback(ex));
        }
        catch (PySyntaxError ex)
        {
            sink.WriteError($"SyntaxError: {ex.Message} (line {ex.Line})");
        }

        return Task.CompletedTask;
    }

    private void CarryGlobalsForward(PyModule module)
    {
        foreach (var (key, value) in module.Dict.Entries)
        {
            if (key is string name && !(name.StartsWith("__") && name.EndsWith("__")))
                _engine.Globals[name] = value;
        }
    }
}
