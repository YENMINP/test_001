using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.Loader;

namespace OverTranslate.Services.VoiceModule;

/// <summary>
/// Loads <c>OverTranslate.VoiceModule.dll</c> and everything it brings (NAudio, Whisper.net, the
/// Whisper native runtime) into its own context, isolated from the host's default context — the
/// standard .NET plugin pattern (see Microsoft's "Create a .NET Core application with plugins"
/// tutorial, which this follows closely).
/// </summary>
/// <remarks>
/// Two resolution paths matter here:
///
/// <para><b>Managed assemblies</b> (<see cref="Load"/>): resolved from the plugin's own
/// <c>.deps.json</c>-driven <see cref="AssemblyDependencyResolver"/> first. A name it doesn't know —
/// chiefly <c>OverTranslate</c> itself, since the module's <c>ProjectReference</c> is
/// <c>Private=false</c> and never copied into <c>plugins/voice/</c> — falls through to returning
/// <see langword="null"/>, which tells the runtime to look in the host's own (default) load context
/// instead. That's what makes <see cref="IVoiceModule"/>, <c>AppServices</c>, <c>AppSettings</c>
/// etc. resolve to the exact same types/instances the running host process already has, rather than
/// a second copy.</para>
///
/// <para><b>Native (unmanaged) libraries</b> (<see cref="LoadUnmanagedDll"/>): whisper.dll/ggml*.dll
/// are P/Invoked by Whisper.net, not referenced as .NET assemblies, so they need this separate hook.
/// A <c>cuda\</c> folder next to the module DLL — populated only if the user separately downloaded
/// the optional GPU add-on — is tried first; the module's own bundled CPU-only native files (from
/// the Whisper.net.Runtime NuGet package, resolved the same way as managed assemblies) are the
/// fallback. This mirrors what Whisper.net's own runtime probing does inside a normal single-context
/// app, reimplemented here because a hand-loaded plugin context does not get that probing for free.</para>
/// </remarks>
internal sealed class VoiceModuleLoadContext : AssemblyLoadContext
{
    private readonly AssemblyDependencyResolver _resolver;
    private readonly string _pluginDirectory;

    public VoiceModuleLoadContext(string pluginAssemblyPath)
        : base(name: "OverTranslate.VoiceModule", isCollectible: true)
    {
        _resolver = new AssemblyDependencyResolver(pluginAssemblyPath);
        _pluginDirectory = Path.GetDirectoryName(pluginAssemblyPath)!;
    }

    protected override Assembly? Load(AssemblyName assemblyName)
    {
        string? path = _resolver.ResolveAssemblyToPath(assemblyName);
        return path is null ? null : LoadFromAssemblyPath(path);
    }

    protected override nint LoadUnmanagedDll(string unmanagedDllName)
    {
        string? cudaPath = FindInCudaAddon(unmanagedDllName);
        if (cudaPath is not null)
            return LoadUnmanagedDllFromPath(cudaPath);

        string? bundledPath = _resolver.ResolveUnmanagedDllToPath(unmanagedDllName);
        if (bundledPath is not null)
            return LoadUnmanagedDllFromPath(bundledPath);

        return base.LoadUnmanagedDll(unmanagedDllName);
    }

    /// <summary>
    /// Looks for <paramref name="unmanagedDllName"/> under <c>plugins/voice/cuda/</c>. That folder's
    /// internal layout is whatever <see cref="VoiceModuleDownloader.CudaAddonDirectory"/> extracts
    /// the GPU add-on zip into — a flat search here keeps this working whether that zip is flat or
    /// mirrors the NuGet package's own <c>runtimes\win-x64\native\</c> shape.
    /// </summary>
    private string? FindInCudaAddon(string unmanagedDllName)
    {
        string cudaDir = Path.Combine(_pluginDirectory, "cuda");
        if (!Directory.Exists(cudaDir))
            return null;

        string fileName = unmanagedDllName.EndsWith(".dll", StringComparison.OrdinalIgnoreCase)
            ? unmanagedDllName
            : unmanagedDllName + ".dll";

        return Directory.EnumerateFiles(cudaDir, fileName, SearchOption.AllDirectories).FirstOrDefault();
    }
}
