using OverTranslate.Services.VoiceModule;
using OverTranslate.Views.Voice;

namespace OverTranslate.VoiceModule;

/// <summary>
/// The one type <see cref="Services.VoiceModule.VoiceModuleLoader"/> looks for by reflection —
/// found by scanning this assembly for any public, non-abstract <see cref="IVoiceModule"/>, so
/// nothing on the host side needs to know this class's name specifically, only that exactly one
/// such type exists here.
/// </summary>
public sealed class VoiceModuleEntry : IVoiceModule
{
    public System.Windows.UIElement CreatePage() => new VoicePage();
}
