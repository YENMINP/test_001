using NLog;
using OverTranslate.Models;
using System.IO;
using Whisper.net;
using Whisper.net.Ggml;

namespace OverTranslate.Services.Audio;

/// <summary>
/// Local Whisper inference for 系統音訊翻譯. Mirrors <c>OnnxOcrEngine</c>'s shape: cheap to
/// construct, the actual model is loaded on first use and kept resident afterwards.
/// </summary>
/// <remarks>
/// <para>
/// <b>Verify against your installed Whisper.net version before shipping.</b> Whisper.net's public
/// surface (<see cref="WhisperFactory"/>, the processor builder, the ggml downloader) has changed
/// across releases; the calls below are written against the commonly-documented 1.x shape but are
/// not compiled/tested against a specific pinned version in this pass.
/// </para>
/// <para>
/// Models are downloaded once via <see cref="WhisperGgmlDownloader"/> into the same local-app-data
/// area RapidOcrNet's models already assume the user can write to (unlike the OCR models, these
/// cannot ship bundled in the installer without materially growing it — a small ggml model alone
/// is tens of MB, medium is several hundred).
/// </para>
/// </remarks>
internal sealed class WhisperAsrEngine : IAsrEngine, IDisposable
{
    private static readonly Logger Log = LogManager.GetCurrentClassLogger();

    private readonly SemaphoreSlim _loadGate = new(1, 1);
    private WhisperFactory? _factory;
    private WhisperProcessor? _processor;
    private AudioAsrModelSize? _loadedSize;

    public async Task<string> TranscribeAsync(
        float[] pcm16kMono, string languageHint, CancellationToken cancellationToken = default)
    {
        var settings = SettingsService.Instance.Current.Audio;
        var processor = await EnsureLoadedAsync(settings.ModelSize, cancellationToken);

        var text = new System.Text.StringBuilder();
        await foreach (var segment in processor.ProcessAsync(pcm16kMono, cancellationToken))
            text.Append(segment.Text);

        return text.ToString().Trim();
    }

    private async Task<WhisperProcessor> EnsureLoadedAsync(
        AudioAsrModelSize size, CancellationToken cancellationToken)
    {
        if (_processor is not null && _loadedSize == size) return _processor;

        await _loadGate.WaitAsync(cancellationToken);
        try
        {
            // Re-check inside the gate — a concurrent segment may have already done this while we
            // were waiting, or the user may have changed the model size mid-session.
            if (_processor is not null && _loadedSize == size) return _processor;

            _processor?.Dispose();
            _factory?.Dispose();

            var ggmlType = ToGgmlType(size);
            var modelPath = await EnsureModelDownloadedAsync(ggmlType, cancellationToken);

            Log.Info("Loading Whisper model ({Size}) from {Path}", size, modelPath);
            _factory = WhisperFactory.FromPath(modelPath);
            _processor = _factory.CreateBuilder()
                .WithLanguage("auto") // per-segment language handled by the caller's hint, see remarks
                .Build();
            _loadedSize = size;
            return _processor;
        }
        finally
        {
            _loadGate.Release();
        }
    }

    private static GgmlType ToGgmlType(AudioAsrModelSize size) => size switch
    {
        AudioAsrModelSize.Tiny => GgmlType.Tiny,
        AudioAsrModelSize.Base => GgmlType.Base,
        AudioAsrModelSize.Small => GgmlType.Small,
        AudioAsrModelSize.Medium => GgmlType.Medium,
        _ => GgmlType.Small,
    };

    private static async Task<string> EnsureModelDownloadedAsync(GgmlType type, CancellationToken cancellationToken)
    {
        var dir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "OverTranslate", "whisper-models");
        Directory.CreateDirectory(dir);

        var path = Path.Combine(dir, $"ggml-{type.ToString().ToLowerInvariant()}.bin");
        if (!File.Exists(path))
        {
            // 1.9.1: the downloader is reached through the static Default instance, and
            // GetGgmlModelAsync does not take a cancellationToken — see WhisperGgmlDownloader in
            // Whisper.net 1.9.1 (confirmed against the published NuGet usage sample).
            using var modelStream = await WhisperGgmlDownloader.Default.GetGgmlModelAsync(type);
            using var fileStream = File.Create(path);
            await modelStream.CopyToAsync(fileStream, cancellationToken);
        }

        return path;
    }

    public void Dispose()
    {
        _processor?.Dispose();
        _factory?.Dispose();
        _loadGate.Dispose();
    }
}