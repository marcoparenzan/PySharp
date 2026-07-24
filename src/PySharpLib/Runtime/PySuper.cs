// Copyright (c) 2026 Marco Parenzan
//
// Licensed under the MIT License. See the LICENSE file in the project
// root for full license information.

namespace PySharpLib.Runtime;

/// <summary>super() object: attribute lookup starts from the MRO after StartClass.</summary>
public sealed class PySuper
{
    public PyClass StartClass { get; }
    public object Self { get; }

    public PySuper(PyClass startClass, object self)
    {
        StartClass = startClass;
        Self = self;
    }

    /// <summary>Looks up name in the MRO of Self's class, skipping everything up to and including StartClass.</summary>
    public bool TryLookup(string name, out object value)
    {
        var mro = Self is PyInstance inst ? inst.Class.Mro
            : Self is PyClass cls ? cls.Mro
            : StartClass.Mro;
        int start = mro.IndexOf(StartClass);
        for (int i = start + 1; i < mro.Count; i++)
        {
            if (mro[i].Dict.TryGet(name, out value!))
                return true;
        }
        value = PyNone.Instance;
        return false;
    }

    public override string ToString() => $"<super: {StartClass.Name}>";
}
