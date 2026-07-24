// Copyright (c) 2026 Marco Parenzan
//
// Licensed under the MIT License. See the LICENSE file in the project
// root for full license information.

using PySharpLib;
using PySharpLib.Runtime;

namespace PySharp.Tests.Corpus;

/// <summary>
/// Runs the RustPython snippet corpus (assert-based, MIT license) inside PySharp.
///
/// <para>
/// <b>Supported</b>: the snippets PySharp runs entirely. They must pass —
/// a gatekeeper against regressions.
/// </para>
/// <para>
/// <b>Xfail</b>: snippets that depend on CPython features intentionally out of
/// PySharp v1's scope (see <see cref="Xfail"/> for the category). The test checks
/// that they <em>still fail</em>: if one starts passing, the red test reminds us
/// to promote it to Supported.
/// </para>
/// </summary>
public class CorpusTests
{
    private static readonly string SnippetsDir =
        Path.Combine(AppContext.BaseDirectory, "Corpus", "snippets");

    /// <summary>
    /// Snippets not supported in v1, with the reason. Categories:
    ///  - dunder-attr: special methods exposed as attributes on builtin TYPES
    ///    (e.g. <c>int.__eq__</c>, <c>range.__eq__</c>, <c>type(None).__dict__</c>);
    ///  - iter-type: map/filter/enumerate/zip as TYPES (<c>type(map(...)) == map</c>);
    ///  - complex: complex numbers (<c>1j</c>) and <c>complex.__pow__</c>;
    ///  - exec: <c>exec()</c>/<c>compile()</c>;
    ///  - except-star: exception groups <c>except*</c> (3.11);
    ///  - gen-send: <c>generator.send(value)</c> with a non-None value;
    ///  - protocol: deep protocols (<c>__index__</c>, <c>__trunc__</c>, <c>__instancecheck__</c>, pickle);
    ///  - float-edge: fine-grained float semantics (signed zero, specific overflows);
    ///  - stdlib-depth: stdlib APIs not yet covered in detail;
    ///  - ext-file: depends on files/modules not included in the downloaded corpus.
    /// </summary>
    private static readonly Dictionary<string, string> Xfail = new()
    {
        ["builtin_int.py"] = "dunder-attr: int.__eq__ etc. as type attributes",
        ["builtin_str.py"] = "dunder-attr: str.__eq__ etc.",
        ["builtin_set.py"] = "dunder-attr: set.__eq__ etc.",
        ["builtin_dict.py"] = "dunder-attr: dict.__doc__/__or__ as attributes",
        ["builtin_dict_union.py"] = "dunder-attr: dict.__or__/__ror__/__ior__",
        ["builtin_none.py"] = "dunder-attr: None.__eq__, type(None).__dict__, wrapper_descriptor",
        ["builtin_range.py"] = "dunder-attr: range.__eq__ as a type attribute",
        ["builtin_tuple.py"] = "dunder-attr: tuple.__ne__ as an attribute",
        ["builtin_issubclass.py"] = "dunder-attr / __subclasshook__",
        ["operator_comparison.py"] = "dunder-attr: comparisons as type methods",
        ["recursion.py"] = "dunder-attr: type.__str__ / recursion limits",
        ["builtin_enumerate.py"] = "iter-type: type(enumerate(...)) == enumerate",
        ["builtin_filter.py"] = "iter-type: type(filter(...)) == filter",
        ["builtin_map.py"] = "iter-type: type(map(...)) == map",
        ["builtin_slice.py"] = "complex: 1j literals",
        ["builtin_pow.py"] = "complex.__pow__ / modular inverse pow(a,-1,m)",
        ["syntax_slice.py"] = "complex: 1j literals in indices",
        ["syntax_function_args.py"] = "exec()",
        ["syntax_global_nonlocal.py"] = "exec()/compile() for the SyntaxError test",
        ["builtin_exceptions.py"] = "except-star: exception groups (3.11)",
        ["syntax_generator.py"] = "gen-send: generator.send(value)",
        ["builtin_isinstance.py"] = "protocol: __instancecheck__ (metaclasses)",
        ["protocol_iternext.py"] = "protocol: __setstate__ (pickle) on iterators",
        ["protocol_iterable.py"] = "protocol: StopIteration inside __next__",
        ["stdlib_math.py"] = "protocol: __trunc__/__index__ on objects",
        ["builtin_divmod.py"] = "float-edge: divmod with signed zero",
        ["builtin_float.py"] = "float-edge: OverflowError cases on floats",
        ["operator_div.py"] = "float-edge: big-int/float division at very high precision",
        ["operator_arithmetic.py"] = "float-edge / specific ValueError",
        ["builtin_round.py"] = "float-edge: round with ndigits and edge cases",
        ["builtin_chr.py"] = "stdlib-depth: chr()/exact error messages",
        ["builtin_ord.py"] = "stdlib-depth: ord(bytearray) and messages",
        ["builtin_bool.py"] = "stdlib-depth: fallacious __bool__ propagation",
        ["builtin_property.py"] = "stdlib-depth: property introspection (fget/fset)",
        ["builtin_list.py"] = "stdlib-depth: list details (extended slice assign)",
        ["builtin_super.py"] = "stdlib-depth: single-argument super(cls)",
        ["stdlib_struct.py"] = "stdlib-depth: struct error cases",
        ["stdlib_time.py"] = "stdlib-depth: struct_time / strftime details",
        ["stdlib_json.py"] = "stdlib-depth: json over a file-like with encoding",
        ["stdlib_string.py"] = "stdlib-depth: string.Template",
        ["stdlib_functools.py"] = "stdlib-depth: partial.__dict__",
        ["stdlib_collections_deque.py"] = "stdlib-depth: advanced deque",
        ["syntax_class.py"] = "stdlib-depth: class introspection (__module__ etc.)",
        ["syntax_try.py"] = "stdlib-depth: exception/traceback details",
        ["syntax_attr.py"] = "stdlib-depth: AttributeError in specific cases",
        ["syntax_assignment.py"] = "stdlib-depth: NameError on del/assignments",
        ["syntax_del.py"] = "stdlib-depth: UnboundLocalError after a local del",
        ["syntax_function2.py"] = "stdlib-depth: function introspection",
        ["syntax_fstring.py"] = "stdlib-depth: advanced f-strings",
        ["syntax_short_circuit_bool.py"] = "semantics: no double evaluation of __bool__ in if/while (compiler optimization)",
        ["name.py"] = "ext-file: import_name module not included",
        ["syntax_comprehension.py"] = "ext-file: itertools module not included",
    };

    public static IEnumerable<object[]> Supported()
        => AllSnippets().Where(n => !Xfail.ContainsKey(n)).Select(n => new object[] { n });

    public static IEnumerable<object[]> Unsupported()
        => AllSnippets().Where(Xfail.ContainsKey).Select(n => new object[] { n });

    private static IEnumerable<string> AllSnippets()
    {
        if (!Directory.Exists(SnippetsDir))
            yield break;
        foreach (var path in Directory.EnumerateFiles(SnippetsDir, "*.py").OrderBy(x => x))
        {
            string name = Path.GetFileName(path);
            if (name != "testutils.py")
                yield return name;
        }
    }

    /// <summary>Fully supported snippets: they must run without errors.</summary>
    [Theory]
    [MemberData(nameof(Supported))]
    public void Supported_snippet_runs_clean(string name)
    {
        var (ok, error) = RunSnippet(name);
        Assert.True(ok, $"{name} was supposed to pass but: {error}");
    }

    /// <summary>
    /// Out-of-scope-v1 snippets: they must still fail. If one passes, it should be promoted
    /// to Supported (by removing it from <see cref="Xfail"/>).
    /// </summary>
    [Theory]
    [MemberData(nameof(Unsupported))]
    public void Unsupported_snippet_still_fails(string name)
    {
        var (ok, _) = RunSnippet(name);
        Assert.False(ok,
            $"{name} now PASSES: remove it from Xfail and move it to Supported. (recorded reason: {Xfail[name]})");
    }

    private static (bool Ok, string Error) RunSnippet(string name)
    {
        string path = Path.Combine(SnippetsDir, name);
        var output = new StringWriter();
        var engine = new PyEngine(output);
        engine.Importer.SearchPaths.Add(SnippetsDir);
        try
        {
            engine.Run(File.ReadAllText(path), name);
            return (true, "");
        }
        catch (PyRaise ex)
        {
            return (false, $"{ex.Value.Class.Name}: {PyErr.FormatForClr(ex.Value)}");
        }
        catch (PySyntaxError ex)
        {
            return (false, $"SyntaxError: {ex.Message}");
        }
    }
}
