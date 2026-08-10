// Copyright (c) 2026 Marco Parenzan
//
// Licensed under the MIT License. See the LICENSE file in the project
// root for full license information.

using System.Numerics;
using System.Text;
using PySharpLib.Interpretation;
using PySharpLib.Runtime;

namespace PySharpLib.Modules;

/// <summary>http.client: a real (not stubbed) low-level HTTP/1.1 client — HTTPConnection/
/// HTTPSConnection (subclassable: putrequest/putheader/endheaders/send/getresponse are real,
/// callable via `super()` from a Python subclass), HTTPResponse (real Content-Length and chunked
/// Transfer-Encoding body decoding), HTTPMessage (reuses email.message.Message, matching real
/// CPython, which does the same), and the real HTTPException hierarchy. All I/O goes through the
/// existing `socket`/`ssl` modules' own Python-visible objects (`self.sock.sendall(...)`/
/// `self.sock.recv(...)`) rather than a separate raw-socket layer — found necessary because
/// `urllib3.connection.HTTPConnection` subclasses this module's `HTTPConnection` and calls
/// `super().putrequest()`/`super().getresponse()` directly while managing `self.sock` itself (via
/// its own `_new_conn()`, built on the real `socket` module), so `self.sock` must be a real,
/// Python-visible socket object that can be swapped in from outside. Found via `import requests`
/// (→ `urllib3` → `http.client`). See HTTP_PLAN.md.</summary>
public static class HttpClientModule
{
    // ---------------------------------------------------------------- exception hierarchy
    public static readonly PyClass HTTPExceptionClass = new("HTTPException", new List<PyClass> { PyErr.Exception });
    private static PyClass Derive(string name, PyClass baseClass) => new(name, new List<PyClass> { baseClass });
    public static readonly PyClass NotConnectedClass = Derive("NotConnected", HTTPExceptionClass);
    public static readonly PyClass InvalidURLClass = Derive("InvalidURL", HTTPExceptionClass);
    public static readonly PyClass UnknownProtocolClass = Derive("UnknownProtocol", HTTPExceptionClass);
    public static readonly PyClass UnknownTransferEncodingClass = Derive("UnknownTransferEncoding", HTTPExceptionClass);
    public static readonly PyClass UnimplementedFileModeClass = Derive("UnimplementedFileMode", HTTPExceptionClass);
    public static readonly PyClass IncompleteReadClass = Derive("IncompleteRead", HTTPExceptionClass);
    public static readonly PyClass ImproperConnectionStateClass = Derive("ImproperConnectionState", HTTPExceptionClass);
    public static readonly PyClass CannotSendRequestClass = Derive("CannotSendRequest", ImproperConnectionStateClass);
    public static readonly PyClass CannotSendHeaderClass = Derive("CannotSendHeader", ImproperConnectionStateClass);
    public static readonly PyClass ResponseNotReadyClass = Derive("ResponseNotReady", ImproperConnectionStateClass);
    public static readonly PyClass BadStatusLineClass = Derive("BadStatusLine", HTTPExceptionClass);
    public static readonly PyClass LineTooLongClass = Derive("LineTooLong", HTTPExceptionClass);
    public static readonly PyClass RemoteDisconnectedClass =
        new("RemoteDisconnected", new List<PyClass> { PyErr.ConnectionResetErrorClass, BadStatusLineClass });

    public static readonly PyClass HTTPResponseClass = BuildResponseClass();
    public static readonly PyClass HTTPConnectionClass = BuildConnectionClass();
    public static readonly PyClass HTTPSConnectionClass = BuildHttpsConnectionClass();

    private static readonly (int Code, string Phrase)[] StatusPhrases = BuildStatusPhrases();

    public static PyModule Create()
    {
        var m = new PyModule("http.client");
        var d = m.Dict;

        var responses = new PyDict();
        foreach (var (code, phrase) in StatusPhrases)
            responses[(BigInteger)code] = phrase;
        d["responses"] = responses;

        d["HTTPException"] = HTTPExceptionClass;
        d["NotConnected"] = NotConnectedClass;
        d["InvalidURL"] = InvalidURLClass;
        d["UnknownProtocol"] = UnknownProtocolClass;
        d["UnknownTransferEncoding"] = UnknownTransferEncodingClass;
        d["UnimplementedFileMode"] = UnimplementedFileModeClass;
        d["IncompleteRead"] = IncompleteReadClass;
        d["ImproperConnectionState"] = ImproperConnectionStateClass;
        d["CannotSendRequest"] = CannotSendRequestClass;
        d["CannotSendHeader"] = CannotSendHeaderClass;
        d["ResponseNotReady"] = ResponseNotReadyClass;
        d["BadStatusLine"] = BadStatusLineClass;
        d["LineTooLong"] = LineTooLongClass;
        d["RemoteDisconnected"] = RemoteDisconnectedClass;

        d["HTTPConnection"] = HTTPConnectionClass;
        d["HTTPSConnection"] = HTTPSConnectionClass;
        d["HTTPResponse"] = HTTPResponseClass;
        d["HTTPMessage"] = EmailModule.MessageClass;

        d["HTTP_PORT"] = (BigInteger)80;
        d["HTTPS_PORT"] = (BigInteger)443;
        d["_MAXLINE"] = (BigInteger)65536;
        d["_MAXHEADERS"] = (BigInteger)100;

        return m;
    }

    // ---------------------------------------------------------------- SocketReader

    /// <summary>A small buffered reader over a real Python socket/SSLSocket object — repeatedly
    /// calls `self.sock.recv(...)` (never a raw C# Stream) so it transparently works whether `sock`
    /// is a plain `socket.socket` or an `ssl.SSLSocket`, and whether it was created by this module's
    /// own `.connect()` or swapped in from outside (as urllib3 does).</summary>
    private sealed class SocketReader
    {
        private readonly Interp _interp;
        private readonly object _sock;
        private readonly List<byte> _buf = new();
        private bool _eof;

        public SocketReader(Interp interp, object sock)
        {
            _interp = interp;
            _sock = sock;
        }

        private bool FillMore()
        {
            if (_eof)
                return false;
            var chunk = (PyBytes)_interp.CallMethod(_sock, "recv", new object[] { (BigInteger)8192 });
            if (chunk.Length == 0)
            {
                _eof = true;
                return false;
            }
            _buf.AddRange(chunk.Data);
            return true;
        }

        private int IndexOfCrlf()
        {
            for (int i = 0; i + 1 < _buf.Count; i++)
                if (_buf[i] == (byte)'\r' && _buf[i + 1] == (byte)'\n')
                    return i;
            return -1;
        }

        /// <summary>Reads one CRLF-terminated line (without the CRLF). Returns null only when the
        /// connection closed with nothing at all buffered — the caller uses that to distinguish "the
        /// peer closed before sending anything" (RemoteDisconnected) from a normal EOF mid-body.</summary>
        public string? ReadLine()
        {
            while (true)
            {
                int idx = IndexOfCrlf();
                if (idx >= 0)
                {
                    var lineBytes = _buf.GetRange(0, idx).ToArray();
                    _buf.RemoveRange(0, idx + 2);
                    return Encoding.ASCII.GetString(lineBytes);
                }
                if (_buf.Count > 65536)
                    throw PyErr.Raise(LineTooLongClass, "got more than 65536 bytes when reading header line");
                if (!FillMore())
                {
                    if (_buf.Count == 0)
                        return null;
                    var lineBytes = _buf.ToArray();
                    _buf.Clear();
                    return Encoding.ASCII.GetString(lineBytes);
                }
            }
        }

        public byte[] ReadExact(int n)
        {
            while (_buf.Count < n && FillMore()) { }
            int take = Math.Min(n, _buf.Count);
            var result = _buf.GetRange(0, take).ToArray();
            _buf.RemoveRange(0, take);
            return result;
        }

        public byte[] ReadToEof()
        {
            while (FillMore()) { }
            var result = _buf.ToArray();
            _buf.Clear();
            return result;
        }
    }

    // ---------------------------------------------------------------- HTTPConnection

    private const string SockKey = "sock";
    private const string ReaderKey = "__reader__";
    private const string StateKey = "__state__";
    private const string MethodKey = "__method__";
    private const string UrlKey = "__url__";
    private const string HeadersBufKey = "__headers_buf__";

    private static object GetSock(Interp interp, PyInstance inst)
        => interp.TryGetAttr(inst, SockKey, out var v) ? v : PyNone.Instance;

    private static PyClass BuildConnectionClass()
    {
        var cls = new PyClass("HTTPConnection", new List<PyClass>());
        cls.Dict["default_port"] = (BigInteger)80;
        void Add(string name, BuiltinFn fn) => cls.Dict[name] = new PyBuiltinFunction($"HTTPConnection.{name}", fn);

        Add("__init__", (interp, a, kwargs) =>
        {
            var inst = (PyInstance)a[0];
            // Real CPython's HTTPConnection.__init__(self, host, port=None, ...): `host` is
            // positional-or-keyword. Found live via real urllib3's own `connection.py`
            // (`super().__init__(host=host, port=port, timeout=..., source_address=...,
            // blocksize=...)` — every argument passed by keyword, none positional), reachable from
            // `import requests`.
            string host = a.Length > 1 ? (string)a[1]
                : kwargs is not null && kwargs.TryGetValue("host", out var h) ? (string)h
                : throw PyErr.TypeError("HTTPConnection.__init__() missing required argument: 'host'");
            object port = a.Length > 2 ? a[2] : kwargs is not null && kwargs.TryGetValue("port", out var p) ? p : PyNone.Instance;
            object timeout = kwargs is not null && kwargs.TryGetValue("timeout", out var t) ? t : PyNone.Instance;
            object sourceAddress = kwargs is not null && kwargs.TryGetValue("source_address", out var sa) ? sa : PyNone.Instance;
            object blocksize = kwargs is not null && kwargs.TryGetValue("blocksize", out var bs) ? bs : (BigInteger)8192;

            if (port is PyNone && host.Contains(':') && !host.StartsWith('['))
            {
                var parts = host.Split(':', 2);
                host = parts[0];
                port = (BigInteger)int.Parse(parts[1]);
            }
            host = host.Trim('[', ']');

            interp.SetAttr(inst, "host", host);
            interp.SetAttr(inst, "port", port is PyNone
                ? (inst.Class.TryLookup("default_port", out var dp) ? dp : (BigInteger)80)
                : port);
            interp.SetAttr(inst, "timeout", timeout);
            interp.SetAttr(inst, "source_address", sourceAddress);
            interp.SetAttr(inst, "blocksize", blocksize);
            interp.SetAttr(inst, SockKey, PyNone.Instance);
            inst.Dict[StateKey] = "idle";
            inst.Dict[HeadersBufKey] = new List<(string, string)>();
            return PyNone.Instance;
        });

        Add("connect", (interp, a, _) =>
        {
            var inst = (PyInstance)a[0];
            string host = (string)interp.GetAttr(inst, "host");
            var port = interp.GetAttr(inst, "port");
            var sockInst = interp.Call(SocketModule.SocketClass, Array.Empty<object>());
            var timeoutVal = interp.GetAttr(inst, "timeout");
            if (timeoutVal is not PyNone)
                interp.CallMethod(sockInst, "settimeout", new[] { timeoutVal });
            interp.CallMethod(sockInst, "connect", new object[] { new PyTuple(new object[] { host, port }) });
            interp.SetAttr(inst, SockKey, sockInst);
            inst.Dict.Remove(ReaderKey);
            return PyNone.Instance;
        });

        AddSharedMethods(cls, Add);
        return cls;
    }

    private static PyClass BuildHttpsConnectionClass()
    {
        var cls = new PyClass("HTTPSConnection", new List<PyClass> { HTTPConnectionClass });
        cls.Dict["default_port"] = (BigInteger)443;
        void Add(string name, BuiltinFn fn) => cls.Dict[name] = new PyBuiltinFunction($"HTTPSConnection.{name}", fn);

        // No __init__ override needed: the inherited HTTPConnection.__init__ already reads
        // `default_port` dynamically via the instance's own class MRO (finds 443 here via normal
        // Python attribute lookup), so HTTPSConnection needs no logic of its own beyond that class
        // attribute and its own `connect` override below.
        Add("connect", (interp, a, kwargs) =>
        {
            var inst = (PyInstance)a[0];
            string host = (string)interp.GetAttr(inst, "host");
            var port = interp.GetAttr(inst, "port");
            var sockInst = interp.Call(SocketModule.SocketClass, Array.Empty<object>());
            var timeoutVal = interp.GetAttr(inst, "timeout");
            if (timeoutVal is not PyNone)
                interp.CallMethod(sockInst, "settimeout", new[] { timeoutVal });
            interp.CallMethod(sockInst, "connect", new object[] { new PyTuple(new object[] { host, port }) });

            object context = kwargs is not null && kwargs.TryGetValue("context", out var ctx) ? ctx
                : interp.TryGetAttr(inst, "_context", out var ic) ? ic
                : PyNone.Instance;
            if (context is PyNone)
            {
                var sslModule = interp.ImportHook!(interp, "ssl", 0, interp.BuiltinsModule);
                context = interp.Call(sslModule.Dict["create_default_context"], Array.Empty<object>());
            }
            var sslSockInst = interp.CallMethod(context, "wrap_socket", new object[] { sockInst },
                new Dictionary<string, object> { ["server_hostname"] = host });

            interp.SetAttr(inst, SockKey, sslSockInst);
            inst.Dict.Remove(ReaderKey);
            return PyNone.Instance;
        });

        return cls;
    }

    private static void AddSharedMethods(PyClass cls, Action<string, BuiltinFn> Add)
    {
        Add("set_debuglevel", (_, _, _) => PyNone.Instance);
        Add("set_tunnel", (_, _, _) => throw PyErr.NotImplementedError("HTTP(S) proxy tunneling is not implemented"));

        Add("putrequest", (interp, a, kwargs) =>
        {
            var inst = (PyInstance)a[0];
            if ((string)inst.Dict[StateKey] != "idle")
                throw PyErr.Raise(CannotSendRequestClass, "Request-sent");
            string method = (string)a[1];
            string url = (string)a[2];
            bool skipHost = a.Length > 3 ? PyOps.Truthy(interp, a[3])
                : kwargs is not null && kwargs.TryGetValue("skip_host", out var sh) && PyOps.Truthy(interp, sh);

            inst.Dict[MethodKey] = method;
            inst.Dict[UrlKey] = url;
            var headers = (List<(string, string)>)inst.Dict[HeadersBufKey];
            headers.Clear();
            if (!skipHost)
            {
                string host = (string)interp.GetAttr(inst, "host");
                var port = interp.GetAttr(inst, "port");
                var defaultPort = inst.Class.TryLookup("default_port", out var dp) ? (BigInteger)dp : (BigInteger)80;
                string hostHeader = port is BigInteger p && p != defaultPort ? $"{host}:{p}" : host;
                headers.Add(("Host", hostHeader));
            }
            inst.Dict[StateKey] = "req_started";
            return PyNone.Instance;
        });

        Add("putheader", (interp, a, _) =>
        {
            var inst = (PyInstance)a[0];
            if ((string)inst.Dict[StateKey] != "req_started")
                throw PyErr.Raise(CannotSendHeaderClass, "");
            string name = (string)a[1];
            string value = string.Join(", ", a.Skip(2).Select(v => PyOps.Str(interp, v)));
            ((List<(string, string)>)inst.Dict[HeadersBufKey]).Add((name, value));
            return PyNone.Instance;
        });

        Add("endheaders", (interp, a, kwargs) =>
        {
            var inst = (PyInstance)a[0];
            if ((string)inst.Dict[StateKey] != "req_started")
                throw PyErr.Raise(CannotSendHeaderClass, "");
            string method = (string)inst.Dict[MethodKey];
            string url = (string)inst.Dict[UrlKey];
            var headers = (List<(string, string)>)inst.Dict[HeadersBufKey];

            var sb = new StringBuilder();
            sb.Append($"{method} {url} HTTP/1.1\r\n");
            foreach (var (name, value) in headers)
                sb.Append($"{name}: {value}\r\n");
            sb.Append("\r\n");

            SendRaw(interp, inst, Encoding.ASCII.GetBytes(sb.ToString()));

            object body = a.Length > 1 ? a[1]
                : kwargs is not null && kwargs.TryGetValue("message_body", out var mb) ? mb
                : PyNone.Instance;
            if (body is not PyNone)
                SendBody(interp, inst, body);

            inst.Dict[StateKey] = "req_sent";
            return PyNone.Instance;
        });

        Add("send", (interp, a, _) =>
        {
            SendBody(interp, (PyInstance)a[0], a[1]);
            return PyNone.Instance;
        });

        Add("request", (interp, a, kwargs) =>
        {
            var inst = (PyInstance)a[0];
            string method = (string)a[1];
            string url = (string)a[2];
            object body = a.Length > 3 ? a[3] : kwargs is not null && kwargs.TryGetValue("body", out var b) ? b : PyNone.Instance;
            object headersArg = a.Length > 4 ? a[4] : kwargs is not null && kwargs.TryGetValue("headers", out var h) ? h : PyNone.Instance;

            interp.CallMethod(inst, "putrequest", new object[] { method, url });
            var headerNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (headersArg is PyDict hd)
            {
                foreach (var e in hd.Entries)
                {
                    string key = (string)e.Key;
                    headerNames.Add(key);
                    interp.CallMethod(inst, "putheader", new object[] { key, e.Value });
                }
            }
            if (body is not PyNone && !headerNames.Contains("Content-Length") && !headerNames.Contains("Transfer-Encoding"))
            {
                int len = body switch
                {
                    string s => Encoding.UTF8.GetByteCount(s),
                    PyBytes by => by.Length,
                    _ => -1,
                };
                if (len >= 0)
                    interp.CallMethod(inst, "putheader", new object[] { "Content-Length", len.ToString() });
            }
            interp.CallMethod(inst, "endheaders", Array.Empty<object>());
            if (body is not PyNone)
                SendBody(interp, inst, body);
            return PyNone.Instance;
        });

        Add("getresponse", (interp, a, _) =>
        {
            var inst = (PyInstance)a[0];
            if ((string)inst.Dict[StateKey] != "req_sent")
                throw PyErr.Raise(ResponseNotReadyClass, "");
            var reader = GetReader(interp, inst);

            string? statusLine = reader.ReadLine();
            if (statusLine is null)
                throw PyErr.Raise(RemoteDisconnectedClass, "Remote end closed connection without response");
            if (!statusLine.StartsWith("HTTP/"))
                throw PyErr.Raise(BadStatusLineClass, statusLine);
            var firstSpace = statusLine.IndexOf(' ');
            if (firstSpace < 0)
                throw PyErr.Raise(BadStatusLineClass, statusLine);
            string versionStr = statusLine[..firstSpace];
            string rest = statusLine[(firstSpace + 1)..];
            int secondSpace = rest.IndexOf(' ');
            string statusStr = secondSpace < 0 ? rest : rest[..secondSpace];
            string reason = secondSpace < 0 ? "" : rest[(secondSpace + 1)..];
            if (!int.TryParse(statusStr, out int status))
                throw PyErr.Raise(BadStatusLineClass, statusLine);
            int version = versionStr == "HTTP/1.0" ? 10 : 11;

            var headers = new List<(string, string)>();
            while (true)
            {
                string? line = reader.ReadLine();
                if (line is null || line.Length == 0)
                    break;
                int colon = line.IndexOf(':');
                if (colon < 0)
                    continue;
                headers.Add((line[..colon].Trim(), line[(colon + 1)..].Trim()));
            }

            string requestMethod = (string)inst.Dict[MethodKey];
            inst.Dict[StateKey] = "idle";

            var respInst = new PyInstance(HTTPResponseClass);
            respInst.Dict["__reader__"] = reader;
            respInst.Dict["__method__"] = requestMethod;
            respInst.Dict["__consumed__"] = false;
            respInst.Dict["__closed__"] = false;
            interp.SetAttr(respInst, "status", (BigInteger)status);
            interp.SetAttr(respInst, "reason", reason);
            interp.SetAttr(respInst, "version", (BigInteger)version);
            var msg = EmailModule.BuildMessage(headers);
            interp.SetAttr(respInst, "headers", msg);
            interp.SetAttr(respInst, "msg", msg);
            return respInst;
        });

        Add("close", (interp, a, _) =>
        {
            var inst = (PyInstance)a[0];
            var sock = GetSock(interp, inst);
            if (sock is not PyNone)
            {
                try
                {
                    interp.CallMethod(sock, "close", Array.Empty<object>());
                }
                catch (PyRaise)
                {
                }
            }
            interp.SetAttr(inst, SockKey, PyNone.Instance);
            inst.Dict.Remove(ReaderKey);
            inst.Dict[StateKey] = "idle";
            return PyNone.Instance;
        });

        Add("__enter__", (_, a, _) => a[0]);
        Add("__exit__", (interp, a, _) =>
        {
            interp.CallMethod(a[0], "close", Array.Empty<object>());
            return false;
        });
    }

    private static SocketReader GetReader(Interp interp, PyInstance inst)
    {
        if (inst.Dict.TryGet(ReaderKey, out var r))
            return (SocketReader)r;
        var sock = GetSock(interp, inst);
        if (sock is PyNone)
            throw PyErr.Raise(NotConnectedClass, "");
        var reader = new SocketReader(interp, sock);
        inst.Dict[ReaderKey] = reader;
        return reader;
    }

    private static void SendRaw(Interp interp, PyInstance inst, byte[] data)
    {
        var sock = GetSock(interp, inst);
        if (sock is PyNone)
        {
            interp.CallMethod(inst, "connect", Array.Empty<object>());
            sock = GetSock(interp, inst);
        }
        interp.CallMethod(sock, "sendall", new object[] { new PyBytes(data) });
    }

    private static void SendBody(Interp interp, PyInstance inst, object body)
    {
        byte[] data = body switch
        {
            string s => Encoding.UTF8.GetBytes(s),
            PyBytes b => b.Data,
            _ => throw PyErr.TypeError($"send() argument must be bytes-like or str, not '{PyOps.TypeName(body)}'"),
        };
        if (data.Length > 0)
            SendRaw(interp, inst, data);
    }

    // ---------------------------------------------------------------- HTTPResponse

    private static PyClass BuildResponseClass()
    {
        var cls = new PyClass("HTTPResponse", new List<PyClass>());
        void Add(string name, BuiltinFn fn) => cls.Dict[name] = new PyBuiltinFunction($"HTTPResponse.{name}", fn);

        Add("read", (interp, a, _) =>
        {
            var inst = (PyInstance)a[0];
            if (inst.Dict.TryGet("__closed__", out var c) && c is true)
                return PyBytes.Empty;
            if (inst.Dict.TryGet("__consumed__", out var consumed) && consumed is true)
                return PyBytes.Empty;

            var reader = (SocketReader)inst.Dict["__reader__"];
            string method = (string)inst.Dict["__method__"];
            int status = (int)(BigInteger)interp.GetAttr(inst, "status");

            bool noBody = method == "HEAD" || status is 204 or 304 || (status >= 100 && status < 200);

            byte[] body;
            if (noBody)
            {
                body = Array.Empty<byte>();
            }
            else
            {
                var headers = interp.GetAttr(inst, "headers");
                string transferEncoding = HeaderGet(interp, headers, "transfer-encoding");
                if (transferEncoding.Contains("chunked", StringComparison.OrdinalIgnoreCase))
                {
                    body = ReadChunked(reader);
                }
                else
                {
                    string contentLengthStr = HeaderGet(interp, headers, "content-length");
                    body = int.TryParse(contentLengthStr, out int contentLength)
                        ? reader.ReadExact(contentLength)
                        : reader.ReadToEof();
                }
            }

            inst.Dict["__consumed__"] = true;
            object amtArg = a.Length > 1 ? a[1] : PyNone.Instance;
            if (amtArg is PyNone)
                return new PyBytes(body);
            int amt = (int)PyOps.AsBigInt(amtArg, "amt");
            return new PyBytes(body.Take(amt).ToArray());
        });

        Add("getheader", (interp, a, _) =>
        {
            var headers = interp.GetAttr((PyInstance)a[0], "headers");
            string name = (string)a[1];
            object def = a.Length > 2 ? a[2] : PyNone.Instance;
            return interp.TryCallMethod(headers, "get", new object[] { name, def }, out var v) ? v : def;
        });

        Add("getheaders", (interp, a, _) =>
        {
            var headers = interp.GetAttr((PyInstance)a[0], "headers");
            return interp.CallMethod(headers, "items", Array.Empty<object>());
        });

        Add("isclosed", (_, a, _) =>
        {
            var inst = (PyInstance)a[0];
            return inst.Dict.TryGet("__closed__", out var c) && c is true;
        });

        Add("close", (_, a, _) =>
        {
            ((PyInstance)a[0]).Dict["__closed__"] = true;
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

    private static string HeaderGet(Interp interp, object headers, string name)
        => interp.TryCallMethod(headers, "get", new object[] { name, "" }, out var v) && v is string s ? s : "";

    private static byte[] ReadChunked(SocketReader reader)
    {
        var result = new List<byte>();
        while (true)
        {
            string? sizeLine = reader.ReadLine();
            if (sizeLine is null)
                throw PyErr.Raise(IncompleteReadClass, "chunked encoding: missing chunk size");
            int semi = sizeLine.IndexOf(';');
            string sizeHex = (semi >= 0 ? sizeLine[..semi] : sizeLine).Trim();
            if (!int.TryParse(sizeHex, System.Globalization.NumberStyles.HexNumber, null, out int size))
                throw PyErr.Raise(IncompleteReadClass, $"chunked encoding: invalid chunk size '{sizeHex}'");
            if (size == 0)
            {
                // trailing headers (if any) up to the final blank line
                while (true)
                {
                    string? trailer = reader.ReadLine();
                    if (trailer is null || trailer.Length == 0)
                        break;
                }
                break;
            }
            result.AddRange(reader.ReadExact(size));
            reader.ReadLine(); // trailing CRLF after each chunk's data
        }
        return result.ToArray();
    }

    private static (int, string)[] BuildStatusPhrases() => new (int, string)[]
    {
        (100, "Continue"), (101, "Switching Protocols"), (102, "Processing"), (103, "Early Hints"),
        (200, "OK"), (201, "Created"), (202, "Accepted"), (203, "Non-Authoritative Information"),
        (204, "No Content"), (205, "Reset Content"), (206, "Partial Content"), (207, "Multi-Status"),
        (208, "Already Reported"), (226, "IM Used"),
        (300, "Multiple Choices"), (301, "Moved Permanently"), (302, "Found"), (303, "See Other"),
        (304, "Not Modified"), (305, "Use Proxy"), (307, "Temporary Redirect"), (308, "Permanent Redirect"),
        (400, "Bad Request"), (401, "Unauthorized"), (402, "Payment Required"), (403, "Forbidden"),
        (404, "Not Found"), (405, "Method Not Allowed"), (406, "Not Acceptable"),
        (407, "Proxy Authentication Required"), (408, "Request Timeout"), (409, "Conflict"),
        (410, "Gone"), (411, "Length Required"), (412, "Precondition Failed"),
        (413, "Request Entity Too Large"), (414, "Request-URI Too Long"), (415, "Unsupported Media Type"),
        (416, "Requested Range Not Satisfiable"), (417, "Expectation Failed"), (418, "I'm a Teapot"),
        (421, "Misdirected Request"), (422, "Unprocessable Entity"), (423, "Locked"),
        (424, "Failed Dependency"), (425, "Too Early"), (426, "Upgrade Required"),
        (428, "Precondition Required"), (429, "Too Many Requests"),
        (431, "Request Header Fields Too Large"), (451, "Unavailable For Legal Reasons"),
        (500, "Internal Server Error"), (501, "Not Implemented"), (502, "Bad Gateway"),
        (503, "Service Unavailable"), (504, "Gateway Timeout"), (505, "HTTP Version Not Supported"),
        (506, "Variant Also Negotiates"), (507, "Insufficient Storage"), (508, "Loop Detected"),
        (510, "Not Extended"), (511, "Network Authentication Required"),
    };
}
