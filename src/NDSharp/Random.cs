// Copyright (c) 2026 Marco Parenzan
//
// Licensed under the MIT License. See the LICENSE file in the project
// root for full license information.

using System.Numerics;
using System.Security.Cryptography;

namespace NDSharp.Random;

/// <summary>numpy's <c>SeedSequence</c>: turns any entropy (an int, a list of ints, or OS entropy)
/// into well-mixed 32-bit words (port of <c>numpy/random/bit_generator.pyx</c>).</summary>
public sealed class SeedSequence
{
    private const uint InitA = 0x43b0d7e5, MultA = 0x931e8875, InitB = 0x8b51f9dd, MultB = 0x58f38ded;
    private const uint MixMultL = 0xca01f9dd, MixMultR = 0x4973f715;
    private const int XShift = 16, PoolSize = 4;

    private readonly uint[] _pool = new uint[PoolSize];

    public SeedSequence(IReadOnlyList<BigInteger>? entropy = null)
    {
        entropy ??= new[] { RandomEntropy() };
        var words = new List<uint>();
        foreach (var e in entropy)
        {
            if (e.Sign < 0) throw new NDValueException("expected non-negative integer");
            var n = e;
            if (n.IsZero) words.Add(0);
            while (n > 0) { words.Add((uint)(n & 0xFFFFFFFF)); n >>= 32; }
        }
        MixEntropy(words);
    }

    public SeedSequence(BigInteger entropy) : this(new[] { entropy }) { }

    private static BigInteger RandomEntropy()
    {
        var bytes = new byte[17]; // 128 bits, as numpy draws from the OS
        RandomNumberGenerator.Fill(bytes.AsSpan(0, 16));
        return new BigInteger(bytes);
    }

    private static uint HashMix(uint value, ref uint hashConst)
    {
        value ^= hashConst;
        hashConst *= MultA;
        value *= hashConst;
        value ^= value >> XShift;
        return value;
    }

    private static uint Mix(uint x, uint y)
    {
        uint result = unchecked(MixMultL * x - MixMultR * y);
        return result ^ (result >> XShift);
    }

    private void MixEntropy(List<uint> entropy)
    {
        uint hashConst = InitA;
        for (int i = 0; i < PoolSize; i++)
            _pool[i] = HashMix(i < entropy.Count ? entropy[i] : 0u, ref hashConst);
        for (int src = 0; src < PoolSize; src++)
            for (int dst = 0; dst < PoolSize; dst++)
                if (src != dst)
                    _pool[dst] = Mix(_pool[dst], HashMix(_pool[src], ref hashConst));
        for (int src = PoolSize; src < entropy.Count; src++)
            for (int dst = 0; dst < PoolSize; dst++)
                _pool[dst] = Mix(_pool[dst], HashMix(entropy[src], ref hashConst));
    }

    /// <summary>numpy <c>generate_state(n, np.uint64)</c>.</summary>
    public ulong[] GenerateState64(int n)
    {
        var words = new uint[n * 2];
        uint hashConst = InitB;
        for (int i = 0; i < words.Length; i++)
        {
            uint v = _pool[i % PoolSize];
            v ^= hashConst;
            hashConst *= MultB;
            v *= hashConst;
            v ^= v >> XShift;
            words[i] = v;
        }
        var result = new ulong[n];
        for (int i = 0; i < n; i++)
            result[i] = words[2 * i] | ((ulong)words[2 * i + 1] << 32);
        return result;
    }
}

/// <summary>numpy's default bit generator: PCG64 (128-bit LCG, XSL-RR output).</summary>
public sealed class PCG64
{
    private static readonly UInt128 Mult = ((UInt128)0x2360ED051FC65DA4UL << 64) | 0x4385DF649FCCF645UL;

    private UInt128 _state;
    private UInt128 _inc;
    private bool _hasUInt32;
    private uint _uinteger;

    public PCG64(SeedSequence seed)
    {
        var s = seed.GenerateState64(4);
        var initState = ((UInt128)s[0] << 64) | s[1];
        var initSeq = ((UInt128)s[2] << 64) | s[3];
        _state = 0;
        _inc = (initSeq << 1) | 1;
        Step();
        _state += initState;
        Step();
    }

    private void Step() => _state = unchecked(_state * Mult + _inc);

    public ulong Next64()
    {
        Step();
        ulong hi = (ulong)(_state >> 64), lo = (ulong)_state;
        ulong x = hi ^ lo;
        int rot = (int)(hi >> 58);
        return (x >> rot) | (x << ((-rot) & 63));
    }

    /// <summary>32-bit draws split a 64-bit output into low then high halves, buffered across calls.</summary>
    public uint Next32()
    {
        if (_hasUInt32)
        {
            _hasUInt32 = false;
            return _uinteger;
        }
        ulong next = Next64();
        _hasUInt32 = true;
        _uinteger = (uint)(next >> 32);
        return (uint)next;
    }

    public double NextDouble() => (Next64() >> 11) * (1.0 / 9007199254740992.0);
}
