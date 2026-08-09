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
    /// <summary>
    /// The custom metaclass this class was built with (`class X(Y, metaclass=M): ...`), or null for
    /// the default (`type`). Subclasses that don't specify their own `metaclass=` inherit the first
    /// non-null one found among their bases — see ExecClassDef. Real CPython computes the winning
    /// metaclass across every base for multi-metaclass conflicts; not needed for anything in scope
    /// so far (single custom-metaclass chains, e.g. pydantic's BaseModel/ModelMetaclass).
    /// </summary>
    public PyClass? Metaclass { get; set; }

    /// <summary>
    /// Classes registered as virtual subclasses via real <c>abc.ABC.register()</c> (e.g.
    /// <c>os.PathLike.register(pathlib.Path)</c>) — recognized by isinstance/issubclass without
    /// appearing in the registered class's actual MRO, matching real ABCMeta.register semantics.
    /// Null until first used (the overwhelming majority of classes never register anything).
    /// </summary>
    public HashSet<PyClass>? VirtualSubclasses { get; private set; }

    public void RegisterVirtualSubclass(PyClass subclass)
        => (VirtualSubclasses ??= new HashSet<PyClass>()).Add(subclass);

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

    public bool IsSubclassOf(PyClass other)
        => Mro.Contains(other) || (other.VirtualSubclasses is { } vs && Mro.Any(vs.Contains));

    /// <summary>Real CPython: a name declared in any class's own `__slots__` (across the whole MRO)
    /// gets dedicated per-instance storage separate from `__dict__`, even when `__dict__` itself is
    /// also one of the declared slots (as pydantic's BaseModel does: `__slots__ = ('__dict__',
    /// '__fields_set__')`). PySharp doesn't implement full slot-descriptor storage, but this targeted
    /// check is enough to keep a real slot name (like `__fields_set__`) out of the instance's regular
    /// `PyInstance.Dict` — see `PyInstance.Slots`. Found via real pydantic's own `.dict()`
    /// (`main.py`'s `_iter`'s fast path, `yield from self.__dict__.items()`), which silently leaked
    /// `__fields_set__` into every serialized model because PySharp had nowhere else to put it.
    public bool HasSlot(string name)
    {
        if (name == "__dict__")
            return false;
        foreach (var cls in Mro)
        {
            if (!cls.Dict.TryGet("__slots__", out var slotsObj))
                continue;
            // pydantic's real ModelMetaclass computes a subclass's effective __slots__ as
            // `slots | private_attributes.keys()` — a real Python set, not a tuple/list.
            bool found = slotsObj switch
            {
                string s => s == name,
                PyTuple t => t.Items.Contains(name),
                PyList l => l.Items.Contains(name),
                PySet st => st.Items.Contains(name),
                PyFrozenSet fs => fs.Items.Contains(name),
                _ => false,
            };
            if (found)
                return true;
        }
        return false;
    }

    public override string ToString() => $"<class '{Name}'>";
}

/// <summary>Instance of a Python class (including exceptions).</summary>
public sealed class PyInstance
{
    public PyClass Class { get; }
    public PyDict Dict { get; } = new();

    /// <summary>Storage for real `__slots__` attributes, kept out of `Dict` (and so out of
    /// `self.__dict__`/`vars(self)`/anything iterating `Dict` directly) — see
    /// <see cref="PyClass.HasSlot"/>. Lazily allocated since the overwhelming majority of instances
    /// never use a slot attribute.</summary>
    public PyDict? Slots { get; private set; }

    public PyDict EnsureSlots() => Slots ??= new PyDict();

    public PyInstance(PyClass cls) => Class = cls;

    public override string ToString() => $"<{Class.Name} object>";
}
