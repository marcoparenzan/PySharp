// Copyright (c) 2026 Marco Parenzan
//
// Licensed under the MIT License. See the LICENSE file in the project
// root for full license information.

using PySharpLib.Runtime;

namespace PySharpLib.Modules;

/// <summary>netrc: real `netrc`/`NetrcParseError` — a whitespace-tokenized parser for the real
/// `.netrc` file format (`machine ... login ... password ... account ...`), scoped to the common
/// case (no `macdef` macro-body support — nothing reachable defines one). Found via real httpx's
/// `_utils.py` (`import netrc`, `netrc.netrc(path)`/`netrc.NetrcParseError`), used to look up
/// credentials for a request's host from `~/.netrc` when `trust_env` is set — only actually
/// constructed when such a file exists on disk, so this is exercised at import time only for our
/// own reachable test/sample scenarios.</summary>
public static class NetrcModule
{
    public static readonly PyClass NetrcParseErrorClass = new("NetrcParseError", new List<PyClass> { PyErr.Exception });
    public static readonly PyClass NetrcClass = BuildNetrcClass();

    public static PyModule Create()
    {
        var m = new PyModule("netrc");
        m.Dict["netrc"] = NetrcClass;
        m.Dict["NetrcParseError"] = NetrcParseErrorClass;
        return m;
    }

    private static PyClass BuildNetrcClass()
    {
        var cls = new PyClass("netrc", new List<PyClass>());

        cls.Dict["__init__"] = new PyBuiltinFunction("netrc.__init__", (_, a, _) =>
        {
            var inst = (PyInstance)a[0];
            string? file = a.Length > 1 && a[1] is string s ? s : null;
            if (file is null)
            {
                string home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
                file = Path.Combine(home, ".netrc");
            }

            var hosts = new PyDict();
            if (File.Exists(file))
            {
                try
                {
                    ParseInto(File.ReadAllText(file), hosts);
                }
                catch (Exception ex)
                {
                    throw PyErr.Raise(NetrcParseErrorClass, ex.Message);
                }
            }
            inst.Dict["hosts"] = hosts;
            return PyNone.Instance;
        });

        cls.Dict["authenticators"] = new PyBuiltinFunction("netrc.authenticators", (_, a, _) =>
        {
            var hosts = (PyDict)((PyInstance)a[0]).Dict["hosts"];
            string host = (string)a[1];
            if (hosts.TryGet(host, out var v))
                return v;
            return hosts.TryGet("default", out var d) ? d : PyNone.Instance;
        });

        return cls;
    }

    private static void ParseInto(string text, PyDict hosts)
    {
        var tokens = text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
        string? machine = null;
        string login = "", account = "", password = "";

        void Flush()
        {
            if (machine is not null)
                hosts[machine] = new PyTuple(new object[]
                {
                    login,
                    account.Length > 0 ? account : PyNone.Instance,
                    password,
                });
        }

        for (int i = 0; i < tokens.Length; i++)
        {
            string Next() => ++i < tokens.Length ? tokens[i] : throw new FormatException("unexpected end of netrc file");
            switch (tokens[i])
            {
                case "machine":
                    Flush();
                    machine = Next();
                    login = account = password = "";
                    break;
                case "default":
                    Flush();
                    machine = "default";
                    login = account = password = "";
                    break;
                case "login":
                    login = Next();
                    break;
                case "password":
                    password = Next();
                    break;
                case "account":
                    account = Next();
                    break;
            }
        }
        Flush();
    }
}
