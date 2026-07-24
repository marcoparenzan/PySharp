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

    /// <summary>urllib.request stub: only what paho needs (getproxies).</summary>
    public static PyModule CreateRequest()
    {
        var m = new PyModule("urllib.request");
        m.Dict["getproxies"] = new PyBuiltinFunction("getproxies", (_, _, _) => new PyDict());
        return m;
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

        return m;
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
