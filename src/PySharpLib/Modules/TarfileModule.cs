// Copyright (c) 2026 Marco Parenzan
//
// Licensed under the MIT License. See the LICENSE file in the project
// root for full license information.

using System.Formats.Tar;
using System.IO.Compression;
using PySharpLib.Runtime;

namespace PySharpLib.Modules;

/// <summary>tarfile: <c>tarfile.open</c> (read: plain, gzip, bzip2-less) with <c>extractall</c>/<c>extract</c>/<c>getnames</c>/
/// <c>getmembers</c>/<c>extractfile</c>, and a simple write mode (<c>add</c>), over System.Formats.Tar.</summary>
public static class TarfileModule
{
    public static readonly PyClass TarErrorClass = new("TarError", new List<PyClass> { PyErr.Exception });
    public static readonly PyClass ReadErrorClass = new("ReadError", new List<PyClass> { TarErrorClass });
    private static readonly Lazy<object> BytesIo = new(() => IoModule.Create().Dict["BytesIO"]);

    private sealed class TarState
    {
        public string Path = "";
        public string Mode = "r";
        public bool Gzip;
        public List<(string Name, byte[] Data, bool IsDir, long Size, int Mode)> Entries = new();
        public List<(string Name, string Source)> ToAdd = new();
    }

    public static PyModule Create()
    {
        var m = new PyModule("tarfile");
        m.Dict["TarError"] = TarErrorClass;
        m.Dict["ReadError"] = ReadErrorClass;
        var tarInfo = new PyClass("TarInfo", new List<PyClass>());
        m.Dict["TarInfo"] = tarInfo;
        var cls = BuildTarFile(tarInfo);
        m.Dict["TarFile"] = cls;
        m.Dict["open"] = new PyBuiltinFunction("open", (interp, a, kw) =>
        {
            var inst = new PyInstance(cls);
            ((PyBuiltinFunction)cls.Dict["__init__"]).Fn(interp, new object[] { inst }.Concat(a).ToArray(), kw);
            return inst;
        });
        m.Dict["is_tarfile"] = new PyBuiltinFunction("is_tarfile", (interp, a, _) =>
        {
            try { using var s = File.OpenRead(OsModule.PathArg(interp, a[0])); return Probe(s) is not null; }
            catch (Exception) { return false; }
        });
        return m;
    }

    private static Stream? Probe(Stream s)
    {
        var head = new byte[2];
        int n = s.Read(head, 0, 2);
        s.Position = 0;
        return n == 2 ? s : null;
    }

    private static PyInstance Member(PyClass tarInfo, (string Name, byte[] Data, bool IsDir, long Size, int Mode) e)
    {
        var mi = new PyInstance(tarInfo);
        mi.Dict["name"] = e.Name;
        mi.Dict["size"] = new System.Numerics.BigInteger(e.Size);
        mi.Dict["mode"] = new System.Numerics.BigInteger(e.Mode);
        mi.Dict["type"] = e.IsDir ? "5" : "0";
        mi.Dict["isdir"] = new PyBuiltinFunction("isdir", (_, _, _) => e.IsDir);
        mi.Dict["isfile"] = new PyBuiltinFunction("isfile", (_, _, _) => !e.IsDir);
        mi.Dict["isreg"] = mi.Dict["isfile"];
        return mi;
    }

    private static PyClass BuildTarFile(PyClass tarInfo)
    {
        var cls = new PyClass("TarFile", new List<PyClass>());
        void Add(string name, BuiltinFn fn) => cls.Dict[name] = new PyBuiltinFunction("TarFile." + name, fn);
        TarState St(object self) => (TarState)((PyInstance)self).Native!;

        Add("__init__", (interp, a, kw) =>
        {
            var inst = (PyInstance)a[0];
            var st = new TarState();
            object? nameObj = a.Length > 1 ? a[1] : kw is not null && kw.TryGetValue("name", out var n0) ? n0 : null;
            string mode = a.Length > 2 && a[2] is string ms ? ms : kw is not null && kw.TryGetValue("mode", out var mo) && mo is string mos ? mos : "r";
            object? fileobj = kw is not null && kw.TryGetValue("fileobj", out var fo) ? fo : null;
            st.Mode = mode;
            st.Gzip = mode.Contains("gz") || mode.EndsWith(":*") && false;
            string baseMode = mode.Split(':')[0];
            if (nameObj is not null and not PyNone) st.Path = OsModule.PathArg(interp, nameObj);
            if (baseMode == "r")
            {
                if (!File.Exists(st.Path)) throw PyErr.Raise(PyErr.FileNotFoundErrorClass, $"[Errno 2] No such file or directory: '{st.Path}'");
                using var fs = File.OpenRead(st.Path);
                Stream src = fs;
                var magic = new byte[2];
                int got = fs.Read(magic, 0, 2);
                fs.Position = 0;
                if (got == 2 && magic[0] == 0x1f && magic[1] == 0x8b) src = new GZipStream(fs, CompressionMode.Decompress);
                try
                {
                    using var reader = new TarReader(src);
                    TarEntry? e;
                    while ((e = reader.GetNextEntry()) is not null)
                    {
                        if (e.EntryType is TarEntryType.Directory)
                        {
                            st.Entries.Add((e.Name.TrimEnd('/'), Array.Empty<byte>(), true, 0, (int)e.Mode));
                            continue;
                        }
                        if (e.EntryType is not (TarEntryType.RegularFile or TarEntryType.V7RegularFile)) continue;
                        using var ms2 = new MemoryStream();
                        e.DataStream?.CopyTo(ms2);
                        var data = ms2.ToArray();
                        st.Entries.Add((e.Name, data, false, data.Length, (int)e.Mode));
                    }
                }
                catch (InvalidDataException ex) { throw new PyRaise(PyErr.MakeInstance(ReadErrorClass, ex.Message)); }
            }
            inst.Native = st;
            return PyNone.Instance;
        });
        Add("__enter__", (_, a, _) => a[0]);
        Add("__exit__", (interp, a, _) => { Close(interp, a[0]); return false; });
        Add("close", (interp, a, _) => { Close(interp, a[0]); return PyNone.Instance; });
        Add("getnames", (_, a, _) => new PyList(St(a[0]).Entries.Select(e => (object)e.Name)));
        Add("getmembers", (_, a, _) => new PyList(St(a[0]).Entries.Select(e => (object)Member(tarInfo, e))));
        Add("getmember", (_, a, _) =>
        {
            var e = St(a[0]).Entries.FirstOrDefault(x => x.Name == (string)a[1]);
            if (e.Name is null) throw PyErr.KeyError($"filename '{a[1]}' not found");
            return Member(tarInfo, e);
        });
        Add("__iter__", (_, a, _) => new PyIterator(St(a[0]).Entries.Select(e => (object)Member(tarInfo, e)).GetEnumerator()));
        Add("extractfile", (interp, a, _) =>
        {
            string name = a[1] is PyInstance mi && mi.Dict.TryGet("name", out var nm) ? (string)nm : (string)a[1];
            var e = St(a[0]).Entries.FirstOrDefault(x => x.Name == name);
            if (e.Name is null) throw PyErr.KeyError($"filename '{name}' not found");
            if (e.IsDir) return PyNone.Instance;
            return interp.Call(BytesIo.Value, new object[] { new PyBytes(e.Data) }, null);
        });
        BuiltinFn extract = (interp, a, kw) =>
        {
            var st = St(a[0]);
            string dest = a.Length > 1 && a[1] is not PyNone && !(a[0] == a[1]) ? OsModule.PathArg(interp, a[1]) : ".";
            if (kw is not null && kw.TryGetValue("path", out var pth) && pth is not PyNone) dest = OsModule.PathArg(interp, pth);
            Directory.CreateDirectory(dest);
            string full = Path.GetFullPath(dest);
            foreach (var e in st.Entries)
            {
                string target = Path.GetFullPath(Path.Combine(full, e.Name));
                if (!target.StartsWith(full, StringComparison.Ordinal)) throw new PyRaise(PyErr.MakeInstance(TarErrorClass, $"{e.Name} would be extracted outside the target directory"));
                if (e.IsDir) { Directory.CreateDirectory(target); continue; }
                Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                File.WriteAllBytes(target, e.Data);
            }
            return PyNone.Instance;
        };
        Add("extractall", extract);
        Add("extract", (interp, a, kw) =>
        {
            var st = St(a[0]);
            string name = a[1] is PyInstance mi && mi.Dict.TryGet("name", out var nm) ? (string)nm : (string)a[1];
            string dest = a.Length > 2 ? OsModule.PathArg(interp, a[2]) : kw is not null && kw.TryGetValue("path", out var p2) ? OsModule.PathArg(interp, p2) : ".";
            var e = st.Entries.FirstOrDefault(x => x.Name == name);
            if (e.Name is null) throw PyErr.KeyError($"filename '{name}' not found");
            string target = Path.Combine(dest, e.Name);
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(target))!);
            File.WriteAllBytes(target, e.Data);
            return PyNone.Instance;
        });
        Add("add", (interp, a, kw) =>
        {
            var st = St(a[0]);
            string src = OsModule.PathArg(interp, a[1]);
            string arc = a.Length > 2 && a[2] is string s ? s : kw is not null && kw.TryGetValue("arcname", out var an) && an is string ans ? ans : Path.GetFileName(src.TrimEnd('/', '\\'));
            st.ToAdd.Add((arc, src));
            return PyNone.Instance;
        });
        return cls;
    }

    private static void Close(Interpretation.Interp interp, object self)
    {
        var st = (TarState)((PyInstance)self).Native!;
        if (!st.Mode.StartsWith("w") || st.ToAdd.Count == 0) { st.ToAdd.Clear(); return; }
        using var fs = File.Create(st.Path);
        Stream outStream = st.Gzip ? new GZipStream(fs, CompressionLevel.Optimal) : fs;
        using (var writer = new TarWriter(outStream, TarEntryFormat.Pax, leaveOpen: false))
        {
            foreach (var (name, src) in st.ToAdd)
            {
                if (Directory.Exists(src))
                {
                    foreach (var f in Directory.EnumerateFiles(src, "*", SearchOption.AllDirectories))
                        writer.WriteEntry(f, name + "/" + Path.GetRelativePath(src, f).Replace('\\', '/'));
                }
                else writer.WriteEntry(src, name);
            }
        }
        st.ToAdd.Clear();
    }

}
