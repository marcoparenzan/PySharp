using System.Numerics;
using PySharpLib;
using PySharpLib.Runtime;
namespace PySharp.Tests.M11_Interop;
public class GenericTypeTests
{
    [Fact] public void ClosedClrListPreservesDecimal128InTheInterpreter()
    {
        var output=new StringWriter();var engine=new PyEngine(output);
        engine.SetVariable("ClrList",typeof(List<>));engine.SetVariable("D128",typeof(Decimal128));
        engine.Run("""
from mathema import decimal128
List128 = ClrList[D128]
values = List128()
values.Add(decimal128('1.0000000000000000000000001'))
values.Add(decimal128('1e-25'))
print(values.Count)
print(values[0] - 1 == values[1])
""");
        Assert.Equal("2\nTrue\n",output.ToString());
    }
    [Fact] public void GenericTypeConstraintsAndArityAreChecked()
    {
        Assert.ThrowsAny<Exception>(()=>ClrBinder.SelectGenericType(new ClrType(typeof(Nullable<>)),new ClrType(typeof(string))));
        Assert.ThrowsAny<Exception>(()=>ClrBinder.SelectGenericType(new ClrType(typeof(Dictionary<,>)),new ClrType(typeof(int))));
        Assert.ThrowsAny<Exception>(()=>ClrBinder.SelectGenericType(new ClrType(typeof(int)),new ClrType(typeof(int))));
    }
}