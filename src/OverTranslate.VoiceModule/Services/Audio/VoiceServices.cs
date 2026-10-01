namespace OverTranslate.Services.Audio;

/// <summary>
/// This module's own long-lived engine, mirroring <see cref="OverTranslate.Services.AppServices"/>
/// in the host — kept separate rather than added onto the host's class because the host must never
/// reference <see cref="WhisperAsrEngine"/> (that would pull Whisper.net back into the base install
/// this module exists to keep it out of). <see cref="OverTranslate.Services.AppServices.Translation"/>
/// is still the shared translation engine; only ASR is module-local.
/// </summary>
internal static class VoiceServices
{
    /// <summary>Local speech recognition for 系統音訊翻譯. Construction is cheap — the Whisper model
    /// loads lazily on first segment, and reloads only if the user changes
    /// <see cref="Models.AudioSettings.ModelSize"/> mid-session.</summary>
    public static WhisperAsrEngine AudioAsr { get; } = new();
}
