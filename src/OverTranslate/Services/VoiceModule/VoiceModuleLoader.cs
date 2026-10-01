using System.IO;
using System.Linq;
using System.Reflection;
using NLog;
using Velopack.Locators;

namespace OverTranslate.Services.VoiceModule;

/// <summary>
/// Finds, loads and caches the optional 系統音訊語音辨識 module. The host calls
/// <see cref="TryGetModule"/> exactly where it used to construct <c>VoicePage</c> directly —
/// see <c>ShellWindow.ShowPage</c> — and shows <c>VoiceModuleMissingView</c> instead when this
/// returns <see langword="null"/>.
/// </summary>
internal static class VoiceModuleLoader
{
    private static readonly Logger Log = LogManager.GetCurrentClassLogger();

    private const string AssemblyFileName = "OverTranslate.VoiceModule.dll";

    /// <summary>
    /// Where a downloaded module is expected: under Velopack's install root when this is a
    /// Velopack build, next to the exe otherwise (e.g. running from the IDE).
    /// </summary>
    /// <remarks>
    /// Deliberately NOT <c>AppContext.BaseDirectory</c> for an installed build. On Windows, Velopack
    /// installs into <c>%LocalAppData%\{packId}\current</c> — which is exactly what
    /// AppContext.BaseDirectory points at — and "during updates, the entire current directory will
    /// be replaced" (see docs.velopack.io, Windows packaging). A plugins folder living there is
    /// deleted on the very next main-app update, silently undoing the whole point of shipping this
    /// as a separate, host-independent assembly — see OverTranslate.VoiceModule.csproj.
    ///
    /// <see cref="VelopackLocator.Current"/>'s <c>RootAppDir</c> is the parent of <c>current</c>
    /// (it also holds <c>packages</c> and <c>Update.exe</c>) and is left alone by an update — only
    /// <c>current</c> is swapped out — so a folder placed there survives every host update. It does
    /// NOT survive an uninstall: Velopack's uninstaller removes the whole <c>{packId}</c> folder,
    /// RootAppDir included. That is fine — uninstalling the host is expected to take the plugin
    /// with it.
    ///
    /// Falls back to <c>AppContext.BaseDirectory</c> whenever there is no process-wide locator —
    /// running unpackaged (F5 from the IDE, a test host) — where "the exe's own folder" is the only
    /// sensible answer and there is no update process to protect against anyway.
    /// </remarks>
    public static string PluginDirectory { get; }

    public static string ModuleAssemblyPath { get; }

    /// <summary>The optional GPU add-on's expected extraction folder — see
    /// <see cref="VoiceModuleLoadContext.LoadUnmanagedDll"/>.</summary>
    public static string CudaAddonDirectory { get; }

    static VoiceModuleLoader()
    {
        var installRoot = VelopackLocator.IsCurrentSet && VelopackLocator.Current?.RootAppDir is { Length: > 0 } root
            ? root
            : AppContext.BaseDirectory;

        PluginDirectory = Path.Combine(installRoot, "plugins", "voice");
        ModuleAssemblyPath = Path.Combine(PluginDirectory, AssemblyFileName);
        CudaAddonDirectory = Path.Combine(PluginDirectory, "cuda");

        MigrateLegacyInstall();
    }

    /// <summary>
    /// One-time move for anyone who installed the module before <see cref="PluginDirectory"/>
    /// moved out of <c>current</c>: their copy sits at the old, update-fragile location and would
    /// otherwise vanish — unexplained, from their point of view — on the next update rather than
    /// ever having been protected. A no-op once the module lives at the new location, and whenever
    /// the two locations are the same path to begin with (unpackaged runs).
    /// </summary>
    private static void MigrateLegacyInstall()
    {
        var legacyDirectory = Path.Combine(AppContext.BaseDirectory, "plugins", "voice");
        if (string.Equals(legacyDirectory, PluginDirectory, StringComparison.OrdinalIgnoreCase))
            return;
        if (Directory.Exists(PluginDirectory))
            return; // something is already at the new location; never clobber it
        if (!File.Exists(Path.Combine(legacyDirectory, AssemblyFileName)))
            return;

        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(PluginDirectory)!);
            Directory.Move(legacyDirectory, PluginDirectory);
            Log.Info(
                "Migrated the voice module from {Old} (inside the updated app folder) to {New}.",
                legacyDirectory, PluginDirectory);
        }
        catch (Exception ex)
        {
            // Best-effort. Worst case the user reinstalls the module once from the zip they already
            // have — the exact outcome this migration exists to avoid, not a new failure mode.
            Log.Warn(ex, "Could not migrate the voice module to its new, update-safe location.");
        }
    }

    public static bool IsModuleInstalled => File.Exists(ModuleAssemblyPath);

    public static bool IsCudaAddonInstalled =>
        Directory.Exists(CudaAddonDirectory) && Directory.EnumerateFiles(CudaAddonDirectory, "*.dll", SearchOption.AllDirectories).Any();

    private static IVoiceModule? _cached;
    private static bool _loadAttempted;

    /// <summary>
    /// Returns the module instance, loading it on first call. Returns <see langword="null"/> if the
    /// module isn't installed or fails to load (e.g. a mismatched/corrupt download) — callers should
    /// treat that the same as "not installed" and offer the download UI again rather than crash.
    /// </summary>
    public static IVoiceModule? TryGetModule()
    {
        if (_loadAttempted)
            return _cached;

        _loadAttempted = true;
        if (!IsModuleInstalled)
            return null;

        try
        {
            var context = new VoiceModuleLoadContext(ModuleAssemblyPath);
            Assembly asm = context.LoadFromAssemblyPath(ModuleAssemblyPath);

            Type? moduleType = asm.GetTypes()
                .FirstOrDefault(t => typeof(IVoiceModule).IsAssignableFrom(t) && !t.IsAbstract && !t.IsInterface);

            if (moduleType is null)
            {
                Log.Error("VoiceModule assembly loaded but contains no IVoiceModule implementation.");
                return null;
            }

            _cached = (IVoiceModule?)Activator.CreateInstance(moduleType);
        }
        catch (Exception ex)
        {
            // A half-downloaded zip, a version mismatch after the host updates, a missing native
            // dependency the user's copy of the CUDA add-on doesn't actually match — all land here
            // rather than taking the app down. 語音翻譯 simply stays unavailable until reinstalled.
            Log.Error(ex, "Failed to load the optional voice module.");
            _cached = null;
        }

        return _cached;
    }

    /// <summary>Call after installing/reinstalling the module (or the CUDA add-on) so the next
    /// <see cref="TryGetModule"/> re-probes disk instead of returning a stale cached result.</summary>
    public static void Reset()
    {
        _cached = null;
        _loadAttempted = false;
    }
}
