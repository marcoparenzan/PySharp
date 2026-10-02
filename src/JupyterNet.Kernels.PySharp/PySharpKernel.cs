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
        PySharpLib.Numpy.NumpyRegistration.Register(_engine.Importer);
        PySharpLib.Cv2.Cv2Registration.Register(_engine.Importer);
        PySharpLib.Pywt.PywtRegistration.Register(_engine.Importer);
        PySharpLib.Matplotlib.MatplotlibRegistration.Register(_engine.Importer);
        PySharpLib.Torch.TorchRegistration.Register(_engine.Importer);
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
        _stdout.GetStringBuilder().Clear();

        try
        {
            var module = _engine.Run(code, "<cell>");
            CarryGlobalsForward(module);
            PySharpLib.Matplotlib.MatplotlibRegistration.FlushFigures();

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
