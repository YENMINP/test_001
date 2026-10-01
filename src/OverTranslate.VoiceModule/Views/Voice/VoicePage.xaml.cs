using System;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using OverTranslate.Models;
using OverTranslate.Services;
using OverTranslate.Services.Audio;
using OverTranslate.Views.Audio;
using UserControl = System.Windows.Controls.UserControl;

namespace OverTranslate.Views.Voice; // 1. Modified Namespace

public partial class VoicePage : UserControl // 2. Modified Class Name
{
    private const string DefaultTargetLanguage = "ZH-HANT";
    private const TranslationProvider DefaultProvider = TranslationProvider.Microsoft;

    /// <summary>步驟3's 辨識模型 choices. A small named-property record, not a tuple — WPF's
    /// DisplayMemberPath/SelectedValuePath bind by reflection, which only sees ValueTuple's
    /// Item1/Item2, not its compile-time-only element names.</summary>
    private sealed record ModelSizeOption(AudioAsrModelSize Value, string Label);

    private static readonly ModelSizeOption[] ModelSizeOptions =
    [
        new(AudioAsrModelSize.Tiny, "最快（Tiny）"),
        new(AudioAsrModelSize.Base, "快速（Base）"),
        new(AudioAsrModelSize.Small, "標準（Small，預設）"),
        new(AudioAsrModelSize.Medium, "最準（Medium，較慢）"),
    ];

    public VoicePage() // 3. Modified Constructor Name
    {
        InitializeComponent();

        BindPickers();
        ApplyPageDefaults();
        ApplyAudioAppearanceDefaults();
        PopulateAudioDevices();

        ProviderBox.SelectionChanged += ProviderBox_SelectionChanged;
        TgtLangBox.SelectionChanged += TgtLangBox_SelectionChanged;
        SrcLangBox.SelectionChanged += SrcLangBox_SelectionChanged;

        RenderState();
    }

    private void ApplyPageDefaults()
    {
        var settings = SettingsService.Instance.Current;
        SrcLangBox.SelectedValue = LanguageData.GetValidRealtimeSourceCode(settings.Realtime.SourceLanguage);
        TgtLangBox.SelectedValue = LanguageData.GetValidTargetCode(settings.Realtime.TargetLanguage) ?? DefaultTargetLanguage;

        ProviderBox.SelectedValue = settings.Realtime.Provider;
        if (ProviderBox.SelectedValue == null) ProviderBox.SelectedIndex = 0;
    }

    /// <summary>Step 3's controls start from whatever was last saved to <see cref="AudioSettings"/>.</summary>
    private void ApplyAudioAppearanceDefaults()
    {
        var audio = SettingsService.Instance.Current.Audio;
        SubtitleScaleSlider.Value = audio.SubtitleScale;
        SubtitleOpacitySlider.Value = audio.ScrimOpacity;
        ShowOriginalCheck.IsChecked = audio.ShowOriginalSubtitle;

        ModelSizeBox.ItemsSource = ModelSizeOptions;
        ModelSizeBox.SelectedValue = audio.ModelSize;
    }

    private void BindPickers()
    {
        var source = SrcLangBox.SelectedValue;
        var target = TgtLangBox.SelectedValue;
        var provider = ProviderBox.SelectedValue;

        LocalizationService.BindLocalizedItems(
            SrcLangBox,
            LanguageData.OcrSourceLanguages
                .Where(language => !LanguageData.IsAutomaticSource(language.Code))
                .ToList());
        LocalizationService.BindLocalizedItems(TgtLangBox, LanguageData.TargetLanguages);
        LocalizationService.BindLocalizedItems(ProviderBox, LanguageData.Providers);

        SrcLangBox.SelectedValue = source;
        TgtLangBox.SelectedValue = target;
        ProviderBox.SelectedValue = provider;
    }

    /// <summary>
    /// (Re)lists 步驟2's devices for whichever mode 步驟1 currently has selected, keeping the
    /// current selection if it still exists in the new list.
    /// </summary>
    private void PopulateAudioDevices()
    {
        // MicModeRadio's IsChecked="True" in XAML fires its Checked handler during
        // InitializeComponent itself, before AudioDeviceBox — declared later in the same XAML —
        // has been constructed. Guard rather than reorder the XAML, since the same fragility would
        // just move to whichever element ends up declared last.
        if (AudioDeviceBox is null) return;

        var previouslySelected = AudioDeviceBox.SelectedValue as string;
        var microphoneMode = MicModeRadio.IsChecked == true;

        var devices = AudioDeviceCatalog.List(microphoneMode);
        AudioDeviceBox.ItemsSource = devices;

        // Keep the previous choice if it's still around (same mode, device still plugged in);
        // otherwise fall back to the first entry, which List() always puts "system default" at.
        AudioDeviceBox.SelectedValue = devices.Any(d => d.Id == previouslySelected)
            ? previouslySelected
            : devices[0].Id;
    }

    private void AudioModeRadio_Checked(object sender, RoutedEventArgs e) => PopulateAudioDevices();

    private void RefreshDevicesBtn_Click(object sender, RoutedEventArgs e) => PopulateAudioDevices();

    public void Reload()
    {
        BindPickers();
        RenderState();
    }

    public void Teardown()
    {
        // Add voice session teardown logic here later
    }

    private void ProviderBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (ProviderBox.SelectedValue is TranslationProvider provider)
            SaveTranslationPreference(settings => settings.Realtime.Provider = provider);
    }

    private void SrcLangBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (SrcLangBox.SelectedValue is string sourceLanguage)
            SaveTranslationPreference(settings => settings.Realtime.SourceLanguage = LanguageData.GetValidOcrSourceCode(sourceLanguage));
        RenderState();
    }

    private void TgtLangBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (TgtLangBox.SelectedValue is not string targetLanguage) return;
        SaveTranslationPreference(settings => settings.Realtime.TargetLanguage = LanguageData.GetValidTargetCode(targetLanguage));
    }

    private static void SaveTranslationPreference(Action<AppSettings> apply)
    {
        apply(SettingsService.Instance.Current);
        SettingsService.Instance.Save();
    }

    private static void SaveAudioPreference(Action<AudioSettings> apply)
    {
        apply(SettingsService.Instance.Current.Audio);
        SettingsService.Instance.Save();
    }

    /// <remarks>
    /// Applied live to the subtitle window when a session is already running, not just saved for
    /// next time — 步驟3 is the kind of setting someone tweaks while watching the subtitle sit on
    /// screen, and making them stop/restart translation just to see the effect would defeat the
    /// point of a slider.
    /// </remarks>
    private void SubtitleScaleSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        SaveAudioPreference(audio => audio.SubtitleScale = e.NewValue);
        AudioSessionController.Instance.ApplyAppearance(SettingsService.Instance.Current.Audio);
    }

    private void SubtitleOpacitySlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        SaveAudioPreference(audio => audio.ScrimOpacity = (int)e.NewValue);
        AudioSessionController.Instance.ApplyAppearance(SettingsService.Instance.Current.Audio);
    }

    private void ShowOriginalCheck_Changed(object sender, RoutedEventArgs e)
    {
        var show = ShowOriginalCheck.IsChecked == true;
        SaveAudioPreference(audio => audio.ShowOriginalSubtitle = show);
        AudioSessionController.Instance.ApplyAppearance(SettingsService.Instance.Current.Audio);
    }

    /// <remarks>
    /// No <see cref="AudioSessionController.ApplyAppearance"/> call needed, unlike the appearance
    /// controls above: <c>WhisperAsrEngine.TranscribeAsync</c> reads <see cref="AudioSettings.ModelSize"/>
    /// fresh from <see cref="SettingsService"/> on every segment, so a change here takes effect on
    /// the next spoken segment on its own, running session or not.
    /// </remarks>
    private void ModelSizeBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (ModelSizeBox.SelectedValue is AudioAsrModelSize size)
            SaveAudioPreference(audio => audio.ModelSize = size);
    }

    private void PrimaryBtn_Click(object sender, RoutedEventArgs e)
    {
        var microphoneMode = MicModeRadio.IsChecked == true;
        var deviceId = AudioDeviceBox.SelectedValue as string;
        AudioSessionController.Instance.Toggle(microphoneMode, deviceId);
        RenderState();
    }

    private void RenderState()
    {
        bool active = AudioSessionController.Instance.IsRunning;
        PrimaryBtn.Content = active ? "停止語音翻譯" : "開始語音翻譯";
        SrcLangBox.IsEnabled = !active;
        TgtLangBox.IsEnabled = !active;
        ProviderBox.IsEnabled = !active;
        MicModeRadio.IsEnabled = !active;
        SystemAudioModeRadio.IsEnabled = !active;
        AudioDeviceBox.IsEnabled = !active;
        RefreshDevicesBtn.IsEnabled = !active;
    }
}