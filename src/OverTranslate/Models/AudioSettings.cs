using System.Text.Json.Serialization;

namespace OverTranslate.Models;

/// <summary>Which local Whisper model 系統音訊翻譯 runs inference with.</summary>
/// <remarks>
/// A user-facing choice rather than a fixed constant, for the same reason 即時翻譯 lets the reader
/// pick how many blocks to frame: how much CPU/GPU headroom is available while a game or video is
/// also running is something only the person watching can judge. Tiny/Base favour reaction speed;
/// Small/Medium favour accuracy at the cost of a slower, heavier model resident in memory.
/// </remarks>
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum AudioAsrModelSize
{
    Tiny,
    Base,
    Small,
    Medium,
}

/// <summary>
/// Everything 系統音訊翻譯 keeps between sittings, under one key — see <see cref="RealtimeSettings"/>
/// for why grouped settings are the shape new features should copy.
/// </summary>
/// <remarks>
/// Deliberately has no source/target language or provider of its own — the optional module's
/// VoicePage reads and writes <see cref="RealtimeSettings.SourceLanguage"/>/
/// <see cref="RealtimeSettings.TargetLanguage"/>/<see cref="RealtimeSettings.Provider"/> directly,
/// one shared choice with 即時翻譯 rather than a second copy the user has to keep in sync. What's
/// left here is audio-only: nothing here means anything to 即時翻譯, and nothing in
/// <see cref="RealtimeSettings"/> means anything to this. Lives in the main app rather than the
/// module (see Services/VoiceModule/) so settings still round-trip through JSON even when the
/// module isn't installed.
/// </remarks>
public class AudioSettings
{
    /// <summary>Whether 系統音訊翻譯 is available from the tray menu / global shortcut at all.</summary>
    public bool Enabled { get; set; } = false;

    /// <inheritdoc cref="AudioAsrModelSize"/>
    public AudioAsrModelSize ModelSize { get; set; } = AudioAsrModelSize.Small;

    /// <summary>
    /// Milliseconds of continuous silence that ends a spoken segment and sends it to the ASR model.
    /// </summary>
    /// <remarks>
    /// 350–400ms is the sweet spot worked out during design: shorter and a speaker's natural
    /// mid-sentence pauses fragment into multiple segments with broken meaning; longer and the
    /// subtitle visibly lags behind speech. Exposed as a setting rather than a constant because a
    /// fast-talking narrator and a slow-paced dialogue scene do not share one right answer.
    /// </remarks>
    public int SilenceThresholdMs { get; set; } = 380;

    /// <summary>
    /// Longest a segment may run before it is force-flushed to the ASR model even without a silence
    /// gap, so a long uninterrupted line of dialogue is not held back indefinitely.
    /// </summary>
    public double MaxSegmentSeconds { get; set; } = 9.0;

    /// <inheritdoc cref="RealtimeSettings.TextColor"/>
    public string TextColor { get; set; } = Services.Realtime.RealtimeSubtitleColors.DefaultText;

    /// <inheritdoc cref="RealtimeSettings.ScrimColor"/>
    public string ScrimColor { get; set; } = Services.Realtime.RealtimeSubtitleColors.DefaultScrim;

    /// <inheritdoc cref="RealtimeSettings.ScrimOpacity"/>
    public int ScrimOpacity { get; set; } = Services.Realtime.RealtimeSubtitleColors.DefaultScrimOpacity;

    /// <summary>
    /// Scales the subtitle bar's font/padding — 步驟3's size control. 1.0 is the shipped default;
    /// stored as a multiplier rather than a raw font size so it scales cleanly with whatever the
    /// default itself is later tuned to.
    /// </summary>
    public double SubtitleScale { get; set; } = 1.0;

    /// <summary>
    /// 步驟3's toggle: show the recognised original-language line beneath the translation, or just
    /// the translation alone. Off by default would suit someone who only reads the target language;
    /// on suits someone checking the ASR's recognition against what they actually said/heard.
    /// </summary>
    public bool ShowOriginalSubtitle { get; set; } = true;

    /// <summary>
    /// The subtitle bar's remembered position — horizontal center and bottom edge, in the same DIP
    /// space <see cref="System.Windows.Window.Left"/>/<see cref="System.Windows.Window.Top"/> use.
    /// Null until the user drags the bar for the first time; while either is null, the module's
    /// AudioSubtitleWindow falls back to its bottom-center-of-primary-
    /// screen default instead. Bottom edge rather than top-left is stored because the bar grows
    /// upward as a line wraps to more rows (<c>SizeToContent="Height"</c>) — anchoring the bottom
    /// keeps that growth from creeping the bar away from wherever the user actually dropped it.
    /// </summary>
    public double? SubtitleAnchorCenterX { get; set; }

    /// <inheritdoc cref="SubtitleAnchorCenterX"/>
    public double? SubtitleAnchorBottomY { get; set; }
}