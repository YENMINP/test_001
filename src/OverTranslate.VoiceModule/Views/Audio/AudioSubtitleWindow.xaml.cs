using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using OverTranslate.Models;
using OverTranslate.Services;

namespace OverTranslate.Views.Audio;

/// <summary>
/// A single fixed subtitle bar, bottom-center of the screen by default but user-movable. Deliberately
/// not click-through today (WS_EX_TRANSPARENT) — unlike 即時翻譯's overlays, which sit over the exact
/// text they replace and must never intercept a click meant for the game underneath, this bar occupies
/// empty space at the bottom of the screen, so it can afford to take the mouse for dragging. Revisit if
/// that turns out to be wrong for fullscreen-exclusive games; see <c>AlwaysOnTop</c>/<c>WindowStyles</c>
/// for how the project already solves click-through elsewhere if so.
/// </summary>
internal sealed partial class AudioSubtitleWindow : Window
{
    private AudioSettings _settings;

    public AudioSubtitleWindow(AudioSettings settings)
    {
        _settings = settings;
        InitializeComponent();

        ApplyColors();
        ApplyScale();
        OriginalText.Visibility = _settings.ShowOriginalSubtitle ? Visibility.Visible : Visibility.Collapsed;

        // Visible at a low opacity from the moment the window opens, before any line has arrived —
        // otherwise a user whose bar landed on the wrong monitor (see Reposition's default) has
        // nothing on screen to find and drag into place. ShowLine's fade-in takes over once real
        // text arrives.
        TranslatedText.Text = "拖曳可移動字幕位置";
        Scrim.Opacity = 0.35;

        Scrim.Cursor = System.Windows.Input.Cursors.SizeAll;
        Scrim.MouseLeftButtonDown += Scrim_MouseLeftButtonDown;

        Loaded += (_, _) => Reposition();
    }

    /// <summary>
    /// Lets the user drag the bar anywhere — including onto whichever monitor they're actually
    /// watching, which is the one thing <see cref="Reposition"/>'s own default (bottom-center of the
    /// *primary* screen's work area) cannot get right on a multi-monitor setup. Left enabled while a
    /// session is running, not just before one starts: nothing about this bar needs to be locked down
    /// the way a click-through overlay would.
    /// </summary>
    private void Scrim_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.LeftButton != MouseButtonState.Pressed) return;
        DragMove(); // blocks until the mouse button is released
        SaveAnchor();
    }

    /// <summary>
    /// Remembers where the user just dropped the bar so <see cref="Reposition"/> uses this spot from
    /// now on instead of recomputing its primary-screen default — see
    /// <see cref="AudioSettings.SubtitleAnchorCenterX"/> for why bottom edge rather than top-left.
    /// </summary>
    private void SaveAnchor()
    {
        _settings.SubtitleAnchorCenterX = Left + Width / 2;
        _settings.SubtitleAnchorBottomY = Top + ActualHeight;
        SettingsService.Instance.Save();
    }

    /// <summary>
    /// Re-applies 步驟3's appearance settings to an already-open window, called from
    /// <see cref="AudioSessionController.ApplyAppearance"/> when the user moves a slider while a
    /// session is running — see that method's remarks for why this updates live instead of waiting
    /// for the next session.
    /// </summary>
    public void ApplyAppearance(AudioSettings settings)
    {
        _settings = settings;
        ApplyColors();
        ApplyScale();
        OriginalText.Visibility = _settings.ShowOriginalSubtitle ? Visibility.Visible : Visibility.Collapsed;
        Reposition(); // size may have changed with the new scale
    }

    private void ApplyScale()
    {
        // 步驟3's size control: a multiplier on the two base font sizes set in XAML (26/15), not a
        // separate absolute size — see AudioSettings.SubtitleScale.
        TranslatedText.FontSize = 26 * _settings.SubtitleScale;
        OriginalText.FontSize = 15 * _settings.SubtitleScale;
        Scrim.Padding = new Thickness(16 * _settings.SubtitleScale, 10 * _settings.SubtitleScale, 16 * _settings.SubtitleScale, 10 * _settings.SubtitleScale);
    }

    private void ApplyColors()
    {
        // TODO: confirm against RealtimeSubtitleColors whether it already exposes a
        // "#RRGGBB" (+opacity) -> Brush helper — reuse it instead of this inline parse if so, for
        // one shared definition of how these strings are interpreted.
        Scrim.Background = new SolidColorBrush(WithOpacity(_settings.ScrimColor, _settings.ScrimOpacity));
        var textBrush = new SolidColorBrush(ParseColor(_settings.TextColor));
        TranslatedText.Foreground = textBrush;
        OriginalText.Foreground = textBrush;
    }

    private static System.Windows.Media.Color ParseColor(string hex) =>
        (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString(hex);

    private static System.Windows.Media.Color WithOpacity(string hex, int opacityPercent)
    {
        var c = ParseColor(hex);
        c.A = (byte)Math.Clamp(opacityPercent * 255 / 100, 0, 255);
        return c;
    }

    /// <summary>
    /// Places the bar at the user's saved anchor if they've ever dragged it
    /// (<see cref="AudioSettings.SubtitleAnchorCenterX"/>/<see cref="AudioSettings.SubtitleAnchorBottomY"/>),
    /// otherwise falls back to the original bottom-center-of-primary-screen default.
    /// </summary>
    /// <remarks>
    /// The default branch's physical-pixel/DPI handling is deliberately left as-is rather than
    /// swapped for the project's <c>ScreenGeometry</c> helper (see <c>OverlayWindow</c>,
    /// <c>RealtimeBlockWindow</c>): it only ever runs once, before the user has dragged the bar
    /// anywhere, and dragging is now the documented way to correct it for a monitor
    /// <c>SystemParameters.WorkArea</c> gets wrong.
    /// </remarks>
    private void Reposition()
    {
        double centerX = _settings.SubtitleAnchorCenterX ?? DefaultCenterX();
        double bottomY = _settings.SubtitleAnchorBottomY ?? DefaultBottomY();
        Left = centerX - Width / 2;
        Top = bottomY - ActualHeight;
    }

    private static double DefaultCenterX()
    {
        var workArea = SystemParameters.WorkArea;
        return workArea.Left + workArea.Width / 2;
    }

    private static double DefaultBottomY()
    {
        var workArea = SystemParameters.WorkArea;
        return workArea.Top + workArea.Height * 0.85;
    }

    /// <summary>Displays one finished line and re-triggers the fade-in.</summary>
    public void ShowLine(string original, string translated)
    {
        TranslatedText.Text = translated;
        OriginalText.Text = original;
        Reposition(); // height changed with the new text

        var fadeIn = new Storyboard();
        fadeIn.Children.Add(new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(180))
            { });
        Storyboard.SetTarget(fadeIn.Children[0], Scrim);
        Storyboard.SetTargetProperty(fadeIn.Children[0], new PropertyPath(OpacityProperty));

        var rise = new DoubleAnimation(12, 0, TimeSpan.FromMilliseconds(180));
        Storyboard.SetTarget(rise, FadeInOffset);
        Storyboard.SetTargetProperty(rise, new PropertyPath(TranslateTransform.YProperty));
        fadeIn.Children.Add(rise);

        fadeIn.Begin();
    }

    /// <summary>
    /// A segment failed to transcribe/translate. Deliberately silent rather than showing an error
    /// bubble mid-video — see RealtimeTranslationSession's own "retry quietly" handling for failed
    /// passes, which this mirrors: one dropped line is not worth interrupting what's playing for.
    /// </summary>
    public void ShowTransientError() { /* intentionally no-op for now — see remarks */ }
}