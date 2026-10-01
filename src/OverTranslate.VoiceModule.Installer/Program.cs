using System.IO.Compression;
using System.Windows.Forms;

namespace OverTranslate.VoiceModule.Installer;

/// <summary>
/// The whole of the voice module's .exe installer: extract this exe's own appended zip payload into
/// wherever <c>OverTranslate.Services.VoiceModule.VoiceModuleLoader.PluginDirectory</c> resolves to
/// for an installed OverTranslate, and say so. See OverTranslate.VoiceModule.Installer.csproj for
/// how the payload gets there and publish-voice-module.ps1 for how this is built.
/// </summary>
/// <remarks>
/// Deliberately talks to nothing: not the registry, not a running OverTranslate process, not
/// Velopack's own locator (which describes THIS process, not OverTranslate's). This project has no
/// reference to OverTranslate.csproj at all — for an installed copy it only needs to agree with
/// <c>VoiceModuleLoader</c> on one path, and does so by naming the same install root Velopack
/// itself uses for a packId of "OverTranslate"; for a portable copy, which could be anywhere, it
/// asks the person running this to point at their own OverTranslate.exe instead — see
/// <see cref="ResolveTargetDirectory"/>. OverTranslate does not need to be running, or even
/// installed yet, for this to work; the next time it starts,
/// <c>VoiceModuleLoader.IsModuleInstalled</c> simply finds the files already there.
/// </remarks>
internal static class Program
{
    // Must match PackId in publish-velopack.ps1 / vpk pack — that is what decides the folder
    // Velopack installs OverTranslate into. Hard-coded rather than discovered: this process has no
    // OverTranslate instance or Velopack locator of its own to ask.
    private const string PackId = "OverTranslate";

    [STAThread]
    private static int Main(string[] args)
    {
        Application.EnableVisualStyles();

        try
        {
            var target = ResolveTargetDirectory(args);
            if (target is null)
                return 1; // user cancelled the folder picker; nothing to report

            using (var confirmForm = new ConfirmInstallForm(target))
            {
                if (confirmForm.ShowDialog() != DialogResult.OK)
                    return 1; // user cancelled on "下一步" step; nothing to report
            }

            Install(target);

            MessageBox.Show(
                $"語音翻譯模組已安裝完成：\n{target}\n\n" +
                "請重新啟動 OverTranslate，即可在導覽列使用「語音翻譯」。",
                "OverTranslate 語音模組",
                MessageBoxButtons.OK,
                MessageBoxIcon.Information);
            return 0;
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                $"安裝失敗：\n{ex.Message}",
                "OverTranslate 語音模組安裝失敗",
                MessageBoxButtons.OK,
                MessageBoxIcon.Error);
            return 1;
        }
    }

    /// <summary>
    /// Where the module lands. An explicit first argument still wins, for silent/scripted installs.
    /// Otherwise tries the standard installed location first — no need to bother anyone for that
    /// common case — and only when that guess doesn't pan out (almost always: a portable copy,
    /// which could be anywhere) asks the person running this to point at their own copy of
    /// OverTranslate.exe, rather than making them dig for the folder or type a path. Returns
    /// <see langword="null"/> if they cancel that dialog.
    /// </summary>
    private static string? ResolveTargetDirectory(string[] args)
    {
        if (args.Length > 0 && !string.IsNullOrWhiteSpace(args[0]))
            return Path.Combine(Path.GetFullPath(args[0]), "plugins", "voice");

        var guessedRoot = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), PackId);
        if (Directory.Exists(guessedRoot))
            return Path.Combine(guessedRoot, "plugins", "voice");

        MessageBox.Show(
            "找不到已安裝的 OverTranslate，可能是免安裝（Portable）版本。\n\n" +
            "請在接下來的視窗裡，找到並選取你的 OverTranslate.exe。",
            "OverTranslate 語音模組",
            MessageBoxButtons.OK,
            MessageBoxIcon.Information);

        using var dialog = new OpenFileDialog
        {
            Title = "選取 OverTranslate.exe",
            Filter = "OverTranslate.exe|OverTranslate.exe|所有執行檔 (*.exe)|*.exe",
            CheckFileExists = true,
        };
        if (dialog.ShowDialog() != DialogResult.OK)
            return null;

        return Path.Combine(RootFromExePath(dialog.FileName), "plugins", "voice");
    }

    /// <summary>
    /// The person may have pointed at either copy of OverTranslate.exe that exists in a Velopack
    /// layout: the one inside "current", or the top-level launcher next to it (see .portable /
    /// Update.exe) — a Portable copy has both, exactly like an installed copy's "current" sits
    /// beneath its own install root. Either selection resolves to the same root either way, one
    /// level above any folder literally named "current".
    /// </summary>
    private static string RootFromExePath(string exePath)
    {
        var folder = Path.GetDirectoryName(Path.GetFullPath(exePath))!;
        var parent = Path.GetDirectoryName(folder);
        return string.Equals(Path.GetFileName(folder), "current", StringComparison.OrdinalIgnoreCase)
               && parent is not null
            ? parent
            : folder;
    }

    /// <summary>
    /// Extracts the zip payload appended after this exe into <paramref name="target"/>. See the
    /// csproj for how the payload gets appended.
    /// </summary>
    /// <remarks>
    /// Reads the trailing 8 bytes as an Int64 zip length (little-endian, written by
    /// publish-voice-module.ps1 right after the zip bytes) and uses it to compute exactly where the
    /// zip starts, then copies just that range out into its own <see cref="MemoryStream"/> before
    /// handing it to <see cref="ZipArchive"/>. Deliberately not simpler — i.e. not just opening
    /// this exe's own file stream directly as a <see cref="ZipArchive"/> and trusting it to locate
    /// the embedded zip on its own, the classic SFX trick. In practice that fails intermittently
    /// with "Number of entries expected in End Of Central Directory does not correspond to number
    /// of entries in Central Directory" — .NET's central-directory-offset correction for an archive
    /// that doesn't start at byte 0 isn't reliable enough to depend on. Handing ZipArchive a clean,
    /// byte-0-based copy of just the zip sidesteps that entirely.
    /// </remarks>
    private static void Install(string target)
    {
        var exePath = Environment.ProcessPath;
        if (string.IsNullOrEmpty(exePath))
            throw new InvalidOperationException("找不到這個安裝程式自身的執行檔路徑。");

        using var exeStream = File.OpenRead(exePath);
        if (exeStream.Length < 8)
            throw new InvalidOperationException("這個安裝程式的檔案長度不正常（太短）。");

        exeStream.Seek(-8, SeekOrigin.End);
        Span<byte> lengthBytes = stackalloc byte[8];
        exeStream.ReadExactly(lengthBytes);
        var zipLength = BitConverter.ToInt64(lengthBytes);
        var zipStart = exeStream.Length - 8 - zipLength;

        if (zipLength <= 0 || zipStart < 0)
            throw new InvalidOperationException(
                "這個安裝程式沒有附帶正確的語音模組內容。它可能是直接發佈出來的 stub，而不是 " +
                "publish-voice-module.ps1 組合出來的完整安裝檔。");

        exeStream.Seek(zipStart, SeekOrigin.Begin);
        using var zipStream = new MemoryStream();
        CopyExactly(exeStream, zipStream, zipLength);
        zipStream.Position = 0;

        using var archive = new ZipArchive(zipStream, ZipArchiveMode.Read);
        if (archive.Entries.Count == 0)
            throw new InvalidOperationException("安裝檔內的語音模組內容是空的。");

        // Wipe and replace, matching VoiceModuleDownloader's in-app installer: a previous install
        // or a version downgrade should never leave stray files an older/newer module doesn't
        // expect.
        if (Directory.Exists(target))
            Directory.Delete(target, recursive: true);
        Directory.CreateDirectory(target);

        archive.ExtractToDirectory(target);
    }

    private static void CopyExactly(Stream source, Stream destination, long count)
    {
        var buffer = new byte[81920];
        while (count > 0)
        {
            var read = source.Read(buffer, 0, (int)Math.Min(buffer.Length, count));
            if (read == 0)
                throw new EndOfStreamException("讀取語音模組內容時檔案提早結束。");
            destination.Write(buffer, 0, read);
            count -= read;
        }
    }
}
