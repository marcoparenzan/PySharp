// Copyright (c) 2026 Marco Parenzan
//
// Licensed under the MIT License. See the LICENSE file in the project
// root for full license information.

using PySharpLib.Runtime;

namespace PySharpLib.Modules;

/// <summary>
/// abc: ABC/ABCMeta as plain subclassable classes, abstractmethod as a passthrough decorator.
/// PySharp's class statement already ignores `metaclass=...` (v1 scope, see Interp.cs), so ABCMeta
/// never needs to actually run as a metaclass — code that subclasses it (e.g. pydantic's
/// `ModelMetaclass(ABCMeta)`) just needs it to exist and be subclassable. No enforcement of "can't
/// instantiate a class with unimplemented abstract methods" — nothing in the scenarios driving this
/// module relies on that failing loudly; add it if/when one does.
/// </summary>
public static class AbcModule
{
    public static readonly PyClass AbcMetaClass = new("ABCMeta", new List<PyClass>());
    public static readonly PyClass AbcClass = new("ABC", new List<PyClass>());

    public static PyModule Create()
    {
        var m = new PyModule("abc");
        var d = m.Dict;

        d["ABCMeta"] = AbcMetaClass;
        d["ABC"] = AbcClass;

        d["abstractmethod"] = new PyBuiltinFunction("abstractmethod", (_, a, _) =>
        {
            if (a[0] is PyFunction fn)
                fn.Attributes["__isabstractmethod__"] = true;
            return a[0];
        });
        // abstractproperty (deprecated in real Python, but old code still imports it):
        // property() wrapping an abstractmethod-marked getter — a thin passthrough here too.
        d["abstractproperty"] = new PyBuiltinFunction("abstractproperty", (_, a, _) => a[0]);

        return m;
    }
}
