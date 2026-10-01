using System.Windows;
using System.Windows.Controls;
using Microsoft.Win32;
using OverTranslate.Services;
using OverTranslate.Services.VoiceModule;
using OpenFileDialog = Microsoft.Win32.OpenFileDialog;
using UserControl = System.Windows.Controls.UserControl;

namespace OverTranslate.Views.Voice;

/// <summary>
/// Shown by <c>ShellWindow.GetVoiceContent</c> in place of the real voice page when
/// <see cref="VoiceModuleLoader"/> reports the optional module isn't installed (or failed to
/// load). Both installs are manual, file-picker imports rather than a download from a hosted
/// URL — the user builds (or receives) the module/CUDA zip themselves and points this at it, so
/// there is no release feed to keep in sync and nothing here can 404. No elevation either way:
/// everything lands under <see cref="VoiceModuleLoader.PluginDirectory"/>, which needs no admin
/// rights the rest of the app doesn't already have.
/// </summary>
public partial class VoiceModuleMissingView : UserControl
{
    /// <summary>Raised after the base module finishes installing, so the host can drop this view
    /// and swap in the real page without asking the user to reopen 語音翻譯.</summary>
    public event EventHandler? Installed;

    public VoiceModuleMissingView()
    {
        InitializeComponent();
        UpdateCudaButtonState();
    }

    private void UpdateCudaButtonState()
    {
        // The GPU add-on is meaningless without the base module (it only supplies native files the
        // module's own Whisper.net loading probes for) — greyed out rather than hidden, so the
        // option is visible before the user has installed anything, not just discoverable after.
        InstallCudaButton.IsEnabled = VoiceModuleLoader.IsModuleInstalled && !VoiceModuleLoader.IsCudaAddonInstalled;
    }

    private async void InstallModuleButton_Click(object sender, RoutedEventArgs e)
    {
        string? zipPath = PickZipFile();
        if (zipPath is null) return;

        InstallModuleButton.IsEnabled = false;
        ModuleProgressBar.Visibility = Visibility.Visible;
        SetStatus(null);

        try
        {
            await VoiceModuleDownloader.InstallModuleFromFileAsync(zipPath);

            VoiceModuleLoader.Reset();
            UpdateCudaButtonState();
            Installed?.Invoke(this, EventArgs.Empty);
        }
        catch (Exception ex)
        {
            SetStatus(LocalizationService.Format("S.VoiceModule.InstallFailed", ex.Message));
        }
        finally
        {
            ModuleProgressBar.Visibility = Visibility.Collapsed;
            InstallModuleButton.IsEnabled = true;
        }
    }

    private async void InstallCudaButton_Click(object sender, RoutedEventArgs e)
    {
        string? zipPath = PickZipFile();
        if (zipPath is null) return;

        InstallCudaButton.IsEnabled = false;
        CudaProgressBar.Visibility = Visibility.Visible;
        SetStatus(null);

        try
        {
            await VoiceModuleDownloader.InstallCudaAddonFromFileAsync(zipPath);

            // A fresh VoiceModuleLoadContext is created next time the module loads, so it will pick
            // up the cuda\ folder's contents even if the module was already loaded this session.
            VoiceModuleLoader.Reset();
            Installed?.Invoke(this, EventArgs.Empty);
        }
        catch (Exception ex)
        {
            SetStatus(LocalizationService.Format("S.VoiceModule.InstallFailed", ex.Message));
        }
        finally
        {
            CudaProgressBar.Visibility = Visibility.Collapsed;
            UpdateCudaButtonState();
        }
    }

    /// <summary>Opens a standard file-picker restricted to .zip. Returns null if the user cancels —
    /// callers treat that as "do nothing", not an error.</summary>
    private static string? PickZipFile()
    {
        var dialog = new OpenFileDialog
        {
            Filter = "Zip 壓縮檔 (*.zip)|*.zip|所有檔案 (*.*)|*.*",
            CheckFileExists = true,
            Multiselect = false,
        };
        return dialog.ShowDialog() == true ? dialog.FileName : null;
    }

    private void SetStatus(string? message)
    {
        StatusText.Text = message;
        StatusText.Visibility = message is null ? Visibility.Collapsed : Visibility.Visible;
    }
}
