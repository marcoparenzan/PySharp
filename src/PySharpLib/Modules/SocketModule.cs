// Copyright (c) 2026 Marco Parenzan
//
// Licensed under the MIT License. See the LICENSE file in the project
// root for full license information.

using System.Net;
using System.Net.Sockets;
using System.Numerics;
using PySharpLib.Interpretation;
using PySharpLib.Runtime;

namespace PySharpLib.Modules;

/// <summary>C# wrapper around a .NET Socket, shared by socket/ssl/select.</summary>
public sealed class SockWrap
{
    public required Socket Socket { get; init; }
    /// <summary>Timeout in secondi: null = blocking, 0 = non-blocking, >0 = timeout.</summary>
    public double? Timeout { get; set; }
    public bool NonBlocking => Timeout == 0;
    public bool Closed { get; set; }
}

/// <summary>socket: TCP client/server on System.Net.Sockets with Python semantics (blocking/non-blocking/timeout).</summary>
public static class SocketModule
{
    public const string WrapKey = "__socket__";

    public static readonly PyClass TimeoutClass = new("timeout", new List<PyClass> { PyErr.OSErrorClass });
    public static readonly PyClass GaiErrorClass = new("gaierror", new List<PyClass> { PyErr.OSErrorClass });
    public static readonly PyClass SocketClass = BuildSocketClass();

    private static double? DefaultTimeout;

    public static PyModule Create(Interp interp)
    {
        var m = new PyModule("socket");
        var d = m.Dict;

        // Real IntEnum classes (built via real parsed Python source, the same trick used for other
        // stdlib IntEnums in this project — see SignalModule) — not just the bare int constants
        // below. Found via anyio's real `from socket import AddressFamily` (abc/_sockets.py), itself
        // a real dependency of starlette. socket.SocketKind added alongside for the same reason,
        // matching real CPython's shape (AF_INET/SOCK_STREAM etc. are literally enum members there,
        // even though PySharp's own plain-int constants below stay as-is — real IntEnum values
        // compare equal to plain ints, so nothing here needs to change to stay consistent). See
        // FASTAPI_PLAN.md.
        interp.RunModule(
            Parsing.Parser.Parse(
                "from enum import IntEnum\n"
                + "class AddressFamily(IntEnum):\n"
                + "    AF_UNSPEC = 0\n"
                + "    AF_UNIX = 1\n"
                + "    AF_INET = 2\n"
                + "    AF_INET6 = 10\n"
                + "class SocketKind(IntEnum):\n"
                + "    SOCK_STREAM = 1\n"
                + "    SOCK_DGRAM = 2\n"),
            m);

        d["AF_INET"] = new BigInteger(2);
        d["AF_INET6"] = new BigInteger(10);
        d["AF_UNSPEC"] = new BigInteger(0);
        d["SOCK_STREAM"] = new BigInteger(1);
        d["SOCK_DGRAM"] = new BigInteger(2);
        d["SOL_SOCKET"] = new BigInteger(1);
        d["SO_REUSEADDR"] = new BigInteger(2);
        d["SO_KEEPALIVE"] = new BigInteger(9);
        d["IPPROTO_IP"] = new BigInteger(0);
        d["IPPROTO_TCP"] = new BigInteger(6);
        d["IPPROTO_UDP"] = new BigInteger(17);
        d["TCP_NODELAY"] = new BigInteger(1);
        d["SO_SNDBUF"] = new BigInteger(7);
        d["SO_RCVBUF"] = new BigInteger(8);
        d["SO_ERROR"] = new BigInteger(4);
        d["SOMAXCONN"] = new BigInteger(128);
        d["SHUT_RD"] = new BigInteger(0);
        d["SHUT_WR"] = new BigInteger(1);
        d["SHUT_RDWR"] = new BigInteger(2);
        d["has_ipv6"] = true;

        d["error"] = PyErr.OSErrorClass;
        d["timeout"] = TimeoutClass;
        d["gaierror"] = GaiErrorClass;
        d["socket"] = SocketClass;

        d["getdefaulttimeout"] = new PyBuiltinFunction("getdefaulttimeout", (_, _, _) =>
            DefaultTimeout is double dt ? dt : PyNone.Instance);
        d["setdefaulttimeout"] = new PyBuiltinFunction("setdefaulttimeout", (_, a, _) =>
        {
            DefaultTimeout = a.Length > 0 && a[0] is not PyNone ? PyOps.AsDouble(a[0]) : null;
            return PyNone.Instance;
        });

        d["create_connection"] = new PyBuiltinFunction("create_connection", (interp, a, kwargs) =>
        {
            var addr = (PyTuple)a[0];
            string host = (string)addr.Items[0];
            int port = (int)PyOps.AsBigInt(addr.Items[1], "port");
            double? timeout = null;
            if (a.Length > 1 && a[1] is not PyNone)
                timeout = PyOps.AsDouble(a[1]);
            else if (kwargs is not null && kwargs.TryGetValue("timeout", out var to) && to is not PyNone)
                timeout = PyOps.AsDouble(to);

            var sock = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp)
                { NoDelay = true };
            try
            {
                if (timeout is double t and > 0)
                {
                    var task = sock.ConnectAsync(host, port);
                    if (!task.Wait(TimeSpan.FromSeconds(t)))
                    {
                        sock.Dispose();
                        throw new PyRaise(PyErr.MakeInstance(TimeoutClass, "timed out"));
                    }
                }
                else
                {
                    sock.Connect(host, port);
                }
            }
            catch (SocketException ex)
            {
                sock.Dispose();
                throw Translate(ex);
            }
            catch (AggregateException ex) when (ex.InnerException is SocketException se)
            {
                sock.Dispose();
                throw Translate(se);
            }

            var inst = new PyInstance(SocketClass);
            inst.Dict[WrapKey] = new SockWrap { Socket = sock, Timeout = timeout };
            return inst;
        });

        d["getaddrinfo"] = new PyBuiltinFunction("getaddrinfo", (_, a, kwargs) =>
        {
            // Real CPython signature: getaddrinfo(host, port, family=0, type=0, proto=0, flags=0)
            // — found via real pika's own `selector_ioloop_adapter.py`, which calls this entirely
            // by keyword (`socket.getaddrinfo(host=..., port=..., family=..., ...)`), a shape the
            // previous positional-only `a[0]`/`a[1]` reads never handled.
            string host = a.Length > 0 ? (string)a[0]
                : kwargs is not null && kwargs.TryGetValue("host", out var h) ? (string)h
                : throw PyErr.TypeError("getaddrinfo() missing required argument: 'host'");
            object port = a.Length > 1 ? a[1]
                : kwargs is not null && kwargs.TryGetValue("port", out var p) ? p : PyNone.Instance;
            IPAddress[] addresses;
            try
            {
                addresses = Dns.GetHostAddresses(host);
            }
            catch (SocketException)
            {
                throw new PyRaise(PyErr.MakeInstance(GaiErrorClass, $"getaddrinfo failed for {host}"));
            }
            var results = new List<object>();
            foreach (var address in addresses.Where(x => x.AddressFamily == AddressFamily.InterNetwork))
            {
                results.Add(new PyTuple(new object[]
                {
                    new BigInteger(2), new BigInteger(1), new BigInteger(6), "",
                    new PyTuple(new[] { (object)address.ToString(), port }),
                }));
            }
            return new PyList(results);
        });

        d["gethostname"] = new PyBuiltinFunction("gethostname", (_, _, _) => Dns.GetHostName());
        d["inet_aton"] = new PyBuiltinFunction("inet_aton", (_, a, _) =>
            new PyBytes(IPAddress.Parse((string)a[0]).GetAddressBytes()));

        return m;
    }

    public static SockWrap Wrap(object self)
    {
        var inst = (PyInstance)self;
        if (!inst.Dict.TryGet(WrapKey, out var w))
            throw PyErr.OSError("socket not initialized");
        return (SockWrap)w;
    }

    // fd (raw OS handle, what fileno() returns) -> Socket, so the event loop's add_reader/
    // add_writer (which only ever get the bare int fd, per the asyncio API) can resolve it back.
    // Populated lazily by fileno() itself, since every caller of add_reader/add_writer always
    // calls fileno() to get the fd in the first place.
    private static readonly Dictionary<long, Socket> HandleRegistry = new();
    private static readonly object HandleRegistryLock = new();

    public static void RegisterHandle(Socket sock)
    {
        lock (HandleRegistryLock)
            HandleRegistry[(long)sock.Handle] = sock;
    }

    public static bool TryResolveHandle(long handle, out Socket? sock)
    {
        lock (HandleRegistryLock)
            return HandleRegistry.TryGetValue(handle, out sock);
    }

    /// <summary>Converte SocketException in eccezione Python appropriata. Uses `PyErr.MakeOSError`
    /// (not the generic `MakeInstance`) so the resulting exception carries real `.errno`/
    /// `.strerror` attributes, not just `.args` — found via real pika's own
    /// `io_services_utils.py` reading `caught_exc.errno` off a real `BlockingIOError` from a
    /// non-blocking `connect()` in progress.</summary>
    public static PyRaise Translate(SocketException ex) => ex.SocketErrorCode switch
    {
        SocketError.WouldBlock or SocketError.InProgress or SocketError.AlreadyInProgress
            => new PyRaise(PyErr.MakeOSError(PyErr.BlockingIOErrorClass,
                11, "Resource temporarily unavailable")),
        SocketError.TimedOut => new PyRaise(PyErr.MakeInstance(TimeoutClass, "timed out")),
        SocketError.ConnectionRefused => new PyRaise(PyErr.MakeOSError(
            PyErr.ConnectionRefusedErrorClass, 111, "Connection refused")),
        SocketError.ConnectionReset => new PyRaise(PyErr.MakeOSError(
            PyErr.ConnectionResetErrorClass, 104, "Connection reset by peer")),
        SocketError.ConnectionAborted => new PyRaise(PyErr.MakeOSError(
            PyErr.ConnectionAbortedErrorClass, 103, "Connection aborted")),
        SocketError.HostNotFound or SocketError.NoData => new PyRaise(PyErr.MakeInstance(
            GaiErrorClass, "Name or service not known")),
        _ => new PyRaise(PyErr.MakeOSError(PyErr.OSErrorClass,
            (int)ex.SocketErrorCode, ex.Message)),
    };

    private static PyClass BuildSocketClass()
    {
        var cls = new PyClass("socket", new List<PyClass>());
        void Add(string name, BuiltinFn fn) => cls.Dict[name] = new PyBuiltinFunction($"socket.{name}", fn);

        Add("__init__", (_, a, kwargs) =>
        {
            var inst = (PyInstance)a[0];
            int family = a.Length > 1 ? (int)PyOps.AsBigInt(a[1], "family")
                : kwargs is not null && kwargs.TryGetValue("family", out var f) ? (int)PyOps.AsBigInt(f, "family")
                : 2; // AF_INET
            int type = a.Length > 2 ? (int)PyOps.AsBigInt(a[2], "type")
                : kwargs is not null && kwargs.TryGetValue("type", out var t) ? (int)PyOps.AsBigInt(t, "type")
                : 1; // SOCK_STREAM
            var af = family == 10 ? AddressFamily.InterNetworkV6 : AddressFamily.InterNetwork;
            var (sockType, proto) = type == 2
                ? (SocketType.Dgram, ProtocolType.Udp)
                : (SocketType.Stream, ProtocolType.Tcp);
            inst.Dict[WrapKey] = new SockWrap { Socket = new Socket(af, sockType, proto) };
            return PyNone.Instance;
        });

        Add("connect", (_, a, _) =>
        {
            var w = Wrap(a[0]);
            var addr = (PyTuple)a[1];
            string host = (string)addr.Items[0];
            int port = (int)PyOps.AsBigInt(addr.Items[1], "port");
            try
            {
                if (w.NonBlocking)
                {
                    w.Socket.Blocking = false;
                    w.Socket.Connect(host, port);
                }
                else if (w.Timeout is double t and > 0)
                {
                    var task = w.Socket.ConnectAsync(host, port);
                    if (!task.Wait(TimeSpan.FromSeconds(t)))
                        throw new PyRaise(PyErr.MakeInstance(TimeoutClass, "timed out"));
                }
                else
                {
                    w.Socket.Connect(host, port);
                }
            }
            catch (SocketException ex)
            {
                throw Translate(ex);
            }
            catch (AggregateException ex) when (ex.InnerException is SocketException se)
            {
                throw Translate(se);
            }
            return PyNone.Instance;
        });

        Add("connect_ex", (interp, a, _) =>
        {
            try
            {
                interp.CallMethod(a[0], "connect", new[] { a[1] });
                return BigInteger.Zero;
            }
            catch (PyRaise ex)
            {
                if (ex.Value.Dict.TryGet("args", out var args) && args is PyTuple t
                    && t.Items.Length > 0 && t.Items[0] is BigInteger code)
                    return code;
                return new BigInteger(111);
            }
        });

        Add("send", (_, a, _) =>
        {
            var w = Wrap(a[0]);
            var data = AsBytes(a[1]);
            try
            {
                return new BigInteger(w.Socket.Send(data));
            }
            catch (SocketException ex)
            {
                throw Translate(ex);
            }
        });

        Add("sendall", (_, a, _) =>
        {
            var w = Wrap(a[0]);
            var data = AsBytes(a[1]);
            int sent = 0;
            while (sent < data.Length)
            {
                try
                {
                    sent += w.Socket.Send(data, sent, data.Length - sent, SocketFlags.None);
                }
                catch (SocketException ex) when (ex.SocketErrorCode == SocketError.WouldBlock)
                {
                    w.Socket.Poll(-1, SelectMode.SelectWrite);
                }
                catch (SocketException ex)
                {
                    throw Translate(ex);
                }
            }
            return PyNone.Instance;
        });

        Add("recv", (_, a, _) =>
        {
            var w = Wrap(a[0]);
            int size = (int)PyOps.AsBigInt(a[1], "bufsize");
            var buffer = new byte[size];
            try
            {
                if (!w.NonBlocking && w.Timeout is double t and > 0)
                {
                    if (!w.Socket.Poll((int)(t * 1_000_000), SelectMode.SelectRead))
                        throw new PyRaise(PyErr.MakeInstance(TimeoutClass, "timed out"));
                }
                int n = w.Socket.Receive(buffer);
                return new PyBytes(buffer[..n]);
            }
            catch (SocketException ex)
            {
                throw Translate(ex);
            }
        });

        Add("bind", (_, a, _) =>
        {
            var w = Wrap(a[0]);
            var addr = (PyTuple)a[1];
            string host = (string)addr.Items[0];
            int port = (int)PyOps.AsBigInt(addr.Items[1], "port");
            try
            {
                w.Socket.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
                w.Socket.Bind(new IPEndPoint(IPAddress.Parse(host), port));
            }
            catch (SocketException ex)
            {
                throw Translate(ex);
            }
            catch (FormatException)
            {
                throw new PyRaise(PyErr.MakeInstance(GaiErrorClass, $"'{host}' is not a valid address"));
            }
            return PyNone.Instance;
        });

        Add("listen", (_, a, _) =>
        {
            Wrap(a[0]).Socket.Listen(a.Length > 1 ? (int)PyOps.AsBigInt(a[1], "backlog") : 5);
            return PyNone.Instance;
        });

        Add("accept", (_, a, _) =>
        {
            var w = Wrap(a[0]);
            try
            {
                var client = w.Socket.Accept();
                var inst = new PyInstance(SocketClass);
                inst.Dict[WrapKey] = new SockWrap { Socket = client };
                var ep = (IPEndPoint)client.RemoteEndPoint!;
                return new PyTuple(new object[]
                {
                    inst,
                    new PyTuple(new object[] { ep.Address.ToString(), new BigInteger(ep.Port) }),
                });
            }
            catch (SocketException ex)
            {
                throw Translate(ex);
            }
        });

        Add("getsockname", (_, a, _) =>
        {
            var ep = (IPEndPoint)Wrap(a[0]).Socket.LocalEndPoint!;
            return new PyTuple(new object[] { ep.Address.ToString(), new BigInteger(ep.Port) });
        });

        Add("getpeername", (_, a, _) =>
        {
            var ep = (IPEndPoint)Wrap(a[0]).Socket.RemoteEndPoint!;
            return new PyTuple(new object[] { ep.Address.ToString(), new BigInteger(ep.Port) });
        });

        Add("setsockopt", (_, _, _) => PyNone.Instance);

        // Real getsockopt(SOL_SOCKET, SO_ERROR): the classic post-nonblocking-connect check (once
        // select()/poll() reports the fd writable, this is how real code learns whether connect()
        // actually succeeded or failed) — 0 means no pending error. Every other (level, optname)
        // combination returns 0 too (matching this module's existing setsockopt no-op — nothing
        // reachable queries a *real* option value besides SO_ERROR). Found via real pika's own
        // `select_connection.py` (`_on_writable`: `sock.getsockopt(SOL_SOCKET, SO_ERROR)`).
        Add("getsockopt", (_, a, _) =>
        {
            var sock = Wrap(a[0]).Socket;
            bool isSoError = a.Length > 1 && a[1] is BigInteger opt && opt == 4;
            if (isSoError)
            {
                var err = (int?)sock.GetSocketOption(SocketOptionLevel.Socket, SocketOptionName.Error) ?? 0;
                return new BigInteger(err);
            }
            return new BigInteger(0);
        });

        Add("settimeout", (_, a, _) =>
        {
            var w = Wrap(a[0]);
            if (a[1] is PyNone)
            {
                w.Timeout = null;
                w.Socket.Blocking = true;
            }
            else
            {
                double t = PyOps.AsDouble(a[1]);
                w.Timeout = t;
                w.Socket.Blocking = t != 0;
            }
            return PyNone.Instance;
        });

        Add("setblocking", (interp, a, _) =>
        {
            var w = Wrap(a[0]);
            bool blocking = PyOps.Truthy(interp, a[1]);
            w.Timeout = blocking ? null : 0;
            w.Socket.Blocking = blocking;
            return PyNone.Instance;
        });

        Add("gettimeout", (_, a, _) =>
            Wrap(a[0]).Timeout is double t ? t : PyNone.Instance);

        Add("fileno", (_, a, _) =>
        {
            var sock = Wrap(a[0]).Socket;
            RegisterHandle(sock);
            return new BigInteger((long)sock.Handle);
        });

        Add("shutdown", (_, a, _) =>
        {
            try
            {
                Wrap(a[0]).Socket.Shutdown((int)PyOps.AsBigInt(a[1], "how") switch
                {
                    0 => SocketShutdown.Receive,
                    1 => SocketShutdown.Send,
                    _ => SocketShutdown.Both,
                });
            }
            catch (SocketException)
            {
                // already closed
            }
            return PyNone.Instance;
        });

        Add("close", (_, a, _) =>
        {
            var w = Wrap(a[0]);
            if (!w.Closed)
            {
                w.Closed = true;
                w.Socket.Dispose();
            }
            return PyNone.Instance;
        });

        Add("__enter__", (_, a, _) => a[0]);
        Add("__exit__", (interp, a, _) =>
        {
            interp.CallMethod(a[0], "close", Array.Empty<object>());
            return false;
        });

        return cls;
    }

    internal static byte[] AsBytes(object o) => o switch
    {
        PyBytes b => b.Data,
        PyByteArray b => b.Data.ToArray(),
        _ => throw PyErr.TypeError($"a bytes-like object is required, not '{PyOps.TypeName(o)}'"),
    };
}
