// Copyright (c) 2026 Marco Parenzan
//
// Licensed under the MIT License. See the LICENSE file in the project
// root for full license information.

using System.Numerics;
using System.Text;
using PySharpLib.Interpretation;
using PySharpLib.Runtime;

namespace PySharpLib.Modules;

/// <summary>json.dumps/loads sull'object model PySharp (per twin e messaggi IoT Hub).</summary>
public static class JsonModule
{
    public static PyModule Create()
    {
        var m = new PyModule("json");
        var d = m.Dict;

        var decodeError = new PyClass("JSONDecodeError", new List<PyClass> { PyErr.ValueErrorClass });
        d["JSONDecodeError"] = decodeError;

        d["dumps"] = new PyBuiltinFunction("dumps", (interp, a, kwargs) =>
        {
            int? indent = null;
            if (kwargs is not null && kwargs.TryGetValue("indent", out var ind) && ind is not PyNone)
                indent = (int)PyOps.AsBigInt(ind, "indent");
            var sb = new StringBuilder();
            Serialize(interp, a[0], sb, indent, 0);
            return sb.ToString();
        });

        d["loads"] = new PyBuiltinFunction("loads", (_, a, _) =>
        {
            string text = a[0] switch
            {
                string s => s,
                PyBytes b => Encoding.UTF8.GetString(b.Data),
                PyByteArray b => Encoding.UTF8.GetString(b.Data.ToArray()),
                _ => throw PyErr.TypeError("the JSON object must be str or bytes"),
            };
            var parser = new JsonParser(text, decodeError);
            var value = parser.ParseValue();
            parser.SkipWhitespace();
            if (!parser.AtEnd)
                throw parser.Error("Extra data");
            return value;
        });

        d["dump"] = new PyBuiltinFunction("dump", (interp, a, kwargs) =>
        {
            var sb = new StringBuilder();
            Serialize(interp, a[0], sb, null, 0);
            interp.CallMethod(a[1], "write", new object[] { sb.ToString() });
            return PyNone.Instance;
        });

        d["load"] = new PyBuiltinFunction("load", (interp, a, _) =>
        {
            var content = interp.CallMethod(a[0], "read", Array.Empty<object>());
            string text = content switch
            {
                string s => s,
                PyBytes b => Encoding.UTF8.GetString(b.Data),
                PyByteArray b => Encoding.UTF8.GetString(b.Data.ToArray()),
                _ => throw PyErr.TypeError("the JSON object must be str or bytes"),
            };
            var parser = new JsonParser(text, decodeError);
            return parser.ParseValue();
        });

        return m;
    }

    private static void Serialize(Interp interp, object value, StringBuilder sb, int? indent, int depth)
    {
        switch (value)
        {
            case PyNone:
                sb.Append("null");
                break;
            case bool b:
                sb.Append(b ? "true" : "false");
                break;
            case BigInteger i:
                sb.Append(i.ToString());
                break;
            case double dd:
                if (double.IsNaN(dd) || double.IsInfinity(dd))
                    sb.Append(double.IsNaN(dd) ? "NaN" : dd > 0 ? "Infinity" : "-Infinity");
                else
                    sb.Append(PyOps.ReprDouble(dd));
                break;
            case string s:
                WriteJsonString(sb, s);
                break;
            case PyList list:
                WriteArray(interp, sb, list.Items, indent, depth);
                break;
            case PyTuple tuple:
                WriteArray(interp, sb, tuple.Items, indent, depth);
                break;
            case PyDict dict:
            {
                if (dict.Count == 0)
                {
                    sb.Append("{}");
                    break;
                }
                sb.Append('{');
                bool first = true;
                foreach (var e in dict.Entries)
                {
                    if (!first)
                        sb.Append(indent is null ? ", " : ",");
                    first = false;
                    NewlineIndent(sb, indent, depth + 1);
                    string key = e.Key switch
                    {
                        string ks => ks,
                        BigInteger ki => ki.ToString(),
                        bool kb => kb ? "true" : "false",
                        double kd => PyOps.ReprDouble(kd),
                        PyNone => "null",
                        _ => throw PyErr.TypeError(
                            $"keys must be str, int, float, bool or None, not {PyOps.TypeName(e.Key)}"),
                    };
                    WriteJsonString(sb, key);
                    sb.Append(": ");
                    Serialize(interp, e.Value, sb, indent, depth + 1);
                }
                NewlineIndent(sb, indent, depth);
                sb.Append('}');
                break;
            }
            default:
                throw PyErr.TypeError($"Object of type {PyOps.TypeName(value)} is not JSON serializable");
        }
    }

    private static void WriteArray(Interp interp, StringBuilder sb, IReadOnlyList<object> items,
        int? indent, int depth)
    {
        if (items.Count == 0)
        {
            sb.Append("[]");
            return;
        }
        sb.Append('[');
        for (int i = 0; i < items.Count; i++)
        {
            if (i > 0)
                sb.Append(indent is null ? ", " : ",");
            NewlineIndent(sb, indent, depth + 1);
            Serialize(interp, items[i], sb, indent, depth + 1);
        }
        NewlineIndent(sb, indent, depth);
        sb.Append(']');
    }

    private static void NewlineIndent(StringBuilder sb, int? indent, int depth)
    {
        if (indent is int n)
            sb.Append('\n').Append(' ', n * depth);
    }

    private static void WriteJsonString(StringBuilder sb, string s)
    {
        sb.Append('"');
        foreach (char c in s)
        {
            switch (c)
            {
                case '"': sb.Append("\\\""); break;
                case '\\': sb.Append("\\\\"); break;
                case '\n': sb.Append("\\n"); break;
                case '\r': sb.Append("\\r"); break;
                case '\t': sb.Append("\\t"); break;
                case '\b': sb.Append("\\b"); break;
                case '\f': sb.Append("\\f"); break;
                default:
                    if (c < 32)
                        sb.Append($"\\u{(int)c:x4}");
                    else
                        sb.Append(c);
                    break;
            }
        }
        sb.Append('"');
    }

    private sealed class JsonParser
    {
        private readonly string _s;
        private readonly PyClass _errorClass;
        private int _pos;

        public JsonParser(string s, PyClass errorClass)
        {
            _s = s;
            _errorClass = errorClass;
        }

        public bool AtEnd => _pos >= _s.Length;

        public PyRaise Error(string message)
            => new(PyErr.MakeInstance(_errorClass, $"{message}: char {_pos}"));

        public void SkipWhitespace()
        {
            while (_pos < _s.Length && char.IsWhiteSpace(_s[_pos]))
                _pos++;
        }

        public object ParseValue()
        {
            SkipWhitespace();
            if (AtEnd)
                throw Error("Expecting value");
            char c = _s[_pos];
            switch (c)
            {
                case '{': return ParseObject();
                case '[': return ParseArray();
                case '"': return ParseString();
                case 't':
                    Expect("true");
                    return true;
                case 'f':
                    Expect("false");
                    return false;
                case 'n':
                    Expect("null");
                    return PyNone.Instance;
                case 'N':
                    Expect("NaN");
                    return double.NaN;
                case 'I':
                    Expect("Infinity");
                    return double.PositiveInfinity;
                default:
                    return ParseNumber();
            }
        }

        private void Expect(string word)
        {
            if (_pos + word.Length > _s.Length || _s.Substring(_pos, word.Length) != word)
                throw Error("Expecting value");
            _pos += word.Length;
        }

        private object ParseObject()
        {
            _pos++; // {
            var dict = new PyDict();
            SkipWhitespace();
            if (!AtEnd && _s[_pos] == '}')
            {
                _pos++;
                return dict;
            }
            while (true)
            {
                SkipWhitespace();
                if (AtEnd || _s[_pos] != '"')
                    throw Error("Expecting property name enclosed in double quotes");
                string key = ParseString();
                SkipWhitespace();
                if (AtEnd || _s[_pos] != ':')
                    throw Error("Expecting ':' delimiter");
                _pos++;
                dict[key] = ParseValue();
                SkipWhitespace();
                if (AtEnd)
                    throw Error("Expecting ',' delimiter");
                if (_s[_pos] == ',')
                {
                    _pos++;
                    continue;
                }
                if (_s[_pos] == '}')
                {
                    _pos++;
                    return dict;
                }
                throw Error("Expecting ',' delimiter");
            }
        }

        private object ParseArray()
        {
            _pos++; // [
            var list = new PyList();
            SkipWhitespace();
            if (!AtEnd && _s[_pos] == ']')
            {
                _pos++;
                return list;
            }
            while (true)
            {
                list.Items.Add(ParseValue());
                SkipWhitespace();
                if (AtEnd)
                    throw Error("Expecting ',' delimiter");
                if (_s[_pos] == ',')
                {
                    _pos++;
                    continue;
                }
                if (_s[_pos] == ']')
                {
                    _pos++;
                    return list;
                }
                throw Error("Expecting ',' delimiter");
            }
        }

        private string ParseString()
        {
            _pos++; // "
            var sb = new StringBuilder();
            while (true)
            {
                if (AtEnd)
                    throw Error("Unterminated string");
                char c = _s[_pos++];
                if (c == '"')
                    return sb.ToString();
                if (c != '\\')
                {
                    sb.Append(c);
                    continue;
                }
                if (AtEnd)
                    throw Error("Unterminated string");
                char e = _s[_pos++];
                switch (e)
                {
                    case '"': sb.Append('"'); break;
                    case '\\': sb.Append('\\'); break;
                    case '/': sb.Append('/'); break;
                    case 'b': sb.Append('\b'); break;
                    case 'f': sb.Append('\f'); break;
                    case 'n': sb.Append('\n'); break;
                    case 'r': sb.Append('\r'); break;
                    case 't': sb.Append('\t'); break;
                    case 'u':
                        if (_pos + 4 > _s.Length)
                            throw Error("Invalid \\uXXXX escape");
                        sb.Append((char)Convert.ToInt32(_s.Substring(_pos, 4), 16));
                        _pos += 4;
                        break;
                    default:
                        throw Error($"Invalid \\escape: {e}");
                }
            }
        }

        private object ParseNumber()
        {
            int start = _pos;
            if (!AtEnd && (_s[_pos] == '-' || _s[_pos] == '+'))
                _pos++;
            if (!AtEnd && _s[_pos] == 'I')
            {
                Expect("Infinity");
                return _s[start] == '-' ? double.NegativeInfinity : double.PositiveInfinity;
            }
            bool isFloat = false;
            while (!AtEnd && (char.IsDigit(_s[_pos]) || _s[_pos] is '.' or 'e' or 'E' or '+' or '-'))
            {
                if (_s[_pos] is '.' or 'e' or 'E')
                    isFloat = true;
                _pos++;
            }
            string text = _s[start.._pos];
            if (text.Length == 0 || text is "-" or "+")
                throw Error("Expecting value");
            if (isFloat)
                return double.Parse(text, System.Globalization.CultureInfo.InvariantCulture);
            return BigInteger.Parse(text);
        }
    }
}
