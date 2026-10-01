namespace OverTranslate.Services.VoiceModule;

/// <summary>
/// Everything the host needs from the optional 系統音訊語音辨識 ("voice translation") module — see
/// <see cref="VoiceModuleLoader"/> for how an implementation gets discovered and loaded. Deliberately
/// tiny: the host must never need a type from NAudio or Whisper.net to compile, so this interface
/// only deals in host-side types (a plain <see cref="System.Windows.UIElement"/> for the page).
/// </summary>
public interface IVoiceModule
{
    /// <summary>
    /// Creates the module's shell page (步驟1/2/3 的 VoicePage). Called at most once per app run —
    /// <see cref="VoiceModuleLoader"/> caches the result — the first time the user opens 語音翻譯
    /// from the nav rail or tray menu.
    /// </summary>
    System.Windows.UIElement CreatePage();
}
