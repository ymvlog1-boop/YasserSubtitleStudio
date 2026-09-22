using System.IO.Compression;
using System.Net.Http;
using System.Security.Cryptography;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;

namespace Yasser.SubtitleDesktop;

// First-run download is opt-in; all binaries stay in LocalAppData, never in media folders.
internal sealed class AutomaticToolSetup : Window
{
    private const string WhisperUrl = "https://github.com/ggml-org/whisper.cpp/releases/download/b5130/whisper-bin-x64.zip";
    private const string WhisperSha256 = "f9ec6c52a2e949b62ab51fa21d0d497958f9e41c3010c157c4e42932d5316f3c";
    private const string FfmpegUrl = "https://www.gyan.dev/ffmpeg/builds/ffmpeg-release-essentials.zip";
    private const string ModelUrl = "https://huggingface.co/ggerganov/whisper.cpp/resolve/main/ggml-base.bin";
    private const string ModelSha1 = "465707469ff3a37a2b9b8d8f89f2f99de7299dac";
    private static readonly string Root = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "YasserSubtitleStudio", "tools");
    private readonly TextBlock _status = new() { TextWrapping = TextWrapping.Wrap };
    private readonly ProgressBar _progress = new() { Minimum = 0, Maximum = 100, Height = 16 };
    private readonly Button _install = new() { Content = "تنزيل وإعداد المحركات تلقائياً" };
    private readonly Button _open = new() { Content = "فتح البرنامج", IsEnabled = false };
    private readonly Button _cancel = new() { Content = "إلغاء التنزيل", IsEnabled = false };
    private CancellationTokenSource? _cts;

    public AutomaticToolSetup()
    {
        Title = "Yasser Subtitle Studio — إعداد المكونات";
        Width = 660; Height = 350; MinWidth = 490; MinHeight = 300;
        var body = new StackPanel { Margin = new Avalonia.Thickness(20), Spacing = 14 };
        body.Children.Add(new TextBlock { Text = "إعداد الترجمة التلقائية", FontSize = 23 });
        body.Children.Add(new TextBlock { Text = "تنزيل FFmpeg ومحرك Whisper ونموذج لغات متعدد (~250 ميغابايت أو أكثر). يلزم اتصال إنترنت للتنزيل الأول فقط. تُحفظ الملفات خارج مجلد الفيديو؛ لا تُرسل مقاطعك إلى خادم.", TextWrapping = TextWrapping.Wrap });
        body.Children.Add(_status); body.Children.Add(_progress);
        var actions = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10 };
        actions.Children.Add(_install); actions.Children.Add(_open); actions.Children.Add(_cancel);
        body.Children.Add(actions); Content = body;
        _install.Click += async (_, _) => await InstallAsync();
        _open.Click += (_, _) => OpenMainWindow();
        _cancel.Click += (_, _) => _cts?.Cancel();
        if (TryLocate(out _)) { _status.Text = "المكونات جاهزة؛ لا حاجة لإعادة التنزيل."; _open.IsEnabled = true; }
        else _status.Text = "اضغط زر الإعداد مرة واحدة. سوف تظهر نسبة تقدم التنزيل.";
    }

    internal sealed record Paths(string Whisper, string Model, string Ffmpeg, string Ffprobe);

    internal static bool TryLocate(out Paths paths)
    {
        string whisper = Path.Combine(Root, "whisper", "whisper-cli.exe");
        string model = Path.Combine(Root, "models", "ggml-base.bin");
        string ffmpeg = Path.Combine(Root, "ffmpeg", "ffmpeg.exe");
        string ffprobe = Path.Combine(Root, "ffmpeg", "ffprobe.exe");
        paths = new Paths(whisper, model, ffmpeg, ffprobe);
        return File.Exists(whisper) && new FileInfo(whisper).Length > 0 && File.Exists(ffmpeg)
            && File.Exists(ffprobe) && File.Exists(model) && new FileInfo(model).Length > 100_000_000;
    }

    private async Task InstallAsync()
    {
        if (_cts is not null) return;
        _cts = new CancellationTokenSource(); _install.IsEnabled = false; _cancel.IsEnabled = true; _open.IsEnabled = false;
        try
        {
            if (!OperatingSystem.IsWindows() || System.Runtime.InteropServices.RuntimeInformation.OSArchitecture != System.Runtime.InteropServices.Architecture.X64)
                throw new PlatformNotSupportedException("هذه الحزمة مخصصة لويندوز x64 فقط.");
            Directory.CreateDirectory(Root);
            var token = _cts.Token;
            var paths = Path.Combine(Root, "whisper");
            if (!File.Exists(Path.Combine(paths, "whisper-cli.exe")))
            {
                string zip = await DownloadAsync(WhisperUrl, "Whisper", token);
                try
                {
                    await VerifyAsync(zip, WhisperSha256, SHA256.Create(), token);
                    await InstallZipAsync(zip, paths, ["whisper-cli.exe"], token);
                }
                finally { File.Delete(zip); }
            }
            paths = Path.Combine(Root, "ffmpeg");
            if (!File.Exists(Path.Combine(paths, "ffmpeg.exe")) || !File.Exists(Path.Combine(paths, "ffprobe.exe")))
            {
                string zip = await DownloadAsync(FfmpegUrl, "FFmpeg", token);
                try { await InstallZipAsync(zip, paths, ["ffmpeg.exe", "ffprobe.exe"], token); }
                finally { File.Delete(zip); }
            }
            string modelFile = Path.Combine(Root, "models", "ggml-base.bin");
            if (!File.Exists(modelFile) || new FileInfo(modelFile).Length < 100_000_000)
            {
                string tmp = await DownloadAsync(ModelUrl, "النموذج متعدد اللغات", token);
                try
                {
                    await VerifyAsync(tmp, ModelSha1, SHA1.Create(), token);
                    Directory.CreateDirectory(Path.GetDirectoryName(modelFile)!);
                    File.Move(tmp, modelFile, true);
                }
                finally { if (File.Exists(tmp)) File.Delete(tmp); }
            }
            if (!TryLocate(out _)) throw new IOException("التنزيل انتهى ولكن أحد الملفات المطلوبة غير موجود.");
            _status.Text = "اكتمل إعداد جميع المكونات. افتح البرنامج وابدأ تحويل الكلام إلى نص.";
            _progress.Value = 100; _open.IsEnabled = true;
        }
        catch (OperationCanceledException) { _status.Text = "تم إلغاء التنزيل. تستطيع إعادة المحاولة دون حذف مشاريع الترجمة."; }
        catch (Exception ex) { _status.Text = "تعذر إكمال الإعداد: " + ex.Message + " — تحقق من الاتصال والمساحة وأعد المحاولة."; }
        finally { _cts.Dispose(); _cts = null; _install.IsEnabled = true; _cancel.IsEnabled = false; }
    }

    private async Task<string> DownloadAsync(string url, string name, CancellationToken token)
    {
        string tmp = Path.Combine(Root, "download-" + Guid.NewGuid().ToString("N") + ".tmp");
        using var http = new HttpClient(new HttpClientHandler { AllowAutoRedirect = true }) { Timeout = TimeSpan.FromMinutes(40) };
        try
        {
            using var response = await http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, token);
            response.EnsureSuccessStatusCode();
            long? total = response.Content.Headers.ContentLength;
            await using var input = await response.Content.ReadAsStreamAsync(token);
            await using var output = new FileStream(tmp, FileMode.CreateNew, FileAccess.Write, FileShare.None, 1024 * 128, true);
            var buffer = new byte[1024 * 128]; long received = 0; int n;
            while ((n = await input.ReadAsync(buffer, token)) != 0)
            {
                await output.WriteAsync(buffer.AsMemory(0, n), token); received += n;
                var pct = total is > 0 ? Math.Min(100.0, received * 100.0 / total.Value) : 0;
                Dispatcher.UIThread.Post(() => { _progress.Value = pct; _status.Text = $"تنزيل {name}: {received / 1048576.0:F1} MB" + (total is > 0 ? $" / {total.Value / 1048576.0:F1} MB" : ""); });
            }
            return tmp;
        }
        catch { if (File.Exists(tmp)) File.Delete(tmp); throw; }
    }

    private static async Task VerifyAsync(string file, string expected, HashAlgorithm hash, CancellationToken token)
    {
        using (hash)
        await using (var stream = File.OpenRead(file))
        {
            string actual = Convert.ToHexString(await hash.ComputeHashAsync(stream, token));
            if (!actual.Equals(expected, StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("فشل التحقق من سلامة الملف المحمّل.");
        }
    }

    private async Task InstallZipAsync(string file, string destination, string[] requiredFiles, CancellationToken token)
    {
        string stage = destination + ".staging-" + Guid.NewGuid().ToString("N");
        Directory.CreateDirectory(stage);
        try
        {
            using var archive = ZipFile.OpenRead(file);
            foreach (var item in archive.Entries)
            {
                token.ThrowIfCancellationRequested();
                string name = Path.GetFileName(item.FullName.Replace('\\', '/'));
                if (string.IsNullOrEmpty(name)) continue;
                string target = Path.Combine(stage, name);
                if (File.Exists(target)) continue;
                await using var input = item.Open();
                await using var output = new FileStream(target, FileMode.CreateNew, FileAccess.Write, FileShare.None, 81920, true);
                await input.CopyToAsync(output, token);
            }
            foreach (string required in requiredFiles)
                if (!File.Exists(Path.Combine(stage, required))) throw new InvalidDataException("أرشيف الأدوات لا يحتوي على " + required);
            Directory.CreateDirectory(destination);
            foreach (string entry in Directory.GetFiles(stage))
                File.Move(entry, Path.Combine(destination, Path.GetFileName(entry)), true);
        }
        finally { if (Directory.Exists(stage)) Directory.Delete(stage, true); }
    }

    private void OpenMainWindow()
    {
        if (!TryLocate(out var paths)) { _status.Text = "لم تكتمل المكونات بعد."; return; }
        var main = new MainWindow();
        // Populate the existing prototype's tool fields without changing its saved projects.
        SetInput(main.Content, "المسار إلى whisper-cli.exe", paths.Whisper);
        SetInput(main.Content, "المسار إلى نموذج Whisper متعدد اللغات (.bin)", paths.Model);
        SetInput(main.Content, "ffmpeg", paths.Ffmpeg);
        SetInput(main.Content, "ffprobe", paths.Ffprobe);
        if (Application.Current?.ApplicationLifetime is Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime desktop)
            desktop.MainWindow = main;
        main.Show(); Close();
    }

    private static void SetInput(object? node, string match, string value)
    {
        if (node is TextBox text && (text.Watermark?.ToString() == match || text.Text == match)) { text.Text = value; return; }
        if (node is ScrollViewer viewer) SetInput(viewer.Content, match, value);
        if (node is Panel panel) foreach (var child in panel.Children) SetInput(child, match, value);
        if (node is ContentControl control) SetInput(control.Content, match, value);
    }
}
