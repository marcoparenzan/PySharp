using System.Numerics;
using PySharpLib.Parsing;
using PySharpLib.Runtime;
namespace PySharpLib.Modules;
public static partial class AstModule
{
    private static readonly PyClass AstBase = new("AST", new List<PyClass>());
    private static readonly Dictionary<string, PyClass> NodeTypes = new();
    private static readonly object Gate = new();
    private static readonly Dictionary<string,string[]> Fields = new()
    {
        ["Module"]=["body", "type_ignores"], ["Expression"]=["body"], ["Assign"]=["targets","value"],
        ["Expr"]=["value"], ["Name"]=["id","ctx"], ["Constant"]=["value"], ["BinOp"]=["left","op","right"],
        ["Import"]=["names"], ["ImportFrom"]=["module","names","level"], ["alias"]=["name","asname"],
        ["If"]=["test","body","orelse"], ["While"]=["test","body","orelse"], ["For"]=["target","iter","body","orelse"],
        ["Return"]=["value"], ["Pass"]=[], ["Break"]=[], ["Continue"]=[],
        ["Attribute"]=["value","attr","ctx"], ["Call"]=["func","args","keywords"], ["keyword"]=["arg","value"],
        ["Compare"]=["left","ops","comparators"], ["BoolOp"]=["op","values"], ["UnaryOp"]=["op","operand"],
        ["List"]=["elts","ctx"], ["Tuple"]=["elts","ctx"], ["Subscript"]=["value","slice","ctx"],
        ["And"]=[], ["Or"]=[], ["Not"]=[], ["UAdd"]=[], ["USub"]=[], ["Invert"]=[],
        ["Eq"]=[], ["NotEq"]=[], ["Lt"]=[], ["LtE"]=[], ["Gt"]=[], ["GtE"]=[], ["Is"]=[], ["IsNot"]=[], ["In"]=[], ["NotIn"]=[],
        ["Dict"]=["keys","values"], ["Set"]=["elts"], ["Slice"]=["lower","upper","step"], ["IfExp"]=["test","body","orelse"],
        ["FunctionDef"]=["name","args","body","decorator_list","returns"], ["AsyncFunctionDef"]=["name","args","body","decorator_list","returns"],
        ["ClassDef"]=["name","bases","keywords","body","decorator_list"],
        ["arguments"]=["posonlyargs","args","vararg","kwonlyargs","kw_defaults","kwarg","defaults"], ["arg"]=["arg","annotation"],
        ["Load"]=[], ["Store"]=[], ["Add"]=[], ["Sub"]=[], ["Mult"]=[], ["Div"]=[]
    };
    private static void RegisterPublicAst(PyModule module)
    {
        lock (Gate)
        {
            if (NodeTypes.Count == 0)
            foreach (var (name, fields) in Fields)
            {
                var cls = new PyClass(name, new List<PyClass> { AstBase });
                cls.Dict["_fields"] = new PyTuple(fields.Cast<object>().ToArray());
                cls.Dict["__init__"] = new PyBuiltinFunction(name+".__init__", (_, args, kw) =>
                {
                    var self = (PyInstance)args[0];
                    if (args.Length - 1 > fields.Length) throw PyErr.TypeError("too many AST arguments");
                    for(int i=1;i<args.Length;i++) self.Dict[fields[i-1]]=args[i];
                    if(kw is not null) foreach(var pair in kw) self.Dict[pair.Key]=pair.Value;
                    return PyNone.Instance;
                });
                NodeTypes[name] = cls;
            }
            module.Dict["AST"] = AstBase;
            foreach(var pair in NodeTypes) module.Dict[pair.Key]=pair.Value;
        }
        module.Dict["PyCF_ONLY_AST"]=new BigInteger(1024);
        module.Dict["parse"]=new PyBuiltinFunction("parse", (_, args, kw) =>
        {
            if(args.Length==0 || args[0] is not string source) throw PyErr.TypeError("parse requires source string");
            var file=args.Length>1?(string)args[1]:"<unknown>";
            var mode=args.Length>2?(string)args[2]:kw is not null && kw.TryGetValue("mode",out var m)?(string)m:"exec";
            return ParsePublic(source,file,mode);
        });
    }
    private static PyInstance Make(string name, params object[] values)
    {
        var value=new PyInstance(NodeTypes[name]);
        for(int i=0;i<values.Length;i++) value.Dict[Fields[name][i]]=values[i];
        return value;
    }
    public static object ParsePublic(string source,string filename,string mode)
    {
        // Ensure constructors are available even when compile is used before importing ast.
        RegisterPublicAst(new PyModule("ast"));
        try
        {
            return mode switch
            {
                "exec" => Make("Module",new PyList(Parser.Parse(source,filename).Body.Select(s=>Export(s))),new PyList(Array.Empty<object>())),
                "eval" => Make("Expression",Export(Parser.ParseExpression(source))),
                _ => throw PyErr.NotImplementedError("AST mode is not supported")
            };
        }
        catch(PySyntaxError e) { throw PyErr.SyntaxLike(e.Message); }
    }
    private static object Export(Node node, bool store=false)
    {
        var result = node switch
        {
            DictExpr d=>Make("Dict",new PyList(d.Items.Select(p=>p.Key is null?PyNone.Instance:Export(p.Key))),new PyList(d.Items.Select(p=>Export(p.Value)))),
            SetExpr s=>Make("Set",new PyList(s.Items.Select(e=>Export(e)))),
            SliceExpr s=>Make("Slice",Optional(s.Start),Optional(s.Stop),Optional(s.Step)),
            IfExpExpr i=>Make("IfExp",Export(i.Cond),Export(i.Then),Export(i.Else)),
            FuncDef f when !f.IsGenerator=>Make(f.IsAsync?"AsyncFunctionDef":"FunctionDef",f.Name,ExportParameters(f.Params),ExportBody(f.Body),new PyList(f.Decorators.Select(e=>Export(e))),Optional(f.Returns)),
            ClassDef c when c.Bases.All(a=>!a.IsStar&&!a.IsDoubleStar)=>Make("ClassDef",c.Name,
                new PyList(c.Bases.Where(a=>a.Name is null).Select(a=>Export(a.Value))),
                new PyList(c.Bases.Where(a=>a.Name is not null).Select(a=>(object)Make("keyword",a.Name!,Export(a.Value)))),ExportBody(c.Body),new PyList(c.Decorators.Select(e=>Export(e)))),
            IfStmt i=>Make("If",Export(i.Cond),ExportBody(i.Body),ExportBody(i.OrElse)),
            WhileStmt i=>Make("While",Export(i.Cond),ExportBody(i.Body),ExportBody(i.OrElse)),
            ForStmt f when !f.IsAsync=>Make("For",Export(f.Target,true),Export(f.Iter),ExportBody(f.Body),ExportBody(f.OrElse)),
            ReturnStmt r=>Make("Return",r.Value is null?PyNone.Instance:Export(r.Value)),
            PassStmt=>Make("Pass"), BreakStmt=>Make("Break"), ContinueStmt=>Make("Continue"),
            AttributeExpr a=>Make("Attribute",Export(a.Obj),a.Name,Make(store?"Store":"Load")),
            CallExpr c when c.Args.All(a=>!a.IsStar&&!a.IsDoubleStar)=>Make("Call",Export(c.Func),
                new PyList(c.Args.Where(a=>a.Name is null).Select(a=>Export(a.Value))),
                new PyList(c.Args.Where(a=>a.Name is not null).Select(a=>(object)Make("keyword",a.Name!,Export(a.Value))))),
            CompareExpr c=>Make("Compare",Export(c.Left),new PyList(c.Ops.Select(op=>(object)Make(OperatorName(op)))),new PyList(c.Comparators.Select(e=>Export(e)))),
            BoolOpExpr b=>Make("BoolOp",Make(OperatorName(b.Op)),new PyList(b.Values.Select(e=>Export(e)))),
            UnaryExpr u=>Make("UnaryOp",Make(u.Op switch {"+"=>"UAdd","-"=>"USub","~"=>"Invert","not"=>"Not",_=>throw PyErr.NotImplementedError("unary AST")}),Export(u.Operand)),
            ListExpr l=>Make("List",new PyList(l.Items.Select(e=>Export(e,store))),Make(store?"Store":"Load")),
            TupleExpr l=>Make("Tuple",new PyList(l.Items.Select(e=>Export(e,store))),Make(store?"Store":"Load")),
            IndexExpr i=>Make("Subscript",Export(i.Obj),Export(i.Index),Make(store?"Store":"Load")),
            ImportStmt i=>Make("Import",new PyList(i.Names.Select(a=>(object)Make("alias",a.DottedName,(object?)a.AsName??PyNone.Instance)))),
            FromImportStmt i=>Make("ImportFrom",string.IsNullOrEmpty(i.Module)?PyNone.Instance:(object)i.Module,
                new PyList((i.Star?new List<ImportAlias>{new("*",null)}:i.Names).Select(a=>(object)Make("alias",a.DottedName,(object?)a.AsName??PyNone.Instance))),new BigInteger(i.Level)),
            AssignStmt a=>Make("Assign",new PyList(a.Targets.Select(x=>Export(x,true))),Export(a.Value)),
            ExprStmt e=>Make("Expr",Export(e.Value)),
            NameExpr n=>Make("Name",n.Id,Make(store?"Store":"Load")),
            IntLit n=>Make("Constant",n.Value), FloatLit n=>Make("Constant",n.Value),
            StrLit s=>Make("Constant",s.Value), BoolLit b=>Make("Constant",b.Value), NoneLit=>Make("Constant",PyNone.Instance),
            BinaryExpr b=>Make("BinOp",Export(b.Left),Make(b.Op switch {"+"=>"Add","-"=>"Sub","*"=>"Mult","/"=>"Div",_=>throw PyErr.NotImplementedError("AST operator not supported")}),Export(b.Right)),
            _=>throw PyErr.NotImplementedError($"Public AST node {node.GetType().Name} is not supported")
        };
        result.Dict["lineno"]=new BigInteger(node.Line);
        result.Dict["col_offset"]=new BigInteger(Math.Max(0,node.Col-1));
        return result;
    }
    private static object Get(PyInstance n,string key) => n.Dict.TryGet(key,out var value)?value:throw PyErr.TypeError("missing AST field "+key);
    private static Node Import(PyInstance n)
    {
        Node result = n.Class.Name switch
        {
            "Dict"=>ImportDict(n),
            "Set"=>new SetExpr(Expressions(n,"elts")),
            "Slice"=>new SliceExpr(OptionalExpression(n,"lower"),OptionalExpression(n,"upper"),OptionalExpression(n,"step")),
            "IfExp"=>new IfExpExpr(Expression(n,"test"),Expression(n,"body"),Expression(n,"orelse")),
            "FunctionDef" or "AsyncFunctionDef"=>new FuncDef((string)Get(n,"name"),ImportParameters((PyInstance)Get(n,"args")),Body(n,"body"),Expressions(n,"decorator_list"),OptionalExpression(n,"returns"),false) { IsAsync=n.Class.Name=="AsyncFunctionDef" },
            "ClassDef"=>new ClassDef((string)Get(n,"name"),ReadClassBases(n),Body(n,"body"),Expressions(n,"decorator_list")),
            "If"=>new IfStmt(Expression(n,"test"),Body(n,"body"),Body(n,"orelse")),
            "While"=>new WhileStmt(Expression(n,"test"),Body(n,"body"),Body(n,"orelse")),
            "For"=>new ForStmt(Expression(n,"target"),Expression(n,"iter"),Body(n,"body"),Body(n,"orelse")),
            "Return"=>new ReturnStmt(Get(n,"value") is PyNone?null:Expression(n,"value")),
            "Pass"=>new PassStmt(), "Break"=>new BreakStmt(), "Continue"=>new ContinueStmt(),
            "Attribute"=>new AttributeExpr(Expression(n,"value"),(string)Get(n,"attr")),
            "Call"=>new CallExpr(Expression(n,"func"),ReadCallArgs(n)),
            "Compare"=>new CompareExpr(Expression(n,"left"),((PyList)Get(n,"ops")).Items.Select(o=>OperatorText(((PyInstance)o).Class.Name)).ToList(),Expressions(n,"comparators")),
            "BoolOp"=>new BoolOpExpr(OperatorText(((PyInstance)Get(n,"op")).Class.Name),Expressions(n,"values")),
            "UnaryOp"=>new UnaryExpr(OperatorText(((PyInstance)Get(n,"op")).Class.Name),Expression(n,"operand")),
            "List"=>new ListExpr(Expressions(n,"elts")), "Tuple"=>new TupleExpr(Expressions(n,"elts")),
            "Subscript"=>new IndexExpr(Expression(n,"value"),Expression(n,"slice")),
            "Import"=>new ImportStmt(ReadAliases(n)),
            "ImportFrom"=>new FromImportStmt(Get(n,"module") is PyNone?"":(string)Get(n,"module"),(int)(BigInteger)Get(n,"level"),ReadAliases(n),ReadAliases(n).Any(a=>a.DottedName=="*")),
            "Assign"=>new AssignStmt(((PyList)Get(n,"targets")).Items.Select(v=>(Expr)Import((PyInstance)v)).ToList(),(Expr)Import((PyInstance)Get(n,"value"))),
            "Expr"=>new ExprStmt((Expr)Import((PyInstance)Get(n,"value"))),
            "Name"=>new NameExpr((string)Get(n,"id")),
            "Constant"=>Get(n,"value") switch { BigInteger i=>new IntLit(i), double d=>new FloatLit(d), string s=>new StrLit(s),bool b=>new BoolLit(b),PyNone=>new NoneLit(),_=>throw PyErr.TypeError("invalid AST constant") },
            "BinOp"=>new BinaryExpr((Expr)Import((PyInstance)Get(n,"left")),((PyInstance)Get(n,"op")).Class.Name switch {"Add"=>"+","Sub"=>"-","Mult"=>"*","Div"=>"/",_=>throw PyErr.NotImplementedError("AST operator")},(Expr)Import((PyInstance)Get(n,"right"))),
            _=>throw PyErr.NotImplementedError("AST node compilation not supported: "+n.Class.Name)
        };
        return result;
    }
    private static object Optional(Expr? e)=>e is null?PyNone.Instance:Export(e);
    private static Expr? OptionalExpression(PyInstance n,string key)=>Get(n,key) is PyNone?null:Expression(n,key);
    private static DictExpr ImportDict(PyInstance n)
    {
        var keys=((PyList)Get(n,"keys")).Items;
        var values=Expressions(n,"values");
        if(keys.Count!=values.Count)throw PyErr.TypeError("Dict keys and values length mismatch");
        return new DictExpr(keys.Select((k,i)=>(k is PyNone?null:(Expr?)Import((PyInstance)k),values[i])).ToList());
    }
    private static object ExportParameters(Parameters p)
    {
        object arg(Param a)=>Make("arg",a.Name,Optional(a.Annotation));
        return Make("arguments",new PyList(),new PyList(p.Positional.Select(arg)),
            string.IsNullOrEmpty(p.StarArgs)?PyNone.Instance:Make("arg",p.StarArgs,PyNone.Instance),
            new PyList(p.KwOnly.Select(arg)),new PyList(p.KwOnly.Select(a=>Optional(a.Default))),
            p.KwArgs is null?PyNone.Instance:Make("arg",p.KwArgs,PyNone.Instance),
            new PyList(p.Positional.Where(a=>a.Default is not null).Select(a=>Export(a.Default!))));
    }
    private static Parameters ImportParameters(PyInstance p)
    {
        if(((PyList)Get(p,"posonlyargs")).Items.Count!=0)throw PyErr.NotImplementedError("positional-only AST parameters");
        var args=((PyList)Get(p,"args")).Items.Cast<PyInstance>().ToList();
        var defaults=Expressions(p,"defaults");
        if(defaults.Count>args.Count)throw PyErr.TypeError("too many parameter defaults");
        Param param(PyInstance a,Expr? def)=>new((string)Get(a,"arg"),def,OptionalExpression(a,"annotation"));
        var pos=args.Select((a,i)=>param(a,i>=args.Count-defaults.Count?defaults[i-(args.Count-defaults.Count)]:null)).ToList();
        var kw=((PyList)Get(p,"kwonlyargs")).Items.Cast<PyInstance>().ToList();
        var kd=((PyList)Get(p,"kw_defaults")).Items;
        if(kw.Count!=kd.Count)throw PyErr.TypeError("keyword defaults length mismatch");
        var kwargs=kw.Select((a,i)=>param(a,kd[i] is PyNone?null:(Expr)Import((PyInstance)kd[i]))).ToList();
        string? name(string field)=>Get(p,field) is PyNone?null:(string)Get((PyInstance)Get(p,field),"arg");
        return new Parameters(pos,name("vararg")??(kw.Count>0?"":null),kwargs,name("kwarg"));
    }
    private static List<CallArg> ReadClassBases(PyInstance n)
    {
        var args=Expressions(n,"bases").Select(e=>new CallArg(null,e,false,false)).ToList();
        foreach(var item in ((PyList)Get(n,"keywords")).Items)
        {
            var k=(PyInstance)item;
            args.Add(new CallArg((string)Get(k,"arg"),Expression(k,"value"),false,false));
        }
        return args;
    }
    private static PyList ExportBody(List<Stmt> body)=>new(body.Select(s=>Export(s)));
    private static List<Stmt> Body(PyInstance n,string key)=>((PyList)Get(n,key)).Items.Select(v=>(Stmt)Import((PyInstance)v)).ToList();
    private static Expr Expression(PyInstance n,string key)=>(Expr)Import((PyInstance)Get(n,key));
    private static List<Expr> Expressions(PyInstance n,string key)=>((PyList)Get(n,key)).Items.Select(v=>(Expr)Import((PyInstance)v)).ToList();
    private static readonly Dictionary<string,string> Operators=new()
    {
        ["=="]="Eq",["!="]="NotEq",["<"]="Lt",["<="]="LtE",[">"]="Gt",[">="]="GtE",
        ["is"]="Is",["is not"]="IsNot",["in"]="In",["not in"]="NotIn",["and"]="And",["or"]="Or"
    };
    private static string OperatorName(string op)=>Operators.TryGetValue(op,out var name)?name:throw PyErr.NotImplementedError("AST operator "+op);
    private static string OperatorText(string name)=>name switch
    {
        "UAdd"=>"+","USub"=>"-","Invert"=>"~","Not"=>"not",
        _=>Operators.FirstOrDefault(p=>p.Value==name).Key??throw PyErr.NotImplementedError("AST operator "+name)
    };
    private static List<CallArg> ReadCallArgs(PyInstance n)
    {
        var args=Expressions(n,"args").Select(e=>new CallArg(null,e,false,false)).ToList();
        foreach(var item in ((PyList)Get(n,"keywords")).Items)
        {
            var k=(PyInstance)item;
            args.Add(new CallArg((string)Get(k,"arg"),Expression(k,"value"),false,false));
        }
        return args;
    }
    private static List<ImportAlias> ReadAliases(PyInstance n)
        => ((PyList)Get(n,"names")).Items.Select(v =>
        {
            if(v is not PyInstance a || a.Class.Name != "alias") throw PyErr.TypeError("invalid import alias");
            return new ImportAlias((string)Get(a,"name"),Get(a,"asname") is PyNone?null:(string)Get(a,"asname"));
        }).ToList();
    public static PyCompiledCode CompilePublic(PyInstance n,string filename,string mode)
    {
        if(mode=="exec" && n.Class.Name=="Module")
            return new(filename,mode,new Module(((PyList)Get(n,"body")).Items.Select(v=>(Stmt)Import((PyInstance)v)).ToList()),null);
        if(mode=="eval" && n.Class.Name=="Expression") return new(filename,mode,null,(Expr)Import((PyInstance)Get(n,"body")));
        throw PyErr.TypeError("AST root does not match compile mode");
    }
}