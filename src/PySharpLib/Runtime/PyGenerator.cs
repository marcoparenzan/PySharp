using PySharpLib.Interpretation;

namespace PySharpLib.Runtime;

/// <summary>
/// Python generator. The body runs on a dedicated (background) thread
/// with a producer/consumer handshake: simple and correct for a tree-walker.
/// </summary>
public sealed class PyGenerator
{
    private readonly PyFunction _fn;
    private readonly Env _env;
    private readonly SemaphoreSlim _resume = new(0, 1);
    private readonly SemaphoreSlim _produced = new(0, 1);

    private Thread? _thread;
    private object _yielded = PyNone.Instance;
    private bool _finished;
    private Exception? _error;

    [ThreadStatic]
    private static PyGenerator? _current;

    /// <summary>Il generatore in esecuzione sul thread corrente (per valutare yield).</summary>
    public static PyGenerator? Current => _current;

    public string Name => _fn.Name;

    public PyGenerator(PyFunction fn, Env env)
    {
        _fn = fn;
        _env = env;
    }

    /// <summary>Called by the generator thread when it evaluates 'yield v'. Returns the sent value (None).</summary>
    public object Yield(object value)
    {
        _yielded = value;
        _produced.Release();
        _resume.Wait();
        return PyNone.Instance;
    }

    public bool MoveNext(Interp interp, out object value)
    {
        if (_finished)
        {
            value = PyNone.Instance;
            return false;
        }

        if (_thread is null)
        {
            _thread = new Thread(() =>
            {
                _current = this;
                _resume.Wait();
                try
                {
                    interp.ExecFunctionBody(_fn, _env);
                }
                catch (ReturnSignal)
                {
                    // return in a generator → end of iteration
                }
                catch (Exception ex)
                {
                    _error = ex;
                }
                finally
                {
                    _finished = true;
                    _produced.Release();
                }
            })
            {
                IsBackground = true,
                Name = $"pygen-{_fn.Name}",
            };
            _thread.Start();
        }

        _resume.Release();
        _produced.Wait();

        if (_finished)
        {
            value = PyNone.Instance;
            if (_error is not null)
            {
                var err = _error;
                _error = null;
                throw err is PyRaise ? err : new PyRaise(PyErr.MakeInstance(PyErr.RuntimeErrorClass, err.Message));
            }
            return false;
        }

        value = _yielded;
        return true;
    }

    public override string ToString() => $"<generator object {_fn.Name}>";
}
