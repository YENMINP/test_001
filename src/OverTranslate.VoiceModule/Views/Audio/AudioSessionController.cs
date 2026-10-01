using OverTranslate.Models;
using OverTranslate.Services;
using OverTranslate.Services.Audio;

namespace OverTranslate.Views.Audio;

/// <summary>
/// Owns one 語音翻譯 sitting end to end: the session and the subtitle window — mirrors
/// <see cref="Realtime.RealtimeSessionController"/>'s shape at a fraction of the size, since this
/// feature has no edit layer, no per-block windows and no capture-backend to hand off between them.
/// </summary>
internal sealed class AudioSessionController
{
    public static AudioSessionController Instance { get; } = new();

    private AudioTranslationSession? _session;
    private AudioSubtitleWindow? _subtitleWindow;

    public bool IsRunning => _session is not null;

    /// <param name="microphoneMode">步驟1的選擇：true 為麥克風，false 為系統音訊。</param>
    /// <param name="deviceId">步驟2的選擇，null 代表該模式下的系統預設裝置。</param>
    public void Toggle(bool microphoneMode, string? deviceId)
    {
        if (IsRunning) Stop();
        else Start(microphoneMode, deviceId);
    }

    public void Start(bool microphoneMode, string? deviceId)
    {
        if (IsRunning) return;

        var settings = SettingsService.Instance.Current.Audio;
        _subtitleWindow = new AudioSubtitleWindow(settings);
        _subtitleWindow.Show();

        _session = new AudioTranslationSession(settings, microphoneMode, deviceId);
        // BeginInvoke, not Invoke: this fires from a background/thread-pool continuation, and a
        // blocking Invoke here is exactly what made Stop() able to deadlock against it — see Stop().
        _session.OnSubtitleReady += line =>
            _subtitleWindow.Dispatcher.BeginInvoke(() => _subtitleWindow?.ShowLine(line.Original, line.Translated));
        _session.OnFailure += _ =>
            _subtitleWindow.Dispatcher.BeginInvoke(() => _subtitleWindow?.ShowTransientError());

        _session.Start();
    }

    /// <remarks>
    /// The subtitle window closes immediately, on the calling (UI) thread, so "Stop" always reads
    /// as instant. The session's own <see cref="AudioTranslationSession.Dispose"/> — which tears
    /// down WASAPI/COM capture and can briefly block the thread that calls it while that unwinds —
    /// runs on a background thread instead. Doing that teardown on the UI thread was the other half
    /// of the deadlock this once had: it could end up waiting for a capture-side continuation that
    /// was itself waiting on a blocking <c>Dispatcher.Invoke</c> back onto that same, now-busy UI
    /// thread. See <see cref="Start"/>'s switch to <c>BeginInvoke</c> for the other half of the fix.
    /// </remarks>
    public void Stop()
    {
        var session = _session;
        _session = null;

        _subtitleWindow?.Close();
        _subtitleWindow = null;

        if (session is not null)
            Task.Run(session.Dispose);
    }

    /// <summary>
    /// Pushes a 步驟3 appearance change to the open subtitle window immediately, if one exists —
    /// see <see cref="Views.Voice.VoicePage"/>'s slider/checkbox handlers and
    /// <see cref="AudioSubtitleWindow.ApplyAppearance"/> for why this doesn't wait for the next
    /// session to start.
    /// </summary>
    public void ApplyAppearance(AudioSettings settings)
    {
        var window = _subtitleWindow;
        window?.Dispatcher.BeginInvoke(() => window.ApplyAppearance(settings));
    }
}
