// Copyright (c) 2026 Marco Parenzan
//
// Licensed under the MIT License. See the LICENSE file in the project
// root for full license information.

using System.Security.Cryptography;

namespace PySharp.Tests.M6_Stdlib;

/// <summary>pysharp_signing: ECDSA P-256/P-384 and RSA-PSS sign/verify, and interop with .NET.</summary>
public class SigningModuleTests
{
    private static string Run(string body) => Py.Run(body).TrimEnd('\n');

    [Theory]
    [InlineData("ecdsa-p256-sha256")]
    [InlineData("ecdsa-p384-sha384")]
    [InlineData("rsa-pss-sha256")]
    public void Sign_then_verify_roundtrips_and_rejects_tampering_and_other_keys(string alg)
        => Assert.Equal("True\nFalse\nFalse\nFalse", Run($$"""
            import pysharp_signing as s
            priv, pub = s.generate_keypair("{{alg}}")
            _, other_pub = s.generate_keypair("{{alg}}")
            data = b"print('hello from payload v2')"
            sig = s.sign("{{alg}}", priv, data)
            print(s.verify("{{alg}}", pub, data, sig))
            print(s.verify("{{alg}}", pub, data + b" ", sig))
            print(s.verify("{{alg}}", other_pub, data, sig))
            print(s.verify("{{alg}}", pub, data, sig[:-1] + bytes([sig[-1] ^ 1])))
            """));

    [Fact]
    public void Garbage_signature_is_false_not_an_error()
        => Assert.Equal("False\nFalse", Run("""
            import pysharp_signing as s
            priv, pub = s.generate_keypair("ecdsa-p256-sha256")
            print(s.verify("ecdsa-p256-sha256", pub, b"x", b""))
            print(s.verify("ecdsa-p256-sha256", pub, b"x", b"not a signature"))
            """));

    [Fact]
    public void Unknown_algorithm_unparsable_key_and_wrong_curve_raise_ValueError()
        => Assert.Equal("ValueError\nValueError\nValueError", Run("""
            import pysharp_signing as s
            priv, pub = s.generate_keypair("ecdsa-p256-sha256")
            for f in (lambda: s.sign("ed25519", priv, b"x"),
                      lambda: s.verify("ecdsa-p256-sha256", "-----BEGIN PUBLIC KEY-----\nAAAA\n-----END PUBLIC KEY-----", b"x", b"y"),
                      lambda: s.verify("ecdsa-p384-sha384", pub, b"x", b"y")):
                try:
                    f()
                    print("no error")
                except ValueError:
                    print("ValueError")
            """));

    [Fact]
    public void Signature_made_by_dotnet_verifies_in_pysharp_and_the_other_way_round()
    {
        // Forge signs in .NET, the device verifies in PySharp: this is the real deployment shape.
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP384);
        byte[] data = System.Text.Encoding.UTF8.GetBytes("def on_tick(ctx): pass\n");
        string sigHex = Convert.ToHexString(key.SignData(data, HashAlgorithmName.SHA384, DSASignatureFormat.IeeeP1363FixedFieldConcatenation));
        string pub = Convert.ToHexString(System.Text.Encoding.ASCII.GetBytes(key.ExportSubjectPublicKeyInfoPem()));
        string priv = Convert.ToHexString(System.Text.Encoding.ASCII.GetBytes(key.ExportPkcs8PrivateKeyPem()));
        string output = Run($$"""
            import binascii
            import pysharp_signing as s
            data = b"def on_tick(ctx): pass\n"
            print(s.verify("ecdsa-p384-sha384", binascii.unhexlify("{{pub}}"), data, binascii.unhexlify("{{sigHex}}")))
            print(binascii.hexlify(s.sign("ecdsa-p384-sha384", binascii.unhexlify("{{priv}}"), data)).decode())
            """);
        var lines = output.Split('\n');
        Assert.Equal("True", lines[0]);
        Assert.True(key.VerifyData(data, Convert.FromHexString(lines[1]), HashAlgorithmName.SHA384, DSASignatureFormat.IeeeP1363FixedFieldConcatenation));
    }
}
