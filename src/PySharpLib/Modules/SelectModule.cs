using System.Net.Sockets;
using PySharpLib.Interpretation;
using PySharpLib.Runtime;

namespace PySharpLib.Modules;

/// <summary>select.select on Socket.Select, with support for SSLSocket (pending buffer).</summary>
public static class SelectModule
{
    public static PyModule Create()
    {
        var m = new PyModule("select");
        m.Dict["select"] = new PyBuiltinFunction("select", (interp, a, _) => DoSelect(interp, a));
        m.Dict["error"] = PyErr.OSErrorClass;
        return m;
    }

    private static object DoSelect(Interp interp, object[] a)
    {
        var rlist = PyOps.Iterate(interp, a[0]).ToList();
        var wlist = a.Length > 1 ? PyOps.Iterate(interp, a[1]).ToList() : new List<object>();
        var xlist = a.Length > 2 ? PyOps.Iterate(interp, a[2]).ToList() : new List<object>();
        double? timeout = a.Length > 3 && a[3] is not PyNone ? PyOps.AsDouble(a[3]) : null;

        var readReady = new List<object>();
        var writeReady = new List<object>();

        // SSLSocket with already-buffered data: ready immediately
        var pendingReady = rlist.Where(o => GetSsl(o) is { Readable: true }).ToList();
        if (pendingReady.Count > 0)
            return Result(pendingReady, new List<object>(), new List<object>());

        var readSockets = rlist.Select(GetRawSocket).ToList();
        var writeSockets = wlist.Select(GetRawSocket).ToList();

        var checkRead = readSockets.Where(s => s is not null).Cast<Socket>().ToList();
        var checkWrite = writeSockets.Where(s => s is not null).Cast<Socket>().ToList();
        var checkError = new List<Socket>();

        if (checkRead.Count == 0 && checkWrite.Count == 0)
        {
            if (timeout is double t0 and > 0)
                Thread.Sleep(TimeSpan.FromSeconds(t0));
            return Result(readReady, writeReady, new List<object>());
        }

        int microseconds = timeout is double t ? (int)(t * 1_000_000) : -1;
        try
        {
            Socket.Select(checkRead, checkWrite, checkError, microseconds);
        }
        catch (SocketException ex)
        {
            throw SocketModule.Translate(ex);
        }
        catch (ObjectDisposedException)
        {
            throw PyErr.OSError("Bad file descriptor (socket closed)");
        }

        for (int i = 0; i < rlist.Count; i++)
        {
            if (readSockets[i] is Socket s && checkRead.Contains(s))
                readReady.Add(rlist[i]);
        }
        for (int i = 0; i < wlist.Count; i++)
        {
            if (writeSockets[i] is Socket s && checkWrite.Contains(s))
                writeReady.Add(wlist[i]);
        }
        return Result(readReady, writeReady, new List<object>());
    }

    private static PyTuple Result(List<object> r, List<object> w, List<object> x)
        => new(new object[] { new PyList(r), new PyList(w), new PyList(x) });

    private static SslWrap? GetSsl(object o)
        => o is PyInstance inst && inst.Dict.TryGet(SslModule.WrapKey, out var v) ? (SslWrap)v : null;

    private static Socket? GetRawSocket(object o)
    {
        if (o is not PyInstance inst)
            return null;
        if (inst.Dict.TryGet(SslModule.WrapKey, out var ssl))
            return ((SslWrap)ssl).Underlying.Socket;
        if (inst.Dict.TryGet(SocketModule.WrapKey, out var sock))
            return ((SockWrap)sock).Socket;
        return null;
    }
}
