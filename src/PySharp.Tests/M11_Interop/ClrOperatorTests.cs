using PySharpLib.Runtime;
using Xunit;
public class ClrOperatorTests
{
    public readonly record struct Value(int Number)
    {
        public static Value operator +(Value a,Value b)=>new(a.Number+b.Number);
        public static Value operator -(Value a)=>new(-a.Number);
        public static bool operator <(Value a,Value b)=>a.Number<b.Number;
        public static bool operator >(Value a,Value b)=>a.Number>b.Number;
    }
    [Fact] public void BinaryRetainsClrType()
    {
        Assert.True(ClrOperators.Binary("+",new ClrObject(new Value(2)),new ClrObject(new Value(3)),out var result));
        Assert.Equal(new Value(5),Assert.IsType<ClrObject>(result).Instance);
    }
    [Fact] public void UnaryRetainsClrType()
    {
        Assert.True(ClrOperators.Unary("-",new ClrObject(new Value(2)),out var result));
        Assert.Equal(new Value(-2),Assert.IsType<ClrObject>(result).Instance);
    }
    [Fact] public void ComparisonReturnsBoolean()
    {
        Assert.True(ClrOperators.Binary("<",new ClrObject(new Value(2)),new ClrObject(new Value(3)),out var result));
        Assert.True(Assert.IsType<bool>(result));
    }
    [Fact] public void FloatAndMixedClrTypesAreNotCoerced()
    {
        Assert.False(ClrOperators.Binary("+",new ClrObject(new Value(2)),1.0,out _));
        Assert.False(ClrOperators.Binary("+",new ClrObject(new Value(2)),new ClrObject(3m),out _));
    }
}