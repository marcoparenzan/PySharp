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

    /// <summary>urllib.request: getproxies (a stub — just what paho needs) plus a real
    /// `parse_http_list` (RFC 2616 §4.2/§14.45 comma-separated header-value-list parsing, respecting
    /// quoted commas) — found via real httpx's `_auth.py` (`from urllib.request import
    /// parse_http_list`), used to split a `WWW-Authenticate`-style header into its comma-separated
    /// auth-challenge fields.</summary>
    public static PyModule CreateRequest()
    {
        var m = new PyModule("urllib.request");
        m.Dict["getproxies"] = new PyBuiltinFunction("getproxies", (_, _, _) => new PyDict());
        m.Dict["parse_http_list"] = new PyBuiltinFunction("parse_http_list", (_, a, _) =>
            new PyList(ParseHttpList((string)a[0]).Cast<object>().ToList()));
        return m;
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
            var parts = new List<string>();
            if (a[0] is PyDict dict)
            {
                foreach (var e in dict.Entries)
                    parts.Add($"{Quote(PyOps.Str(interp, e.Key), "")}={Quote(PyOps.Str(interp, e.Value), "")}");
            }
            else
            {
                foreach (var pair in PyOps.Iterate(interp, a[0]))
                {
                    var kv = PyOps.Iterate(interp, pair).ToList();
                    parts.Add($"{Quote(PyOps.Str(interp, kv[0]), "")}={Quote(PyOps.Str(interp, kv[1]), "")}");
                }
            }
            return string.Join("&", parts);
        });

        d["urlparse"] = new PyBuiltinFunction("urlparse", (_, a, _) =>
        {
            string url = (string)a[0];
            string scheme = "", netloc = "", path = "", query = "", fragment = "";
            int i = url.IndexOf("://", StringComparison.Ordinal);
            if (i >= 0)
            {
                scheme = url[..i];
                url = url[(i + 3)..];
                int slash = url.IndexOf('/');
                if (slash >= 0)
                {
                    netloc = url[..slash];
                    url = url[slash..];
                }
                else
                {
                    netloc = url;
                    url = "";
                }
            }
            int hash = url.IndexOf('#');
            if (hash >= 0)
            {
                fragment = url[(hash + 1)..];
                url = url[..hash];
            }
            int q = url.IndexOf('?');
            if (q >= 0)
            {
                query = url[(q + 1)..];
                url = url[..q];
            }
            path = url;
            return new PyTuple(new object[] { scheme, netloc, path, "", query, fragment });
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
        d["urljoin"] = new PyBuiltinFunction("urljoin", (interp, a, kwargs) =>
        {
            string bs = (string)a[0], url = (string)a[1];
            bool allowFragments = a.Length > 2 ? PyOps.Truthy(interp, a[2])
                : kwargs is null || !kwargs.TryGetValue("allow_fragments", out var af) || PyOps.Truthy(interp, af);
            return UrlJoin(bs, url, allowFragments);
        });

        d["parse_qsl"] = new PyBuiltinFunction("parse_qsl", (interp, a, kwargs) =>
        {
            string qs = (string)a[0];
            bool keepBlank = kwargs is not null && kwargs.TryGetValue("keep_blank_values", out var kb) && PyOps.Truthy(interp, kb);
            var result = new List<object>();
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
                result.Add(new PyTuple(new object[] { Unquote(key.Replace('+', ' ')), Unquote(value.Replace('+', ' ')) }));
            }
            return new PyList(result);
        });

        return m;
    }

    /// <summary>Real (not stubbed) urlsplit algorithm — ported from CPython's own, scoped to the
    /// common `scheme://netloc/path?query#fragment` / `path?query` shapes real code actually builds
    /// (no RFC-3986 percent-encoded-scheme edge cases, no scheme-specific netloc allowlist). Found
    /// via starlette's real `urlsplit`/`SplitResult` usage (datastructures.URL). See
    /// FASTAPI_PLAN.md.</summary>
    private static (string Scheme, string Netloc, string Path, string Query, string Fragment) SplitUrl(string url, bool allowFragments)
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
