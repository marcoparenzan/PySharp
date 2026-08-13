// Copyright (c) 2026 Marco Parenzan
//
// Licensed under the MIT License. See the LICENSE file in the project
// root for full license information.

using System.Numerics;
using PySharpLib.Runtime;

namespace PySharpLib.Modules;

/// <summary>
/// enum: the Enum/IntEnum base classes are marked with __is_enum__; member transformation
/// happens in Interp.ExecClassDef, lookup-by-value in Interp.Instantiate.
/// </summary>
public static class EnumModule
{
    public static readonly PyClass EnumClass = BuildEnumBase("Enum", intLike: false, isFlag: false);
    public static readonly PyClass IntEnumClass = BuildEnumBase("IntEnum", intLike: true, isFlag: false);
    // Real CPython: `Flag`/`IntFlag` are genuinely distinct from `Enum`/`IntEnum` — auto() generates
    // successive powers of two (not 1,2,3,...) and members combine via `|`/`&`/`^`/`~` into composite
    // values, rather than those operators being unsupported (plain Enum) or returning a bare int
    // (would-be IntEnum-style). Found via real sqlalchemy's own `engine/reflection.py`
    // `class ObjectKind(Flag): TABLE = auto(); ...; ANY_VIEW = VIEW | MATERIALIZED_VIEW` (combining
    // two auto()-assigned members with `|` inside the very same class body).
    public static readonly PyClass FlagClass = BuildEnumBase("Flag", intLike: false, isFlag: true);
    public static readonly PyClass IntFlagClass = BuildEnumBase("IntFlag", intLike: true, isFlag: true);
    public static readonly PyClass AutoClass = new("auto", new List<PyClass>());

    public static PyModule Create()
    {
        var m = new PyModule("enum");
        var d = m.Dict;
        d["Enum"] = EnumClass;
        d["IntEnum"] = IntEnumClass;
        d["IntFlag"] = IntFlagClass;
        d["Flag"] = FlagClass;
        d["auto"] = new PyBuiltinFunction("auto", (_, _, _) => new PyInstance(AutoClass));
        d["unique"] = new PyBuiltinFunction("unique", (_, a, _) => a[0]); // decorator identità
        return m;
    }

    private static BigInteger MemberValue(object self)
        => PyOps.AsBigInt(((PyInstance)self).Dict["value"], "enum value");

    private static object OtherValue(object other)
        => other is PyInstance inst && inst.Dict.TryGet("value", out var v) ? v : other;

    /// <summary>Real CPython Flag: combining members with `|`/`&`/`^` either returns the exact
    /// canonical member already defined for that value (if any), or a fresh, unnamed composite
    /// instance of the same class carrying the combined int value — never a bare int and never a
    /// distinct new registered member. Not added to `__members__`/iteration, matching real CPython
    /// (a pseudo-member is never itself part of the defined member set).</summary>
    private static object MakeFlagValue(PyClass cls, BigInteger value)
    {
        if (cls.Dict.TryGet("__members__", out var membersObj) && membersObj is PyDict members)
        {
            foreach (var e in members.Entries)
            {
                if (e.Value is PyInstance mi && PyOps.AsBigInt(mi.Dict["value"], "flag value") == value)
                    return mi;
            }
        }
        var inst = new PyInstance(cls);
        inst.Dict["name"] = PyNone.Instance;
        inst.Dict["_name_"] = PyNone.Instance;
        inst.Dict["value"] = value;
        inst.Dict["_value_"] = value;
        return inst;
    }

    private static PyClass BuildEnumBase(string name, bool intLike, bool isFlag)
    {
        var cls = new PyClass(name, new List<PyClass>());
        cls.Dict["__is_enum__"] = true;
        if (isFlag)
            cls.Dict["__is_flag__"] = true;
        // Real CPython: even the base `Enum`/`IntEnum` classes themselves (with no members of their
        // own) carry a real (empty) `__members__` — a plain user subclass's own body gets this
        // overwritten with its real members by ConvertToEnum. Found via real sqlalchemy's own
        // `sql/sqltypes.py` module-level `Enum(enum.Enum)` (a "template" Enum type with no concrete
        // members), whose `_parse_into_values` branches on `hasattr(enums[0], "__members__")` to
        // detect "this arg is itself an Enum class" rather than a literal member name.
        cls.Dict["__members__"] = new PyDict();
        void Add(string method, BuiltinFn fn) => cls.Dict[method] = new PyBuiltinFunction($"{name}.{method}", fn);

        Add("__repr__", (interp, a, _) =>
        {
            var inst = (PyInstance)a[0];
            return $"<{inst.Class.Name}.{inst.Dict["name"]}: {PyOps.Repr(interp, inst.Dict["value"])}>";
        });
        Add("__str__", (_, a, _) =>
        {
            var inst = (PyInstance)a[0];
            return $"{inst.Class.Name}.{inst.Dict["name"]}";
        });
        Add("__eq__", (interp, a, _) =>
        {
            var self = (PyInstance)a[0];
            var other = a[1];
            if (other is PyInstance oi && oi.Class.TryLookup("__is_enum__", out var _ignored))
                return ReferenceEquals(self, other)
                       || (self.Class == oi.Class && PyOps.PyEquals(self.Dict["value"], oi.Dict["value"]));
            if (intLike && PyOps.IsNumber(other))
                return PyOps.PyEquals(self.Dict["value"], other);
            return (object)PyNotImplemented.Instance;
        });
        Add("__hash__", (_, a, _) => new BigInteger(PyOps.PyHash(((PyInstance)a[0]).Dict["value"])));

        if (intLike)
        {
            Add("__int__", (_, a, _) => MemberValue(a[0]));
            Add("__index__", (_, a, _) => MemberValue(a[0]));
            Add("__bool__", (_, a, _) => !MemberValue(a[0]).IsZero);
            Add("__format__", (interp, a, _) =>
                PyFormat.Format(interp, MemberValue(a[0]), a.Length > 1 ? (string)a[1] : ""));

            foreach (var op in new[] { "add", "sub", "mul", "mod", "and", "or", "xor", "lshift", "rshift", "floordiv" })
            {
                string binOp = op switch
                {
                    "add" => "+", "sub" => "-", "mul" => "*", "mod" => "%",
                    "and" => "&", "or" => "|", "xor" => "^",
                    "lshift" => "<<", "rshift" => ">>", "floordiv" => "//",
                    _ => throw new InvalidOperationException(),
                };
                Add($"__{op}__", (interp, a, _) =>
                    interp.BinaryOp(binOp, MemberValue(a[0]), OtherValue(a[1])));
                Add($"__r{op}__", (interp, a, _) =>
                    interp.BinaryOp(binOp, OtherValue(a[1]), MemberValue(a[0])));
            }
            foreach (var (dunder, op) in new[] { ("__lt__", "<"), ("__le__", "<="), ("__gt__", ">"), ("__ge__", ">=") })
            {
                string cmpOp = op;
                Add(dunder, (interp, a, _) =>
                {
                    var other = OtherValue(a[1]);
                    if (!PyOps.IsNumber(other))
                        return PyNotImplemented.Instance;
                    return interp.Compare(MemberValue(a[0]), other) switch
                    {
                        var c => cmpOp switch
                        {
                            "<" => c < 0,
                            "<=" => c <= 0,
                            ">" => c > 0,
                            _ => c >= 0,
                        },
                    };
                });
            }
        }

        if (isFlag)
        {
            // Overrides the raw-int `__or__`/`__and__`/`__xor__` the intLike block above may have
            // just registered (IntFlag is both intLike and isFlag) — real Flag/IntFlag composition
            // always yields another member/pseudo-member of the same class, never a bare int.
            Add("__or__", (_, a, _) => MakeFlagValue(cls, MemberValue(a[0]) | MemberValue(a[1])));
            Add("__ror__", (_, a, _) => MakeFlagValue(cls, MemberValue(a[1]) | MemberValue(a[0])));
            Add("__and__", (_, a, _) => MakeFlagValue(cls, MemberValue(a[0]) & MemberValue(a[1])));
            Add("__rand__", (_, a, _) => MakeFlagValue(cls, MemberValue(a[1]) & MemberValue(a[0])));
            Add("__xor__", (_, a, _) => MakeFlagValue(cls, MemberValue(a[0]) ^ MemberValue(a[1])));
            Add("__rxor__", (_, a, _) => MakeFlagValue(cls, MemberValue(a[1]) ^ MemberValue(a[0])));
            Add("__invert__", (_, a, _) =>
            {
                // Real CPython: `~flag` complements only within the union of every canonical
                // member's bits (not an unbounded two's-complement `~`).
                BigInteger allBits = BigInteger.Zero;
                if (cls.Dict.TryGet("__members__", out var membersObj) && membersObj is PyDict members)
                    foreach (var e in members.Values)
                        if (e is PyInstance mi)
                            allBits |= PyOps.AsBigInt(mi.Dict["value"], "flag value");
                return MakeFlagValue(cls, allBits & ~MemberValue(a[0]));
            });
            Add("__contains__", (_, a, _) =>
            {
                var otherVal = MemberValue(a[1]);
                return (MemberValue(a[0]) & otherVal) == otherVal;
            });
            Add("__bool__", (_, a, _) => !MemberValue(a[0]).IsZero);
        }

        return cls;
    }
}
