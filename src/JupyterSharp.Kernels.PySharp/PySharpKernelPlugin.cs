using JupyterSharp.Kernels.Abstractions;

namespace JupyterSharp.Kernels.PySharp;

/// <summary>The entire surface JupyterSharp.Host needs to load this plugin — see IKernelPlugin.</summary>
public sealed class PySharpKernelPlugin : IKernelPlugin
{
    public string KernelId => "pysharp";

    public IKernel CreateKernel(INotebookHost notebookHost) => new PySharpKernel();
}
