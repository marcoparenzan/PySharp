using System.Globalization;
using System.Numerics;
using System.Reflection;
using System.Runtime.ExceptionServices;
namespace PySharpLib.Runtime;

/// <summary>Explicit CLR operator dispatch. No implicit conversion from Python float or between different CLR numeric types.</summary>
public static class ClrOperators
{
    private static object Invoke(MethodInfo method, object?[] args)
    {
        try { return ClrMarshal.ToPython(method.Invoke(null,args)); }
        catch(TargetInvocationException e) when(e.InnerException is not null)
        { ExceptionDispatchInfo.Capture(e.InnerException).Throw(); throw; }
    }
    public static bool Unary(string op, object operand, out object result)
    {
        result=null!;
        if(operand is not ClrObject value) return false;
        string? name=op switch {"+"=>"op_UnaryPlus","-"=>"op_UnaryNegation","~"=>"op_OnesComplement",_=>null};
        if(name is null) return false;
        var method=value.Type.GetMethod(name,BindingFlags.Public|BindingFlags.Static,null,[value.Type],null);
        if(method is null)return false;
        result=Invoke(method,[value.Instance]);return true;
    }
    public static bool Binary(string op,object left,object right,out object result)
    {
        result=null!;
        string? name=op switch {"+"=>"op_Addition","-"=>"op_Subtraction","*"=>"op_Multiply","/"=>"op_Division","%"=>"op_Modulus","=="=>"op_Equality","!="=>"op_Inequality","<"=>"op_LessThan",">"=>"op_GreaterThan","<="=>"op_LessThanOrEqual",">="=>"op_GreaterThanOrEqual",_=>null};
        if(name is null)return false;
        var type=(left as ClrObject)?.Type ?? (right as ClrObject)?.Type;
        if(type is null)return false;
        bool ConvertOperand(object value,out object? native)
        {
            native=null;
            if(value is ClrObject wrapped && wrapped.Type==type){native=wrapped.Instance;return true;}
            if(value is BigInteger integer && type.GetInterfaces().Any(i=>i.IsGenericType&&i.GetGenericTypeDefinition()==typeof(IFloatingPoint<>)))
            {
                var parse=type.GetMethod("Parse",BindingFlags.Public|BindingFlags.Static,null,[typeof(string),typeof(IFormatProvider)],null);
                if(parse is null)return false;
                native=parse.Invoke(null,[integer.ToString(CultureInfo.InvariantCulture),CultureInfo.InvariantCulture]);
                return true;
            }
            return false;
        }
        if(!ConvertOperand(left,out var a)||!ConvertOperand(right,out var b))return false;
        var method=type.GetMethod(name,BindingFlags.Public|BindingFlags.Static,null,[type,type],null);
        if(method is null)return false;
        result=Invoke(method,[a,b]);return true;
    }
}