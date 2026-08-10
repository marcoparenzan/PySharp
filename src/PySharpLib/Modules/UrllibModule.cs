// Copyright (c) 2026 Marco Parenzan
//
// Licensed under the MIT License. See the LICENSE file in the project
// root for full license information.

using System.Text;
using PySharpLib.Runtime;

namespace PySharpLib.Modules;

/// <summary>urllib + urllib.parse: quote/unquote/urlencode/urlparse (per SAS token e websocket).</summary>
public static class UrllibModule
{
    public static PyModule Create()
    {
        var urllib = new PyModule("urllib");
        urllib.Dict["parse"] = CreateParse();
        urllib.Dict["request"] = CreateRequest();
        return urllib;
    }

    /// <summary>urllib.request: getproxies (a stub — just what paho needs), a real `parse_http_list`
    /// (RFC 2616 §4.2/§14.45 comma-separated header-value-list parsing, respecting quoted commas) —
    /// found via real httpx's `_auth.py` (`from urllib.request import parse_http_list`), used to
    /// split a `WWW-Authenticate`-style header into its comma-separated auth-challenge fields — and a
    /// real `Request` (found via real httpx's `_models.py`'s `Cookies._CookieCompatRequest`, which
    /// subclasses it to give `http.cookiejar.CookieJar` the interface it expects).</summary>
    public static PyModule CreateRequest()
    {
        var m = new PyModule("urllib.request");
        m.Dict["getproxies"] = new PyBuiltinFunction("getproxies", (_, _, _) => GetProxiesFromEnvironment());
        m.Dict["getproxies_environment"] = new PyBuiltinFunction("getproxies_environment", (_, _, _) => GetProxiesFromEnvironment());
        m.Dict["parse_http_list"] = new PyBuiltinFunction("parse_http_list", (_, a, _) =>
            new PyList(ParseHttpList((string)a[0]).Cast<object>().ToList()));
        // Real (env-var-driven) proxy bypass check — found via real requests' own `compat.py`
        // (`from urllib.request import ..., proxy_bypass, proxy_bypass_environment`), reachable
        // from `import requests`. `proxy_bypass` itself is platform-specific in real CPython
        // (consults the Windows registry / macOS SystemConfiguration on those platforms); this
        // module scopes to the portable NO_PROXY-environment-variable check both platforms share.
        m.Dict["proxy_bypass_environment"] = new PyBuiltinFunction("proxy_bypass_environment", (_, a, _) =>
            ProxyBypassEnvironment((string)a[0]));
        m.Dict["proxy_bypass"] = new PyBuiltinFunction("proxy_bypass", (_, a, _) =>
            ProxyBypassEnvironment((string)a[0]));
        m.Dict["Request"] = RequestClass;
        return m;
    }

    private static PyDict GetProxiesFromEnvironment()
    {
        var result = new PyDict();
        foreach (var scheme in new[] { "http", "https", "ftp", "all", "no" })
        {
            string? value = Environment.GetEnvironmentVariable($"{scheme}_proxy")
                ?? Environment.GetEnvironmentVariable($"{scheme.ToUpperInvariant()}_PROXY");
            if (!string.IsNullOrEmpty(value))
                result[scheme] = value;
        }
        return result;
    }

    private static bool ProxyBypassEnvironment(string host)
    {
        string? noProxy = Environment.GetEnvironmentVariable("no_proxy") ?? Environment.GetEnvironmentVariable("NO_PROXY");
        if (string.IsNullOrEmpty(noProxy))
            return false;
        string hostOnly = host.Split(':')[0];
        foreach (var raw in noProxy.Split(','))
        {
            string entry = raw.Trim();
            if (entry.Length == 0)
                continue;
            if (entry == "*")
                return true;
            entry = entry.TrimStart('.');
            if (hostOnly.Equals(entry, StringComparison.OrdinalIgnoreCase)
                || hostOnly.EndsWith("." + entry, StringComparison.OrdinalIgnoreCase))
                return true;
        }
        return false;
    }

    public static readonly PyClass RequestClass = BuildRequestClass();

    private static object Arg(object[] a, Dictionary<string, object>? kwargs, string name, int pos, object def)
        => pos < a.Length ? a[pos] : kwargs is not null && kwargs.TryGetValue(name, out var v) ? v : def;

    private static bool DictTryGetCI(PyDict d, string name, out object value)
    {
        foreach (var e in d.Entries)
        {
            if (e.Key is string k && string.Equals(k, name, StringComparison.OrdinalIgnoreCase))
            {
                value = e.Value;
                return true;
            }
        }
        value = PyNone.Instance;
        return false;
    }

    /// <summary>Real (not stubbed) urllib.request.Request — scoped to the interface
    /// http.cookiejar.CookieJar's add_cookie_header/extract_cookies actually calls (get_full_url,
    /// get_method, get_type, get_host, get_origin_req_host, is_unverifiable, has_header, get_header,
    /// add_header, add_unredirected_header, header_items), not the full real class (no proxy/redirect
    /// machinery — nothing reachable here uses it).</summary>
    private static PyClass BuildRequestClass()
    {
        var cls = new PyClass("Request", new List<PyClass>());
        void Add(string n, BuiltinFn fn) => cls.Dict[n] = new PyBuiltinFunction($"Request.{n}", fn);

        Add("__init__", (_, a, kwargs) =>
        {
            var inst = (PyInstance)a[0];
            object urlArg = Arg(a, kwargs, "url", 1, PyNone.Instance);
            if (urlArg is PyNone)
                throw PyErr.TypeError("Request() missing required argument: 'url'");
            string url = (string)urlArg;
            object data = Arg(a, kwargs, "data", 2, PyNone.Instance);
            object headersObj = Arg(a, kwargs, "headers", 3, PyNone.Instance);
            object origin = Arg(a, kwargs, "origin_req_host", 4, PyNone.Instance);
            object unverifiable = Arg(a, kwargs, "unverifiable", 5, false);
            object method = Arg(a, kwargs, "method", 6, PyNone.Instance);

            var (scheme, netloc, _, _, _) = SplitUrl(url, true);

            inst.Dict["full_url"] = url;
            inst.Dict["type"] = scheme;
            inst.Dict["host"] = netloc;
            var hdrs = new PyDict();
            if (headersObj is PyDict hd)
                foreach (var e in hd.Entries)
                    hdrs[e.Key] = e.Value;
            inst.Dict["headers"] = hdrs;
            inst.Dict["unredirected_hdrs"] = new PyDict();
            inst.Dict["origin_req_host"] = origin is PyNone ? netloc : origin;
            inst.Dict["unverifiable"] = unverifiable;
            inst.Dict["data"] = data;
            inst.Dict["method"] = method is PyNone ? (data is PyNone ? "GET" : "POST") : method;
            return PyNone.Instance;
        });

        Add("get_full_url", (_, a, _) => ((PyInstance)a[0]).Dict["full_url"]);
        Add("get_method", (_, a, _) => ((PyInstance)a[0]).Dict["method"]);
        Add("get_type", (_, a, _) => ((PyInstance)a[0]).Dict["type"]);
        Add("get_host", (_, a, _) => ((PyInstance)a[0]).Dict["host"]);
        Add("get_origin_req_host", (_, a, _) => ((PyInstance)a[0]).Dict["origin_req_host"]);
        Add("is_unverifiable", (_, a, _) => ((PyInstance)a[0]).Dict["unverifiable"]);

        Add("has_header", (_, a, _) =>
        {
            var inst = (PyInstance)a[0];
            string name = (string)a[1];
            return DictTryGetCI((PyDict)inst.Dict["headers"], name, out _)
                || DictTryGetCI((PyDict)inst.Dict["unredirected_hdrs"], name, out _);
        });

        Add("get_header", (_, a, _) =>
        {
            var inst = (PyInstance)a[0];
            string name = (string)a[1];
            object def = a.Length > 2 ? a[2] : PyNone.Instance;
            if (DictTryGetCI((PyDict)inst.Dict["headers"], name, out var v1))
                return v1;
            return DictTryGetCI((PyDict)inst.Dict["unredirected_hdrs"], name, out var v2) ? v2 : def;
        });

        Add("add_header", (_, a, _) =>
        {
            ((PyDict)((PyInstance)a[0]).Dict["headers"])[a[1]] = a[2];
            return PyNone.Instance;
        });

        Add("add_unredirected_header", (_, a, _) =>
        {
            ((PyDict)((PyInstance)a[0]).Dict["unredirected_hdrs"])[a[1]] = a[2];
            return PyNone.Instance;
        });

        Add("header_items", (_, a, _) =>
        {
            var inst = (PyInstance)a[0];
            var items = new List<object>();
            foreach (var e in ((PyDict)inst.Dict["headers"]).Entries)
                items.Add(new PyTuple(new object[] { e.Key, e.Value }));
            foreach (var e in ((PyDict)inst.Dict["unredirected_hdrs"]).Entries)
                items.Add(new PyTuple(new object[] { e.Key, e.Value }));
            return new PyList(items);
        });

        return cls;
    }

    /// <summary>Real CPython's `urllib.parse._decode_args` treats a falsy `qs` argument (`None`,
    /// `""`) as an empty string rather than raising — found via real httpx's own `_urls.py`
    /// (`QueryParams.__init__`: `parse_qs(value, keep_blank_values=True)` where `value` is `None`
    /// when no query params were given at all, e.g. constructing a `Client()`/`TestClient(app)` with
    /// no explicit `params=`).</summary>
    private static string CoerceQs(object o) => o switch
    {
        null or PyNone => "",
        string s => s,
        _ => throw PyErr.TypeError("Cannot mix str and non-str arguments"),
    };

    private static List<(string Key, string Value)> ParseQsl(string qs, bool keepBlank)
    {
        var result = new List<(string, string)>();
        foreach (var pair in qs.Split('&', ';'))
        {
            if (pair.Length == 0)
                continue;
            int eq = pair.IndexOf('=');
            string key = eq >= 0 ? pair[..eq] : pair;
            string value = eq >= 0 ? pair[(eq + 1)..] : "";
            if (eq < 0 && !keepBlank)
                continue;
            if (value.Length == 0 && !keepBlank && eq >= 0)
                continue;
            result.Add((Unquote(key.Replace('+', ' ')), Unquote(value.Replace('+', ' '))));
        }
        return result;
    }

    /// <summary>Direct port of CPython's own urllib.request.parse_http_list.</summary>
    private static List<string> ParseHttpList(string s)
    {
        var res = new List<string>();
        var part = new StringBuilder();
        bool escape = false, quote = false;

        foreach (char cur in s)
        {
            if (escape)
            {
                part.Append(cur);
                escape = false;
                continue;
            }
            if (quote)
            {
                if (cur == '\\')
                {
                    escape = true;
                    continue;
                }
                if (cur == '"')
                    quote = false;
                part.Append(cur);
                continue;
            }
            if (cur == ',')
            {
                res.Add(part.ToString());
                part.Clear();
                continue;
            }
            if (cur == '"')
                quote = true;
            part.Append(cur);
        }
        if (part.Length > 0)
            res.Add(part.ToString());

        return res.Select(p => p.Trim()).ToList();
    }

    public static PyModule CreateParse()
    {
        var m = new PyModule("urllib.parse");
        var d = m.Dict;

        d["quote"] = new PyBuiltinFunction("quote", (_, a, kwargs) =>
        {
            string s = a[0] as string ?? Encoding.UTF8.GetString(CryptoModules.AsBytes(a[0]));
            string safe = a.Length > 1 ? (string)a[1]
                : kwargs is not null && kwargs.TryGetValue("safe", out var sf) ? (string)sf
                : "/";
            return Quote(s, safe);
        });

        d["quote_plus"] = new PyBuiltinFunction("quote_plus", (_, a, _) =>
        {
            string s = a[0] as string ?? Encoding.UTF8.GetString(CryptoModules.AsBytes(a[0]));
            return Quote(s, "").Replace("%20", "+");
        });

        d["unquote"] = new PyBuiltinFunction("unquote", (_, a, _) => Unquote((string)a[0]));
        d["unquote_plus"] = new PyBuiltinFunction("unquote_plus", (_, a, _) =>
            Unquote(((string)a[0]).Replace('+', ' ')));

        d["urlencode"] = new PyBuiltinFunction("urlencode", (interp, a, _) =>
        {
            // Real urlencode: a bytes key/value is quoted from its raw bytes directly, not from
            // Python's `str(bytes_obj)` repr ("b'...'") — found live via real requests' own
            // `models.py` (`_encode_params`: `k.encode("utf-8")`/`v.encode("utf-8")` pairs handed
            // straight to `urlencode(result, doseq=True)`), reachable from `import requests`.
            string EncodeComponent(object v) => v is PyBytes b
                ? Quote(Encoding.UTF8.GetString(b.Data), "")
                : Quote(PyOps.Str(interp, v), "");

            var parts = new List<string>();
            if (a[0] is PyDict dict)
            {
                foreach (var e in dict.Entries)
                    parts.Add($"{EncodeComponent(e.Key)}={EncodeComponent(e.Value)}");
            }
            else
            {
                foreach (var pair in PyOps.Iterate(interp, a[0]))
                {
                    var kv = PyOps.Iterate(interp, pair).ToList();
                    parts.Add($"{EncodeComponent(kv[0])}={EncodeComponent(kv[1])}");
                }
            }
            return string.Join("&", parts);
        });

        // Real CPython: urlparse() is urlsplit() plus a real ParseResult (attribute *and* index
        // access, matching real SplitResult's own shape) — reusing the same SplitUrl() algorithm
        // already backing urlsplit()/urljoin() rather than the separate, less thorough ad-hoc
        // parser this used to have (which, unlike SplitUrl, didn't handle e.g. a bare path with no
        // "://" the same way). Found live via real requests' own `cookies.py` (`MockRequest.__init__`
        // reading `urlparse(url).scheme` as a real attribute — a plain tuple has no `.scheme`),
        // reachable from `import requests`.
        d["ParseResult"] = ParseResultClass;
        d["urlparse"] = new PyBuiltinFunction("urlparse", (interp, a, kwargs) =>
        {
            string url = (string)a[0];
            bool allowFragments = a.Length > 2 ? PyOps.Truthy(interp, a[2])
                : kwargs is null || !kwargs.TryGetValue("allow_fragments", out var af) || PyOps.Truthy(interp, af);
            var (scheme, netloc, path, query, fragment) = SplitUrl(url, allowFragments);
            return MakeParseResult(scheme, netloc, path, query, fragment);
        });
        d["SplitResult"] = SplitResultClass;
        d["urlsplit"] = new PyBuiltinFunction("urlsplit", (interp, a, kwargs) =>
        {
            string url = (string)a[0];
            bool allowFragments = a.Length > 2 ? PyOps.Truthy(interp, a[2])
                : kwargs is null || !kwargs.TryGetValue("allow_fragments", out var af) || PyOps.Truthy(interp, af);
            var (scheme, netloc, path, query, fragment) = SplitUrl(url, allowFragments);
            return MakeSplitResult(scheme, netloc, path, query, fragment);
        });
        d["urldefrag"] = new PyBuiltinFunction("urldefrag", (_, a, _) =>
        {
            string url = (string)a[0];
            int hash = url.IndexOf('#');
            return hash >= 0
                ? new PyTuple(new object[] { url[..hash], url[(hash + 1)..] })
                : new PyTuple(new object[] { url, "" });
        });

        d["urlunsplit"] = new PyBuiltinFunction("urlunsplit", (_, a, _) =>
        {
            var t = ComponentsOf(a[0], 5, "urlunsplit");
            return UnparseUrl(StrOrEmpty(t[0]), StrOrEmpty(t[1]), StrOrEmpty(t[2]), StrOrEmpty(t[3]), StrOrEmpty(t[4]));
        });

        d["urlunparse"] = new PyBuiltinFunction("urlunparse", (_, a, _) =>
        {
            var t = ComponentsOf(a[0], 6, "urlunparse");
            string scheme = StrOrEmpty(t[0]), netloc = StrOrEmpty(t[1]), path = StrOrEmpty(t[2]);
            string parameters = StrOrEmpty(t[3]), query = StrOrEmpty(t[4]), fragment = StrOrEmpty(t[5]);
            if (parameters.Length > 0)
                path = $"{path};{parameters}";
            return UnparseUrl(scheme, netloc, path, query, fragment);
        });

        d["urljoin"] = new PyBuiltinFunction("urljoin", (interp, a, kwargs) =>
        {
            string bs = (string)a[0], url = (string)a[1];
            bool allowFragments = a.Length > 2 ? PyOps.Truthy(interp, a[2])
                : kwargs is null || !kwargs.TryGetValue("allow_fragments", out var af) || PyOps.Truthy(interp, af);
            return UrlJoin(bs, url, allowFragments);
        });

        d["parse_qsl"] = new PyBuiltinFunction("parse_qsl", (interp, a, kwargs) =>
        {
            string qs = CoerceQs(a[0]);
            bool keepBlank = kwargs is not null && kwargs.TryGetValue("keep_blank_values", out var kb) && PyOps.Truthy(interp, kb);
            var pairs = ParseQsl(qs, keepBlank);
            return new PyList(pairs.Select(p => (object)new PyTuple(new object[] { p.Key, p.Value })).ToList());
        });

        d["parse_qs"] = new PyBuiltinFunction("parse_qs", (interp, a, kwargs) =>
        {
            string qs = CoerceQs(a[0]);
            bool keepBlank = kwargs is not null && kwargs.TryGetValue("keep_blank_values", out var kb) && PyOps.Truthy(interp, kb);
            var dict = new PyDict();
            foreach (var (key, value) in ParseQsl(qs, keepBlank))
            {
                if (dict.TryGet(key, out var existing) && existing is PyList list)
                    list.Items.Add(value);
                else
                    dict[key] = new PyList(new List<object> { value });
            }
            return dict;
        });

        return m;
    }

    /// <summary>Real (not stubbed) urlsplit algorithm — ported from CPython's own, scoped to the
    /// common `scheme://netloc/path?query#fragment` / `path?query` shapes real code actually builds
    /// (no RFC-3986 percent-encoded-scheme edge cases, no scheme-specific netloc allowlist). Found
    /// via starlette's real `urlsplit`/`SplitResult` usage (datastructures.URL). See
    /// FASTAPI_PLAN.md.</summary>
    internal static (string Scheme, string Netloc, string Path, string Query, string Fragment) SplitUrl(string url, bool allowFragments)
    {
        string scheme = "", netloc = "", query = "", fragment = "";

        int colon = url.IndexOf(':');
        if (colon > 0 && char.IsLetter(url[0])
            && url[..colon].All(c => char.IsLetterOrDigit(c) || c is '+' or '-' or '.'))
        {
            scheme = url[..colon].ToLowerInvariant();
            url = url[(colon + 1)..];
        }

        if (url.StartsWith("//", StringComparison.Ordinal))
        {
            int end = url.Length;
            foreach (char stop in "/?#")
            {
                int idx = url.IndexOf(stop, 2);
                if (idx >= 0 && idx < end)
                    end = idx;
            }
            netloc = url[2..end];
            url = url[end..];
        }

        if (allowFragments)
        {
            int hash = url.IndexOf('#');
            if (hash >= 0)
            {
                fragment = url[(hash + 1)..];
                url = url[..hash];
            }
        }

        int q = url.IndexOf('?');
        if (q >= 0)
        {
            query = url[(q + 1)..];
            url = url[..q];
        }

        return (scheme, netloc, url, query, fragment);
    }

    /// <summary>Real (not stubbed) urljoin — a direct port of CPython's own Lib/urllib/parse.py
    /// algorithm (RFC 3986 §5 relative resolution), scoped to the no-`;params` shape our urlparse/
    /// urlsplit already use throughout this file. Found via real starlette's testclient.py
    /// (`urljoin("ws://testserver", url)`), needed to construct a real TestClient — the next step
    /// past route registration/openapi() on the FASTAPI_PLAN.md path. See CPython's own
    /// `uses_relative`/`uses_netloc` scheme allowlists, reproduced verbatim below.</summary>
    private static readonly string[] UsesRelative = { "", "ftp", "http", "gopher", "nntp", "imap",
        "wais", "file", "https", "shttp", "mms", "prospero", "rtsp", "rtspu", "sftp", "svn",
        "svn+ssh", "ws", "wss" };
    private static readonly string[] UsesNetloc = { "", "ftp", "http", "gopher", "nntp", "telnet",
        "imap", "wais", "file", "mms", "https", "shttp", "snews", "prospero", "rtsp", "rtspu",
        "rsync", "svn", "svn+ssh", "sftp", "nfs", "git", "git+ssh", "ws", "wss" };

    /// <summary>Real port of CPython's urlunsplit — notably, when a netloc is present (or implied by
    /// the scheme), a path that doesn't already start with '/' gets one forced on, e.g. joining
    /// "http://example.com" + "path" must yield "http://example.com/path", not
    /// "http://example.compath".</summary>
    /// <summary>Real CPython's urlunsplit/urlunparse never actually coerce a falsy (in particular
    /// None) component — they only ever test it with `if component:` before conditionally
    /// appending it, so a None query/fragment (real urllib3's own `Url` namedtuple leaves unset
    /// fields as None, unlike this project's own urlsplit which always returns "") silently
    /// contributes nothing rather than being a type error. Found live via real requests' own
    /// `models.py` (`urlunparse((scheme, netloc, path, "", query, fragment))` with a real `None`
    /// fragment from urllib3's `parse_url`), reachable from `import requests`.</summary>
    private static string StrOrEmpty(object o) => o as string ?? "";

    private static object[] ComponentsOf(object arg, int expected, string fnName)
    {
        var items = arg switch
        {
            PyTuple pt => pt.Items,
            PyList pl => pl.Items.ToArray(),
            _ => throw PyErr.TypeError($"{fnName}() argument must be a {expected}-item iterable"),
        };
        if (items.Length != expected)
            throw PyErr.ValueError($"{fnName}(): expected {expected} components, got {items.Length}");
        return items;
    }

    private static string UnparseUrl(string scheme, string netloc, string path, string query, string fragment)
    {
        string url = path;
        if (netloc.Length > 0 || (scheme.Length > 0 && UsesNetloc.Contains(scheme) && !url.StartsWith("//", StringComparison.Ordinal)))
        {
            if (url.Length > 0 && url[0] != '/')
                url = "/" + url;
            url = "//" + netloc + url;
        }
        if (scheme.Length > 0)
            url = $"{scheme}:{url}";
        if (query.Length > 0)
            url += $"?{query}";
        if (fragment.Length > 0)
            url += $"#{fragment}";
        return url;
    }

    private static string UrlJoin(string bs, string url, bool allowFragments)
    {
        if (bs.Length == 0)
            return url;
        if (url.Length == 0)
            return bs;

        var (bscheme, bnetloc, bpath, bquery, bfragment) = SplitUrl(bs, allowFragments);
        var (scheme, netloc, path, query, fragment) = SplitUrl(url, allowFragments);
        if (scheme.Length == 0)
            scheme = bscheme;

        if (scheme != bscheme || !UsesRelative.Contains(scheme))
            return url;
        if (UsesNetloc.Contains(scheme))
        {
            if (netloc.Length > 0)
                return UnparseUrl(scheme, netloc, path, query, fragment);
            netloc = bnetloc;
        }

        if (path.Length == 0)
        {
            path = bpath;
            if (query.Length == 0)
                query = bquery;
            return UnparseUrl(scheme, netloc, path, query, fragment);
        }

        var baseParts = bpath.Split('/').ToList();
        if (baseParts[^1].Length != 0)
            baseParts.RemoveAt(baseParts.Count - 1);

        List<string> segments;
        if (path.StartsWith('/'))
        {
            segments = path.Split('/').ToList();
        }
        else
        {
            segments = baseParts.Concat(path.Split('/')).ToList();
            // Ports Python's in-place slice assignment `segments[1:-1] = filter(None, ...)`. When
            // segments has exactly one element, index 0 and index -1 are the SAME element, so the
            // slice [1:-1] is empty and the list must be left untouched — naively concatenating
            // "first element" + middle + "last element" would duplicate that single element.
            if (segments.Count > 1)
            {
                string head = segments[0], tail = segments[^1];
                var middle = segments.Skip(1).SkipLast(1).Where(s => s.Length > 0);
                segments = new List<string> { head }.Concat(middle).Append(tail).ToList();
            }
        }

        var resolved = new List<string>();
        foreach (var seg in segments)
        {
            if (seg == "..")
            {
                if (resolved.Count > 0)
                    resolved.RemoveAt(resolved.Count - 1);
            }
            else if (seg == ".")
                continue;
            else
                resolved.Add(seg);
        }
        if (segments[^1] is "." or "..")
            resolved.Add("");

        string joinedPath = string.Join('/', resolved);
        return UnparseUrl(scheme, netloc, joinedPath.Length > 0 ? joinedPath : "/", query, fragment);
    }

    private static readonly string[] SplitResultFields = { "scheme", "netloc", "path", "query", "fragment" };
    public static readonly PyClass SplitResultClass = BuildSplitResultClass();

    private static PyClass BuildSplitResultClass()
    {
        var cls = new PyClass("SplitResult", new List<PyClass>());
        void Add(string n, BuiltinFn fn) => cls.Dict[n] = new PyBuiltinFunction($"SplitResult.{n}", fn);

        Add("__init__", (_, a, kwargs) =>
        {
            var inst = (PyInstance)a[0];
            for (int i = 0; i < SplitResultFields.Length; i++)
            {
                object value = i + 1 < a.Length ? a[i + 1]
                    : kwargs is not null && kwargs.TryGetValue(SplitResultFields[i], out var v) ? v
                    : throw PyErr.TypeError($"SplitResult() missing argument: '{SplitResultFields[i]}'");
                inst.Dict[SplitResultFields[i]] = value;
            }
            return PyNone.Instance;
        });
        Add("geturl", (_, a, _) =>
        {
            var inst = (PyInstance)a[0];
            string scheme = (string)inst.Dict["scheme"], netloc = (string)inst.Dict["netloc"],
                path = (string)inst.Dict["path"], query = (string)inst.Dict["query"], fragment = (string)inst.Dict["fragment"];
            string url = netloc.Length > 0 || path.StartsWith("//", StringComparison.Ordinal) ? $"//{netloc}{path}" : path;
            if (scheme.Length > 0)
                url = $"{scheme}:{url}";
            if (query.Length > 0)
                url += $"?{query}";
            if (fragment.Length > 0)
                url += $"#{fragment}";
            return url;
        });
        Add("__getitem__", (_, a, _) =>
        {
            var inst = (PyInstance)a[0];
            int i = (int)PyOps.AsBigInt(a[1], "index");
            if (i < 0)
                i += SplitResultFields.Length;
            if (i < 0 || i >= SplitResultFields.Length)
                throw PyErr.IndexError("SplitResult index out of range");
            return inst.Dict[SplitResultFields[i]];
        });
        Add("__len__", (_, _, _) => new System.Numerics.BigInteger(SplitResultFields.Length));
        Add("__iter__", (_, a, _) =>
        {
            var inst = (PyInstance)a[0];
            return new PyIterator(SplitResultFields.Select(f => inst.Dict[f]).GetEnumerator());
        });
        Add("__eq__", (interp, a, _) =>
        {
            var inst = (PyInstance)a[0];
            var self = SplitResultFields.Select(f => inst.Dict[f]).ToArray();
            var other = a[1] switch
            {
                PyInstance oi when oi.Class == cls => SplitResultFields.Select(f => oi.Dict[f]).ToArray(),
                PyTuple t => t.Items,
                _ => null,
            };
            return other is not null && self.Length == other.Length && self.Zip(other).All(p => interp.RichEquals(p.First, p.Second));
        });
        Add("__repr__", (interp, a, _) =>
        {
            var inst = (PyInstance)a[0];
            return $"SplitResult({string.Join(", ", SplitResultFields.Select(f => $"{f}={PyOps.Repr(interp, inst.Dict[f])}"))})";
        });

        (string? User, string? Pass, string Host, int? Port) SplitNetloc(PyInstance inst)
        {
            string netloc = (string)inst.Dict["netloc"];
            string? user = null, pass = null;
            int at = netloc.LastIndexOf('@');
            if (at >= 0)
            {
                string userinfo = netloc[..at];
                netloc = netloc[(at + 1)..];
                int c = userinfo.IndexOf(':');
                user = c >= 0 ? userinfo[..c] : userinfo;
                pass = c >= 0 ? userinfo[(c + 1)..] : null;
            }
            string host = netloc;
            int? port = null;
            if (netloc.StartsWith('[') && netloc.Contains(']'))
            {
                int close = netloc.IndexOf(']');
                host = netloc[1..close];
                string rest = netloc[(close + 1)..];
                if (rest.StartsWith(':') && int.TryParse(rest[1..], out var p1))
                    port = p1;
            }
            else
            {
                int colon = netloc.LastIndexOf(':');
                if (colon >= 0 && int.TryParse(netloc[(colon + 1)..], out var p2))
                {
                    host = netloc[..colon];
                    port = p2;
                }
            }
            return (user, pass, host.ToLowerInvariant(), port);
        }

        cls.Dict["hostname"] = new PyProperty { Getter = new PyBuiltinFunction("SplitResult.hostname", (_, a, _) =>
        {
            var (_, _, host, _) = SplitNetloc((PyInstance)a[0]);
            return host.Length == 0 ? (object)PyNone.Instance : host;
        }) };
        cls.Dict["port"] = new PyProperty { Getter = new PyBuiltinFunction("SplitResult.port", (_, a, _) =>
        {
            var (_, _, _, port) = SplitNetloc((PyInstance)a[0]);
            return port is null ? (object)PyNone.Instance : new System.Numerics.BigInteger(port.Value);
        }) };
        cls.Dict["username"] = new PyProperty { Getter = new PyBuiltinFunction("SplitResult.username", (_, a, _) =>
        {
            var (user, _, _, _) = SplitNetloc((PyInstance)a[0]);
            return user is null ? (object)PyNone.Instance : user;
        }) };
        cls.Dict["password"] = new PyProperty { Getter = new PyBuiltinFunction("SplitResult.password", (_, a, _) =>
        {
            var (_, pass, _, _) = SplitNetloc((PyInstance)a[0]);
            return pass is null ? (object)PyNone.Instance : pass;
        }) };

        return cls;
    }

    private static PyInstance MakeSplitResult(string scheme, string netloc, string path, string query, string fragment)
    {
        var inst = new PyInstance(SplitResultClass);
        inst.Dict["scheme"] = scheme;
        inst.Dict["netloc"] = netloc;
        inst.Dict["path"] = path;
        inst.Dict["query"] = query;
        inst.Dict["fragment"] = fragment;
        return inst;
    }

    // ---------------------------------------------------------------- ParseResult
    //
    // Real CPython's urlparse() returns a namedtuple-*and*-attribute-access ParseResult (scheme,
    // netloc, path, params, query, fragment — one extra field vs SplitResult: the real, rarely-used
    // ";params" path segment), with the same real hostname/port/username/password properties as
    // SplitResult. Deliberately a parallel implementation (not a shared base with SplitResult)
    // rather than a refactor of the already-working, tested SplitResult builder under time
    // pressure — same project convention as SQL_PLAN.md's "template, not shared base class until a
    // second real use proves what's genuinely identical". Found live via real requests' own
    // `cookies.py`/`utils.py` (`MockRequest.__init__` reading `urlparse(url).scheme` as a real
    // attribute, not a tuple index), reachable from `import requests`.
    private static readonly string[] ParseResultFields = { "scheme", "netloc", "path", "params", "query", "fragment" };
    public static readonly PyClass ParseResultClass = BuildParseResultClass();

    private static PyClass BuildParseResultClass()
    {
        var cls = new PyClass("ParseResult", new List<PyClass>());
        void Add(string n, BuiltinFn fn) => cls.Dict[n] = new PyBuiltinFunction($"ParseResult.{n}", fn);

        Add("__init__", (_, a, kwargs) =>
        {
            var inst = (PyInstance)a[0];
            for (int i = 0; i < ParseResultFields.Length; i++)
            {
                object value = i + 1 < a.Length ? a[i + 1]
                    : kwargs is not null && kwargs.TryGetValue(ParseResultFields[i], out var v) ? v
                    : throw PyErr.TypeError($"ParseResult() missing argument: '{ParseResultFields[i]}'");
                inst.Dict[ParseResultFields[i]] = value;
            }
            return PyNone.Instance;
        });
        Add("geturl", (_, a, _) =>
        {
            var inst = (PyInstance)a[0];
            string scheme = (string)inst.Dict["scheme"], netloc = (string)inst.Dict["netloc"],
                path = (string)inst.Dict["path"], parameters = (string)inst.Dict["params"],
                query = (string)inst.Dict["query"], fragment = (string)inst.Dict["fragment"];
            if (parameters.Length > 0)
                path = $"{path};{parameters}";
            string url = netloc.Length > 0 || path.StartsWith("//", StringComparison.Ordinal) ? $"//{netloc}{path}" : path;
            if (scheme.Length > 0)
                url = $"{scheme}:{url}";
            if (query.Length > 0)
                url += $"?{query}";
            if (fragment.Length > 0)
                url += $"#{fragment}";
            return url;
        });
        Add("__getitem__", (_, a, _) =>
        {
            var inst = (PyInstance)a[0];
            int i = (int)PyOps.AsBigInt(a[1], "index");
            if (i < 0)
                i += ParseResultFields.Length;
            if (i < 0 || i >= ParseResultFields.Length)
                throw PyErr.IndexError("ParseResult index out of range");
            return inst.Dict[ParseResultFields[i]];
        });
        Add("__len__", (_, _, _) => new System.Numerics.BigInteger(ParseResultFields.Length));
        Add("__iter__", (_, a, _) =>
        {
            var inst = (PyInstance)a[0];
            return new PyIterator(ParseResultFields.Select(f => inst.Dict[f]).GetEnumerator());
        });
        Add("__eq__", (interp, a, _) =>
        {
            var inst = (PyInstance)a[0];
            var self = ParseResultFields.Select(f => inst.Dict[f]).ToArray();
            var other = a[1] switch
            {
                PyInstance oi when oi.Class == cls => ParseResultFields.Select(f => oi.Dict[f]).ToArray(),
                PyTuple t => t.Items,
                _ => null,
            };
            return other is not null && self.Length == other.Length && self.Zip(other).All(p => interp.RichEquals(p.First, p.Second));
        });
        Add("__repr__", (interp, a, _) =>
        {
            var inst = (PyInstance)a[0];
            return $"ParseResult({string.Join(", ", ParseResultFields.Select(f => $"{f}={PyOps.Repr(interp, inst.Dict[f])}"))})";
        });

        (string? User, string? Pass, string Host, int? Port) SplitNetloc(PyInstance inst)
        {
            string netloc = (string)inst.Dict["netloc"];
            string? user = null, pass = null;
            int at = netloc.LastIndexOf('@');
            if (at >= 0)
            {
                string userinfo = netloc[..at];
                netloc = netloc[(at + 1)..];
                int c = userinfo.IndexOf(':');
                user = c >= 0 ? userinfo[..c] : userinfo;
                pass = c >= 0 ? userinfo[(c + 1)..] : null;
            }
            string host = netloc;
            int? port = null;
            if (netloc.StartsWith('[') && netloc.Contains(']'))
            {
                int close = netloc.IndexOf(']');
                host = netloc[1..close];
                string rest = netloc[(close + 1)..];
                if (rest.StartsWith(':') && int.TryParse(rest[1..], out var p1))
                    port = p1;
            }
            else
            {
                int colon = netloc.LastIndexOf(':');
                if (colon >= 0 && int.TryParse(netloc[(colon + 1)..], out var p2))
                {
                    host = netloc[..colon];
                    port = p2;
                }
            }
            return (user, pass, host.ToLowerInvariant(), port);
        }

        cls.Dict["hostname"] = new PyProperty { Getter = new PyBuiltinFunction("ParseResult.hostname", (_, a, _) =>
        {
            var (_, _, host, _) = SplitNetloc((PyInstance)a[0]);
            return host.Length == 0 ? (object)PyNone.Instance : host;
        }) };
        cls.Dict["port"] = new PyProperty { Getter = new PyBuiltinFunction("ParseResult.port", (_, a, _) =>
        {
            var (_, _, _, port) = SplitNetloc((PyInstance)a[0]);
            return port is null ? (object)PyNone.Instance : new System.Numerics.BigInteger(port.Value);
        }) };
        cls.Dict["username"] = new PyProperty { Getter = new PyBuiltinFunction("ParseResult.username", (_, a, _) =>
        {
            var (user, _, _, _) = SplitNetloc((PyInstance)a[0]);
            return user is null ? (object)PyNone.Instance : user;
        }) };
        cls.Dict["password"] = new PyProperty { Getter = new PyBuiltinFunction("ParseResult.password", (_, a, _) =>
        {
            var (_, pass, _, _) = SplitNetloc((PyInstance)a[0]);
            return pass is null ? (object)PyNone.Instance : pass;
        }) };

        return cls;
    }

    private static PyInstance MakeParseResult(string scheme, string netloc, string path, string query, string fragment)
    {
        var inst = new PyInstance(ParseResultClass);
        inst.Dict["scheme"] = scheme;
        inst.Dict["netloc"] = netloc;
        inst.Dict["path"] = path;
        inst.Dict["params"] = "";
        inst.Dict["query"] = query;
        inst.Dict["fragment"] = fragment;
        return inst;
    }

    private static string Quote(string s, string safe)
    {
        var sb = new StringBuilder();
        foreach (byte b in Encoding.UTF8.GetBytes(s))
        {
            char c = (char)b;
            bool unreserved = c is (>= 'A' and <= 'Z') or (>= 'a' and <= 'z') or (>= '0' and <= '9')
                or '-' or '_' or '.' or '~';
            if (unreserved || safe.Contains(c))
                sb.Append(c);
            else
                sb.Append($"%{b:X2}");
        }
        return sb.ToString();
    }

    private static string Unquote(string s)
    {
        var bytes = new List<byte>();
        int i = 0;
        while (i < s.Length)
        {
            if (s[i] == '%' && i + 2 < s.Length && Uri.IsHexDigit(s[i + 1]) && Uri.IsHexDigit(s[i + 2]))
            {
                bytes.Add((byte)(Uri.FromHex(s[i + 1]) * 16 + Uri.FromHex(s[i + 2])));
                i += 3;
                continue;
            }
            bytes.AddRange(Encoding.UTF8.GetBytes(s[i].ToString()));
            i++;
        }
        return Encoding.UTF8.GetString(bytes.ToArray());
    }
}
