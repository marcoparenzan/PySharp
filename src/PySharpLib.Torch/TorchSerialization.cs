// Copyright (c) 2026 Marco Parenzan
//
// Licensed under the MIT License. See the LICENSE file in the project
// root for full license information.

using System.IO.Compression;
using System.Numerics;
using PySharpLib.Interpretation;
using PySharpLib.Modules;
using PySharpLib.Runtime;
using TorchSharp;
using static TorchSharp.torch;
using Tensor = TorchSharp.torch.Tensor;

namespace PySharpLib.Torch;

/// <summary>The raw bytes of a torch storage plus its element type.</summary>
internal sealed class StorageBox
{
    public byte[] Data { get; }
    public ScalarType DType { get; }
    public StorageBox(byte[] data, ScalarType dtype) { Data = data; DType = dtype; }
}

/// <summary>Reads torch's zip checkpoint format (<c>.pth</c> / <c>torch.save</c> with the default serializer): a pickle whose tensors
/// refer to raw storages stored next to it in the archive.</summary>
internal static class TorchSerialization
{
    private static readonly (string name, ScalarType t)[] StorageTypes =
    {
        ("FloatStorage", ScalarType.Float32), ("DoubleStorage", ScalarType.Float64), ("HalfStorage", ScalarType.Float16),
        ("BFloat16Storage", ScalarType.BFloat16), ("LongStorage", ScalarType.Int64), ("IntStorage", ScalarType.Int32),
        ("ShortStorage", ScalarType.Int16), ("CharStorage", ScalarType.Int8), ("ByteStorage", ScalarType.Byte), ("BoolStorage", ScalarType.Bool),
    };

    public static readonly Dictionary<string, PyClass> StorageClasses = StorageTypes.ToDictionary(
        s => s.name, s => new PyClass(s.name, new List<PyClass>()) { Dict = { ["__module__"] = "torch" } });

    private static ScalarType DTypeOf(PyClass cls) => StorageTypes.First(s => s.name == cls.Name).t;

    private static int ElementSize(ScalarType t) => t switch
    {
        ScalarType.Float64 or ScalarType.Int64 => 8,
        ScalarType.Float32 or ScalarType.Int32 => 4,
        ScalarType.Float16 or ScalarType.BFloat16 or ScalarType.Int16 => 2,
        _ => 1,
    };

    [ThreadStatic] private static bool _dryRun;

    public static PyModule UtilsModule()
    {
        var m = new PyModule("torch._utils");
        m.Dict["_rebuild_tensor_v2"] = Ops.Fn("_rebuild_tensor_v2", (i, a, kw) =>
        {
            if (_dryRun) return PyNone.Instance;
            var box = (StorageBox)((PyInstance)a[0]).Native!;
            long offset = TC.ToLong(a[1]);
            var size = TC.ToLongs(a[2]);
            var stride = TC.ToLongs(a[3]);
            bool requiresGrad = a.Length > 4 && a[4] is true;
            long numel = box.Data.Length / ElementSize(box.DType);
            var flat = torch.empty(new[] { numel }, box.DType);
            if (box.Data.Length > 0) box.Data.AsSpan().CopyTo(flat.bytes);
            var t = size.Length == 0 ? flat.narrow(0, offset, 1).reshape(Array.Empty<long>()) : flat.as_strided(size, stride, offset);
            t = t.clone();
            if (requiresGrad) t.requires_grad_(true);
            return TC.Wrap(t);
        });
        m.Dict["_rebuild_tensor"] = m.Dict["_rebuild_tensor_v2"];
        m.Dict["_rebuild_parameter"] = Ops.Fn("_rebuild_parameter", (i, a, kw) =>
        {
            if (_dryRun) return PyNone.Instance;
            var data = TC.Unwrap(a[0]);
            return TC.WrapParameter(data.detach().requires_grad_(a.Length > 1 ? a[1] is true : true));
        });
        return m;
    }

    /// <summary>torch.load of the legacy (pre-1.6, non-zip) format: three header pickles, the object pickle whose tensors point at
    /// storages by persistent id, a pickle listing the storage keys, then every storage's raw data (int64 element count + bytes).
    /// The object pickle is read twice: once to learn the storages and where the data starts, once to build the tensors.</summary>
    public static object LoadLegacy(Interp interp, string path)
    {
        var all = File.ReadAllBytes(path);
        int pos = 0;
        var magic = RealPickle.Load(interp, all, "utf-8", null, pos, out pos);
        if (magic is not BigInteger mg || mg != BigInteger.Parse("119547037146038801333356"))
            throw PyErr.RuntimeError("Invalid magic number; corrupt file?");
        RealPickle.Load(interp, all, "utf-8", null, pos, out pos); // protocol version
        RealPickle.Load(interp, all, "utf-8", null, pos, out pos); // sys_info
        int mainStart = pos;

        // pass 1: roots (class, element count) and views, no tensors built
        var roots = new Dictionary<string, (PyClass cls, long numel)>();
        object Pass1(object pid)
        {
            var t = (PyTuple)pid;
            string typename = t.Items[0] is PyBytes tb ? System.Text.Encoding.ASCII.GetString(tb.Data) : (string)t.Items[0];
            if (typename != "storage") return PyNone.Instance;
            var cls = (PyClass)t.Items[1];
            string rootKey = KeyOf(t.Items[2]);
            roots.TryAdd(rootKey, (cls, TC.ToLong(t.Items[4])));
            return new PyInstance(cls) { Native = new StorageBox(Array.Empty<byte>(), DTypeOf(cls)) };
        }
        int mainEnd;
        _dryRun = true;
        try { RealPickle.Load(interp, all, "utf-8", Pass1, mainStart, out mainEnd); }
        finally { _dryRun = false; }

        var keysObj = RealPickle.Load(interp, all, "utf-8", null, mainEnd, out int dataPos);
        var data = new Dictionary<string, byte[]>();
        foreach (var k in PyOps.Iterate(interp, keysObj))
        {
            string key = KeyOf(k);
            var (cls, _) = roots[key];
            long count = BitConverter.ToInt64(all, dataPos);
            dataPos += 8;
            long bytes = count * ElementSize(DTypeOf(cls));
            var buf = new byte[bytes];
            Buffer.BlockCopy(all, dataPos, buf, 0, (int)bytes);
            dataPos += (int)bytes;
            data[key] = buf;
        }

        // pass 2: the real thing
        var made = new Dictionary<string, PyInstance>();
        object Pass2(object pid)
        {
            var t = (PyTuple)pid;
            string typename = t.Items[0] is PyBytes tb ? System.Text.Encoding.ASCII.GetString(tb.Data) : (string)t.Items[0];
            if (typename != "storage") return PyNone.Instance;
            var cls = (PyClass)t.Items[1];
            string rootKey = KeyOf(t.Items[2]);
            var dtype = DTypeOf(cls);
            if (t.Items.Length > 5 && t.Items[5] is PyTuple view)
            {
                string viewKey = KeyOf(view.Items[0]);
                if (made.TryGetValue(viewKey, out var v)) return v;
                int esz = ElementSize(dtype);
                long off = TC.ToLong(view.Items[1]) * esz, len = TC.ToLong(view.Items[2]) * esz;
                var slice = new byte[len];
                Buffer.BlockCopy(data[rootKey], (int)off, slice, 0, (int)len);
                return made[viewKey] = new PyInstance(cls) { Native = new StorageBox(slice, dtype) };
            }
            if (made.TryGetValue(rootKey, out var done)) return done;
            return made[rootKey] = new PyInstance(cls) { Native = new StorageBox(data[rootKey], dtype) };
        }
        return RealPickle.Load(interp, all, "utf-8", Pass2, mainStart, out _);
    }

    private static string KeyOf(object o) => o switch
    {
        string s => s,
        PyBytes b => System.Text.Encoding.ASCII.GetString(b.Data),
        BigInteger bi => bi.ToString(),
        _ => PyOps.TypeName(o),
    };

    /// <summary>torch.load of a zip-format checkpoint.</summary>
    public static object LoadZip(Interp interp, string path)
    {
        using var zip = ZipFile.OpenRead(path);
        var pklEntry = zip.Entries.FirstOrDefault(e => e.FullName.EndsWith("data.pkl", StringComparison.Ordinal))
            ?? throw PyErr.RuntimeError("not a torch checkpoint (no data.pkl in the archive)");
        string prefix = pklEntry.FullName[..^"data.pkl".Length];
        byte[] pkl;
        using (var s = pklEntry.Open()) { var ms = new MemoryStream(); s.CopyTo(ms); pkl = ms.ToArray(); }
        var cache = new Dictionary<string, PyInstance>();
        object Persistent(object pid)
        {
            if (pid is PyTuple { Items.Length: >= 5 } t && t.Items[0] is string tag && tag == "storage" && t.Items[1] is PyClass cls)
            {
                string key = (string)t.Items[2];
                if (cache.TryGetValue(key, out var done)) return done;
                var entry = zip.GetEntry(prefix + "data/" + key) ?? throw PyErr.RuntimeError($"missing storage {key} in the checkpoint");
                using var s = entry.Open();
                var ms = new MemoryStream();
                s.CopyTo(ms);
                var inst = new PyInstance(cls) { Native = new StorageBox(ms.ToArray(), DTypeOf(cls)) };
                cache[key] = inst;
                return inst;
            }
            throw PyErr.NotImplementedError("unsupported persistent id in the checkpoint");
        }
        return RealPickle.Load(interp, pkl, "utf-8", Persistent);
    }
}
