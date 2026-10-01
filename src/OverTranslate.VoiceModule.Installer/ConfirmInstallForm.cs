using System.Drawing;
using System.Windows.Forms;

namespace OverTranslate.VoiceModule.Installer;

/// <summary>
/// The one confirmation screen before <c>Install()</c> writes anything: shows exactly where the
/// module is about to go and lets the person back out. Styled like an ordinary installer wizard
/// page — a "下一步" button to proceed — rather than a blunt Yes/No message box, since this is the
/// last chance to catch a wrong folder (auto-detected, or just picked in the file dialog) before
/// anything gets deleted or written.
/// </summary>
internal sealed class ConfirmInstallForm : Form
{
    public ConfirmInstallForm(string targetDirectory)
    {
        Text = "OverTranslate 語音模組";
        FormBorderStyle = FormBorderStyle.FixedDialog;
        StartPosition = FormStartPosition.CenterScreen;
        MaximizeBox = false;
        MinimizeBox = false;
        ShowInTaskbar = true;
        ClientSize = new Size(440, 176);
        Font = new Font("Microsoft JhengHei UI", 9F);
        AutoScaleMode = AutoScaleMode.Font;

        var introLabel = new Label
        {
            Text = "即將安裝語音翻譯模組到：",
            AutoSize = true,
            Location = new Point(16, 16),
        };

        var pathBox = new TextBox
        {
            Text = targetDirectory,
            ReadOnly = true,
            Location = new Point(16, 40),
            Width = 408,
            BackColor = SystemColors.Control,
            BorderStyle = BorderStyle.FixedSingle,
        };

        var noteLabel = new Label
        {
            Text = "如果這個資料夾不是你的 OverTranslate 所在位置，請按「取消」重新選取，" +
                   "不要按下一步。",
            AutoSize = false,
            Location = new Point(16, 70),
            Size = new Size(408, 48),
            ForeColor = SystemColors.GrayText,
        };

        var cancelButton = new Button
        {
            Text = "取消",
            DialogResult = DialogResult.Cancel,
            Location = new Point(240, 130),
            Size = new Size(84, 28),
        };

        var nextButton = new Button
        {
            Text = "下一步",
            DialogResult = DialogResult.OK,
            Location = new Point(332, 130),
            Size = new Size(92, 28),
        };

        Controls.AddRange([introLabel, pathBox, noteLabel, cancelButton, nextButton]);
        AcceptButton = nextButton;
        CancelButton = cancelButton;
    }
}
