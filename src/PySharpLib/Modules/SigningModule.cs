// Copyright (c) 2026 Marco Parenzan
//
// Licensed under the MIT License. See the LICENSE file in the project
// root for full license information.

using System.Security.Cryptography;
using PySharpLib.Runtime;

namespace PySharpLib.Modules;

/// <summary>pysharp_signing: asymmetric signatures (ECDSA P-256/P-384, RSA-PSS) backed by .NET's
/// System.Security.Cryptography. Not a CPython stdlib module — CPython has no asymmetric crypto in
/// the stdlib (people use the third-party `cryptography` package, which PySharp cannot load) — so it
/// is a small, deliberately boring PySharp-specific builtin, like `mathema`. The reason it exists:
/// a device that runs downloaded code must verify a signature with a *public* key it cannot sign
/// with; hashlib/hmac alone only give a shared secret.
///
/// API: `generate_keypair(alg) -> (private_pem, public_pem)`; `sign(alg, private_pem, data) -> bytes`;
/// `verify(alg, public_pem, data, signature) -> bool`. Algorithms: "ecdsa-p256-sha256",
/// "ecdsa-p384-sha384", "rsa-pss-sha256". Keys are PEM (PKCS#8 private, SubjectPublicKeyInfo
/// public). ECDSA signatures are IEEE P1363 (fixed-size r||s), RSA-PSS uses a salt as long as the
/// hash. `verify` returns False for a wrong/garbled signature; ValueError for an unknown algorithm
/// or a key that does not parse or does not match the algorithm.</summary>
public static class SigningModule
{
    public static PyModule Create()
    {
        var m = new PyModule("pysharp_signing");
        var d = m.Dict;

        d["generate_keypair"] = new PyBuiltinFunction("generate_keypair", (_, a, _) =>
        {
            string alg = Alg(a, 0);
            switch (alg)
            {
                case "ecdsa-p256-sha256":
                case "ecdsa-p384-sha384":
                {
                    using var k = ECDsa.Create(alg == "ecdsa-p256-sha256" ? ECCurve.NamedCurves.nistP256 : ECCurve.NamedCurves.nistP384);
                    return new PyTuple(new object[] { k.ExportPkcs8PrivateKeyPem(), k.ExportSubjectPublicKeyInfoPem() });
                }
                default:
                {
                    using var k = RSA.Create(3072);
                    return new PyTuple(new object[] { k.ExportPkcs8PrivateKeyPem(), k.ExportSubjectPublicKeyInfoPem() });
                }
            }
        });

        d["sign"] = new PyBuiltinFunction("sign", (_, a, _) =>
        {
            string alg = Alg(a, 0);
            string pem = Pem(a, 1);
            byte[] data = CryptoModules.AsBytes(a[2]);
            try
            {
                if (alg == "rsa-pss-sha256")
                {
                    using var k = RSA.Create();
                    k.ImportFromPem(pem);
                    return new PyBytes(k.SignData(data, HashAlgorithmName.SHA256, RSASignaturePadding.Pss));
                }
                using var e = EcKey(alg, pem, privateKey: true);
                return new PyBytes(e.SignData(data, EcHash(alg), DSASignatureFormat.IeeeP1363FixedFieldConcatenation));
            }
            catch (CryptographicException ex)
            {
                throw PyErr.Raise(PyErr.ValueErrorClass, $"cannot sign: {ex.Message}");
            }
        });

        d["verify"] = new PyBuiltinFunction("verify", (_, a, _) =>
        {
            string alg = Alg(a, 0);
            string pem = Pem(a, 1);
            byte[] data = CryptoModules.AsBytes(a[2]);
            byte[] sig = CryptoModules.AsBytes(a[3]);

            // 1) the key: a key that does not parse (or is on the wrong curve) is a caller error.
            RSA? rsa = null;
            ECDsa? ec = null;
            try
            {
                if (alg == "rsa-pss-sha256")
                {
                    rsa = RSA.Create();
                    rsa.ImportFromPem(pem);
                }
                else
                {
                    ec = EcKey(alg, pem, privateKey: false);
                }
            }
            catch (Exception ex) when (ex is CryptographicException or ArgumentException)
            {
                rsa?.Dispose();
                throw PyErr.Raise(PyErr.ValueErrorClass, $"cannot read key: {ex.Message}");
            }

            // 2) the signature: anything wrong with it simply means "not valid".
            try
            {
                return rsa is not null
                    ? rsa.VerifyData(data, sig, HashAlgorithmName.SHA256, RSASignaturePadding.Pss)
                    : ec!.VerifyData(data, sig, EcHash(alg), DSASignatureFormat.IeeeP1363FixedFieldConcatenation);
            }
            catch (CryptographicException)
            {
                return false;
            }
            finally
            {
                rsa?.Dispose();
                ec?.Dispose();
            }
        });

        return m;
    }

    private static string Alg(object?[] a, int i)
    {
        string alg = a.Length > i && a[i] is string s ? s : throw PyErr.TypeError("algorithm name (str) required");
        return alg is "ecdsa-p256-sha256" or "ecdsa-p384-sha384" or "rsa-pss-sha256"
            ? alg
            : throw PyErr.Raise(PyErr.ValueErrorClass, $"unsupported algorithm '{alg}' (supported: ecdsa-p256-sha256, ecdsa-p384-sha384, rsa-pss-sha256)");
    }

    private static string Pem(object?[] a, int i)
        => a.Length > i && a[i] is string s ? s
         : a.Length > i && a[i] is PyBytes b ? System.Text.Encoding.ASCII.GetString(b.Data)
         : throw PyErr.TypeError("key must be a PEM str or bytes");

    private static HashAlgorithmName EcHash(string alg) => alg == "ecdsa-p256-sha256" ? HashAlgorithmName.SHA256 : HashAlgorithmName.SHA384;

    /// <summary>Imports an EC key and checks it is on the curve the algorithm name promises.</summary>
    private static ECDsa EcKey(string alg, string pem, bool privateKey)
    {
        var e = ECDsa.Create();
        try
        {
            e.ImportFromPem(pem);
            int want = alg == "ecdsa-p256-sha256" ? 256 : 384;
            if (e.KeySize != want)
                throw PyErr.Raise(PyErr.ValueErrorClass, $"key is P-{e.KeySize}, but algorithm '{alg}' needs P-{want}");
            return e;
        }
        catch
        {
            e.Dispose();
            throw;
        }
    }
}
