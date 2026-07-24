using System.Numerics;
using System.Security.Cryptography;
using PySharpLib.Runtime;

namespace PySharpLib.Modules;

public static class OsModule
{
    public static PyModule Create()
    {
        var m = new PyModule("os");
        var d = m.Dict;

        d["name"] = OperatingSystem.IsWindows() ? "nt" : "posix";
        d["sep"] = Path.DirectorySeparatorChar.ToString();
        d["linesep"] = Environment.NewLine;
        d["curdir"] = ".";
        d["pardir"] = "..";

        var environ = new PyDict();
        foreach (System.Collections.DictionaryEntry e in Environment.GetEnvironmentVariables())
            environ[(string)e.Key] = (string?)e.Value ?? "";
        d["environ"] = environ;

        d["getenv"] = new PyBuiltinFunction("getenv", (_, a, _) =>
        {
            var v = Environment.GetEnvironmentVariable((string)a[0]);
            return v is null ? (a.Length > 1 ? a[1] : PyNone.Instance) : v;
        });

        d["getcwd"] = new PyBuiltinFunction("getcwd", (_, _, _) => Directory.GetCurrentDirectory());
        d["urandom"] = new PyBuiltinFunction("urandom", (_, a, _) =>
            new PyBytes(RandomNumberGenerator.GetBytes((int)PyOps.AsBigInt(a[0], "n"))));
        d["getpid"] = new PyBuiltinFunction("getpid", (_, _, _) =>
            new BigInteger(Environment.ProcessId));
        d["listdir"] = new PyBuiltinFunction("listdir", (_, a, _) =>
        {
            string path = a.Length > 0 ? (string)a[0] : ".";
            return new PyList(Directory.EnumerateFileSystemEntries(path)
                .Select(p => (object)Path.GetFileName(p)));
        });
        d["makedirs"] = new PyBuiltinFunction("makedirs", (_, a, _) =>
        {
            Directory.CreateDirectory((string)a[0]);
            return PyNone.Instance;
        });
        d["remove"] = new PyBuiltinFunction("remove", (_, a, _) =>
        {
            File.Delete((string)a[0]);
            return PyNone.Instance;
        });

        // os.path
        var path = new PyModule("os.path");
        var pd = path.Dict;
        pd["join"] = new PyBuiltinFunction("join", (_, a, _) =>
            Path.Combine(a.Select(x => (string)x).ToArray()));
        pd["exists"] = new PyBuiltinFunction("exists", (_, a, _) =>
            File.Exists((string)a[0]) || Directory.Exists((string)a[0]));
        pd["isfile"] = new PyBuiltinFunction("isfile", (_, a, _) => File.Exists((string)a[0]));
        pd["isdir"] = new PyBuiltinFunction("isdir", (_, a, _) => Directory.Exists((string)a[0]));
        pd["basename"] = new PyBuiltinFunction("basename", (_, a, _) => Path.GetFileName((string)a[0]));
        pd["dirname"] = new PyBuiltinFunction("dirname", (_, a, _) =>
            Path.GetDirectoryName((string)a[0]) ?? "");
        pd["abspath"] = new PyBuiltinFunction("abspath", (_, a, _) => Path.GetFullPath((string)a[0]));
        pd["expanduser"] = new PyBuiltinFunction("expanduser", (_, a, _) =>
        {
            string p = (string)a[0];
            return p.StartsWith('~')
                ? Environment.GetFolderPath(Environment.SpecialFolder.UserProfile) + p[1..]
                : p;
        });
        pd["splitext"] = new PyBuiltinFunction("splitext", (_, a, _) =>
        {
            string p = (string)a[0];
            string ext = Path.GetExtension(p);
            return new PyTuple(new object[] { ext.Length > 0 ? p[..^ext.Length] : p, ext });
        });
        pd["getsize"] = new PyBuiltinFunction("getsize", (_, a, _) =>
            new BigInteger(new FileInfo((string)a[0]).Length));
        d["path"] = path;

        return m;
    }
}
