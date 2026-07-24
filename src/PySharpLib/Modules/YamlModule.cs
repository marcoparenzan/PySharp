using System.Globalization;
using System.Numerics;
using System.Text;
using PySharpLib.Interpretation;
using PySharpLib.Runtime;

namespace PySharpLib.Modules;

/// <summary>
/// `yaml` module (practical subset of PyYAML): safe_load/load and safe_dump/dump.
/// Covers block mapping/sequence with indentation, flow style ([..]/{..}), typed
/// scalars (null/bool/int/float/str, single and double quoting), '#' comments and the
/// document marker '---'. Out of scope for v1: block scalars '|'/'>', anchors/aliases,
/// explicit tags, multiple documents.
/// </summary>
public static class YamlModule
{
    public static PyModule Create()
    {
        var m = new PyModule("yaml");
        var d = m.Dict;

        var yamlError = new PyClass("YAMLError", new List<PyClass> { PyErr.Exception });
        d["YAMLError"] = yamlError;

        d["safe_load"] = new PyBuiltinFunction("safe_load", (interp, a, _) => Load(interp, a[0], yamlError));
        d["load"] = new PyBuiltinFunction("load", (interp, a, _) => Load(interp, a[0], yamlError));
        d["safe_dump"] = new PyBuiltinFunction("safe_dump", (interp, a, kwargs) => Dump(a[0], kwargs));
        d["dump"] = new PyBuiltinFunction("dump", (interp, a, kwargs) => Dump(a[0], kwargs));

        return m;
    }

    // ---------------------------------------------------------------- load

    private static object Load(Interp interp, object source, PyClass errorClass)
    {
        string text = source switch
        {
            string s => s,
            PyBytes b => Encoding.UTF8.GetString(b.Data),
            PyByteArray b => Encoding.UTF8.GetString(b.Data.ToArray()),
            _ => Encoding.UTF8.GetString(ReadStreamText(interp, source)),
        };
        return new YamlParser(text, errorClass).ParseDocument();
    }

    private static byte[] ReadStreamText(Interp interp, object source)
    {
        var content = interp.CallMethod(source, "read", Array.Empty<object>());
        return content switch
        {
            string s => Encoding.UTF8.GetBytes(s),
            PyBytes b => b.Data,
            PyByteArray b => b.Data.ToArray(),
            _ => throw PyErr.TypeError("yaml.load: invalid source"),
        };
    }

    private sealed class YamlParser
    {
        private readonly List<(int Indent, string Content)> _lines = new();
        private readonly PyClass _errorClass;
        private int _pos;

        public YamlParser(string text, PyClass errorClass)
        {
            _errorClass = errorClass;
            foreach (var raw in text.Replace("\r\n", "\n").Replace("\r", "\n").Split('\n'))
            {
                string stripped = StripComment(raw);
                if (stripped.TrimEnd().Length == 0)
                    continue;
                string trimmedStart = stripped.TrimStart(' ');
                if (trimmedStart is "---" or "...")
                    continue;
                int indent = stripped.Length - trimmedStart.Length;
                _lines.Add((indent, trimmedStart.TrimEnd()));
            }
        }

        private PyRaise Error(string message)
            => new(PyErr.MakeInstance(_errorClass, message));

        public object ParseDocument()
        {
            if (_pos >= _lines.Count)
                return PyNone.Instance;
            return ParseNode(_lines[_pos].Indent);
        }

        private object ParseNode(int indent)
        {
            if (_pos >= _lines.Count || _lines[_pos].Indent < indent)
                return PyNone.Instance;
            var (lineIndent, content) = _lines[_pos];
            if (IsSequenceItem(content))
                return ParseSequence(lineIndent);
            if (FindColon(content) >= 0)
                return ParseMapping(lineIndent);
            _pos++;
            return ParseScalarOrFlow(content);
        }

        private static bool IsSequenceItem(string content)
            => content == "-" || content.StartsWith("- ");

        private object ParseSequence(int indent)
        {
            var list = new PyList();
            while (_pos < _lines.Count && _lines[_pos].Indent == indent && IsSequenceItem(_lines[_pos].Content))
            {
                string content = _lines[_pos].Content;
                string rest = content == "-" ? "" : content.Substring(2).TrimStart(' ');
                _pos++;
                if (rest.Length == 0)
                {
                    // value on a more deeply nested block
                    list.Items.Add(_pos < _lines.Count && _lines[_pos].Indent > indent
                        ? ParseNode(_lines[_pos].Indent)
                        : PyNone.Instance);
                }
                else if (FindColon(rest) >= 0 && !IsFlow(rest))
                {
                    // "- key: value ..." -> mapping whose keys align after the dash
                    int mapIndent = indent + (content.Length - content.Substring(2).TrimStart(' ').Length);
                    list.Items.Add(ParseMapping(mapIndent, rest));
                }
                else
                {
                    list.Items.Add(ParseScalarOrFlow(rest));
                }
            }
            return list;
        }

        private object ParseMapping(int indent, string? firstContent = null)
        {
            var dict = new PyDict();
            while (true)
            {
                string content;
                if (firstContent is not null)
                {
                    content = firstContent;
                    firstContent = null;
                }
                else
                {
                    if (_pos >= _lines.Count || _lines[_pos].Indent != indent
                        || IsSequenceItem(_lines[_pos].Content) || FindColon(_lines[_pos].Content) < 0)
                        break;
                    content = _lines[_pos].Content;
                    _pos++;
                }

                int colon = FindColon(content);
                if (colon < 0)
                    throw Error("expected 'key: value'");
                object key = ParseScalar(content.Substring(0, colon).Trim());
                string valuePart = content.Substring(colon + 1).Trim();
                if (valuePart.Length == 0)
                {
                    // block value: a SEQUENCE may sit at the same indent as the key
                    // (pyyaml style), a nested mapping must be deeper.
                    if (_pos < _lines.Count && IsSequenceItem(_lines[_pos].Content)
                        && _lines[_pos].Indent >= indent)
                        dict[key] = ParseSequence(_lines[_pos].Indent);
                    else if (_pos < _lines.Count && _lines[_pos].Indent > indent)
                        dict[key] = ParseNode(_lines[_pos].Indent);
                    else
                        dict[key] = PyNone.Instance;
                }
                else
                {
                    dict[key] = ParseScalarOrFlow(valuePart);
                }
            }
            return dict;
        }

        private object ParseScalarOrFlow(string text)
        {
            text = text.Trim();
            if (text.StartsWith("["))
                return ParseFlow(text, ']');
            if (text.StartsWith("{"))
                return ParseFlow(text, '}');
            return ParseScalar(text);
        }

        private object ParseFlow(string text, char close)
        {
            // single-line flow-style parser: [a, b], {k: v}
            int i = 0;
            object result = ParseFlowValue(text, ref i);
            return result;
        }

        private object ParseFlowValue(string s, ref int i)
        {
            SkipSpaces(s, ref i);
            if (i >= s.Length)
                throw Error("truncated flow YAML");
            if (s[i] == '[')
            {
                i++;
                var list = new PyList();
                SkipSpaces(s, ref i);
                if (i < s.Length && s[i] == ']') { i++; return list; }
                while (true)
                {
                    list.Items.Add(ParseFlowValue(s, ref i));
                    SkipSpaces(s, ref i);
                    if (i < s.Length && s[i] == ',') { i++; continue; }
                    if (i < s.Length && s[i] == ']') { i++; break; }
                    throw Error("expected ',' or ']' in the flow");
                }
                return list;
            }
            if (s[i] == '{')
            {
                i++;
                var dict = new PyDict();
                SkipSpaces(s, ref i);
                if (i < s.Length && s[i] == '}') { i++; return dict; }
                while (true)
                {
                    object key = ParseFlowScalarToken(s, ref i, isKey: true);
                    SkipSpaces(s, ref i);
                    if (i >= s.Length || s[i] != ':')
                        throw Error("expected ':' in the flow mapping");
                    i++;
                    dict[key] = ParseFlowValue(s, ref i);
                    SkipSpaces(s, ref i);
                    if (i < s.Length && s[i] == ',') { i++; SkipSpaces(s, ref i); continue; }
                    if (i < s.Length && s[i] == '}') { i++; break; }
                    throw Error("expected ',' or '}' in the flow");
                }
                return dict;
            }
            return ParseFlowScalarToken(s, ref i, isKey: false);
        }

        private object ParseFlowScalarToken(string s, ref int i, bool isKey)
        {
            SkipSpaces(s, ref i);
            if (i < s.Length && (s[i] == '"' || s[i] == '\''))
                return ParseScalar(ReadQuoted(s, ref i));
            int start = i;
            while (i < s.Length && s[i] != ',' && s[i] != ']' && s[i] != '}'
                   && !(isKey && s[i] == ':'))
                i++;
            return ParseScalar(s.Substring(start, i - start).Trim());
        }

        private static string ReadQuoted(string s, ref int i)
        {
            char q = s[i];
            int start = i;
            i++;
            while (i < s.Length)
            {
                if (s[i] == '\\' && q == '"') { i += 2; continue; }
                if (s[i] == q)
                {
                    if (q == '\'' && i + 1 < s.Length && s[i + 1] == '\'') { i += 2; continue; }
                    i++;
                    break;
                }
                i++;
            }
            return s.Substring(start, i - start);
        }

        private static void SkipSpaces(string s, ref int i)
        {
            while (i < s.Length && s[i] == ' ')
                i++;
        }

        private object ParseScalar(string text)
        {
            text = text.Trim();
            if (text.Length == 0)
                return PyNone.Instance;
            if (text.Length >= 2 && text[0] == '"' && text[^1] == '"')
                return UnescapeDouble(text.Substring(1, text.Length - 2));
            if (text.Length >= 2 && text[0] == '\'' && text[^1] == '\'')
                return text.Substring(1, text.Length - 2).Replace("''", "'");

            string lower = text.ToLowerInvariant();
            if (lower is "null" or "~" or "none")
                return PyNone.Instance;
            if (lower is "true" or "yes" or "on")
                return true;
            if (lower is "false" or "no" or "off")
                return false;
            if (BigInteger.TryParse(text, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var bi))
                return bi;
            if (double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var dbl))
                return dbl;
            return text;
        }

        private static string UnescapeDouble(string s)
        {
            var sb = new StringBuilder();
            for (int i = 0; i < s.Length; i++)
            {
                if (s[i] != '\\' || i + 1 >= s.Length) { sb.Append(s[i]); continue; }
                char e = s[++i];
                sb.Append(e switch
                {
                    'n' => '\n', 't' => '\t', 'r' => '\r', '"' => '"',
                    '\\' => '\\', '0' => '\0', _ => e,
                });
            }
            return sb.ToString();
        }

        private static string StripComment(string line)
        {
            bool inSingle = false, inDouble = false;
            for (int i = 0; i < line.Length; i++)
            {
                char c = line[i];
                if (c == '\'' && !inDouble) inSingle = !inSingle;
                else if (c == '"' && !inSingle) inDouble = !inDouble;
                else if (c == '#' && !inSingle && !inDouble && (i == 0 || line[i - 1] == ' ' || line[i - 1] == '\t'))
                    return line.Substring(0, i);
            }
            return line;
        }

        /// <summary>Index of the ':' key/value separator (followed by a space or end), at level 0.</summary>
        private static int FindColon(string s)
        {
            bool inSingle = false, inDouble = false;
            int depth = 0;
            for (int i = 0; i < s.Length; i++)
            {
                char c = s[i];
                if (c == '\'' && !inDouble) inSingle = !inSingle;
                else if (c == '"' && !inSingle) inDouble = !inDouble;
                else if (!inSingle && !inDouble)
                {
                    if (c is '[' or '{') depth++;
                    else if (c is ']' or '}') depth--;
                    else if (c == ':' && depth == 0 && (i + 1 == s.Length || s[i + 1] == ' '))
                        return i;
                }
            }
            return -1;
        }

        private static bool IsFlow(string s)
            => s.StartsWith("[") || s.StartsWith("{");
    }

    // ---------------------------------------------------------------- dump

    private static object Dump(object value, Dictionary<string, object>? kwargs)
    {
        bool flow = kwargs is not null && kwargs.TryGetValue("default_flow_style", out var f) && f is true;
        var sb = new StringBuilder();
        if (flow)
        {
            EmitFlow(value, sb);
            sb.Append('\n');
        }
        else
        {
            EmitBlock(value, 0, sb, topLevel: true);
        }
        return sb.ToString();
    }

    private static void EmitBlock(object value, int indent, StringBuilder sb, bool topLevel)
    {
        switch (value)
        {
            case PyDict dict when dict.Count > 0:
                foreach (var e in dict.Entries)
                {
                    sb.Append(' ', indent).Append(EmitKey(e.Key)).Append(':');
                    EmitBlockValue(e.Value, indent, sb);
                }
                break;
            case PyList list when list.Items.Count > 0:
                foreach (var item in list.Items)
                {
                    sb.Append(' ', indent).Append('-');
                    EmitSequenceValue(item, indent, sb);
                }
                break;
            case PyTuple tup when tup.Items.Length > 0:
                EmitBlock(new PyList(tup.Items), indent, sb, topLevel);
                break;
            default:
                sb.Append(' ', indent).Append(EmitScalar(value)).Append('\n');
                break;
        }
    }

    private static void EmitBlockValue(object value, int indent, StringBuilder sb)
    {
        if (value is PyDict { Count: > 0 } or PyList { Items.Count: > 0 } or PyTuple { Items.Length: > 0 })
        {
            // nested containers go on new lines; sequences stay at the parent level
            int childIndent = value is PyDict ? indent + 2 : indent;
            sb.Append('\n');
            EmitBlock(value, childIndent, sb, topLevel: false);
        }
        else
        {
            sb.Append(' ').Append(EmitScalar(value)).Append('\n');
        }
    }

    private static void EmitSequenceValue(object value, int indent, StringBuilder sb)
    {
        if (value is PyDict { Count: > 0 } dict)
        {
            // "- key: value" with the first entry inline, the rest indented
            bool first = true;
            foreach (var e in dict.Entries)
            {
                if (first) { sb.Append(' '); first = false; }
                else sb.Append(' ', indent + 2);
                sb.Append(EmitKey(e.Key)).Append(':');
                EmitBlockValue(e.Value, indent + 2, sb);
            }
        }
        else if (value is PyList { Items.Count: > 0 } or PyTuple { Items.Length: > 0 })
        {
            sb.Append('\n');
            EmitBlock(value, indent + 2, sb, topLevel: false);
        }
        else
        {
            sb.Append(' ').Append(EmitScalar(value)).Append('\n');
        }
    }

    private static void EmitFlow(object value, StringBuilder sb)
    {
        switch (value)
        {
            case PyDict dict:
                sb.Append('{');
                bool firstD = true;
                foreach (var e in dict.Entries)
                {
                    if (!firstD) sb.Append(", ");
                    firstD = false;
                    sb.Append(EmitKey(e.Key)).Append(": ");
                    EmitFlow(e.Value, sb);
                }
                sb.Append('}');
                break;
            case PyList list:
                EmitFlowSeq(list.Items, sb);
                break;
            case PyTuple tup:
                EmitFlowSeq(tup.Items, sb);
                break;
            default:
                sb.Append(EmitScalar(value));
                break;
        }
    }

    private static void EmitFlowSeq(IReadOnlyList<object> items, StringBuilder sb)
    {
        sb.Append('[');
        for (int i = 0; i < items.Count; i++)
        {
            if (i > 0) sb.Append(", ");
            EmitFlow(items[i], sb);
        }
        sb.Append(']');
    }

    private static string EmitKey(object key)
        => key switch
        {
            string s => EmitScalar(s),
            _ => EmitScalar(key),
        };

    private static string EmitScalar(object value)
    {
        switch (value)
        {
            case PyNone:
                return "null";
            case bool b:
                return b ? "true" : "false";
            case BigInteger i:
                return i.ToString();
            case double d:
                return PyOps.ReprDouble(d);
            case PyDict { Count: 0 }:
                return "{}";
            case PyList { Items.Count: 0 }:
            case PyTuple { Items.Length: 0 }:
                return "[]";
            case string s:
                return NeedsQuote(s) ? "'" + s.Replace("'", "''") + "'" : s;
            default:
                return "'" + (value.ToString() ?? "").Replace("'", "''") + "'";
        }
    }

    private static bool NeedsQuote(string s)
    {
        if (s.Length == 0)
            return true;
        string lower = s.ToLowerInvariant();
        if (lower is "null" or "~" or "none" or "true" or "false" or "yes" or "no" or "on" or "off")
            return true;
        if (BigInteger.TryParse(s, out _) || double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out _))
            return true;
        if (s[0] is ' ' or '-' or '?' or ':' or ',' or '[' or ']' or '{' or '}' or '#' or '&'
            or '*' or '!' or '|' or '>' or '\'' or '"' or '%' or '@' or '`')
            return true;
        foreach (char c in s)
            if (c is ':' or '#' or '\n' or '\t')
                return true;
        return false;
    }
}
