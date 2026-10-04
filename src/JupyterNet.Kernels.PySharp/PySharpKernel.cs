using JupyterNet.Kernels.Abstractions;
using PySharpLib;
using PySharpLib.Parsing;
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
        PySharpLib.Numpy.NumpyRegistration.Register(_engine.Importer);
        PySharpLib.Cv2.Cv2Registration.Register(_engine.Importer);
        PySharpLib.Pywt.PywtRegistration.Register(_engine.Importer);
        PySharpLib.Pandas.PandasRegistration.Register(_engine.Importer);
        PySharpLib.Matplotlib.MatplotlibRegistration.Register(_engine.Importer);
        PySharpLib.Torch.TorchRegistration.Register(_engine.Importer);
        // pandas shows at most 20 columns in a notebook (Jupyter's default), unlike a terminal
        _engine.Run("import pandas as pd\npd.set_option('display.max_columns', 20)\n", "<kernel-init>");
    }

    /// <summary>Splits a cell into everything before its last statement and that statement, when the last statement is a bare expression
    /// (the value Jupyter echoes as the cell's result). The expression is null when there is none or it cannot be split cleanly.</summary>
    private static (string prefix, string? last, int line) SplitLastExpression(string code)
    {
        try
        {
            var ast = Parser.Parse(code, "<cell>");
            if (ast.Body.Count == 0 || ast.Body[^1] is not ExprStmt) return (code, null, 0);
            int ln = ast.Body[^1].Line;
            if (ast.Body.Count > 1 && ast.Body[^2].Line >= ln) return (code, null, 0);
            var lines = code.Split('\n');
            if (ln < 1 || ln > lines.Length) return (code, null, 0);
            return (string.Join("\n", lines.Take(ln - 1)), string.Join("\n", lines.Skip(ln - 1)), ln);
        }
        catch (Exception)
        {
            return (code, null, 0);
        }
    }

    /// <summary>Shows a value like Jupyter does: its <c>_repr_html_</c> if it has one, else its repr.</summary>
    private void Show(IKernelOutputSink sink, object value)
    {
        if (value is PyNone) return;
        var interp = _engine.Interp;
        if (value is PyInstance pi && pi.Class.Mro.Any(c => c.Dict.ContainsKey("_repr_html_")))
        {
            var html = interp.CallMethod(value, "_repr_html_", Array.Empty<object>());
            if (html is string h) { sink.WriteHtml(h); return; }
        }
        sink.WriteText(PyOps.Repr(interp, value) + "\n");
    }

    public Task ExecuteAsync(string code, IKernelOutputSink sink, CancellationToken cancellationToken)
    {
        // Re-injected every cell: cheap, and keeps the callback current if the sink changes.
        _engine.SetVariable("display_html", (Action<string>)sink.WriteHtml);
        // display_image(data, mime="image/png"): data is bytes/bytearray (an encoded image). This is what
        // matplotlib's plt.show() and image helpers call to put a picture in the cell's output.
        _engine.SetVariable("display_image", new PyBuiltinFunction("display_image", (_, a, kw) =>
        {
            if (a.Length < 1) throw PyErr.TypeError("display_image() missing required argument 'data'");
            byte[] data = a[0] switch
            {
                PyBytes b => b.Data,
                PyByteArray ba => ba.Data.ToArray(),
                _ => throw PyErr.TypeError("display_image() data must be bytes or bytearray"),
            };
            string mime = a.Length > 1 && a[1] is string m ? m
                : kw is not null && kw.TryGetValue("mime", out var km) && km is string ks ? ks : "image/png";
            sink.WriteImage(mime, data);
            return PyNone.Instance;
        }));
        // matplotlib: figures shown by plt.show() or still open when the cell ends go to this cell's output (inline backend)
        PySharpLib.Matplotlib.MatplotlibRegistration.SetShowSink(png => sink.WriteImage("image/png", png));
        // display(obj, ...): explicit rich output
        _engine.SetVariable("display", new PyBuiltinFunction("display", (_, a, _) => { foreach (var v in a) Show(sink, v); return PyNone.Instance; }));
        _stdout.GetStringBuilder().Clear();

        try
        {
            var (prefix, last, line) = SplitLastExpression(code);
            var module = _engine.Run(prefix, "<cell>");
            CarryGlobalsForward(module);
            object? echoed = null;
            if (last is not null)
            {
                // line numbers in a traceback stay those of the cell: pad with the lines that were split off
                var tail = _engine.Run(new string('\n', line - 1) + "__jn_echo__ = (" + last + "\n)", "<cell>");
                if (tail.Dict.TryGet("__jn_echo__", out var v)) echoed = v;
                tail.Dict.Remove("__jn_echo__");
                CarryGlobalsForward(tail);
            }
            var text = _stdout.ToString();
            if (text.Length > 0) sink.WriteText(text);
            if (echoed is not null) Show(sink, echoed);
            PySharpLib.Matplotlib.MatplotlibRegistration.FlushFigures();
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
