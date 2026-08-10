// Copyright (c) 2026 Marco Parenzan
//
// Licensed under the MIT License. See the LICENSE file in the project
// root for full license information.

using System.IO;
using PySharpLib.Builtins;
using PySharpLib.Runtime;

namespace PySharpLib.Modules;

/// <summary>
/// pathlib: Path/PurePath backed by System.IO.Path string manipulation for the pure operations
/// (join, name/stem/suffix/parent, str/repr) and System.IO.File/Directory for the real filesystem
/// ones (exists/is_file/is_dir/mkdir/open). v1 scope — the common surface, not full API parity
/// (no globbing/symlink resolution edge cases). See FASTAPI_PLAN.md Phase 1.9 / ROADMAP.md
/// scenario 8, whose "pathlib" item this partially closes.
/// </summary>
public static class PathlibModule
{
    private const string ValueKey = "__path__";

    public static readonly PyClass PurePathClass = BuildPathClass("PurePath", new List<PyClass> { OsModule.PathLikeClass });
    public static readonly PyClass PathClass = BuildPathClass("Path", new List<PyClass> { PurePathClass });

    public static PyModule Create()
    {
        var m = new PyModule("pathlib");
        m.Dict["Path"] = PathClass;
        m.Dict["PurePath"] = PurePathClass;
        // Real pathlib picks a platform subclass at construction time; a single concrete class
        // covers what scripts actually need here (they just import Path).
        m.Dict["PosixPath"] = PathClass;
        m.Dict["WindowsPath"] = PathClass;
        m.Dict["PurePosixPath"] = PurePathClass;
        m.Dict["PureWindowsPath"] = PurePathClass;
        return m;
    }

    public static PyInstance Make(string path)
    {
        var inst = new PyInstance(PathClass);
        inst.Dict[ValueKey] = path;
        return inst;
    }

    private static string Str(object o) => o switch
    {
        PyInstance inst when inst.Class == PathClass || inst.Class == PurePathClass => (string)inst.Dict[ValueKey],
        string s => s,
        _ => throw PyErr.TypeError($"argument should be a str or a Path, not {PyOps.TypeName(o)}"),
    };

    private static string Value(object self) => (string)((PyInstance)self).Dict[ValueKey];

    private static PyClass BuildPathClass(string name, List<PyClass>? bases = null)
    {
        var cls = new PyClass(name, bases ?? new List<PyClass>());
        void Add(string n, BuiltinFn fn) => cls.Dict[n] = new PyBuiltinFunction($"{name}.{n}", fn);

        Add("__init__", (_, a, _) =>
        {
            var inst = (PyInstance)a[0];
            string joined = a.Length > 1
                ? Path.Combine(a.Skip(1).Select(Str).ToArray())
                : ".";
            inst.Dict[ValueKey] = NormalizeSeparators(joined);
            return PyNone.Instance;
        });

        Add("__str__", (_, a, _) => Value(a[0]));
        Add("__repr__", (_, a, _) => $"{name}('{Value(a[0])}')");
        Add("__fspath__", (_, a, _) => Value(a[0]));
        Add("__eq__", (_, a, _) => a[1] is PyInstance other
            && (other.Class == PathClass || other.Class == PurePathClass)
            && Value(a[0]) == Value(a[1]));
        Add("__hash__", (_, a, _) => (System.Numerics.BigInteger)Value(a[0]).GetHashCode());

        // Real pathlib orders paths by their parts tuple (lexicographic on each component), which
        // string comparison of the normalized path matches for every case reachable here (no mixed
        // separators once normalized). Needed by `sorted()` over a list of Path objects.
        Add("__lt__", (_, a, _) => string.CompareOrdinal(Value(a[0]), Str(a[1])) < 0);
        Add("__le__", (_, a, _) => string.CompareOrdinal(Value(a[0]), Str(a[1])) <= 0);
        Add("__gt__", (_, a, _) => string.CompareOrdinal(Value(a[0]), Str(a[1])) > 0);
        Add("__ge__", (_, a, _) => string.CompareOrdinal(Value(a[0]), Str(a[1])) >= 0);

        Add("__truediv__", (_, a, _) => Make(NormalizeSeparators(Path.Combine(Value(a[0]), Str(a[1])))));

        cls.Dict["name"] = MakeProperty(self => Path.GetFileName(Value(self)));
        cls.Dict["stem"] = MakeProperty(self => Path.GetFileNameWithoutExtension(Value(self)));
        cls.Dict["suffix"] = MakeProperty(self => Path.GetExtension(Value(self)));
        cls.Dict["parent"] = MakeProperty(self =>
        {
            var parent = Path.GetDirectoryName(Value(self));
            return Make(string.IsNullOrEmpty(parent) ? "." : NormalizeSeparators(parent));
        });
        cls.Dict["parts"] = MakeProperty(self =>
        {
            var full = Value(self);
            var pieces = full.Split('/', StringSplitOptions.RemoveEmptyEntries);
            return new PyTuple(pieces.Select(p => (object)p).ToArray());
        });

        Add("as_posix", (_, a, _) => Value(a[0]));
        Add("is_absolute", (_, a, _) => Path.IsPathRooted(Value(a[0])));
        Add("joinpath", (_, a, _) => Make(NormalizeSeparators(Path.Combine(new[] { Value(a[0]) }.Concat(a.Skip(1).Select(Str)).ToArray()))));
        Add("with_name", (_, a, _) =>
        {
            var parent = Path.GetDirectoryName(Value(a[0])) ?? "";
            return Make(NormalizeSeparators(Path.Combine(parent, (string)a[1])));
        });
        Add("with_suffix", (_, a, _) =>
        {
            var noExt = Path.Combine(Path.GetDirectoryName(Value(a[0])) ?? "", Path.GetFileNameWithoutExtension(Value(a[0])));
            return Make(NormalizeSeparators(noExt + (string)a[1]));
        });

        // Real filesystem operations (only meaningful for Path, but harmless to expose on
        // PurePath too — real pathlib doesn't, but nothing here relies on that distinction).
        Add("exists", (_, a, _) => File.Exists(Value(a[0])) || Directory.Exists(Value(a[0])));
        Add("is_file", (_, a, _) => File.Exists(Value(a[0])));
        Add("is_dir", (_, a, _) => Directory.Exists(Value(a[0])));
        Add("mkdir", (_, a, kwargs) =>
        {
            bool parents = kwargs is not null && kwargs.TryGetValue("parents", out var p) && p is true;
            bool existOk = kwargs is not null && kwargs.TryGetValue("exist_ok", out var e) && e is true;
            if (Directory.Exists(Value(a[0])) && !existOk)
                throw PyErr.OSError($"[Errno 17] File exists: '{Value(a[0])}'");
            if (!parents && !Directory.Exists(Path.GetDirectoryName(Value(a[0])) ?? "."))
                throw PyErr.OSError($"[Errno 2] No such file or directory: '{Value(a[0])}'");
            Directory.CreateDirectory(Value(a[0]));
            return PyNone.Instance;
        });
        Add("resolve", (_, a, _) => Make(NormalizeSeparators(Path.GetFullPath(Value(a[0])))));
        Add("absolute", (_, a, _) => Make(NormalizeSeparators(Path.GetFullPath(Value(a[0])))));
        // Real Path.expanduser(): a leading `~` (or `~/...`) is replaced with the current user's
        // home directory — real CPython's `os.path.expanduser` semantics, scoped to that common
        // case (no `~otheruser/...` support; nothing reachable needs another user's home). Found
        // via real httpx's own `_utils.py` (`NetRCInfo`: `Path("~/.netrc").expanduser()`).
        Add("expanduser", (_, a, _) =>
        {
            string p = Value(a[0]);
            if (p != "~" && !p.StartsWith("~/", StringComparison.Ordinal) && !p.StartsWith(@"~\", StringComparison.Ordinal))
                return Make(p);
            string home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            string rest = p.Length > 1 ? p[2..] : "";
            return Make(NormalizeSeparators(rest.Length > 0 ? Path.Combine(home, rest) : home));
        });
        Add("read_text", (_, a, _) => File.ReadAllText(Value(a[0])));
        Add("write_text", (_, a, _) => { File.WriteAllText(Value(a[0]), (string)a[1]); return new System.Numerics.BigInteger(((string)a[1]).Length); });
        Add("open", (interp, a, kwargs) =>
            FileObject.Open(interp, new object[] { Value(a[0]) }.Concat(a.Skip(1)).ToArray(), kwargs));

        // Real recursive/non-recursive globbing, reusing glob.iglob()'s own real segment-by-segment
        // filesystem walk (GlobModule.Iglob) rather than duplicating it. rglob("*.py") == real
        // CPython's `glob(f"**/{pattern}", recursive=True)`; glob() only walks recursively when the
        // pattern itself contains "**" (real pathlib semantics, distinct from the glob module's own
        // opt-in `recursive=` flag).
        Add("glob", (_, a, _) =>
        {
            string pattern = (string)a[1];
            bool recursive = pattern.Contains("**");
            return new PyList(GlobModule.Iglob(Path.Combine(Value(a[0]), pattern), recursive).Select(p => (object)Make(NormalizeSeparators(p))));
        });
        Add("rglob", (_, a, _) =>
        {
            string pattern = (string)a[1];
            return new PyList(GlobModule.Iglob(Path.Combine(Value(a[0]), "**", pattern), true).Select(p => (object)Make(NormalizeSeparators(p))));
        });

        // Real directory listing — one Path per real entry directly inside this directory (not
        // recursive; matches real CPython's Path.iterdir()).
        Add("iterdir", (_, a, _) =>
        {
            string dir = Value(a[0]);
            return new PyList(Directory.EnumerateFileSystemEntries(dir).Select(p => (object)Make(NormalizeSeparators(p))));
        });

        // Real Path.relative_to(): the portion of this path after `other`, computed purely from the
        // path strings (no filesystem access), raising a real ValueError when this path doesn't
        // actually start with `other` — matching real CPython.
        Add("relative_to", (_, a, _) =>
        {
            string self = Value(a[0]);
            string other = Str(a[1]);
            string selfFull = NormalizeSeparators(Path.GetFullPath(self));
            string otherFull = NormalizeSeparators(Path.GetFullPath(other));
            if (selfFull == otherFull)
                return Make(".");
            string prefix = otherFull.EndsWith("/", StringComparison.Ordinal) ? otherFull : otherFull + "/";
            if (!selfFull.StartsWith(prefix, StringComparison.Ordinal))
                throw PyErr.ValueError($"'{self}' is not in the subpath of '{other}'");
            return Make(selfFull[prefix.Length..]);
        });

        return cls;
    }

    private static string NormalizeSeparators(string path) => path.Replace('\\', '/');

    private static PyProperty MakeProperty(Func<object, object> getter)
        => new() { Getter = new PyBuiltinFunction("path.<property>", (_, a, _) => getter(a[0])) };
}
