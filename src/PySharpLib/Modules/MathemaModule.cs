using System.Globalization;
using System.Numerics;
using System.Reflection;
using PySharpLib.Runtime;
namespace PySharpLib.Modules;

/// <summary>Native IEEE decimal constructors, resolved on the active runtime without a compile-time dependency on .NET 11.</summary>
public static class MathemaModule
{
    public static PyModule Create()
    {
        var module=new PyModule("mathema");
        foreach(var bits in new[]{32,64,128})
        {
            string name="decimal"+bits;
            module.Dict[name]=new PyBuiltinFunction(name,(_,args,kwargs)=>
            {
                if(kwargs is {Count:>0} || args.Length!=1) throw PyErr.TypeError(name+" expects one positional string or integer");
                var type=typeof(object).Assembly.GetType("System.Numerics.Decimal"+bits)
                    ?? AppDomain.CurrentDomain.GetAssemblies().Select(a=>a.GetType("System.Numerics.Decimal"+bits)).FirstOrDefault(t=>t is not null);
                if(type is null)throw PyErr.RuntimeError(name+" requires a runtime providing .NET 11 IEEE Decimal types");
                if(args[0] is ClrObject wrapped && wrapped.Type==type)return wrapped;
                string value=args[0] switch
                {
                    string s=>s,
                    BigInteger n=>n.ToString(CultureInfo.InvariantCulture),
                    _=>throw PyErr.TypeError(name+" accepts a string, integer or same-type CLR value; float conversion must be explicit")
                };
                var parse=type.GetMethod("Parse",BindingFlags.Public|BindingFlags.Static,null,[typeof(string),typeof(IFormatProvider)],null)
                    ?? throw PyErr.RuntimeError("Runtime decimal parser unavailable");
                try { return ClrMarshal.ToPython(parse.Invoke(null,[value,CultureInfo.InvariantCulture])); }
                catch(TargetInvocationException e) when(e.InnerException is FormatException)
                { throw PyErr.ValueError("Invalid "+name+" literal: "+value); }
                catch(TargetInvocationException e) when(e.InnerException is OverflowException)
                { throw PyErr.OverflowError("Value outside "+name+" range"); }
            });
        }
        return module;
    }
}