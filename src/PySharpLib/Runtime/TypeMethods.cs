// Copyright (c) 2026 Marco Parenzan
//
// Licensed under the MIT License. See the LICENSE file in the project
// root for full license information.

using System.Numerics;
using System.Text;
using PySharpLib.Interpretation;

namespace PySharpLib.Runtime;

/// <summary>Dispatch of the attributes/methods of builtin types.</summary>
public static class TypeMethods
{
    public static bool TryGetBuiltinAttr(Interp interp, object obj, string name, out object value)
    {
        // str.upper, dict.get, ... : unbound method on the builtin "class"
        if (obj is PyBuiltinFunction typeFn)
        {
            var typeTable = typeFn.Name switch
            {
                "str" => StrModules.Table,
                "list" => ListMethods.Table,
                "dict" => DictMethods.Table,
                "tuple" => TupleMethods.Table,
                "set" => SetMethods.Table,
                "bytes" => BytesMethods.Table,
                "bytearray" => ByteArrayMethods.Table,
                "type" => TypeConstructorMethods.Table,
                "chain" => ChainMethods.Table,
                _ => null,
            };
            if (typeTable is not null && typeTable.TryGetValue(name, out var unbound))
            {
                value = unbound;
                return true;
            }
            // Universal fallback (was unreachable before: this branch always returned before falling
            // through to the shared one below, meaning `some_builtin_function.__class__` — e.g. after
            // pydantic's real `typing.NewType(...)` stub returns a builtin type object directly —
            // always raised AttributeError instead of returning the "function"/"type" pseudo-class).
            if (name == "__class__")
            {
                value = PySharpLib.Builtins.BuiltinsFactory.TypeNamePseudoClass(interp, obj);
                return true;
            }
            value = PyNone.Instance;
            return false;
        }

        var table = obj switch
        {
            string => StrModules.Table,
            PyList => ListMethods.Table,
            PyDict => DictMethods.Table,
            PyTuple => TupleMethods.Table,
            PySet => SetMethods.Table,
            PyBytes => BytesMethods.Table,
            PyByteArray => ByteArrayMethods.Table,
            PyGenerator => GeneratorMethods.Table,
            PyIterator => IteratorMethods.Table,
            PyRange => RangeMethods.Table,
            // Task derives from Future and shares its method surface, plus get_name/set_name — the
            // PyTask case must come first, since it's also a PyFuture and switch patterns match in
            // order.
            PyTask => Modules.AsyncioModule.TaskTable,
            PyFuture => Modules.AsyncioModule.FutureTable,
            PyCoroutine => Modules.AsyncioModule.CoroutineTable,
            PyAsyncGenerator => Modules.AsyncioModule.AsyncGeneratorTable,
            PyEventLoop => Modules.AsyncioModule.EventLoopTable,
            ConcurrentFuture => Modules.ConcurrentModule.FutureTable,
            _ => null,
        };
        if (table is not null && table.TryGetValue(name, out var fn))
        {
            value = new PyBoundMethod(obj, fn);
            return true;
        }
        // data attributes (not methods)
        if (obj is PyRange r)
        {
            switch (name)
            {
                case "start": value = r.Start; return true;
                case "stop": value = r.Stop; return true;
                case "step": value = r.Step; return true;
            }
        }
        // Real CPython's Future/Task keep the owning loop in a private `_loop` attribute, read
        // directly (bypassing get_loop()) by real library code for perf. Found via anyio's real
        // `_backends/_asyncio.py` WorkerThread.__init__: `self.loop = root_task._loop`.
        if (obj is PyFuture fut && name == "_loop")
        {
            value = (object?)fut.Loop ?? PyNone.Instance;
            return true;
        }
        // Universal fallback: `x.__class__` for any builtin value (PyInstance has its own, correct,
        // earlier in Interp.GetAttr's switch — this only runs for everything else: None, str, int,
        // list, ...). Found via a real `NoneType = None.__class__` idiom (pydantic/typing.py).
        if (name == "__class__")
        {
            value = PySharpLib.Builtins.BuiltinsFactory.TypeNamePseudoClass(interp, obj);
            return true;
        }
        value = PyNone.Instance;
        return false;
    }

    // shared helpers
    internal static object Self(object[] args) => args[0];

    internal static object Arg(object[] args, int i, object? def = null)
        => i < args.Length ? args[i] : def ?? throw PyErr.TypeError("missing required argument");

    internal static object? OptArg(object[] args, int i)
        => i < args.Length ? args[i] : null;

    internal static string StrArg(object o, string what)
        => o as string ?? throw PyErr.TypeError($"{what} must be str, not {PyOps.TypeName(o)}");
}

public static class StrModules
{
    public static readonly Dictionary<string, PyBuiltinFunction> Table = Build();

    private static Dictionary<string, PyBuiltinFunction> Build()
    {
        var t = new Dictionary<string, PyBuiltinFunction>();
        void Add(string name, BuiltinFn fn) => t[name] = new PyBuiltinFunction($"str.{name}", fn);

        Add("upper", (_, a, _) => S(a).ToUpperInvariant());
        Add("lower", (_, a, _) => S(a).ToLowerInvariant());
        Add("casefold", (_, a, _) => S(a).ToLowerInvariant());
        Add("capitalize", (_, a, _) =>
        {
            string s = S(a);
            return s.Length == 0 ? s : char.ToUpperInvariant(s[0]) + s[1..].ToLowerInvariant();
        });
        Add("title", (_, a, _) =>
        {
            var sb = new StringBuilder();
            bool prevAlpha = false;
            foreach (char c in S(a))
            {
                sb.Append(prevAlpha ? char.ToLowerInvariant(c) : char.ToUpperInvariant(c));
                prevAlpha = char.IsLetter(c);
            }
            return sb.ToString();
        });
        Add("strip", (_, a, _) => StripImpl(S(a), TypeMethods.OptArg(a, 1), both: true, left: false));
        Add("lstrip", (_, a, _) => StripImpl(S(a), TypeMethods.OptArg(a, 1), both: false, left: true));
        Add("rstrip", (_, a, _) => StripImpl(S(a), TypeMethods.OptArg(a, 1), both: false, left: false));
        Add("startswith", (interp, a, _) => MatchesAffix(S(a), a.Length > 1 ? a[1] : "", prefix: true));
        Add("endswith", (interp, a, _) => MatchesAffix(S(a), a.Length > 1 ? a[1] : "", prefix: false));
        Add("find", (_, a, _) => new BigInteger(FindImpl(S(a), a, forward: true)));
        Add("rfind", (_, a, _) => new BigInteger(FindImpl(S(a), a, forward: false)));
        Add("index", (_, a, _) =>
        {
            int i = FindImpl(S(a), a, forward: true);
            if (i < 0)
                throw PyErr.ValueError("substring not found");
            return new BigInteger(i);
        });
        Add("rindex", (_, a, _) =>
        {
            int i = FindImpl(S(a), a, forward: false);
            if (i < 0)
                throw PyErr.ValueError("substring not found");
            return new BigInteger(i);
        });
        Add("count", (_, a, _) =>
        {
            string s = S(a), sub = TypeMethods.StrArg(a[1], "sub");
            if (sub.Length == 0)
                return new BigInteger(s.Length + 1);
            int count = 0, pos = 0;
            while ((pos = s.IndexOf(sub, pos, StringComparison.Ordinal)) >= 0)
            {
                count++;
                pos += sub.Length;
            }
            return new BigInteger(count);
        });
        Add("replace", (_, a, _) =>
        {
            string s = S(a);
            string oldS = TypeMethods.StrArg(a[1], "old");
            string newS = TypeMethods.StrArg(a[2], "new");
            if (a.Length > 3)
            {
                int count = (int)PyOps.AsBigInt(a[3], "count");
                var sb = new StringBuilder();
                int pos = 0;
                while (count-- > 0)
                {
                    int idx = s.IndexOf(oldS, pos, StringComparison.Ordinal);
                    if (idx < 0)
                        break;
                    sb.Append(s, pos, idx - pos).Append(newS);
                    pos = idx + oldS.Length;
                }
                sb.Append(s, pos, s.Length - pos);
                return sb.ToString();
            }
            return oldS.Length == 0 ? s : s.Replace(oldS, newS);
        });
        Add("split", (_, a, _) => SplitImpl(S(a), a, fromRight: false));
        Add("rsplit", (_, a, _) => SplitImpl(S(a), a, fromRight: true));
        Add("splitlines", (_, a, _) =>
            new PyList(S(a).Replace("\r\n", "\n").Replace('\r', '\n')
                .Split('\n') is var lines && lines.Length > 0 && lines[^1].Length == 0
                ? lines[..^1].Select(x => (object)x)
                : lines.Select(x => (object)x)));
        Add("join", (interp, a, _) =>
        {
            string sep = S(a);
            var parts = new List<string>();
            foreach (var item in PyOps.Iterate(interp, a[1]))
            {
                parts.Add(item as string
                          ?? throw PyErr.TypeError(
                              $"sequence item {parts.Count}: expected str instance, {PyOps.TypeName(item)} found"));
            }
            return string.Join(sep, parts);
        });
        Add("partition", (_, a, _) =>
        {
            string s = S(a), sep = TypeMethods.StrArg(a[1], "sep");
            int i = s.IndexOf(sep, StringComparison.Ordinal);
            return i < 0
                ? new PyTuple(new object[] { s, "", "" })
                : new PyTuple(new object[] { s[..i], sep, s[(i + sep.Length)..] });
        });
        Add("rpartition", (_, a, _) =>
        {
            string s = S(a), sep = TypeMethods.StrArg(a[1], "sep");
            int i = s.LastIndexOf(sep, StringComparison.Ordinal);
            return i < 0
                ? new PyTuple(new object[] { "", "", s })
                : new PyTuple(new object[] { s[..i], sep, s[(i + sep.Length)..] });
        });
        Add("zfill", (_, a, _) =>
        {
            string s = S(a);
            int width = (int)PyOps.AsBigInt(a[1], "width");
            if (s.Length >= width)
                return s;
            string sign = s.StartsWith('-') || s.StartsWith('+') ? s[..1] : "";
            return sign + s[sign.Length..].PadLeft(width - sign.Length, '0');
        });
        Add("ljust", (_, a, _) => S(a).PadRight((int)PyOps.AsBigInt(a[1], "width"), FillChar(a)));
        Add("rjust", (_, a, _) => S(a).PadLeft((int)PyOps.AsBigInt(a[1], "width"), FillChar(a)));
        Add("center", (_, a, _) =>
        {
            string s = S(a);
            int width = (int)PyOps.AsBigInt(a[1], "width");
            char fill = FillChar(a);
            if (s.Length >= width)
                return s;
            int total = width - s.Length;
            int left = total / 2;
            return new string(fill, left) + s + new string(fill, total - left);
        });
        Add("encode", (_, a, kw) =>
        {
            string encoding = a.Length > 1 ? TypeMethods.StrArg(a[1], "encoding")
                : kw is not null && kw.TryGetValue("encoding", out var e) ? TypeMethods.StrArg(e, "encoding")
                : "utf-8";
            return new PyBytes(GetEncoding(encoding).GetBytes(S(a)));
        });
        Add("format", (interp, a, kw) => FormatMethod(interp, S(a), a.Skip(1).ToArray(), kw));
        Add("isdigit", (_, a, _) => S(a).Length > 0 && S(a).All(char.IsDigit));
        Add("isalpha", (_, a, _) => S(a).Length > 0 && S(a).All(char.IsLetter));
        Add("isalnum", (_, a, _) => S(a).Length > 0 && S(a).All(char.IsLetterOrDigit));
        Add("isspace", (_, a, _) => S(a).Length > 0 && S(a).All(char.IsWhiteSpace));
        Add("isupper", (_, a, _) => S(a).Any(char.IsLetter) && !S(a).Any(char.IsLower));
        Add("islower", (_, a, _) => S(a).Any(char.IsLetter) && !S(a).Any(char.IsUpper));
        Add("isidentifier", (_, a, _) =>
        {
            string s = S(a);
            return s.Length > 0 && (char.IsLetter(s[0]) || s[0] == '_')
                   && s.All(c => char.IsLetterOrDigit(c) || c == '_');
        });
        Add("removeprefix", (_, a, _) =>
        {
            string s = S(a), p = TypeMethods.StrArg(a[1], "prefix");
            return s.StartsWith(p, StringComparison.Ordinal) ? s[p.Length..] : s;
        });
        Add("removesuffix", (_, a, _) =>
        {
            string s = S(a), p = TypeMethods.StrArg(a[1], "suffix");
            return p.Length > 0 && s.EndsWith(p, StringComparison.Ordinal) ? s[..^p.Length] : s;
        });
        return t;
    }

    private static string S(object[] args) => (string)args[0];

    private static char FillChar(object[] a)
        => a.Length > 2 ? TypeMethods.StrArg(a[2], "fillchar")[0] : ' ';

    internal static Encoding GetEncoding(string name) => name.ToLowerInvariant().Replace("-", "").Replace("_", "") switch
    {
        "utf8" => new UTF8Encoding(false),
        "ascii" => Encoding.ASCII,
        "latin1" or "iso88591" => Encoding.Latin1,
        "utf16" => Encoding.Unicode,
        "utf16le" => Encoding.Unicode,
        "utf16be" => Encoding.BigEndianUnicode,
        _ => throw PyErr.Raise(PyErr.LookupError, $"unknown encoding: {name}"),
    };

    private static object StripImpl(string s, object? charsArg, bool both, bool left)
    {
        if (charsArg is null or PyNone)
            return both ? s.Trim() : left ? s.TrimStart() : s.TrimEnd();
        var chars = TypeMethods.StrArg(charsArg, "chars").ToCharArray();
        return both ? s.Trim(chars) : left ? s.TrimStart(chars) : s.TrimEnd(chars);
    }

    private static object MatchesAffix(string s, object affix, bool prefix)
    {
        switch (affix)
        {
            case string sub:
                return prefix ? s.StartsWith(sub, StringComparison.Ordinal) : s.EndsWith(sub, StringComparison.Ordinal);
            case PyTuple t:
                return t.Items.Any(x => x is string sub
                    && (prefix ? s.StartsWith(sub, StringComparison.Ordinal) : s.EndsWith(sub, StringComparison.Ordinal)));
            default:
                throw PyErr.TypeError("startswith/endswith argument must be str or tuple of str");
        }
    }

    private static int FindImpl(string s, object[] a, bool forward)
    {
        string sub = TypeMethods.StrArg(a[1], "sub");
        int start = a.Length > 2 && a[2] is not PyNone ? NormIndex((int)PyOps.AsBigInt(a[2], "start"), s.Length) : 0;
        int end = a.Length > 3 && a[3] is not PyNone ? NormIndex((int)PyOps.AsBigInt(a[3], "end"), s.Length) : s.Length;
        if (start > end)
            return -1;
        string slice = s[start..end];
        int i = forward
            ? slice.IndexOf(sub, StringComparison.Ordinal)
            : slice.LastIndexOf(sub, StringComparison.Ordinal);
        return i < 0 ? -1 : i + start;
    }

    private static int NormIndex(int i, int len)
    {
        if (i < 0)
            i += len;
        return Math.Clamp(i, 0, len);
    }

    private static object SplitImpl(string s, object[] a, bool fromRight)
    {
        object? sepArg = TypeMethods.OptArg(a, 1);
        int maxSplit = a.Length > 2 && a[2] is not PyNone ? (int)PyOps.AsBigInt(a[2], "maxsplit") : -1;

        if (sepArg is null or PyNone)
        {
            // split on whitespace, consecutive groups
            var parts = s.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries).ToList();
            if (maxSplit >= 0 && parts.Count > maxSplit + 1)
            {
                if (!fromRight)
                {
                    // reassemble the tail
                    var whitespaceSplit = s.Split((char[]?)null, maxSplit + 1, StringSplitOptions.RemoveEmptyEntries);
                    parts = whitespaceSplit.ToList();
                    if (parts.Count > 0)
                        parts[^1] = parts[^1].TrimStart();
                }
                else
                {
                    var tail = parts.Skip(parts.Count - maxSplit).ToList();
                    var headJoin = string.Join(" ", parts.Take(parts.Count - maxSplit));
                    parts = new List<string> { headJoin };
                    parts.AddRange(tail);
                }
            }
            return new PyList(parts.Select(x => (object)x));
        }

        string sep = TypeMethods.StrArg(sepArg, "sep");
        if (sep.Length == 0)
            throw PyErr.ValueError("empty separator");

        var result = new List<string>();
        if (!fromRight)
        {
            int pos = 0;
            while (maxSplit != 0)
            {
                int idx = s.IndexOf(sep, pos, StringComparison.Ordinal);
                if (idx < 0)
                    break;
                result.Add(s[pos..idx]);
                pos = idx + sep.Length;
                if (maxSplit > 0)
                    maxSplit--;
            }
            result.Add(s[pos..]);
        }
        else
        {
            int pos = s.Length;
            var reversed = new List<string>();
            while (maxSplit != 0)
            {
                int idx = s.LastIndexOf(sep, Math.Max(0, pos - 1), StringComparison.Ordinal);
                if (idx < 0 || idx + sep.Length > pos)
                    break;
                reversed.Add(s[(idx + sep.Length)..pos]);
                pos = idx;
                if (maxSplit > 0)
                    maxSplit--;
            }
            reversed.Add(s[..pos]);
            reversed.Reverse();
            result = reversed;
        }
        return new PyList(result.Select(x => (object)x));
    }

    /// <summary>str.format with auto-numbered {}, {0}, {name}, conversions and format specs.</summary>
    private static string FormatMethod(Interp interp, string fmt, object[] args, Dictionary<string, object>? kwargs)
    {
        var sb = new StringBuilder();
        int auto = 0;
        int i = 0;
        while (i < fmt.Length)
        {
            char c = fmt[i];
            if (c == '{' && i + 1 < fmt.Length && fmt[i + 1] == '{')
            {
                sb.Append('{');
                i += 2;
                continue;
            }
            if (c == '}' && i + 1 < fmt.Length && fmt[i + 1] == '}')
            {
                sb.Append('}');
                i += 2;
                continue;
            }
            if (c != '{')
            {
                sb.Append(c);
                i++;
                continue;
            }

            int close = fmt.IndexOf('}', i);
            if (close < 0)
                throw PyErr.ValueError("Single '{' encountered in format string");
            string field = fmt[(i + 1)..close];
            i = close + 1;

            char conversion = '\0';
            string spec = "";
            int bang = field.IndexOf('!');
            int colon = field.IndexOf(':');
            if (colon >= 0)
            {
                spec = field[(colon + 1)..];
                field = field[..colon];
                bang = field.IndexOf('!');
            }
            if (bang >= 0)
            {
                conversion = field[bang + 1];
                field = field[..bang];
            }

            object value;
            if (field.Length == 0)
            {
                if (auto >= args.Length)
                    throw PyErr.IndexError("Replacement index out of range");
                value = args[auto++];
            }
            else if (field.All(char.IsDigit))
            {
                int idx = int.Parse(field);
                if (idx >= args.Length)
                    throw PyErr.IndexError("Replacement index out of range");
                value = args[idx];
            }
            else
            {
                // name, name.attr, name[key]
                string root = field;
                string rest = "";
                int sepIdx = field.IndexOfAny(new[] { '.', '[' });
                if (sepIdx >= 0)
                {
                    root = field[..sepIdx];
                    rest = field[sepIdx..];
                }
                if (kwargs is null || !kwargs.TryGetValue(root, out value!))
                    throw PyErr.KeyError(root);
                value = ApplyFieldPath(interp, value, rest);
            }

            value = conversion switch
            {
                'r' => PyOps.Repr(interp, value),
                's' => PyOps.Str(interp, value),
                _ => value,
            };
            sb.Append(interp.FormatValue(value, spec));
        }
        return sb.ToString();
    }

    private static object ApplyFieldPath(Interp interp, object value, string path)
    {
        int i = 0;
        while (i < path.Length)
        {
            if (path[i] == '.')
            {
                int end = i + 1;
                while (end < path.Length && path[end] != '.' && path[end] != '[')
                    end++;
                value = interp.GetAttr(value, path[(i + 1)..end]);
                i = end;
            }
            else if (path[i] == '[')
            {
                int end = path.IndexOf(']', i);
                string key = path[(i + 1)..end];
                object index = key.All(char.IsDigit) ? new BigInteger(long.Parse(key)) : key;
                value = interp.GetItem(value, index);
                i = end + 1;
            }
            else
            {
                break;
            }
        }
        return value;
    }

    /// <summary>Formatting old-style "fmt % values".</summary>
    public static string PercentFormat(Interp interp, string fmt, object right)
    {
        object[] values = right switch
        {
            PyTuple t => t.Items,
            PyDict => Array.Empty<object>(), // handled by name
            _ => new[] { right },
        };
        var dict = right as PyDict;
        var sb = new StringBuilder();
        int vi = 0;
        int i = 0;
        while (i < fmt.Length)
        {
            char c = fmt[i];
            if (c != '%')
            {
                sb.Append(c);
                i++;
                continue;
            }
            i++;
            if (i >= fmt.Length)
                throw PyErr.ValueError("incomplete format");
            if (fmt[i] == '%')
            {
                sb.Append('%');
                i++;
                continue;
            }

            // %(name)s
            object value;
            if (fmt[i] == '(')
            {
                int close = fmt.IndexOf(')', i);
                if (close < 0)
                    throw PyErr.ValueError("incomplete format key");
                string key = fmt[(i + 1)..close];
                i = close + 1;
                if (dict is null)
                    throw PyErr.TypeError("format requires a mapping");
                value = dict[key];
            }
            else
            {
                value = null!;
            }

            // flags
            bool leftAlign = false, zeroPad = false, plusSign = false, spaceSign = false, alternate = false;
            while (i < fmt.Length && fmt[i] is '-' or '0' or '+' or ' ' or '#')
            {
                switch (fmt[i])
                {
                    case '-': leftAlign = true; break;
                    case '0': zeroPad = true; break;
                    case '+': plusSign = true; break;
                    case ' ': spaceSign = true; break;
                    case '#': alternate = true; break;
                }
                i++;
            }
            int width = 0;
            bool hasWidth = false;
            if (i < fmt.Length && fmt[i] == '*')
            {
                width = (int)PyOps.AsBigInt(values[vi++], "width");
                hasWidth = true;
                i++;
            }
            else
            {
                while (i < fmt.Length && char.IsDigit(fmt[i]))
                {
                    width = width * 10 + (fmt[i++] - '0');
                    hasWidth = true;
                }
            }
            int precision = -1;
            if (i < fmt.Length && fmt[i] == '.')
            {
                i++;
                precision = 0;
                if (i < fmt.Length && fmt[i] == '*')
                {
                    precision = (int)PyOps.AsBigInt(values[vi++], "prec");
                    i++;
                }
                else
                {
                    while (i < fmt.Length && char.IsDigit(fmt[i]))
                        precision = precision * 10 + (fmt[i++] - '0');
                }
            }
            if (i >= fmt.Length)
                throw PyErr.ValueError("incomplete format");
            char conv = fmt[i++];

            if (value is null)
            {
                if (vi >= values.Length)
                    throw PyErr.TypeError("not enough arguments for format string");
                value = values[vi++];
            }

            string piece = conv switch
            {
                's' => PyOps.Str(interp, value),
                'r' => PyOps.Repr(interp, value),
                'a' => PyOps.Repr(interp, value),
                'd' or 'i' or 'u' => FormatInt(value, 10, false, plusSign, spaceSign),
                'x' => FormatInt(value, 16, false, plusSign, spaceSign, alternate ? "0x" : ""),
                'X' => FormatInt(value, 16, true, plusSign, spaceSign, alternate ? "0X" : ""),
                'o' => FormatInt(value, 8, false, plusSign, spaceSign, alternate ? "0o" : ""),
                'f' or 'F' => FormatFixed(value, precision < 0 ? 6 : precision, plusSign, spaceSign),
                'e' or 'E' => PyFormat.Format(interp, PyOps.AsDouble(value),
                    "." + (precision < 0 ? 6 : precision) + conv),
                'g' or 'G' => PyFormat.Format(interp, PyOps.AsDouble(value),
                    "." + (precision < 0 ? 6 : precision) + conv),
                'c' => value is string cs ? cs : ((char)(int)PyOps.AsBigInt(value, "char")).ToString(),
                _ => throw PyErr.ValueError($"unsupported format character '{conv}'"),
            };

            if (conv == 's' && precision >= 0 && piece.Length > precision)
                piece = piece[..precision];

            if (hasWidth && piece.Length < width)
            {
                if (leftAlign)
                    piece = piece.PadRight(width);
                else if (zeroPad && conv is 'd' or 'i' or 'u' or 'x' or 'X' or 'o' or 'f' or 'F' or 'e' or 'E' or 'g' or 'G')
                {
                    string sign = piece.StartsWith('-') || piece.StartsWith('+') || piece.StartsWith(' ') ? piece[..1] : "";
                    piece = sign + piece[sign.Length..].PadLeft(width - sign.Length, '0');
                }
                else
                    piece = piece.PadLeft(width);
            }
            sb.Append(piece);
        }
        return sb.ToString();
    }

    private static string FormatInt(object value, int numBase, bool upper, bool plus, bool space, string prefix = "")
    {
        var n = value is double d ? new BigInteger(d) : PyOps.AsBigInt(value, "int format");
        bool neg = n.Sign < 0;
        var abs = BigInteger.Abs(n);
        string digits = numBase switch
        {
            16 => AbsToBase(abs, 16),
            8 => AbsToBase(abs, 8),
            _ => abs.ToString(),
        };
        if (upper)
            digits = digits.ToUpperInvariant();
        string sign = neg ? "-" : plus ? "+" : space ? " " : "";
        return sign + prefix + digits;
    }

    private static string AbsToBase(BigInteger n, int numBase)
    {
        if (n.IsZero)
            return "0";
        const string digits = "0123456789abcdef";
        var sb = new StringBuilder();
        while (!n.IsZero)
        {
            sb.Insert(0, digits[(int)(n % numBase)]);
            n /= numBase;
        }
        return sb.ToString();
    }

    private static string FormatFixed(object value, int precision, bool plus, bool space)
    {
        double d = PyOps.AsDouble(value);
        string s = Math.Abs(d).ToString("F" + precision, System.Globalization.CultureInfo.InvariantCulture);
        string sign = d < 0 ? "-" : plus ? "+" : space ? " " : "";
        return sign + s;
    }
}

public static class ListMethods
{
    public static readonly Dictionary<string, PyBuiltinFunction> Table = Build();

    private static Dictionary<string, PyBuiltinFunction> Build()
    {
        var t = new Dictionary<string, PyBuiltinFunction>();
        void Add(string name, BuiltinFn fn) => t[name] = new PyBuiltinFunction($"list.{name}", fn);

        Add("append", (_, a, _) =>
        {
            L(a).Items.Add(a[1]);
            return PyNone.Instance;
        });
        Add("extend", (interp, a, _) =>
        {
            L(a).Items.AddRange(PyOps.Iterate(interp, a[1]));
            return PyNone.Instance;
        });
        Add("insert", (_, a, _) =>
        {
            var list = L(a).Items;
            int i = (int)PyOps.AsBigInt(a[1], "index");
            if (i < 0)
                i = Math.Max(0, list.Count + i);
            i = Math.Min(i, list.Count);
            list.Insert(i, a[2]);
            return PyNone.Instance;
        });
        Add("remove", (interp, a, _) =>
        {
            var list = L(a).Items;
            for (int i = 0; i < list.Count; i++)
            {
                if (interp.RichEquals(list[i], a[1]))
                {
                    list.RemoveAt(i);
                    return PyNone.Instance;
                }
            }
            throw PyErr.ValueError("list.remove(x): x not in list");
        });
        Add("pop", (_, a, _) =>
        {
            var list = L(a).Items;
            if (list.Count == 0)
                throw PyErr.IndexError("pop from empty list");
            int i = a.Length > 1 ? (int)PyOps.AsBigInt(a[1], "index") : -1;
            if (i < 0)
                i += list.Count;
            if (i < 0 || i >= list.Count)
                throw PyErr.IndexError("pop index out of range");
            var v = list[i];
            list.RemoveAt(i);
            return v;
        });
        Add("clear", (_, a, _) =>
        {
            L(a).Items.Clear();
            return PyNone.Instance;
        });
        Add("index", (interp, a, _) =>
        {
            var list = L(a).Items;
            for (int i = 0; i < list.Count; i++)
                if (interp.RichEquals(list[i], a[1]))
                    return new BigInteger(i);
            throw PyErr.ValueError($"{PyOps.Repr(interp, a[1])} is not in list");
        });
        Add("count", (interp, a, _) =>
            new BigInteger(L(a).Items.Count(x => interp.RichEquals(x, a[1]))));
        Add("sort", (interp, a, kw) =>
        {
            SortInPlace(interp, L(a).Items, kw);
            return PyNone.Instance;
        });
        Add("reverse", (_, a, _) =>
        {
            L(a).Items.Reverse();
            return PyNone.Instance;
        });
        Add("copy", (_, a, _) => new PyList(L(a).Items));
        return t;
    }

    private static PyList L(object[] args) => (PyList)args[0];

    /// <summary>Stable sort with key= and reverse= (shared with sorted()).</summary>
    public static void SortInPlace(Interp interp, List<object> items, Dictionary<string, object>? kwargs)
    {
        object? key = null;
        bool reverse = false;
        if (kwargs is not null)
        {
            if (kwargs.TryGetValue("key", out var k) && k is not PyNone)
                key = k;
            if (kwargs.TryGetValue("reverse", out var r))
                reverse = PyOps.Truthy(interp, r);
        }

        var keyed = items
            .Select(x => (Key: key is null ? x : interp.Call(key, new[] { x }), Value: x))
            .ToList();
        var sorted = keyed.OrderBy(p => p.Key, new InterpComparer(interp)).Select(p => p.Value).ToList();
        if (reverse)
            sorted.Reverse();
        items.Clear();
        items.AddRange(sorted);
    }

    private sealed class InterpComparer : IComparer<object>
    {
        private readonly Interp _interp;
        public InterpComparer(Interp interp) => _interp = interp;
        public int Compare(object? x, object? y) => _interp.Compare(x!, y!);
    }
}

public static class DictMethods
{
    public static readonly Dictionary<string, PyBuiltinFunction> Table = Build();

    private static Dictionary<string, PyBuiltinFunction> Build()
    {
        var t = new Dictionary<string, PyBuiltinFunction>();
        void Add(string name, BuiltinFn fn) => t[name] = new PyBuiltinFunction($"dict.{name}", fn);

        Add("get", (_, a, _) => D(a).TryGet(a[1], out var v) ? v : TypeMethods.OptArg(a, 2) ?? PyNone.Instance);
        Add("keys", (_, a, _) => new PyDictKeysView(D(a)));
        Add("values", (_, a, _) => new PyList(D(a).Values));
        Add("items", (_, a, _) =>
            new PyList(D(a).Entries.Select(e => (object)new PyTuple(new[] { e.Key, e.Value }))));
        Add("pop", (_, a, _) =>
        {
            var d = D(a);
            if (d.TryGet(a[1], out var v))
            {
                d.Remove(a[1]);
                return v;
            }
            if (a.Length > 2)
                return a[2];
            throw PyErr.KeyError(a[1]);
        });
        Add("popitem", (_, a, _) =>
        {
            var d = D(a);
            var last = d.LastEntry ?? throw PyErr.KeyError("popitem(): dictionary is empty");
            d.Remove(last.Key);
            return new PyTuple(new[] { last.Key, last.Value });
        });
        Add("setdefault", (_, a, _) =>
        {
            var d = D(a);
            if (d.TryGet(a[1], out var v))
                return v;
            var def = TypeMethods.OptArg(a, 2) ?? PyNone.Instance;
            d[a[1]] = def;
            return def;
        });
        Add("update", (interp, a, kw) =>
        {
            var d = D(a);
            if (a.Length > 1)
            {
                if (a[1] is PyDict other)
                    d.Update(other);
                else
                    foreach (var pair in PyOps.Iterate(interp, a[1]))
                    {
                        var kv = PyOps.Iterate(interp, pair).ToList();
                        if (kv.Count != 2)
                            throw PyErr.ValueError("dictionary update sequence element is not a pair");
                        d[kv[0]] = kv[1];
                    }
            }
            if (kw is not null)
                foreach (var pair in kw)
                    d[pair.Key] = pair.Value;
            return PyNone.Instance;
        });
        Add("clear", (_, a, _) =>
        {
            D(a).Clear();
            return PyNone.Instance;
        });
        Add("copy", (_, a, _) => D(a).Copy());
        return t;
    }

    private static PyDict D(object[] args) => (PyDict)args[0];
}

public static class TupleMethods
{
    public static readonly Dictionary<string, PyBuiltinFunction> Table = new()
    {
        ["count"] = new PyBuiltinFunction("tuple.count", (interp, a, _) =>
            new BigInteger(((PyTuple)a[0]).Items.Count(x => interp.RichEquals(x, a[1])))),
        ["index"] = new PyBuiltinFunction("tuple.index", (interp, a, _) =>
        {
            var items = ((PyTuple)a[0]).Items;
            for (int i = 0; i < items.Length; i++)
                if (interp.RichEquals(items[i], a[1]))
                    return new BigInteger(i);
            throw PyErr.ValueError("tuple.index(x): x not in tuple");
        }),
    };
}

public static class SetMethods
{
    public static readonly Dictionary<string, PyBuiltinFunction> Table = Build();

    private static Dictionary<string, PyBuiltinFunction> Build()
    {
        var t = new Dictionary<string, PyBuiltinFunction>();
        void Add(string name, BuiltinFn fn) => t[name] = new PyBuiltinFunction($"set.{name}", fn);

        Add("add", (_, a, _) =>
        {
            S(a).Items.Add(a[1]);
            return PyNone.Instance;
        });
        Add("remove", (_, a, _) =>
        {
            if (!S(a).Items.Remove(a[1]))
                throw PyErr.KeyError(a[1]);
            return PyNone.Instance;
        });
        Add("discard", (_, a, _) =>
        {
            S(a).Items.Remove(a[1]);
            return PyNone.Instance;
        });
        Add("pop", (_, a, _) =>
        {
            var set = S(a).Items;
            if (set.Count == 0)
                throw PyErr.KeyError("pop from an empty set");
            var v = set.First();
            set.Remove(v);
            return v;
        });
        Add("clear", (_, a, _) =>
        {
            S(a).Items.Clear();
            return PyNone.Instance;
        });
        Add("union", (interp, a, _) =>
        {
            var result = new PySet(S(a).Items);
            for (int i = 1; i < a.Length; i++)
                result.Items.UnionWith(PyOps.Iterate(interp, a[i]));
            return result;
        });
        Add("intersection", (interp, a, _) =>
        {
            var result = new PySet(S(a).Items);
            for (int i = 1; i < a.Length; i++)
                result.Items.IntersectWith(PyOps.Iterate(interp, a[i]));
            return result;
        });
        Add("difference", (interp, a, _) =>
        {
            var result = new PySet(S(a).Items);
            for (int i = 1; i < a.Length; i++)
                result.Items.ExceptWith(PyOps.Iterate(interp, a[i]));
            return result;
        });
        Add("symmetric_difference", (interp, a, _) =>
        {
            var result = new PySet(S(a).Items);
            result.Items.SymmetricExceptWith(PyOps.Iterate(interp, a[1]));
            return result;
        });
        Add("update", (interp, a, _) =>
        {
            for (int i = 1; i < a.Length; i++)
                S(a).Items.UnionWith(PyOps.Iterate(interp, a[i]));
            return PyNone.Instance;
        });
        Add("issubset", (interp, a, _) => S(a).Items.IsSubsetOf(PyOps.Iterate(interp, a[1])));
        Add("issuperset", (interp, a, _) => S(a).Items.IsSupersetOf(PyOps.Iterate(interp, a[1])));
        Add("copy", (_, a, _) => new PySet(S(a).Items));
        return t;
    }

    private static PySet S(object[] args) => (PySet)args[0];
}

public static class BytesMethods
{
    public static readonly Dictionary<string, PyBuiltinFunction> Table = Build();

    private static Dictionary<string, PyBuiltinFunction> Build()
    {
        var t = new Dictionary<string, PyBuiltinFunction>();
        void Add(string name, BuiltinFn fn) => t[name] = new PyBuiltinFunction($"bytes.{name}", fn);

        Add("decode", (_, a, kw) =>
        {
            string encoding = a.Length > 1 ? TypeMethods.StrArg(a[1], "encoding")
                : kw is not null && kw.TryGetValue("encoding", out var e) ? TypeMethods.StrArg(e, "encoding")
                : "utf-8";
            return StrModules.GetEncoding(encoding).GetString(B(a).Data);
        });
        Add("hex", (_, a, _) => Convert.ToHexString(B(a).Data).ToLowerInvariant());
        Add("find", (_, a, _) =>
        {
            var data = B(a).Data;
            var sub = ((PyBytes)a[1]).Data;
            return new BigInteger(data.AsSpan().IndexOf(sub));
        });
        Add("startswith", (_, a, _) =>
        {
            var data = B(a).Data;
            var sub = ((PyBytes)a[1]).Data;
            return data.Length >= sub.Length && data.AsSpan(0, sub.Length).SequenceEqual(sub);
        });
        Add("endswith", (_, a, _) =>
        {
            var data = B(a).Data;
            var sub = ((PyBytes)a[1]).Data;
            return data.Length >= sub.Length && data.AsSpan(data.Length - sub.Length).SequenceEqual(sub);
        });
        Add("split", (_, a, _) =>
        {
            var data = B(a).Data;
            var sep = ((PyBytes)a[1]).Data;
            if (sep.Length == 0)
                throw PyErr.ValueError("empty separator");
            var result = new List<object>();
            int pos = 0;
            while (true)
            {
                int idx = data.AsSpan(pos).IndexOf(sep);
                if (idx < 0)
                    break;
                result.Add(new PyBytes(data[pos..(pos + idx)]));
                pos += idx + sep.Length;
            }
            result.Add(new PyBytes(data[pos..]));
            return new PyList(result);
        });
        // Real CPython bytes.partition/rpartition — missing entirely (only str had it). Found via
        // a real HTTP/1.1 request-line split (`b"GET / HTTP/1.1".partition(b" ")`-style parsing) in
        // a hand-rolled ASGI server sample exercising real socket recv'd bytes. See FASTAPI_PLAN.md
        // Phase 3.2.
        Add("partition", (_, a, _) =>
        {
            var data = B(a).Data;
            var sep = ((PyBytes)a[1]).Data;
            int i = data.AsSpan().IndexOf(sep);
            return i < 0
                ? new PyTuple(new object[] { new PyBytes(data), new PyBytes(Array.Empty<byte>()), new PyBytes(Array.Empty<byte>()) })
                : new PyTuple(new object[] { new PyBytes(data[..i]), new PyBytes(sep), new PyBytes(data[(i + sep.Length)..]) });
        });
        Add("rpartition", (_, a, _) =>
        {
            var data = B(a).Data;
            var sep = ((PyBytes)a[1]).Data;
            int i = data.AsSpan().LastIndexOf(sep);
            return i < 0
                ? new PyTuple(new object[] { new PyBytes(Array.Empty<byte>()), new PyBytes(Array.Empty<byte>()), new PyBytes(data) })
                : new PyTuple(new object[] { new PyBytes(data[..i]), new PyBytes(sep), new PyBytes(data[(i + sep.Length)..]) });
        });
        Add("join", (interp, a, _) =>
        {
            var sep = B(a).Data;
            var parts = PyOps.Iterate(interp, a[1])
                .Select(x => x switch
                {
                    PyBytes bb => bb.Data,
                    PyByteArray bb => bb.Data.ToArray(),
                    _ => throw PyErr.TypeError($"sequence item: expected bytes, {PyOps.TypeName(x)} found"),
                })
                .ToList();
            var result = new List<byte>();
            for (int i = 0; i < parts.Count; i++)
            {
                if (i > 0)
                    result.AddRange(sep);
                result.AddRange(parts[i]);
            }
            return new PyBytes(result.ToArray());
        });
        Add("replace", (_, a, _) =>
        {
            var data = B(a).Data;
            var oldB = ((PyBytes)a[1]).Data;
            var newB = ((PyBytes)a[2]).Data;
            if (oldB.Length == 0)
                return new PyBytes(data);
            var result = new List<byte>();
            int pos = 0;
            while (true)
            {
                int idx = data.AsSpan(pos).IndexOf(oldB);
                if (idx < 0)
                    break;
                result.AddRange(data[pos..(pos + idx)]);
                result.AddRange(newB);
                pos += idx + oldB.Length;
            }
            result.AddRange(data[pos..]);
            return new PyBytes(result.ToArray());
        });
        Add("strip", (_, a, _) =>
        {
            var data = B(a).Data;
            int start = 0, end = data.Length;
            while (start < end && IsWs(data[start])) start++;
            while (end > start && IsWs(data[end - 1])) end--;
            return new PyBytes(data[start..end]);
        });
        Add("upper", (_, a, _) => new PyBytes(B(a).Data.Select(x => x is >= (byte)'a' and <= (byte)'z' ? (byte)(x - 32) : x).ToArray()));
        Add("lower", (_, a, _) => new PyBytes(B(a).Data.Select(x => x is >= (byte)'A' and <= (byte)'Z' ? (byte)(x + 32) : x).ToArray()));
        // Real bytes.count(sub[, start[, end]]) — non-overlapping occurrences. Found via real
        // rfc3986's own normalizers.py (`uri_bytes.count(b"%")`), an httpx transitive dependency.
        Add("count", (_, a, _) =>
        {
            var data = B(a).Data;
            var sub = ((PyBytes)a[1]).Data;
            int start = a.Length > 2 ? (int)PyOps.AsBigInt(a[2], "start") : 0;
            int end = a.Length > 3 ? (int)PyOps.AsBigInt(a[3], "end") : data.Length;
            start = Math.Clamp(start < 0 ? start + data.Length : start, 0, data.Length);
            end = Math.Clamp(end < 0 ? end + data.Length : end, 0, data.Length);
            if (sub.Length == 0)
                return new BigInteger(start <= end ? end - start + 1 : 0);
            int count = 0, pos = start;
            while (pos <= end - sub.Length)
            {
                int idx = data.AsSpan(pos, end - pos).IndexOf(sub);
                if (idx < 0)
                    break;
                count++;
                pos += idx + sub.Length;
            }
            return new BigInteger(count);
        });
        return t;
    }

    private static bool IsWs(byte b) => b is 32 or 9 or 10 or 13 or 11 or 12;
    private static PyBytes B(object[] args) => (PyBytes)args[0];
}

public static class ByteArrayMethods
{
    public static readonly Dictionary<string, PyBuiltinFunction> Table = Build();

    private static Dictionary<string, PyBuiltinFunction> Build()
    {
        var t = new Dictionary<string, PyBuiltinFunction>();
        void Add(string name, BuiltinFn fn) => t[name] = new PyBuiltinFunction($"bytearray.{name}", fn);

        Add("append", (_, a, _) =>
        {
            BA(a).Data.Add((byte)PyOps.AsBigInt(a[1], "byte"));
            return PyNone.Instance;
        });
        Add("extend", (interp, a, _) =>
        {
            var data = BA(a).Data;
            switch (a[1])
            {
                case PyBytes b:
                    data.AddRange(b.Data);
                    break;
                case PyByteArray b:
                    data.AddRange(b.Data);
                    break;
                default:
                    foreach (var x in PyOps.Iterate(interp, a[1]))
                        data.Add((byte)PyOps.AsBigInt(x, "byte"));
                    break;
            }
            return PyNone.Instance;
        });
        Add("decode", (_, a, _) =>
        {
            string encoding = a.Length > 1 ? TypeMethods.StrArg(a[1], "encoding") : "utf-8";
            return StrModules.GetEncoding(encoding).GetString(BA(a).Data.ToArray());
        });
        Add("clear", (_, a, _) =>
        {
            BA(a).Data.Clear();
            return PyNone.Instance;
        });
        Add("hex", (_, a, _) => Convert.ToHexString(BA(a).Data.ToArray()).ToLowerInvariant());
        return t;
    }

    private static PyByteArray BA(object[] args) => (PyByteArray)args[0];
}

/// <summary>type.__new__: the dynamic-class-creation path (`type.__new__(metaclass, name, bases,
/// namespace)`), used by metaprogramming-heavy library code (e.g. pydantic/typing_extensions'
/// TypedDict machinery) instead of the plain `type(name, bases, namespace)` 3-arg call. The
/// metaclass argument is accepted but not used as an actual metaclass (PySharp already ignores
/// custom metaclasses everywhere else — see ExecClassDef).</summary>
public static class TypeConstructorMethods
{
    public static readonly Dictionary<string, PyBuiltinFunction> Table = new()
    {
        ["__new__"] = new PyBuiltinFunction("type.__new__", (_, a, _) =>
            BuildClass((string)a[1], a[2], a.Length > 3 ? a[3] : PyNone.Instance)),
    };

    public static PyClass BuildClass(string name, object basesObj, object namespaceObj)
    {
        var bases = basesObj is PyTuple bt ? bt.Items.OfType<PyClass>().ToList() : new List<PyClass>();
        var cls = new PyClass(name, bases);
        if (namespaceObj is PyDict ns)
            foreach (var e in ns.Entries)
                cls.Dict[e.Key] = e.Value;
        return cls;
    }
}

/// <summary>itertools.chain.from_iterable: the alternate constructor real CPython's chain exposes
/// as a classmethod, dispatched the same unbound-method way as type.__new__/dict.get. Found via
/// pydantic's real `chain.from_iterable(...)` usage (class_validators.check_for_unused). See
/// FASTAPI_PLAN.md Phase 1.9.</summary>
public static class ChainMethods
{
    public static readonly Dictionary<string, PyBuiltinFunction> Table = new()
    {
        ["from_iterable"] = new PyBuiltinFunction("chain.from_iterable", (interp, a, _) =>
            new PyIterator(PyOps.Iterate(interp, a[0]).SelectMany(x => PyOps.Iterate(interp, x)).GetEnumerator())),
    };
}

public static class GeneratorMethods
{
    public static readonly Dictionary<string, PyBuiltinFunction> Table = new()
    {
        ["__next__"] = new PyBuiltinFunction("generator.__next__", (interp, a, _) =>
        {
            var gen = (PyGenerator)a[0];
            if (gen.MoveNext(interp, out var v))
                return v;
            throw PyErr.StopIteration();
        }),
        ["__iter__"] = new PyBuiltinFunction("generator.__iter__", (_, a, _) => a[0]),
        // send(None) is equivalent to next(); non-None values not supported in v1
        ["send"] = new PyBuiltinFunction("generator.send", (interp, a, _) =>
        {
            if (a.Length > 1 && a[1] is not PyNone)
                throw PyErr.TypeError("can't send non-None value to a generator (not supported in PySharp v1)");
            var gen = (PyGenerator)a[0];
            if (gen.MoveNext(interp, out var v))
                return v;
            throw PyErr.StopIteration();
        }),
        ["close"] = new PyBuiltinFunction("generator.close", (_, _, _) => PyNone.Instance),
    };
}

public static class IteratorMethods
{
    public static readonly Dictionary<string, PyBuiltinFunction> Table = new()
    {
        ["__iter__"] = new PyBuiltinFunction("iterator.__iter__", (_, a, _) => a[0]),
        ["__next__"] = new PyBuiltinFunction("iterator.__next__", (interp, a, _) =>
        {
            if (PyOps.IterNext(interp, a[0], out var v))
                return v;
            throw PyErr.StopIteration();
        }),
    };
}

public static class RangeMethods
{
    public static readonly Dictionary<string, PyBuiltinFunction> Table = Build();

    private static Dictionary<string, PyBuiltinFunction> Build()
    {
        var t = new Dictionary<string, PyBuiltinFunction>();
        void Add(string name, BuiltinFn fn) => t[name] = new PyBuiltinFunction($"range.{name}", fn);
        Add("count", (interp, a, _) =>
        {
            var r = (PyRange)a[0];
            return new BigInteger(r.Enumerate().Count(x => interp.RichEquals(x, a[1])));
        });
        Add("index", (interp, a, _) =>
        {
            var r = (PyRange)a[0];
            BigInteger i = 0;
            foreach (var x in r.Enumerate())
            {
                if (interp.RichEquals(x, a[1]))
                    return i;
                i++;
            }
            throw PyErr.ValueError($"{PyOps.Repr(interp, a[1])} is not in range");
        });
        return t;
    }
}
