// Copyright (c) 2026 Marco Parenzan
//
// Licensed under the MIT License. See the LICENSE file in the project
// root for full license information.

using System.Numerics;
using System.Security.Cryptography;
using PySharpLib.Runtime;

namespace PySharpLib.Modules;

/// <summary>secrets: token_bytes/token_hex/token_urlsafe/choice/randbelow/compare_digest, backed
/// by a real CSPRNG (System.Security.Cryptography.RandomNumberGenerator, the same one os.urandom
/// already uses) — real, not `random.random()`-backed. Found via starlette's real `from secrets
/// import token_hex` (responses.py, for FileResponse's ETag generation), reachable from `import
/// starlette`. See FASTAPI_PLAN.md Phase 3.</summary>
public static class SecretsModule
{
    private const int DefaultEntropy = 32;

    public static PyModule Create()
    {
        var m = new PyModule("secrets");
        var d = m.Dict;

        d["token_bytes"] = new PyBuiltinFunction("token_bytes", (_, a, _) =>
            new PyBytes(RandomNumberGenerator.GetBytes(NBytes(a))));

        d["token_hex"] = new PyBuiltinFunction("token_hex", (_, a, _) =>
            Convert.ToHexString(RandomNumberGenerator.GetBytes(NBytes(a))).ToLowerInvariant());

        d["token_urlsafe"] = new PyBuiltinFunction("token_urlsafe", (_, a, _) =>
            Convert.ToBase64String(RandomNumberGenerator.GetBytes(NBytes(a)))
                .Replace('+', '-').Replace('/', '_').TrimEnd('='));

        d["randbelow"] = new PyBuiltinFunction("randbelow", (_, a, _) =>
        {
            var bound = (int)PyOps.AsBigInt(a[0], "exclusive_upper_bound");
            if (bound <= 0)
                return (BigInteger)0;
            return (BigInteger)RandomNumberGenerator.GetInt32(bound);
        });

        d["choice"] = new PyBuiltinFunction("choice", (_, a, _) =>
        {
            var items = a[0] switch
            {
                PyList l => l.Items,
                PyTuple t => (IReadOnlyList<object>)t.Items,
                _ => throw PyErr.TypeError("choice() requires a sequence"),
            };
            if (items.Count == 0)
                throw PyErr.IndexError("Cannot choose from an empty sequence");
            return items[RandomNumberGenerator.GetInt32(items.Count)];
        });

        // Constant-time comparison — real CPython's actual point of this function.
        d["compare_digest"] = new PyBuiltinFunction("compare_digest", (_, a, _) =>
        {
            byte[] x = BytesOf(a[0]), y = BytesOf(a[1]);
            if (x.Length != y.Length)
                return false;
            int diff = 0;
            for (int i = 0; i < x.Length; i++)
                diff |= x[i] ^ y[i];
            return diff == 0;
        });

        return m;
    }

    private static int NBytes(object[] a)
        => a.Length > 0 && a[0] is not PyNone ? (int)PyOps.AsBigInt(a[0], "nbytes") : DefaultEntropy;

    private static byte[] BytesOf(object v) => v switch
    {
        PyBytes b => b.Data,
        PyByteArray b => b.Data.ToArray(),
        string s => System.Text.Encoding.UTF8.GetBytes(s),
        _ => throw PyErr.TypeError("comparison requires bytes or str instances"),
    };
}
