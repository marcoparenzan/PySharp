using System.Numerics;
using PySharpLib.Runtime;

namespace PySharpLib.Modules;

/// <summary>
/// enum: the Enum/IntEnum base classes are marked with __is_enum__; member transformation
/// happens in Interp.ExecClassDef, lookup-by-value in Interp.Instantiate.
/// </summary>
public static class EnumModule
{
    public static readonly PyClass EnumClass = BuildEnumBase("Enum", intLike: false);
    public static readonly PyClass IntEnumClass = BuildEnumBase("IntEnum", intLike: true);
    public static readonly PyClass AutoClass = new("auto", new List<PyClass>());

    public static PyModule Create()
    {
        var m = new PyModule("enum");
        var d = m.Dict;
        d["Enum"] = EnumClass;
        d["IntEnum"] = IntEnumClass;
        d["IntFlag"] = IntEnumClass;
        d["Flag"] = EnumClass;
        d["auto"] = new PyBuiltinFunction("auto", (_, _, _) => new PyInstance(AutoClass));
        d["unique"] = new PyBuiltinFunction("unique", (_, a, _) => a[0]); // decorator identità
        return m;
    }

    private static BigInteger MemberValue(object self)
        => PyOps.AsBigInt(((PyInstance)self).Dict["value"], "enum value");

    private static object OtherValue(object other)
        => other is PyInstance inst && inst.Dict.TryGet("value", out var v) ? v : other;

    private static PyClass BuildEnumBase(string name, bool intLike)
    {
        var cls = new PyClass(name, new List<PyClass>());
        cls.Dict["__is_enum__"] = true;
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

        return cls;
    }
}
