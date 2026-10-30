using System.Numerics;
using PySharpLib.Runtime;
using Xunit;
public class GenericBindingTests
{
    public static T Echo<T>(T value) where T:INumber<T> => value;
    public static T First<T,U>(T value,U ignored)=>value;
    public static T SpanEcho<T>(ReadOnlySpan<T> values) where T:INumber<T> => values[0];
    private static ClrMethod Method(string name)=>new(null,typeof(GenericBindingTests),name);
    [Fact] public void ExplicitTypeIsApplied()
    {
        var bound=ClrBinder.SelectGeneric(Method(nameof(Echo)),new ClrType(typeof(int)));
        Assert.Equal(new BigInteger(42),ClrBinder.InvokeMethod(bound,[new BigInteger(42)]));
    }
    [Fact] public void NonTypeArgumentIsRejected()=>Assert.ThrowsAny<Exception>(()=>ClrBinder.SelectGeneric(Method(nameof(Echo)),new BigInteger(1)));
    [Fact] public void GenericConstraintIsEnforced()
    {
        var bound=ClrBinder.SelectGeneric(Method(nameof(Echo)),new ClrType(typeof(string)));
        Assert.ThrowsAny<Exception>(()=>ClrBinder.InvokeMethod(bound,["text"]));
    }
    [Fact] public void SpanIsNotPassedToReflection()
    {
        var bound=ClrBinder.SelectGeneric(Method(nameof(SpanEcho)),new ClrType(typeof(int)));
        Assert.ThrowsAny<Exception>(()=>ClrBinder.InvokeMethod(bound,[new ClrObject(new int[]{1})]));
    }
}