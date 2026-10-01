using System.IO;
using System.IO.Compression;
using NLog;

namespace OverTranslate.Services.VoiceModule;

/// <summary>
/// Installs the optional voice module and its optional CUDA add-on from a zip file the user picks
/// themselves (<c>VoiceModuleMissingView</c> opens the file dialog) — no release feed, no network
/// call, nothing that can 404. Both are plain zips of a publish/extraction folder — no installer, no
/// elevation, just files under <see cref="VoiceModuleLoader.PluginDirectory"/> — so uninstalling
/// either is just deleting a folder.
/// </summary>
internal static class VoiceModuleDownloader
{
    private static readonly Logger Log = LogManager.GetCurrentClassLogger();

    /// <summary>
    /// Extracts <paramref name="zipPath"/> into <see cref="VoiceModuleLoader.PluginDirectory"/>,
    /// replacing any previous install. Call <see cref="VoiceModuleLoader.Reset"/> afterwards so the
    /// next <see cref="VoiceModuleLoader.TryGetModule"/> picks it up.
    /// </summary>
    public static Task InstallModuleFromFileAsync(string zipPath, CancellationToken cancellationToken = default) =>
        ExtractAsync(zipPath, VoiceModuleLoader.PluginDirectory, cancellationToken);

    /// <summary>
    /// Extracts the optional GPU add-on into <see cref="VoiceModuleLoader.CudaAddonDirectory"/>.
    /// Requires the base module to already be installed — this only supplies native runtime files
    /// <see cref="VoiceModuleLoadContext"/> loads instead of the CPU-only ones the module itself
    /// bundles; it is not usable on its own.
    /// </summary>
    public static Task InstallCudaAddonFromFileAsync(string zipPath, CancellationToken cancellationToken = default) =>
        ExtractAsync(zipPath, VoiceModuleLoader.CudaAddonDirectory, cancellationToken);

    private static Task ExtractAsync(string zipPath, string destinationDirectory, CancellationToken cancellationToken)
    {
        return Task.Run(() =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                if (!File.Exists(zipPath))
                    throw new FileNotFoundException("找不到選取的檔案。", zipPath);

                // Wipe and replace rather than merge on top of — a previous install or a
                // version downgrade should never leave stray files an older/newer module
                // doesn't expect.
                if (Directory.Exists(destinationDirectory))
                    Directory.Delete(destinationDirectory, recursive: true);
                Directory.CreateDirectory(destinationDirectory);

                ZipFile.ExtractToDirectory(zipPath, destinationDirectory, overwriteFiles: true);
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Failed to install {Target} from {Path}.", destinationDirectory, zipPath);
                throw;
            }
        }, cancellationToken);
    }
}
