// Copyright (c) 2026 Marco Parenzan
//
// Licensed under the MIT License. See the LICENSE file in the project
// root for full license information.

using PySharpLib.Runtime;
using System.Net.Security;
using System.Net.Sockets;
using System.Numerics;
using System.Security.Authentication;
using System.Security.Cryptography.X509Certificates;

namespace PySharpLib.Modules;

/// <summary>State of an SSLSocket: SslStream + a buffer of decrypted data for non-blocking emulation.</summary>
public sealed class SslWrap
{
    public required SockWrap Underlying { get; init; }
    public required SslStream Stream { get; init; }
    public List<byte> Buffer { get; } = new();
    public bool NonBlocking { get; set; }
    public double? Timeout { get; set; }
    public bool Closed { get; set; }

    /// <summary>True if recv can return data without blocking.</summary>
    public bool Readable => Buffer.Count > 0 || (!Closed && Underlying.Socket.Available > 0);
}

/// <summary>ssl: SSLContext + SSLSocket on SslStream (TLS client, X.509 cert for IoT Hub).</summary>
public static class SslModule
{
    public const string WrapKey = "__sslwrap__";
    private const string CtxKey = "__sslctx__";

    public static readonly PyClass SslErrorClass = new("SSLError", new List<PyClass> { PyErr.OSErrorClass });
    public static readonly PyClass SslWantReadClass = new("SSLWantReadError", new List<PyClass> { SslErrorClass });
    public static readonly PyClass SslWantWriteClass = new("SSLWantWriteError", new List<PyClass> { SslErrorClass });
    public static readonly PyClass CertVerificationErrorClass =
        new("SSLCertVerificationError", new List<PyClass> { SslErrorClass });

    private sealed class CtxState
    {
        public X509Certificate2? ClientCertificate;
        public X509Certificate2Collection CaCertificates { get; } = new();
        public bool CheckHostname = true;
        public int VerifyMode = 2; // CERT_REQUIRED
    }

    public static PyModule Create()
    {
        var m = new PyModule("ssl");
        var d = m.Dict;

        d["PROTOCOL_TLS"] = new BigInteger(2);
        d["PROTOCOL_TLS_CLIENT"] = new BigInteger(16);
        d["PROTOCOL_TLSv1_2"] = new BigInteger(5);
        d["CERT_NONE"] = new BigInteger(0);
        d["CERT_OPTIONAL"] = new BigInteger(1);
        d["CERT_REQUIRED"] = new BigInteger(2);
        d["HAS_SNI"] = true;
        d["OP_NO_SSLv2"] = new BigInteger(0x01000000);
        d["OP_NO_SSLv3"] = new BigInteger(0x02000000);
        d["OP_NO_TLSv1"] = new BigInteger(0x04000000);
        d["OP_NO_TLSv1_1"] = new BigInteger(0x10000000);
        d["OP_NO_COMPRESSION"] = new BigInteger(0x20000);
        d["OP_NO_TICKET"] = new BigInteger(0x4000);
        d["VERIFY_X509_PARTIAL_CHAIN"] = new BigInteger(0x80000);
        d["VERIFY_X509_STRICT"] = new BigInteger(0x20000000);
        d["HAS_NEVER_CHECK_COMMON_NAME"] = false;
        // Backed by .NET's own TLS stack (SChannel/OpenSSL depending on platform), not literally
        // OpenSSL — a real, plausible-format version string so real callers that pattern-match
        // against it (urllib3's own LibreSSL-bug detection, e.g.) see an ordinary modern OpenSSL
        // string rather than nothing. Found via urllib3's own `util/ssl_.py`
        // (`from ssl import (..., OPENSSL_VERSION, ...)`), reachable from `import requests`.
        d["OPENSSL_VERSION"] = "OpenSSL 3.0.13 30 Jan 2024";
        d["OPENSSL_VERSION_NUMBER"] = new BigInteger(0x30000130);
        d["OPENSSL_VERSION_INFO"] = new PyTuple(new object[] { new BigInteger(3), new BigInteger(0), new BigInteger(13), new BigInteger(0), new BigInteger(0) });

        var tlsVersionClass = new PyClass("TLSVersion", new List<PyClass>());
        tlsVersionClass.Dict["MINIMUM_SUPPORTED"] = new BigInteger(-2);
        tlsVersionClass.Dict["MAXIMUM_SUPPORTED"] = new BigInteger(-1);
        tlsVersionClass.Dict["SSLv3"] = new BigInteger(768);
        tlsVersionClass.Dict["TLSv1"] = new BigInteger(769);
        tlsVersionClass.Dict["TLSv1_1"] = new BigInteger(770);
        tlsVersionClass.Dict["TLSv1_2"] = new BigInteger(771);
        tlsVersionClass.Dict["TLSv1_3"] = new BigInteger(772);
        d["TLSVersion"] = tlsVersionClass;

        d["SSLError"] = SslErrorClass;
        d["SSLWantReadError"] = SslWantReadClass;
        d["SSLWantWriteError"] = SslWantWriteClass;
        d["SSLCertVerificationError"] = CertVerificationErrorClass;
        // CPython: CertificateError is a deprecated alias for SSLCertVerificationError. paho-mqtt's
        // TLS path (`except ssl.CertificateError:`) references it unconditionally when matching
        // exception handlers, so it must exist even if that branch is never actually taken.
        d["CertificateError"] = CertVerificationErrorClass;

        var contextClass = BuildContextClass();
        d["SSLContext"] = contextClass;

        d["create_default_context"] = new PyBuiltinFunction("create_default_context", (interp, _, _) =>
        {
            var inst = new PyInstance(contextClass);
            inst.Dict[CtxKey] = new CtxState();
            inst.Dict["check_hostname"] = true;
            inst.Dict["verify_mode"] = new BigInteger(2); // CERT_REQUIRED
            inst.Dict["options"] = new BigInteger(0x02000000 | 0x01000000 | 0x20000);
            inst.Dict["minimum_version"] = PyNone.Instance;
            inst.Dict["maximum_version"] = PyNone.Instance;
            inst.Dict["verify_flags"] = BigInteger.Zero; // real default: ssl.VERIFY_DEFAULT (0)
            return inst;
        });

        // ssl.match_hostname: removed in CPython 3.12; here host validation
        // is already done by SslStream during the handshake, so this is a safe no-op.
        d["match_hostname"] = new PyBuiltinFunction("match_hostname", (_, _, _) => PyNone.Instance);

        return m;
    }

    private static CtxState Ctx(object self)
    {
        var inst = (PyInstance)self;
        if (!inst.Dict.TryGet(CtxKey, out var v))
        {
            v = new CtxState();
            inst.Dict[CtxKey] = v;
        }
        return (CtxState)v;
    }

    private static PyClass BuildContextClass()
    {
        var cls = new PyClass("SSLContext", new List<PyClass>());
        void Add(string name, BuiltinFn fn) => cls.Dict[name] = new PyBuiltinFunction($"SSLContext.{name}", fn);

        Add("__init__", (_, a, _) =>
        {
            var inst = (PyInstance)a[0];
            inst.Dict[CtxKey] = new CtxState();
            // defaults like CPython for PROTOCOL_TLS_CLIENT: host verification + cert required
            inst.Dict["check_hostname"] = true;
            inst.Dict["verify_mode"] = new BigInteger(2); // CERT_REQUIRED
            // Real CPython's default SSLContext.options already has OP_NO_SSLv2/OP_NO_SSLv3/
            // OP_NO_COMPRESSION set; real code (urllib3's own `util/ssl_.py`) reads/OR-assigns into
            // this — the wrap_socket() below always negotiates TLS1.2+/no compression regardless, so
            // this attribute's value doesn't otherwise change behavior, it just needs to exist as a
            // real int. Found via real urllib3's own `create_urllib3_context`, reachable from
            // `import requests`.
            inst.Dict["options"] = new BigInteger(0x02000000 | 0x01000000 | 0x20000);
            inst.Dict["minimum_version"] = PyNone.Instance;
            inst.Dict["maximum_version"] = PyNone.Instance;
            inst.Dict["verify_flags"] = BigInteger.Zero; // real default: ssl.VERIFY_DEFAULT (0)
            return PyNone.Instance;
        });

        Add("load_cert_chain", (_, a, kwargs) =>
        {
            string certfile = (string)a[1];
            string? keyfile = a.Length > 2 && a[2] is string kf ? kf
                : kwargs is not null && kwargs.TryGetValue("keyfile", out var k) && k is string kf2 ? kf2
                : null;
            try
            {
                var cert = keyfile is null
                    ? X509Certificate2.CreateFromPemFile(certfile)
                    : X509Certificate2.CreateFromPemFile(certfile, keyfile);
                // On Windows SChannel requires a persisted key: re-import as PKCS#12
                Ctx(a[0]).ClientCertificate = X509CertificateLoader.LoadPkcs12(
                    cert.Export(X509ContentType.Pkcs12), null);
            }
            catch (Exception ex) when (ex is System.Security.Cryptography.CryptographicException
                or FileNotFoundException or ArgumentException)
            {
                throw new PyRaise(PyErr.MakeInstance(SslErrorClass, $"load_cert_chain failed: {ex.Message}"));
            }
            return PyNone.Instance;
        });

        Add("load_verify_locations", (_, a, kwargs) =>
        {
            string? cafile = a.Length > 1 && a[1] is string cf ? cf
                : kwargs is not null && kwargs.TryGetValue("cafile", out var c) && c is string cf2 ? cf2
                : null;
            if (cafile is not null)
                Ctx(a[0]).CaCertificates.ImportFromPemFile(cafile);
            return PyNone.Instance;
        });

        Add("load_default_certs", (_, _, _) => PyNone.Instance);
        Add("set_ciphers", (_, _, _) => PyNone.Instance);
        Add("set_alpn_protocols", (_, _, _) => PyNone.Instance);

        Add("wrap_socket", (interp, a, kwargs) =>
        {
            var ctx = Ctx(a[0]);
            var sockInst = (PyInstance)a[1];
            var underlying = SocketModule.Wrap(sockInst);

            string? serverHostname = kwargs is not null
                && kwargs.TryGetValue("server_hostname", out var sh) && sh is string shs
                ? shs
                : null;

            // synchronize check_hostname/verify_mode set as Python attributes
            var ctxInst = (PyInstance)a[0];
            if (ctxInst.Dict.TryGet("check_hostname", out var chk))
                ctx.CheckHostname = PyOps.Truthy(interp, chk);
            if (ctxInst.Dict.TryGet("verify_mode", out var vm) && vm is BigInteger vmi)
                ctx.VerifyMode = (int)vmi;

            bool wasBlocking = underlying.Socket.Blocking;
            underlying.Socket.Blocking = true; // SslStream requires a blocking socket

            var networkStream = new NetworkStream(underlying.Socket, ownsSocket: false);
            var sslStream = new SslStream(networkStream, leaveInnerStreamOpen: false,
                (_, _, _, errors) =>
                {
                    if (ctx.VerifyMode == 0)
                        return true;
                    if (errors == SslPolicyErrors.None)
                        return true;
                    if (!ctx.CheckHostname && errors == SslPolicyErrors.RemoteCertificateNameMismatch)
                        return true;
                    return false;
                });

            var options = new SslClientAuthenticationOptions
            {
                TargetHost = serverHostname ?? "",
                EnabledSslProtocols = SslProtocols.Tls12 | SslProtocols.Tls13,
            };
            if (ctx.ClientCertificate is not null)
                options.ClientCertificates = new X509CertificateCollection { ctx.ClientCertificate };

            try
            {
                sslStream.AuthenticateAsClient(options);
            }
            catch (Exception ex) when (ex is AuthenticationException or IOException)
            {
                sslStream.Dispose();
                underlying.Socket.Blocking = wasBlocking;
                throw new PyRaise(PyErr.MakeInstance(SslErrorClass,
                    $"[SSL] handshake failed: {ex.InnerException?.Message ?? ex.Message}"));
            }

            var wrap = new SslWrap
            {
                Underlying = underlying,
                Stream = sslStream,
                NonBlocking = underlying.NonBlocking,
                Timeout = underlying.Timeout,
            };

            var inst = new PyInstance(SslSocketClass);
            inst.Dict[WrapKey] = wrap;
            return inst;
        });

        return cls;
    }

    public static readonly PyClass SslSocketClass = BuildSslSocketClass();

    public static SslWrap Wrap(object self) => (SslWrap)((PyInstance)self).Dict[WrapKey];

    private static PyClass BuildSslSocketClass()
    {
        var cls = new PyClass("SSLSocket", new List<PyClass>());
        void Add(string name, BuiltinFn fn) => cls.Dict[name] = new PyBuiltinFunction($"SSLSocket.{name}", fn);

        Add("recv", (_, a, _) =>
        {
            var w = Wrap(a[0]);
            int size = (int)PyOps.AsBigInt(a[1], "bufsize");

            if (w.Buffer.Count == 0)
            {
                if (w.Closed)
                    return PyBytes.Empty;
                if (w.NonBlocking && w.Underlying.Socket.Available == 0)
                    throw new PyRaise(PyErr.MakeInstance(SslWantReadClass,
                        "The operation did not complete (read)"));
                // read a TLS record (blocking; after select it is typically available)
                var chunk = new byte[16384];
                int n;
                try
                {
                    n = w.Stream.Read(chunk, 0, chunk.Length);
                }
                catch (IOException ex) when (ex.InnerException is SocketException se)
                {
                    throw SocketModule.Translate(se);
                }
                if (n == 0)
                    return PyBytes.Empty;
                w.Buffer.AddRange(chunk[..n]);
            }

            int take = Math.Min(size, w.Buffer.Count);
            var result = w.Buffer.Take(take).ToArray();
            w.Buffer.RemoveRange(0, take);
            return new PyBytes(result);
        });

        Add("send", (_, a, _) =>
        {
            var w = Wrap(a[0]);
            var data = SocketModule.AsBytes(a[1]);
            try
            {
                w.Stream.Write(data);
                w.Stream.Flush();
            }
            catch (IOException ex) when (ex.InnerException is SocketException se)
            {
                throw SocketModule.Translate(se);
            }
            return new BigInteger(data.Length);
        });

        Add("sendall", (interp, a, _) =>
        {
            interp.CallMethod(a[0], "send", new[] { a[1] });
            return PyNone.Instance;
        });

        Add("pending", (_, a, _) => new BigInteger(Wrap(a[0]).Buffer.Count));

        Add("setblocking", (interp, a, _) =>
        {
            Wrap(a[0]).NonBlocking = !PyOps.Truthy(interp, a[1]);
            return PyNone.Instance;
        });

        Add("settimeout", (_, a, _) =>
        {
            var w = Wrap(a[0]);
            if (a[1] is PyNone)
            {
                w.Timeout = null;
                w.NonBlocking = false;
            }
            else
            {
                double t = PyOps.AsDouble(a[1]);
                w.Timeout = t;
                w.NonBlocking = t == 0;
            }
            return PyNone.Instance;
        });

        Add("gettimeout", (_, a, _) => Wrap(a[0]).Timeout is double t ? t : PyNone.Instance);

        Add("close", (_, a, _) =>
        {
            var w = Wrap(a[0]);
            if (!w.Closed)
            {
                w.Closed = true;
                try
                {
                    w.Stream.Dispose();
                }
                catch (IOException)
                {
                }
                w.Underlying.Closed = true;
                w.Underlying.Socket.Dispose();
            }
            return PyNone.Instance;
        });

        Add("do_handshake", (_, _, _) => PyNone.Instance); // already done in wrap_socket
        // Real (not stubbed) peer certificate details — SslStream itself already validated the
        // certificate/hostname during the handshake (AuthenticateAsClient above), but real urllib3
        // does its *own* independent application-level hostname check against getpeercert()'s
        // subjectAltName (a defense-in-depth cross-check, `_match_hostname` in its vendored
        // `ssl_match_hostname.py`), so an empty dict here made every real HTTPS request fail with
        // "empty or no certificate". Found live via `import requests` making a real HTTPS GET.
        Add("getpeercert", (_, a, _) =>
        {
            var result = new PyDict();
            if (Wrap(a[0]).Stream.RemoteCertificate is X509Certificate2 cert)
            {
                var sanEntries = new List<object>();
                foreach (var ext in cert.Extensions)
                {
                    if (ext is X509SubjectAlternativeNameExtension sanExt)
                        foreach (var dns in sanExt.EnumerateDnsNames())
                            sanEntries.Add(new PyTuple(new object[] { "DNS", dns }));
                }
                result["subjectAltName"] = new PyTuple(sanEntries.ToArray());
                string cn = cert.GetNameInfo(X509NameType.SimpleName, false);
                result["subject"] = new PyTuple(new object[]
                {
                    new PyTuple(new object[] { new PyTuple(new object[] { "commonName", cn }) }),
                });
            }
            return result;
        });
        Add("version", (_, a, _) => Wrap(a[0]).Stream.SslProtocol.ToString());
        Add("selected_alpn_protocol", (_, _, _) => PyNone.Instance);
        Add("fileno", (_, a, _) =>
        {
            var sock = Wrap(a[0]).Underlying.Socket;
            // add_reader/add_writer only ever get this bare int fd back (the asyncio API
            // shape), so it must resolve to the raw Socket the same way the plain `socket`
            // module's fileno() already does — otherwise the event loop's poller can never
            // find it and a TLS connection's reader/writer never fires (see AIOMQTT_PLAN.md
            // Phase 6: this is what made the real Azure IoT Hub run hang forever, while the
            // plaintext test.mosquitto.org run worked, since only TLS goes through here).
            SocketModule.RegisterHandle(sock);
            return new BigInteger((long)sock.Handle);
        });

        Add("__enter__", (_, a, _) => a[0]);
        Add("__exit__", (interp, a, _) =>
        {
            interp.CallMethod(a[0], "close", Array.Empty<object>());
            return false;
        });

        return cls;
    }
}
