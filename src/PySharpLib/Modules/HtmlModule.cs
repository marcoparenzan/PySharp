// Copyright (c) 2026 Marco Parenzan
//
// Licensed under the MIT License. See the LICENSE file in the project
// root for full license information.

using PySharpLib.Runtime;

namespace PySharpLib.Modules;

/// <summary>html: escape/unescape. `escape` is a direct, real port of CPython's own replace chain
/// (same order, same `&#x27;` apostrophe encoding). `unescape` is backed by .NET's own
/// `WebUtility.HtmlDecode` (a real, correct named/numeric-entity decoder) rather than porting
/// CPython's full `html.entities` table by hand — a real decoder, just not guaranteed identical
/// entity-for-entity coverage to CPython's own table for obscure entities. Found via starlette's
/// real `import html` (middleware/errors.py, reachable from `import starlette`). See
/// FASTAPI_PLAN.md Phase 3.</summary>
public static class HtmlModule
{
    public static PyModule Create()
    {
        var m = new PyModule("html");
        var d = m.Dict;

        d["escape"] = new PyBuiltinFunction("escape", (_, a, kwargs) =>
        {
            string s = (string)a[0];
            bool quote = a.Length > 1 ? a[1] is true
                : kwargs is not null && kwargs.TryGetValue("quote", out var q) ? q is true
                : true;
            s = s.Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;");
            if (quote)
                s = s.Replace("\"", "&quot;").Replace("'", "&#x27;");
            return s;
        });
        d["unescape"] = new PyBuiltinFunction("unescape", (_, a, _) =>
            System.Net.WebUtility.HtmlDecode((string)a[0]));

        return m;
    }
}
