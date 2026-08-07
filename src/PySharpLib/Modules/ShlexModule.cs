// Copyright (c) 2026 Marco Parenzan
//
// Licensed under the MIT License. See the LICENSE file in the project
// root for full license information.

using PySharpLib.Runtime;

namespace PySharpLib.Modules;

/// <summary>
/// shlex: a real (not stubbed) POSIX-aware tokenizer, ported from CPython's own shlex.shlex
/// algorithm — string input only (no file/stream sourcing, no `sourcehook`/`push_source`, no
/// `punctuation_chars`/non-posix wordchars mode): the scope real usage has needed so far. Handles
/// configurable `whitespace`/`whitespace_split`/`commenters`/`quotes`/`escape`/`escapedquotes`, with
/// posix-mode quote stripping and backslash-escape handling inside escaped quotes. Found via
/// starlette's real `shlex(value, posix=True)` (datastructures.CommaSeparatedStrings, splitting a
/// comma-separated header value while respecting quoted commas). See FASTAPI_PLAN.md.
/// </summary>
public static class ShlexModule
{
    private const string InputKey = "__input__";
    private const string PosKey = "__pos__";

    public static readonly PyClass ShlexClass = BuildShlexClass();

    public static PyModule Create()
    {
        var m = new PyModule("shlex");
        m.Dict["shlex"] = ShlexClass;
        m.Dict["split"] = new PyBuiltinFunction("split", (interp, a, kwargs) =>
        {
            string s = (string)a[0];
            bool posix = kwargs is null || !kwargs.TryGetValue("posix", out var p) || PyOps.Truthy(interp, p);
            var inst = new PyInstance(ShlexClass);
            InitShlex(inst, s, posix);
            inst.Dict["whitespace_split"] = true;
            var items = new List<object>();
            while (TryNextToken(inst, out var tok))
                items.Add(tok);
            return new PyList(items);
        });
        return m;
    }

    private static PyClass BuildShlexClass()
    {
        var cls = new PyClass("shlex", new List<PyClass>());
        void Add(string n, BuiltinFn fn) => cls.Dict[n] = new PyBuiltinFunction($"shlex.{n}", fn);

        Add("__init__", (interp, a, kwargs) =>
        {
            var inst = (PyInstance)a[0];
            string s = a.Length > 1 && a[1] is string str ? str : "";
            bool posix = a.Length > 3 && a[3] is bool pb ? pb
                : kwargs is not null && kwargs.TryGetValue("posix", out var p) && PyOps.Truthy(interp, p);
            InitShlex(inst, s, posix);
            return PyNone.Instance;
        });
        Add("__iter__", (_, a, _) => a[0]);
        Add("__next__", (_, a, _) =>
        {
            if (!TryNextToken((PyInstance)a[0], out var tok))
                throw PyErr.StopIteration();
            return tok;
        });
        Add("get_token", (_, a, _) => TryNextToken((PyInstance)a[0], out var tok) ? tok : PyNone.Instance);

        return cls;
    }

    private static void InitShlex(PyInstance inst, string s, bool posix)
    {
        inst.Dict[InputKey] = s;
        inst.Dict[PosKey] = 0L;
        inst.Dict["whitespace"] = " \t\r\n";
        inst.Dict["whitespace_split"] = false;
        inst.Dict["commenters"] = "#";
        inst.Dict["quotes"] = "'\"";
        inst.Dict["escape"] = "\\";
        inst.Dict["escapedquotes"] = "\"";
        inst.Dict["posix"] = posix;
    }

    private static string S(PyInstance inst, string key) => inst.Dict.TryGet(key, out var v) && v is string s ? s : "";
    private static bool B(PyInstance inst, string key) => inst.Dict.TryGet(key, out var v) && v is bool b && b;

    /// <summary>Reads the next token starting at the instance's current position, advancing it.
    /// Returns false (no mutation beyond position) when the input is exhausted.</summary>
    private static bool TryNextToken(PyInstance inst, out string token)
    {
        string input = S(inst, InputKey);
        int pos = inst.Dict.TryGet(PosKey, out var pv) ? (int)(long)pv : 0;
        string whitespace = S(inst, "whitespace");
        bool whitespaceSplit = B(inst, "whitespace_split");
        string commenters = S(inst, "commenters");
        string quotes = S(inst, "quotes");
        string escape = S(inst, "escape");
        string escapedquotes = S(inst, "escapedquotes");
        bool posix = B(inst, "posix");

        // Skip leading whitespace and comment-to-end-of-line runs.
        while (pos < input.Length)
        {
            if (whitespace.IndexOf(input[pos]) >= 0)
            {
                pos++;
            }
            else if (commenters.IndexOf(input[pos]) >= 0)
            {
                while (pos < input.Length && input[pos] != '\n')
                    pos++;
            }
            else
            {
                break;
            }
        }

        if (pos >= input.Length)
        {
            inst.Dict[PosKey] = (long)pos;
            token = "";
            return false;
        }

        var sb = new System.Text.StringBuilder();
        while (pos < input.Length)
        {
            char c = input[pos];
            if (quotes.IndexOf(c) >= 0)
            {
                char quoteChar = c;
                pos++;
                bool closed = false;
                while (pos < input.Length)
                {
                    if (input[pos] == quoteChar)
                    {
                        pos++;
                        closed = true;
                        break;
                    }
                    if (posix && escape.IndexOf(input[pos]) >= 0 && escapedquotes.IndexOf(quoteChar) >= 0
                        && pos + 1 < input.Length && (input[pos + 1] == quoteChar || escape.IndexOf(input[pos + 1]) >= 0))
                    {
                        sb.Append(input[pos + 1]);
                        pos += 2;
                    }
                    else
                    {
                        sb.Append(input[pos]);
                        pos++;
                    }
                }
                if (!closed)
                    throw PyErr.ValueError("No closing quotation");
                continue;
            }
            if (whitespace.IndexOf(c) >= 0 || commenters.IndexOf(c) >= 0)
                break;
            if (whitespaceSplit)
            {
                sb.Append(c);
                pos++;
            }
            else if (sb.Length == 0)
            {
                sb.Append(c);
                pos++;
                break;
            }
            else
            {
                break;
            }
        }

        inst.Dict[PosKey] = (long)pos;
        token = sb.ToString();
        return true;
    }
}
