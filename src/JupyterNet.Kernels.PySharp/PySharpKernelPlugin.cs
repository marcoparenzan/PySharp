using JupyterNet.Kernels.Abstractions;

namespace JupyterNet.Kernels.PySharp;

/// <summary>The entire surface JupyterNet.Host needs to load this plugin — see IKernelPlugin.</summary>
public sealed class PySharpKernelPlugin : IKernelPlugin
{
    public string KernelId => "pysharp";

    public IKernel CreateKernel(INotebookHost notebookHost) => new PySharpKernel();
}
