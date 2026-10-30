using System.Numerics;
using PySharpLib.Runtime;
namespace PySharpLib.Interpretation;
public sealed partial class Interp
{
    /// <summary>Expose the interpreter's actual thread-local stack, innermost first.</summary>
    public static object GetPythonFrame(int depth)
    {
        var frames = _frames?.ToArray() ?? [];
        if (depth < 0) depth = 0;
        if (depth >= frames.Length) throw PyErr.ValueError("call stack is not deep enough");
        object back = PyNone.Instance;
        for (int i = frames.Length - 1; i >= depth; i--)
        {
            var frame = frames[i];
            var value = new PyInstance(new PyClass("frame", new List<PyClass>()));
            var locals = frame.Env.Module.Dict;
            if (!frame.Env.IsGlobalScope)
            {
                locals = new PyDict();
                foreach (var pair in frame.Env.Locals) locals[pair.Key] = pair.Value;
            }
            var code = new PyInstance(new PyClass("code", new List<PyClass>()));
            code.Dict["co_name"] = frame.Name;
            code.Dict["co_filename"] = frame.File;
            value.Dict["f_globals"] = frame.Env.Module.Dict;
            value.Dict["f_locals"] = locals;
            value.Dict["f_lineno"] = new BigInteger(frame.Line);
            value.Dict["f_code"] = code;
            value.Dict["f_back"] = back;
            back = value;
        }
        return back;
    }
}