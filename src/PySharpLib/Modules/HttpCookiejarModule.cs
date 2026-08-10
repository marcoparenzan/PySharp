// Copyright (c) 2026 Marco Parenzan
//
// Licensed under the MIT License. See the LICENSE file in the project
// root for full license information.

using System.Numerics;
using PySharpLib.Interpretation;
using PySharpLib.Runtime;

namespace PySharpLib.Modules;

/// <summary>http.cookiejar: real `Cookie`/`CookieJar` — RFC 6265 domain/path matching and a real
/// Set-Cookie parser, scoped to the interface real httpx's `_models.py`'s `Cookies` class actually
/// drives (`set_cookie`, iteration, `len()`, `clear`, `extract_cookies(response, request)`,
/// `add_cookie_header(request)`), not CPython's full internal architecture (no `CookiePolicy`
/// object, no locking, no RFC 2965/2109 legacy support) — matching this project's "real observable
/// behavior, not a byte-identical internal port" standard, the same way `re` is backed by
/// System.Text.RegularExpressions rather than a hand-rolled NFA.</summary>
public static class HttpCookiejarModule
{
    public static PyModule Create()
    {
        var m = new PyModule("http.cookiejar");
        m.Dict["Cookie"] = CookieClass;
        m.Dict["CookieJar"] = CookieJarClass;
        // Real name only (no policy-checking logic) — nothing reachable calls a policy method
        // directly, it's only ever referenced as a type annotation / `get_policy()` return type.
        // Found via real requests' own `cookies.py` (`from http.cookiejar import ..., CookiePolicy`),
        // reachable from `import requests`.
        m.Dict["CookiePolicy"] = CookiePolicyClass;
        return m;
    }

    public static readonly PyClass CookiePolicyClass = new("CookiePolicy", new List<PyClass>());

    public static readonly PyClass CookieClass = BuildCookieClass();
    public static readonly PyClass CookieJarClass = BuildCookieJarClass();

    private static PyClass BuildCookieClass()
    {
        var cls = new PyClass("Cookie", new List<PyClass>());

        // Real httpx always constructs via Cookie(**kwargs) with every field given explicitly
        // (version, name, value, port, port_specified, domain, domain_specified,
        // domain_initial_dot, path, path_specified, secure, expires, discard, comment,
        // comment_url, rest, rfc2109) — stored verbatim as attributes.
        cls.Dict["__init__"] = new PyBuiltinFunction("Cookie.__init__", (_, a, kwargs) =>
        {
            var inst = (PyInstance)a[0];
            if (kwargs is not null)
                foreach (var kv in kwargs)
                    inst.Dict[kv.Key] = kv.Value;
            return PyNone.Instance;
        });

        cls.Dict["__repr__"] = new PyBuiltinFunction("Cookie.__repr__", (interp, a, _) =>
        {
            var inst = (PyInstance)a[0];
            string name = inst.Dict.TryGet("name", out var n) ? PyOps.Str(interp, n) : "?";
            string value = inst.Dict.TryGet("value", out var v) ? PyOps.Str(interp, v) : "?";
            string domain = inst.Dict.TryGet("domain", out var d) ? PyOps.Str(interp, d) : "";
            return $"<Cookie {name}={value} for {domain}>";
        });

        return cls;
    }

    private const string JarKey = "__jar__";

    private static List<object> Jar(PyInstance inst)
    {
        if (!inst.Dict.TryGet(JarKey, out var v) || v is not List<object> list)
        {
            list = new List<object>();
            inst.Dict[JarKey] = list;
        }
        return list;
    }

    private static object? Attr(object cookie, string name)
        => cookie is PyInstance inst && inst.Dict.TryGet(name, out var v) ? v : null;

    private static string AttrStr(object cookie, string name, string def)
        => Attr(cookie, name) is string s ? s : def;

    private static bool AttrBool(object cookie, string name)
        => Attr(cookie, name) is bool b && b;

    private static PyClass BuildCookieJarClass()
    {
        var cls = new PyClass("CookieJar", new List<PyClass>());
        void Add(string n, BuiltinFn fn) => cls.Dict[n] = new PyBuiltinFunction($"CookieJar.{n}", fn);

        Add("__init__", (_, a, kwargs) =>
        {
            var inst = (PyInstance)a[0];
            Jar(inst);
            // Real CPython CookieJar.__init__(self, policy=None): self._policy = policy or
            // DefaultCookiePolicy() — a REAL, plain (not double-underscore-mangled) attribute, since
            // real subclasses (real requests' own `RequestsCookieJar`) read `self._policy` directly,
            // bypassing this class's own get_policy() override entirely. No policy-checking logic
            // behind it here (see this module's own doc comment on why: scoped to the interface real
            // callers actually drive, not a full RFC 2965/2109 policy engine) — found via real
            // requests' own `cookies.py` (`RequestsCookieJar.get_policy(self): return self._policy`,
            // called while `Session.resolve_redirects` copies the jar), reachable from `import
            // requests` when following an HTTP redirect.
            object policy = a.Length > 1 && a[1] is not PyNone ? a[1]
                : kwargs is not null && kwargs.TryGetValue("policy", out var p) && p is not PyNone ? p
                : new PyInstance(CookiePolicyClass);
            inst.Dict["_policy"] = policy;
            return PyNone.Instance;
        });

        Add("set_policy", (_, a, _) =>
        {
            ((PyInstance)a[0]).Dict["_policy"] = a[1];
            return PyNone.Instance;
        });
        Add("get_policy", (_, a, _) =>
        {
            var inst = (PyInstance)a[0];
            return inst.Dict.TryGet("_policy", out var p) ? p : PyNone.Instance;
        });

        Add("set_cookie", (_, a, _) =>
        {
            var jar = Jar((PyInstance)a[0]);
            var cookie = a[1];
            string CKey(object c) => $"{AttrStr(c, "domain", "")}\0{AttrStr(c, "path", "/")}\0{AttrStr(c, "name", "")}";
            string newKey = CKey(cookie);
            jar.RemoveAll(c => CKey(c) == newKey);
            jar.Add(cookie);
            return PyNone.Instance;
        });

        Add("__iter__", (_, a, _) => new PyIterator(Jar((PyInstance)a[0]).GetEnumerator()));
        Add("__len__", (_, a, _) => new BigInteger(Jar((PyInstance)a[0]).Count));

        Add("clear", (_, a, _) =>
        {
            var jar = Jar((PyInstance)a[0]);
            string? domain = a.Length > 1 && a[1] is string d ? d : null;
            string? path = a.Length > 2 && a[2] is string p ? p : null;
            string? name = a.Length > 3 && a[3] is string n ? n : null;
            if (domain is null)
            {
                jar.Clear();
                return PyNone.Instance;
            }
            jar.RemoveAll(c => AttrStr(c, "domain", "") == domain
                && (path is null || AttrStr(c, "path", "/") == path)
                && (name is null || AttrStr(c, "name", "") == name));
            return PyNone.Instance;
        });

        Add("extract_cookies", (interp, a, _) =>
        {
            var response = a[1];
            var request = a[2];
            var info = interp.CallMethod(response, "info", Array.Empty<object>());
            var setCookieHeaders = interp.CallMethod(info, "get_all", new object[] { "Set-Cookie", new PyList() });
            string requestHost = (string)interp.CallMethod(request, "get_host", Array.Empty<object>());
            string requestPath = RequestPath(interp, request);
            foreach (var headerVal in PyOps.Iterate(interp, setCookieHeaders))
            {
                if (headerVal is not string headerStr)
                    continue;
                var cookie = ParseSetCookie(headerStr, requestHost, requestPath);
                if (cookie is not null)
                    interp.CallMethod(a[0], "set_cookie", new object[] { cookie });
            }
            return PyNone.Instance;
        });

        Add("add_cookie_header", (interp, a, _) =>
        {
            var request = a[1];
            string requestHost = (string)interp.CallMethod(request, "get_host", Array.Empty<object>());
            string requestPath = RequestPath(interp, request);
            bool isSecure = (string)interp.CallMethod(request, "get_type", Array.Empty<object>()) == "https";
            long now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();

            var parts = new List<string>();
            foreach (var cookie in Jar((PyInstance)a[0]))
            {
                if (CookieMatches(cookie, requestHost, requestPath, isSecure, now))
                    parts.Add($"{AttrStr(cookie, "name", "")}={AttrStr(cookie, "value", "")}");
            }
            if (parts.Count > 0)
            {
                bool hasHeader = PyOps.Truthy(interp,
                    interp.CallMethod(request, "has_header", new object[] { "Cookie" }));
                if (!hasHeader)
                    interp.CallMethod(request, "add_unredirected_header",
                        new object[] { "Cookie", string.Join("; ", parts) });
            }
            return PyNone.Instance;
        });

        return cls;
    }

    private static string RequestPath(Interp interp, object request)
    {
        string fullUrl = (string)interp.CallMethod(request, "get_full_url", Array.Empty<object>());
        var (_, _, path, _, _) = UrllibModule.SplitUrl(fullUrl, true);
        return path.Length == 0 ? "/" : path;
    }

    /// <summary>RFC 6265 §5.1.3 domain-match, plus real CPython's own extension: an empty cookie
    /// domain (httpx's `Cookies.set(name, value)` default) matches every request host — since
    /// `str.endswith("")` is always true in real CPython's own domain_return_ok, an empty domain is
    /// effectively a wildcard.</summary>
    private static bool DomainMatch(string host, string cookieDomain, bool hostOnly)
    {
        if (cookieDomain.Length == 0)
            return true;
        host = host.ToLowerInvariant();
        string domain = cookieDomain.TrimStart('.').ToLowerInvariant();
        if (host == domain)
            return true;
        if (hostOnly)
            return false;
        return host.EndsWith("." + domain, StringComparison.Ordinal);
    }

    /// <summary>RFC 6265 §5.1.4 path-match.</summary>
    private static bool PathMatch(string requestPath, string cookiePath)
    {
        if (requestPath == cookiePath)
            return true;
        if (!requestPath.StartsWith(cookiePath, StringComparison.Ordinal))
            return false;
        if (cookiePath.EndsWith('/'))
            return true;
        return requestPath.Length > cookiePath.Length && requestPath[cookiePath.Length] == '/';
    }

    private static bool CookieMatches(object cookie, string requestHost, string requestPath, bool isSecure, long now)
    {
        if (Attr(cookie, "expires") is BigInteger exp && (long)exp < now)
            return false;
        string domain = AttrStr(cookie, "domain", "");
        bool domainSpecified = AttrBool(cookie, "domain_specified");
        if (!DomainMatch(requestHost, domain, !domainSpecified))
            return false;
        if (!PathMatch(requestPath, AttrStr(cookie, "path", "/")))
            return false;
        if (AttrBool(cookie, "secure") && !isSecure)
            return false;
        return true;
    }

    /// <summary>Real Set-Cookie response-header parser (name=value; Domain=; Path=; Expires=;
    /// Max-Age=; Secure; HttpOnly), scoped to the attributes real target apps set — no RFC 2965
    /// legacy attributes (Version, Comment, Port, CommentURL).</summary>
    private static PyInstance? ParseSetCookie(string header, string requestHost, string requestPath)
    {
        var parts = header.Split(';');
        var nv = parts[0].Split('=', 2);
        if (nv.Length != 2)
            return null;
        string name = nv[0].Trim();
        string value = nv[1].Trim();

        string? domain = null;
        string? path = null;
        bool secure = false;
        long? expires = null;

        for (int i = 1; i < parts.Length; i++)
        {
            string p = parts[i].Trim();
            if (p.Length == 0)
                continue;
            int eq = p.IndexOf('=');
            string attrName = (eq >= 0 ? p[..eq] : p).Trim().ToLowerInvariant();
            string attrVal = eq >= 0 ? p[(eq + 1)..].Trim() : "";
            switch (attrName)
            {
                case "domain":
                    domain = attrVal.ToLowerInvariant();
                    break;
                case "path":
                    path = attrVal;
                    break;
                case "secure":
                    secure = true;
                    break;
                case "max-age":
                    if (long.TryParse(attrVal, out var secs))
                        expires = DateTimeOffset.UtcNow.ToUnixTimeSeconds() + secs;
                    break;
                case "expires":
                    if (expires is null && DateTimeOffset.TryParse(attrVal,
                        System.Globalization.CultureInfo.InvariantCulture,
                        System.Globalization.DateTimeStyles.AssumeUniversal, out var dt))
                        expires = dt.ToUnixTimeSeconds();
                    break;
            }
        }

        bool domainSpecified = domain is not null;
        string effectiveDomain = domain ?? requestHost;
        string effectivePath = path ?? DefaultCookiePath(requestPath);

        var inst = new PyInstance(CookieClass);
        inst.Dict["version"] = BigInteger.Zero;
        inst.Dict["name"] = name;
        inst.Dict["value"] = value;
        inst.Dict["port"] = PyNone.Instance;
        inst.Dict["port_specified"] = false;
        inst.Dict["domain"] = effectiveDomain;
        inst.Dict["domain_specified"] = domainSpecified;
        inst.Dict["domain_initial_dot"] = domain?.StartsWith('.') ?? false;
        inst.Dict["path"] = effectivePath;
        inst.Dict["path_specified"] = path is not null;
        inst.Dict["secure"] = secure;
        inst.Dict["expires"] = expires is null ? PyNone.Instance : new BigInteger(expires.Value);
        inst.Dict["discard"] = expires is null;
        inst.Dict["comment"] = PyNone.Instance;
        inst.Dict["comment_url"] = PyNone.Instance;
        inst.Dict["rest"] = new PyDict();
        inst.Dict["rfc2109"] = false;
        return inst;
    }

    private static string DefaultCookiePath(string requestPath)
    {
        int lastSlash = requestPath.LastIndexOf('/');
        return lastSlash <= 0 ? "/" : requestPath[..lastSlash];
    }
}
