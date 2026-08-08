// Copyright (c) 2026 Marco Parenzan
//
// Licensed under the MIT License. See the LICENSE file in the project
// root for full license information.

using PySharpLib.Interpretation;
using PySharpLib.Runtime;

namespace PySharpLib.Modules;

/// <summary>
/// http: HTTPStatus (a real IntEnum, with real `.phrase` per member — built by hand in C# rather
/// than replicating CPython's `__new__(cls, value, phrase, description='')` tuple-unpacking Enum
/// idiom, which PySharp's enum machinery doesn't support in general; nothing in scope needs that
/// generality beyond this one case) and http.cookies (SimpleCookie/BaseCookie/Morsel, a real,
/// if simplified, port of CPython's own Lib/http/cookies.py — real quoting/unquoting, real
/// Set-Cookie formatting). Found via starlette's real `import http` (exceptions.py) and `import
/// http.cookies` (responses.py/requests.py), itself a real dependency chain of `import starlette`.
/// See FASTAPI_PLAN.md Phase 3.
/// </summary>
public static class HttpModule
{
    // (code, name, phrase) — the standard IANA-registered set, matching real CPython's HTTPStatus.
    private static readonly (int Code, string Name, string Phrase)[] Statuses =
    {
        (100, "CONTINUE", "Continue"),
        (101, "SWITCHING_PROTOCOLS", "Switching Protocols"),
        (102, "PROCESSING", "Processing"),
        (103, "EARLY_HINTS", "Early Hints"),
        (200, "OK", "OK"),
        (201, "CREATED", "Created"),
        (202, "ACCEPTED", "Accepted"),
        (203, "NON_AUTHORITATIVE_INFORMATION", "Non-Authoritative Information"),
        (204, "NO_CONTENT", "No Content"),
        (205, "RESET_CONTENT", "Reset Content"),
        (206, "PARTIAL_CONTENT", "Partial Content"),
        (207, "MULTI_STATUS", "Multi-Status"),
        (208, "ALREADY_REPORTED", "Already Reported"),
        (226, "IM_USED", "IM Used"),
        (300, "MULTIPLE_CHOICES", "Multiple Choices"),
        (301, "MOVED_PERMANENTLY", "Moved Permanently"),
        (302, "FOUND", "Found"),
        (303, "SEE_OTHER", "See Other"),
        (304, "NOT_MODIFIED", "Not Modified"),
        (305, "USE_PROXY", "Use Proxy"),
        (307, "TEMPORARY_REDIRECT", "Temporary Redirect"),
        (308, "PERMANENT_REDIRECT", "Permanent Redirect"),
        (400, "BAD_REQUEST", "Bad Request"),
        (401, "UNAUTHORIZED", "Unauthorized"),
        (402, "PAYMENT_REQUIRED", "Payment Required"),
        (403, "FORBIDDEN", "Forbidden"),
        (404, "NOT_FOUND", "Not Found"),
        (405, "METHOD_NOT_ALLOWED", "Method Not Allowed"),
        (406, "NOT_ACCEPTABLE", "Not Acceptable"),
        (407, "PROXY_AUTHENTICATION_REQUIRED", "Proxy Authentication Required"),
        (408, "REQUEST_TIMEOUT", "Request Timeout"),
        (409, "CONFLICT", "Conflict"),
        (410, "GONE", "Gone"),
        (411, "LENGTH_REQUIRED", "Length Required"),
        (412, "PRECONDITION_FAILED", "Precondition Failed"),
        (413, "REQUEST_ENTITY_TOO_LARGE", "Request Entity Too Large"),
        (414, "REQUEST_URI_TOO_LONG", "Request-URI Too Long"),
        (415, "UNSUPPORTED_MEDIA_TYPE", "Unsupported Media Type"),
        (416, "REQUESTED_RANGE_NOT_SATISFIABLE", "Requested Range Not Satisfiable"),
        (417, "EXPECTATION_FAILED", "Expectation Failed"),
        (418, "IM_A_TEAPOT", "I'm a Teapot"),
        (421, "MISDIRECTED_REQUEST", "Misdirected Request"),
        (422, "UNPROCESSABLE_ENTITY", "Unprocessable Entity"),
        (423, "LOCKED", "Locked"),
        (424, "FAILED_DEPENDENCY", "Failed Dependency"),
        (425, "TOO_EARLY", "Too Early"),
        (426, "UPGRADE_REQUIRED", "Upgrade Required"),
        (428, "PRECONDITION_REQUIRED", "Precondition Required"),
        (429, "TOO_MANY_REQUESTS", "Too Many Requests"),
        (431, "REQUEST_HEADER_FIELDS_TOO_LARGE", "Request Header Fields Too Large"),
        (451, "UNAVAILABLE_FOR_LEGAL_REASONS", "Unavailable For Legal Reasons"),
        (500, "INTERNAL_SERVER_ERROR", "Internal Server Error"),
        (501, "NOT_IMPLEMENTED", "Not Implemented"),
        (502, "BAD_GATEWAY", "Bad Gateway"),
        (503, "SERVICE_UNAVAILABLE", "Service Unavailable"),
        (504, "GATEWAY_TIMEOUT", "Gateway Timeout"),
        (505, "HTTP_VERSION_NOT_SUPPORTED", "HTTP Version Not Supported"),
        (506, "VARIANT_ALSO_NEGOTIATES", "Variant Also Negotiates"),
        (507, "INSUFFICIENT_STORAGE", "Insufficient Storage"),
        (508, "LOOP_DETECTED", "Loop Detected"),
        (510, "NOT_EXTENDED", "Not Extended"),
        (511, "NETWORK_AUTHENTICATION_REQUIRED", "Network Authentication Required"),
    };

    public static PyModule Create(Interp interp)
    {
        var m = new PyModule("http") { Builtins = interp.BuiltinsModule };

        var src = new System.Text.StringBuilder();
        src.Append("from enum import IntEnum\nclass HTTPStatus(IntEnum):\n");
        foreach (var (code, name, _) in Statuses)
            src.Append($"    {name} = {code}\n");
        interp.RunModule(Parsing.Parser.Parse(src.ToString()), m);

        if (m.Dict.TryGet("HTTPStatus", out var hsObj) && hsObj is PyClass hs
            && hs.Dict.TryGet("__members__", out var membersObj) && membersObj is PyDict members)
        {
            var phraseByCode = Statuses.ToDictionary(s => (System.Numerics.BigInteger)s.Code, s => s.Phrase);
            foreach (var e in members.Entries)
            {
                var member = (PyInstance)e.Value;
                if (member.Dict.TryGet("value", out var v) && v is System.Numerics.BigInteger code
                    && phraseByCode.TryGetValue(code, out var phrase))
                {
                    member.Dict["phrase"] = phrase;
                    member.Dict["description"] = "";
                }
                m.Dict[(string)e.Key] = member;
            }
        }

        m.Dict["cookies"] = CookiesModule.Create(interp);
        return m;
    }
}

/// <summary>http.cookies: a real (simplified) port of CPython's Lib/http/cookies.py — real
/// quoting (`_quote`) and unquoting (`_unquote`, found via starlette's real `http_cookies._unquote`
/// direct call in requests.py's cookie_parser) and real Set-Cookie formatting via Morsel/BaseCookie/
/// SimpleCookie, matching real attribute names and ordering. `.load()` is a straightforward
/// semicolon-split parser rather than CPython's exact tokenizing regex — real for the common case,
/// simplified for RFC edge cases nothing in scope has exercised (starlette itself doesn't even use
/// `.load()`, it parses cookies with its own `cookie_parser`). Real CPython's Morsel/BaseCookie
/// subclass `dict` directly; here they hold their own internal dict instead of inheriting from
/// `dict` — PySharp's `class X(dict):` doesn't back subclass instances with real storage yet (a
/// separate, standing interpreter gap, not something worth taking on just for this), so real dict
/// inheritance would silently misbehave. Everything starlette actually calls (`cookie[key] =
/// value`, `cookie[key]["path"] = ...`, `.output()`) behaves identically either way; only
/// `isinstance(cookie, dict)` would differ from real CPython, and nothing in scope checks that.</summary>
internal static class CookiesModule
{
    public static PyModule Create(Interp interp)
    {
        var m = new PyModule("http.cookies") { Builtins = interp.BuiltinsModule };
        interp.RunModule(Parsing.Parser.Parse(Source), m);
        return m;
    }

    private const string Source = """
        import re

        _LegalChars = ("ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789"
                       + "!#$%&'*+-.^_`|~:")

        def _is_legal_key(s):
            if len(s) == 0:
                return False
            for ch in s:
                if ch not in _LegalChars:
                    return False
            return True

        def _quote(s):
            if s is None:
                return s
            if _is_legal_key(s):
                return s
            escaped = s.replace("\\", "\\\\").replace('"', '\\"')
            return '"' + escaped + '"'

        _OctalPatt = re.compile(r"\\[0-3][0-7][0-7]")
        _QuotePatt = re.compile(r"[\\].")

        def _unquote(s):
            if s is None or len(s) < 2:
                return s
            if s[0] != '"' or s[-1] != '"':
                return s
            s = s[1:-1]
            i = 0
            n = len(s)
            res = []
            while 0 <= i < n:
                o_match = _OctalPatt.search(s, i)
                q_match = _QuotePatt.search(s, i)
                if not o_match and not q_match:
                    res.append(s[i:])
                    break
                j = -1
                k = -1
                if o_match:
                    j = o_match.start(0)
                if q_match:
                    k = q_match.start(0)
                if q_match and (not o_match or k < j):
                    res.append(s[i:k])
                    res.append(s[k + 1])
                    i = k + 2
                else:
                    res.append(s[i:j])
                    res.append(chr(int(s[j + 1:j + 4], 8)))
                    i = j + 4
            return "".join(res)

        class Morsel:
            # Real CPython class attributes, not module-level names — starlette's real responses.py
            # patches this directly (`http.cookies.Morsel._reserved["samesite"] = "SameSite"`), so it
            # must be reachable as an attribute of the class itself, not just closed over by methods.
            _reserved = {
                "expires": "expires",
                "path": "Path",
                "comment": "Comment",
                "domain": "Domain",
                "max-age": "Max-Age",
                "secure": "Secure",
                "httponly": "HttpOnly",
                "version": "Version",
                "samesite": "SameSite",
                "partitioned": "Partitioned",
            }
            _flags = {"secure", "httponly", "partitioned"}

            def __init__(self):
                self.key = None
                self.value = None
                self.coded_value = None
                self._attrs = {}
                for k in self._reserved:
                    self._attrs[k] = ""

            def __setitem__(self, key, value):
                key = key.lower()
                if key not in self._reserved:
                    raise KeyError(f"Invalid attribute {key!r}")
                self._attrs[key] = value

            def __getitem__(self, key):
                return self._attrs[key.lower()]

            def get(self, key, default=None):
                return self._attrs.get(key.lower(), default)

            def set(self, key, value, coded_value):
                self.key = key
                self.value = value
                self.coded_value = coded_value

            def OutputString(self, attrs=None):
                result = [f"{self.key}={self.coded_value}"]
                if attrs is None:
                    attrs = self._reserved
                for key in self._reserved:
                    value = self._attrs.get(key, "")
                    if value == "" or value is False:
                        continue
                    if key not in attrs:
                        continue
                    if key == "max-age" and isinstance(value, int):
                        result.append(f"{self._reserved[key]}={value}")
                    elif key in self._flags:
                        if value:
                            result.append(self._reserved[key])
                    else:
                        result.append(f"{self._reserved[key]}={value}")
                return "; ".join(result)

            def output(self, attrs=None, header="Set-Cookie:"):
                return f"{header} {self.OutputString(attrs)}"

            def __str__(self):
                return self.output()

            def __repr__(self):
                return f"<Morsel: {self.key}={self.value}>"

        class BaseCookie:
            def __init__(self):
                self._morsels = {}

            def value_decode(self, val):
                return val, val

            def value_encode(self, val):
                strval = str(val)
                return strval, _quote(strval)

            def __set(self, key, real_value, coded_value):
                m = Morsel()
                m.set(key, real_value, coded_value)
                self._morsels[key] = m

            def __setitem__(self, key, value):
                if isinstance(value, Morsel):
                    self._morsels[key] = value
                else:
                    rval, cval = self.value_encode(value)
                    self.__set(key, rval, cval)

            def __getitem__(self, key):
                return self._morsels[key]

            def __contains__(self, key):
                return key in self._morsels

            def __iter__(self):
                return iter(self._morsels)

            def __len__(self):
                return len(self._morsels)

            def keys(self):
                return self._morsels.keys()

            def items(self):
                return self._morsels.items()

            def get(self, key, default=None):
                return self._morsels.get(key, default)

            def output(self, attrs=None, header="Set-Cookie:", sep="\r\n"):
                return sep.join(self._morsels[key].output(attrs, header) for key in self._morsels)

            def __str__(self):
                return self.output()

            def load(self, rawdata):
                if isinstance(rawdata, dict):
                    for key, value in rawdata.items():
                        self[key] = value
                    return
                for part in rawdata.split(";"):
                    part = part.strip()
                    if not part or "=" not in part:
                        continue
                    key, _, value = part.partition("=")
                    key = key.strip()
                    value = value.strip()
                    if key.lower() in Morsel._reserved:
                        continue
                    self[key] = _unquote(value)

        class SimpleCookie(BaseCookie):
            pass
        """;
}
