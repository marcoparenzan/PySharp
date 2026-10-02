// Copyright (c) 2026 Marco Parenzan
//
// Licensed under the MIT License. See the LICENSE file in the project
// root for full license information.

// Runs every code cell of the cvintro notebooks in one PySharp session per notebook (cell state is carried
// over exactly like the JupyterNet PySharp kernel does) and reports which cells fail and why — the progress
// meter for NOTEBOOKS_PLAN.md Phases 4-8. matplotlib is replaced by a recording-free stub unless
// --no-stub is given (use that once PySharp.Matplotlib exists).
//
//   dotnet run --project tools/NotebookRunner -- [notebooksDir] [--filter lesson01] [--stub] [--timeout 120]
//                                                  [--out NOTEBOOKS_RUN.md]

using System.Diagnostics;
using System.Numerics;
using System.Text;
using System.Text.Json;
using PySharpLib;
using PySharpLib.Runtime;

string dir = @"D:\clones\sbirchfield.github.io\cvintro\notebooks";
string? filter = null;
bool stub = false;
int timeoutSeconds = 120;
string? outPath = null;
for (int i = 0; i < args.Length; i++)
{
    switch (args[i])
    {
        case "--filter": filter = args[++i]; break;
        case "--stub": stub = true; break;
        case "--timeout": timeoutSeconds = int.Parse(args[++i]); break;
        case "--out": outPath = args[++i]; break;
        default: dir = args[i]; break;
    }
}

var results = new List<(string name, int cells, int ok, int failed, string first)>();
foreach (var file in Directory.GetFiles(dir, "*.ipynb").OrderBy(x => x))
{
    string name = Path.GetFileNameWithoutExtension(file);
    if (filter is not null && !name.Contains(filter, StringComparison.OrdinalIgnoreCase)) continue;
    results.Add(RunNotebook(file, name, stub, timeoutSeconds));
}

var md = new StringBuilder();
md.AppendLine("# Notebook run report");
md.AppendLine();
md.AppendLine($"Generated {DateTime.Now:yyyy-MM-dd HH:mm} by tools/NotebookRunner ({(stub ? "matplotlib STUBBED" : "real matplotlib")}). Do not edit by hand.");
md.AppendLine();
md.AppendLine("| Notebook | Code cells | OK | Failed | First failure |");
md.AppendLine("|---|---|---|---|---|");
foreach (var r in results)
    md.AppendLine($"| {r.name} | {r.cells} | {r.ok} | {r.failed} | {r.first.Replace("|", "\\|")} |");
int clean = results.Count(r => r.failed == 0);
md.AppendLine();
md.AppendLine($"**Notebooks with every cell passing: {clean} / {results.Count}**  (cells: {results.Sum(r => r.ok)} ok / {results.Sum(r => r.cells)})");
Console.WriteLine(md.ToString());
if (outPath is not null) File.WriteAllText(outPath, md.ToString());
return 0;

static (string, int, int, int, string) RunNotebook(string path, string name, bool stub, int timeoutSeconds)
{
    var doc = JsonDocument.Parse(File.ReadAllText(path)).RootElement;
    var cells = doc.GetProperty("cells").EnumerateArray()
        .Where(c => c.GetProperty("cell_type").GetString() == "code")
        .Select(c => c.GetProperty("source") is { ValueKind: JsonValueKind.Array } src ? string.Concat(src.EnumerateArray().Select(s => s.GetString())) : c.GetProperty("source").GetString() ?? "")
        .ToList();

    var writer = new StringWriter();
    var engine = new PyEngine(writer);
    PySharpLib.Numpy.NumpyRegistration.Register(engine.Importer);
    PySharpLib.Cv2.Cv2Registration.Register(engine.Importer);
    PySharpLib.Pywt.PywtRegistration.Register(engine.Importer);
    if (stub) StubModules.Register(engine);
    engine.SetVariable("display_image", new PyBuiltinFunction("display_image", (_, _, _) => PyNone.Instance));
    engine.SetVariable("display_html", (Action<string>)(_ => { }));

    int ok = 0, failed = 0;
    string first = "";
    Directory.SetCurrentDirectory(Path.GetDirectoryName(path)!);
    for (int ci = 0; ci < cells.Count; ci++)
    {
        string code = string.Join("\n", cells[ci].Split('\n').Where(l => !l.TrimStart().StartsWith('%') && !l.TrimStart().StartsWith('!')));
        string? error = null;
        var sw = Stopwatch.StartNew();
        var thread = new Thread(() =>
        {
            try
            {
                var module = engine.Run(code, $"{name}[{ci}]");
                foreach (var (key, value) in module.Dict.Entries)
                    if (key is string k && !(k.StartsWith("__") && k.EndsWith("__")))
                        engine.Globals[k] = value;
            }
            catch (PyRaise ex) { error = PyErr.FormatTraceback(ex).Split('\n').Where(l => l.Trim().Length > 0).LastOrDefault() ?? ex.Message; }
            catch (PySyntaxError ex) { error = $"SyntaxError: {ex.Message} (line {ex.Line})"; }
            catch (Exception ex) { error = $"{ex.GetType().Name}: {ex.Message}"; }
        }, 256 * 1024 * 1024);
        thread.Start();
        if (!thread.Join(TimeSpan.FromSeconds(timeoutSeconds)))
        {
            error = $"TIMEOUT after {timeoutSeconds}s";
            failed++;
            if (first.Length == 0) first = $"cell {ci}: {error}";
            break; // the abandoned cell may still be running; do not continue this notebook
        }
        if (error is null) ok++;
        else
        {
            failed++;
            if (first.Length == 0) first = $"cell {ci}: {Trim(error)}";
        }
        writer.GetStringBuilder().Clear();
    }
    Console.Error.WriteLine($"{name}: {ok}/{cells.Count} ok");
    return (name, cells.Count, ok, failed, first);
}

static string Trim(string s) => s.Length > 160 ? s[..160] + "…" : s;

/// <summary>A do-nothing stand-in for matplotlib (every attribute is callable and returns itself) so the
/// numeric/cv2 parts of a notebook can be exercised before the real renderer exists.</summary>
static class StubModules
{
    private static readonly PyClass AnyClass = BuildAnyClass();
    private static readonly PyClass AxesArrayClass = BuildAxesArrayClass();
    private static readonly PyInstance Any = new(AnyClass);

    public static void Register(PyEngine engine)
    {
        foreach (var name in new[] { "matplotlib", "matplotlib.pyplot", "matplotlib.patches", "matplotlib.cm", "matplotlib.colors", "matplotlib.collections", "mpl_toolkits", "mpl_toolkits.mplot3d", "mpl_toolkits.mplot3d.art3d" })
        {
            var module = new PyModule(name);
            module.Dict["__getattr__"] = new PyBuiltinFunction("__getattr__", (_, a, _) => Any);
            string n = name;
            engine.Importer.RegisterBuiltin(n, _ => StubModule(n));
        }
    }

    private static PyModule StubModule(string name)
    {
        var m = new PyModule(name);
        foreach (var attr in new[] { "imshow", "show", "tight_layout", "figure", "scatter", "plot", "colorbar", "title", "xlabel", "ylabel", "legend", "axis", "hist", "bar", "text", "suptitle", "subplot", "gca", "gcf", "savefig", "close", "semilogy", "stem", "fill_between", "contour", "contourf", "axhline", "axvline", "xlim", "ylim", "xticks", "yticks", "grid", "clf", "cla", "pause", "arrow", "annotate", "errorbar", "step", "loglog", "xscale", "yscale", "set_cmap", "subplots_adjust", "ion", "ioff", "draw", "quiver", "streamplot", "pcolormesh", "imsave", "imread", "pyplot", "patches", "cm", "colors", "collections", "mplot3d", "art3d", "Rectangle", "Circle", "Polygon", "Axes3D", "Poly3DCollection", "Line3DCollection", "gray", "viridis", "jet", "hot", "gray_r", "rcParams", "use", "Normalize", "ListedColormap", "LinearSegmentedColormap", "get_cmap", "Ellipse", "FancyArrowPatch", "Arrow", "Wedge" })
            m.Dict[attr] = Any;
        m.Dict["subplots"] = new PyBuiltinFunction("subplots", (interp, a, kw) => Subplots(interp, a, kw));
        return m;
    }

    private static object Subplots(PySharpLib.Interpretation.Interp interp, object[] a, Dictionary<string, object>? kw)
    {
        int rows = 1, cols = 1;
        if (a.Length > 0) rows = (int)PyOps.AsBigInt(a[0], "nrows");
        if (a.Length > 1) cols = (int)PyOps.AsBigInt(a[1], "ncols");
        if (kw is not null)
        {
            if (kw.TryGetValue("nrows", out var r)) rows = (int)PyOps.AsBigInt(r, "nrows");
            if (kw.TryGetValue("ncols", out var c)) cols = (int)PyOps.AsBigInt(c, "ncols");
        }
        if (rows == 1 && cols == 1) return new PyTuple(new object[] { Any, Any });
        var grid = new PyInstance(AxesArrayClass);
        grid.Dict["rows"] = rows;
        grid.Dict["cols"] = cols;
        return new PyTuple(new object[] { Any, grid });
    }

    private static PyClass BuildAnyClass()
    {
        var cls = new PyClass("StubAny", new List<PyClass>());
        cls.Dict["__getattr__"] = new PyBuiltinFunction("__getattr__", (interp, a, _) =>
        {
            string n = (string)a[1];
            if (n == "subplots") return new PyBuiltinFunction("subplots", (i2, args, kw) => Subplots(i2, args, kw));
            if (n.StartsWith("__")) throw PyErr.AttributeError(n);
            return Any;
        });
        cls.Dict["__call__"] = new PyBuiltinFunction("__call__", (_, a, _) => Any);
        cls.Dict["__getitem__"] = new PyBuiltinFunction("__getitem__", (_, a, _) => Any);
        cls.Dict["__setitem__"] = new PyBuiltinFunction("__setitem__", (_, a, _) => PyNone.Instance);
        cls.Dict["__iter__"] = new PyBuiltinFunction("__iter__", (_, a, _) => new PyIterator(new List<object> { Any, Any, Any }.GetEnumerator()));
        cls.Dict["__len__"] = new PyBuiltinFunction("__len__", (_, a, _) => new BigInteger(3));
        cls.Dict["__bool__"] = new PyBuiltinFunction("__bool__", (_, a, _) => true);
        foreach (var op in new[] { "__add__", "__radd__", "__sub__", "__rsub__", "__mul__", "__rmul__", "__truediv__", "__rtruediv__", "__lt__", "__gt__", "__le__", "__ge__" })
            cls.Dict[op] = new PyBuiltinFunction(op, (_, a, _) => Any);
        return cls;
    }

    private static PyClass BuildAxesArrayClass()
    {
        var cls = new PyClass("StubAxesArray", new List<PyClass>());
        int Count(object self) => (int)((PyInstance)self).Dict["rows"] * (int)((PyInstance)self).Dict["cols"];
        cls.Dict["__getitem__"] = new PyBuiltinFunction("__getitem__", (_, a, _) =>
        {
            var self = (PyInstance)a[0];
            int rows = (int)self.Dict["rows"], cols = (int)self.Dict["cols"];
            if (a[1] is PyTuple) return Any;
            int i = (int)PyOps.AsBigInt(a[1], "index");
            int n = rows == 1 || cols == 1 ? rows * cols : rows;
            if (i >= n || i < -n) throw PyErr.IndexError("index out of range");
            return rows == 1 || cols == 1 ? Any : new PyInstance(AxesArrayClass) { Dict = { ["rows"] = 1, ["cols"] = cols } };
        });
        cls.Dict["ravel"] = new PyBuiltinFunction("ravel", (_, a, _) => new PyList(Enumerable.Repeat((object)Any, Count(a[0]))));
        cls.Dict["flatten"] = cls.Dict["ravel"];
        cls.Dict["tolist"] = cls.Dict["ravel"];
        cls.Dict["flat"] = new PyProperty { Getter = new PyBuiltinFunction("flat", (_, a, _) => new PyList(Enumerable.Repeat((object)Any, Count(a[0])))) };
        cls.Dict["shape"] = new PyProperty { Getter = new PyBuiltinFunction("shape", (_, a, _) =>
        {
            var self = (PyInstance)a[0];
            int rows = (int)self.Dict["rows"], cols = (int)self.Dict["cols"];
            return rows == 1 || cols == 1 ? new PyTuple(new object[] { new BigInteger(rows * cols) }) : new PyTuple(new object[] { new BigInteger(rows), new BigInteger(cols) });
        }) };
        cls.Dict["__len__"] = new PyBuiltinFunction("__len__", (_, a, _) =>
        {
            var self = (PyInstance)a[0];
            int rows = (int)self.Dict["rows"], cols = (int)self.Dict["cols"];
            return new BigInteger(rows == 1 || cols == 1 ? rows * cols : rows);
        });
        cls.Dict["__iter__"] = new PyBuiltinFunction("__iter__", (_, a, _) =>
        {
            var self = (PyInstance)a[0];
            int rows = (int)self.Dict["rows"], cols = (int)self.Dict["cols"];
            if (rows == 1 || cols == 1) return new PyIterator(Enumerable.Repeat((object)Any, rows * cols).GetEnumerator());
            var rowsList = Enumerable.Range(0, rows).Select(_ => (object)new PyInstance(AxesArrayClass) { Dict = { ["rows"] = 1, ["cols"] = cols } }).ToList();
            return new PyIterator(rowsList.GetEnumerator());
        });
        return cls;
    }
}
