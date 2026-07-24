// Copyright (c) 2026 Marco Parenzan
//
// Licensed under the MIT License. See the LICENSE file in the project
// root for full license information.

namespace PySharpLib.Runtime;

/// <summary>Python class (also for builtin types and exceptions).</summary>
public sealed class PyClass
{
    public string Name { get; }
    public List<PyClass> Bases { get; }
    public PyDict Dict { get; } = new();
    /// <summary>MRO computed with C3 linearization (this included at the head).</summary>
    public List<PyClass> Mro { get; }

    public PyClass(string name, List<PyClass> bases)
    {
        Name = name;
        Bases = bases;
        Mro = ComputeMro();
    }

    private List<PyClass> ComputeMro()
    {
        // C3: mro(C) = C + merge(mro(B1), ..., mro(Bn), [B1..Bn])
        var sequences = new List<List<PyClass>> { new() { this } };
        foreach (var b in Bases)
            sequences.Add(new List<PyClass>(b.Mro));
        if (Bases.Count > 0)
            sequences.Add(new List<PyClass>(Bases));

        var result = new List<PyClass>();
        while (sequences.Any(s => s.Count > 0))
        {
            PyClass? candidate = null;
            foreach (var seq in sequences)
            {
                if (seq.Count == 0)
                    continue;
                var head = seq[0];
                bool inTail = sequences.Any(s => s.Skip(1).Contains(head));
                if (!inTail)
                {
                    candidate = head;
                    break;
                }
            }
            if (candidate is null)
                throw PyErr.TypeError($"Cannot create a consistent MRO for class {Name}");
            result.Add(candidate);
            foreach (var seq in sequences)
                if (seq.Count > 0 && ReferenceEquals(seq[0], candidate))
                    seq.RemoveAt(0);
        }
        return result;
    }

    /// <summary>Looks up a name along the MRO. False if absent.</summary>
    public bool TryLookup(string name, out object value)
    {
        foreach (var cls in Mro)
        {
            if (cls.Dict.TryGet(name, out value!))
                return true;
        }
        value = PyNone.Instance;
        return false;
    }

    public bool IsSubclassOf(PyClass other) => Mro.Contains(other);

    public override string ToString() => $"<class '{Name}'>";
}

/// <summary>Instance of a Python class (including exceptions).</summary>
public sealed class PyInstance
{
    public PyClass Class { get; }
    public PyDict Dict { get; } = new();

    public PyInstance(PyClass cls) => Class = cls;

    public override string ToString() => $"<{Class.Name} object>";
}
