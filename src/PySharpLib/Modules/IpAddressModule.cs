// Copyright (c) 2026 Marco Parenzan
//
// Licensed under the MIT License. See the LICENSE file in the project
// root for full license information.

using System.Net;
using System.Net.Sockets;
using System.Numerics;
using PySharpLib.Runtime;

namespace PySharpLib.Modules;

/// <summary>
/// ipaddress: IPv4/IPv6 Address/Network/Interface, backed by System.Net.IPAddress and
/// System.Net.IPNetwork (.NET 8+, native CIDR support). Arithmetic isn't a thing here — mostly
/// construction/validation, string formatting, containment and comparison, all riding the
/// interpreter's existing generic instance-dunder dispatch. v1 scope: construction from a string
/// (or int for addresses) and the commonly-read properties — not the full API (no arithmetic on
/// addresses, no subnet-splitting helpers). Found via pydantic v1's real dependency chain
/// (IP-address field validators). See FASTAPI_PLAN.md Phase 1.9.
/// </summary>
public static class IpAddressModule
{
    private const string ValueKey = "__value__";

    // Real CPython's ipaddress module gives IPv4Address/IPv6Address and IPv4Network/IPv6Network
    // shared, otherwise-empty-here base classes. pydantic v1's IPvAnyAddress/IPvAnyNetwork use
    // these purely as base classes to hang their own classmethods off of (see networks.py) — they
    // never rely on inherited behavior — so bare marker classes are enough.
    public static readonly PyClass BaseAddressClass = new("_BaseAddress", new List<PyClass>());
    public static readonly PyClass BaseNetworkClass = new("_BaseNetwork", new List<PyClass>());

    public static readonly PyClass IPv4AddressClass = BuildAddressClass("IPv4Address", AddressFamily.InterNetwork, 4);
    public static readonly PyClass IPv6AddressClass = BuildAddressClass("IPv6Address", AddressFamily.InterNetworkV6, 6);
    public static readonly PyClass IPv4NetworkClass = BuildNetworkClass("IPv4Network", AddressFamily.InterNetwork, 4);
    public static readonly PyClass IPv6NetworkClass = BuildNetworkClass("IPv6Network", AddressFamily.InterNetworkV6, 6);
    public static readonly PyClass IPv4InterfaceClass = BuildInterfaceClass("IPv4Interface", IPv4AddressClass, IPv4NetworkClass, 4);
    public static readonly PyClass IPv6InterfaceClass = BuildInterfaceClass("IPv6Interface", IPv6AddressClass, IPv6NetworkClass, 6);

    public static PyModule Create()
    {
        var m = new PyModule("ipaddress");
        m.Dict["_BaseAddress"] = BaseAddressClass;
        m.Dict["_BaseNetwork"] = BaseNetworkClass;
        m.Dict["IPv4Address"] = IPv4AddressClass;
        m.Dict["IPv6Address"] = IPv6AddressClass;
        m.Dict["IPv4Network"] = IPv4NetworkClass;
        m.Dict["IPv6Network"] = IPv6NetworkClass;
        m.Dict["IPv4Interface"] = IPv4InterfaceClass;
        m.Dict["IPv6Interface"] = IPv6InterfaceClass;
        m.Dict["ip_address"] = new PyBuiltinFunction("ip_address", (_, a, _) =>
        {
            var addr = ParseAddress((string)a[0]);
            return addr.AddressFamily == AddressFamily.InterNetwork ? MakeAddress(IPv4AddressClass, addr) : MakeAddress(IPv6AddressClass, addr);
        });
        return m;
    }

    private static IPAddress ParseAddress(string s)
    {
        if (!IPAddress.TryParse(s.Trim(), out var addr))
            throw new PyRaise(PyErr.MakeInstance(PyErr.ValueErrorClass, $"'{s}' does not appear to be an IPv4 or IPv6 address"));
        return addr;
    }

    private static PyInstance MakeAddress(PyClass cls, IPAddress addr)
    {
        var inst = new PyInstance(cls);
        inst.Dict[ValueKey] = addr;
        return inst;
    }

    private static PyClass BuildAddressClass(string name, AddressFamily family, int version)
    {
        var cls = new PyClass(name, new List<PyClass> { BaseAddressClass });
        void Add(string n, BuiltinFn fn) => cls.Dict[n] = new PyBuiltinFunction($"{name}.{n}", fn);

        Add("__init__", (_, a, _) =>
        {
            IPAddress addr = a[1] switch
            {
                string s => ParseAddress(s),
                BigInteger bi => new IPAddress(IntToBytes(bi, version == 4 ? 4 : 16)),
                _ => throw PyErr.TypeError($"{name}() argument must be a string or int"),
            };
            if (addr.AddressFamily != family)
                throw new PyRaise(PyErr.MakeInstance(PyErr.ValueErrorClass, $"'{addr}' does not appear to be an IPv{version} address"));
            ((PyInstance)a[0]).Dict[ValueKey] = addr;
            return PyNone.Instance;
        });

        IPAddress Value(object self) => (IPAddress)((PyInstance)self).Dict[ValueKey];

        Add("__str__", (_, a, _) => Value(a[0]).ToString());
        Add("__repr__", (_, a, _) => $"{name}('{Value(a[0])}')");
        Add("__eq__", (_, a, _) => a[1] is PyInstance i && i.Class == cls && Value(a[0]).Equals(Value(a[1])));
        Add("__hash__", (_, a, _) => new BigInteger(Value(a[0]).GetHashCode()));
        cls.Dict["version"] = new PyProperty { Getter = new PyBuiltinFunction($"{name}.version", (_, _, _) => new BigInteger(version)) };
        cls.Dict["packed"] = new PyProperty { Getter = new PyBuiltinFunction($"{name}.packed", (_, a, _) => new PyBytes(Value(a[0]).GetAddressBytes())) };
        cls.Dict["is_private"] = new PyProperty { Getter = new PyBuiltinFunction($"{name}.is_private", (_, a, _) => IsPrivate(Value(a[0]))) };
        cls.Dict["is_loopback"] = new PyProperty { Getter = new PyBuiltinFunction($"{name}.is_loopback", (_, a, _) => IPAddress.IsLoopback(Value(a[0]))) };
        cls.Dict["is_multicast"] = new PyProperty { Getter = new PyBuiltinFunction($"{name}.is_multicast", (_, a, _) => Value(a[0]).IsIPv6Multicast || Value(a[0]).GetAddressBytes() is [>= 224 and <= 239, ..]) };

        Add("__int__", (_, a, _) => BytesToInt(Value(a[0]).GetAddressBytes()));

        return cls;
    }

    private static bool IsPrivate(IPAddress addr)
    {
        var b = addr.GetAddressBytes();
        if (addr.AddressFamily == AddressFamily.InterNetwork)
        {
            return b[0] == 10
                   || (b[0] == 172 && b[1] is >= 16 and <= 31)
                   || (b[0] == 192 && b[1] == 168)
                   || b[0] == 127;
        }
        return addr.IsIPv6LinkLocal || addr.IsIPv6SiteLocal || IPAddress.IsLoopback(addr);
    }

    private static byte[] IntToBytes(BigInteger value, int length)
    {
        var bytes = new byte[length];
        var be = value.ToByteArray(isUnsigned: true, isBigEndian: true);
        Array.Copy(be, 0, bytes, length - be.Length, Math.Min(be.Length, length));
        return bytes;
    }

    private static BigInteger BytesToInt(byte[] bytes) => new(bytes, isUnsigned: true, isBigEndian: true);

    private static PyClass BuildNetworkClass(string name, AddressFamily family, int version)
    {
        var cls = new PyClass(name, new List<PyClass> { BaseNetworkClass });
        void Add(string n, BuiltinFn fn) => cls.Dict[n] = new PyBuiltinFunction($"{name}.{n}", fn);
        int maxPrefix = version == 4 ? 32 : 128;

        Add("__init__", (_, a, _) =>
        {
            string s = a[1] is string str ? str : a[1].ToString()!;
            var (addr, prefix) = ParseNetwork(s, maxPrefix, family, name, version);
            var inst = (PyInstance)a[0];
            inst.Dict[ValueKey] = addr;
            inst.Dict["__prefix__"] = prefix;
            return PyNone.Instance;
        });

        (IPAddress NetworkAddr, int Prefix) V(object self)
        {
            var inst = (PyInstance)self;
            return ((IPAddress)inst.Dict[ValueKey], (int)inst.Dict["__prefix__"]);
        }

        Add("__str__", (_, a, _) => { var (addr, p) = V(a[0]); return $"{addr}/{p}"; });
        Add("__repr__", (_, a, _) => { var (addr, p) = V(a[0]); return $"{name}('{addr}/{p}')"; });
        Add("__eq__", (_, a, _) =>
        {
            if (a[1] is not PyInstance i || i.Class != cls)
                return false;
            var (a1, p1) = V(a[0]);
            var (a2, p2) = V(a[1]);
            return a1.Equals(a2) && p1 == p2;
        });
        cls.Dict["version"] = new PyProperty { Getter = new PyBuiltinFunction($"{name}.version", (_, _, _) => new BigInteger(version)) };
        cls.Dict["prefixlen"] = new PyProperty { Getter = new PyBuiltinFunction($"{name}.prefixlen", (_, a, _) => new BigInteger(V(a[0]).Prefix)) };
        cls.Dict["network_address"] = new PyProperty { Getter = new PyBuiltinFunction($"{name}.network_address", (_, a, _) => MakeAddress(version == 4 ? IPv4AddressClass : IPv6AddressClass, V(a[0]).NetworkAddr)) };
        cls.Dict["num_addresses"] = new PyProperty { Getter = new PyBuiltinFunction($"{name}.num_addresses", (_, a, _) => BigInteger.Pow(2, maxPrefix - V(a[0]).Prefix)) };
        cls.Dict["broadcast_address"] = new PyProperty
        {
            Getter = new PyBuiltinFunction($"{name}.broadcast_address", (_, a, _) =>
            {
                var (netAddr, prefix) = V(a[0]);
                var bytes = netAddr.GetAddressBytes();
                int hostBits = maxPrefix - prefix;
                for (int bit = 0; bit < hostBits; bit++)
                {
                    int byteIdx = bytes.Length - 1 - bit / 8;
                    bytes[byteIdx] |= (byte)(1 << (bit % 8));
                }
                return MakeAddress(version == 4 ? IPv4AddressClass : IPv6AddressClass, new IPAddress(bytes));
            }),
        };
        Add("__contains__", (_, a, _) =>
        {
            var (netAddr, prefix) = V(a[0]);
            if (a[1] is not PyInstance other || other.Dict[ValueKey] is not IPAddress otherAddr)
                return false;
            return AddressInNetwork(otherAddr, netAddr, prefix);
        });

        return cls;
    }

    private static (IPAddress NetworkAddr, int Prefix) ParseNetwork(string s, int maxPrefix, AddressFamily family, string name, int version)
    {
        s = s.Trim();
        int slash = s.IndexOf('/');
        string addrPart = slash >= 0 ? s[..slash] : s;
        int prefix = slash >= 0 ? int.Parse(s[(slash + 1)..]) : maxPrefix;
        if (!IPAddress.TryParse(addrPart, out var addr) || addr.AddressFamily != family)
            throw new PyRaise(PyErr.MakeInstance(PyErr.ValueErrorClass, $"'{s}' does not appear to be an IPv{version} network"));
        if (prefix < 0 || prefix > maxPrefix)
            throw new PyRaise(PyErr.MakeInstance(PyErr.ValueErrorClass, $"{prefix} is not a valid netmask"));

        var bytes = addr.GetAddressBytes();
        int hostBits = maxPrefix - prefix;
        for (int bit = 0; bit < hostBits; bit++)
        {
            int byteIdx = bytes.Length - 1 - bit / 8;
            bytes[byteIdx] &= (byte)~(1 << (bit % 8));
        }
        return (new IPAddress(bytes), prefix);
    }

    private static bool AddressInNetwork(IPAddress addr, IPAddress networkAddr, int prefix)
    {
        if (addr.AddressFamily != networkAddr.AddressFamily)
            return false;
        var a = addr.GetAddressBytes();
        var n = networkAddr.GetAddressBytes();
        int fullBytes = prefix / 8;
        for (int i = 0; i < fullBytes; i++)
            if (a[i] != n[i])
                return false;
        int remBits = prefix % 8;
        if (remBits > 0)
        {
            int mask = 0xFF << (8 - remBits) & 0xFF;
            if ((a[fullBytes] & mask) != (n[fullBytes] & mask))
                return false;
        }
        return true;
    }

    private static PyClass BuildInterfaceClass(string name, PyClass addressClass, PyClass networkClass, int version)
    {
        var cls = new PyClass(name, new List<PyClass>());
        void Add(string n, BuiltinFn fn) => cls.Dict[n] = new PyBuiltinFunction($"{name}.{n}", fn);
        int maxPrefix = version == 4 ? 32 : 128;

        Add("__init__", (interp, a, _) =>
        {
            string s = a[1] is string str ? str : a[1].ToString()!;
            int slash = s.IndexOf('/');
            string addrPart = slash >= 0 ? s[..slash] : s;
            int prefix = slash >= 0 ? int.Parse(s[(slash + 1)..]) : maxPrefix;
            var addr = ParseAddress(addrPart);
            var inst = (PyInstance)a[0];
            inst.Dict["ip"] = MakeAddress(addressClass, addr);
            inst.Dict["network"] = interp.Call(networkClass, new object[] { $"{addr}/{prefix}" });
            inst.Dict["__addr__"] = addr; // raw IPAddress, so __str__/__repr__ don't need Python dispatch
            inst.Dict["__prefix__"] = prefix;
            return PyNone.Instance;
        });

        Add("__str__", (_, a, _) =>
        {
            var inst = (PyInstance)a[0];
            return $"{inst.Dict["__addr__"]}/{inst.Dict["__prefix__"]}";
        });
        Add("__repr__", (_, a, _) =>
        {
            var inst = (PyInstance)a[0];
            return $"{name}('{inst.Dict["__addr__"]}/{inst.Dict["__prefix__"]}')";
        });
        cls.Dict["version"] = new PyProperty { Getter = new PyBuiltinFunction($"{name}.version", (_, _, _) => new BigInteger(version)) };

        return cls;
    }
}
