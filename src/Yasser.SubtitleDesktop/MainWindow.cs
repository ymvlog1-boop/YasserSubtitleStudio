using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using Yasser.ResumeCore;

namespace Yasser.SubtitleDesktop;

/// <summary>Independent desktop PREVIEW; upstream Subtitle Edit integration is still pending.</summary>
public sealed class MainWindow : Window
{
    private readonly TextBox _media = new() { Watermark = "ملف الفيديو أو الصوت" };
    private readonly TextBox _project = new() { Watermark = "ملف المشروع .yssproj" };
    private readonly TextBox _whisper = new() { Watermark = "المسار إلى whisper-cli.exe" };
    private readonly TextBox _model = new() { Watermark = "المسار إلى نموذج Whisper متعدد اللغات (.bin)" };
    private readonly TextBox _ffmpeg = new() { Text = "ffmpeg" };
    private readonly TextBox _ffprobe = new() { Text = "ffprobe" };
    private readonly Button _start = new() { Content = "تحويل الكلام إلى نص / استئناف", MinWidth = 220 };
    private readonly Button _translate = new() { Content = "ترجمة النص إلى العربية / استئناف", IsEnabled = false };
    private readonly Button _cancel = new() { Content = "إيقاف بعد حفظ المقاطع المكتملة", IsEnabled = false };
    private readonly Button _export = new() { Content = "تصدير النص الأصلي SRT", IsEnabled = false };
    private readonly Button _exportArabic = new() { Content = "تصدير الترجمة العربية SRT", IsEnabled = false };
    private readonly TextBlock _message = new() { Text = "اختر فيديو لبدء تحويل الكلام إلى نص." };
    private readonly ListBox _captions = new() { Height = 240 };
    private readonly ProjectStore _store = new();
    private readonly string _recentFile = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "YasserSubtitleStudio", "last-project.txt");
    private CancellationTokenSource? _running;

    public MainWindow()
    {
        Title = "Yasser Subtitle Studio — معاينة التطوير";
        Width = 1010; Height = 790; MinWidth = 650; MinHeight = 580;
        var body = new StackPanel { Spacing = 10, Margin = new Thickness(18) };
        body.Children.Add(new TextBlock { Text = "Yasser Subtitle Studio", FontSize = 27 });
        body.Children.Add(new TextBlock { Text = "تحويل صوت الفيديو إلى نص بلغته الأصلية، وترجمة السطور إلى العربية مع الحفظ والاستئناف." });
        body.Children.Add(new TextBlock { Text = "الفيديو / الصوت" });
        body.Children.Add(PickRow(_media, "اختيار فيديو", async () => await ChooseFileAsync(_media, "اختيار فيديو أو صوت", isMedia: true)));
        body.Children.Add(new TextBlock { Text = "مشروعك المحفوظ (افتحه لاحقاً للاستئناف)" });
        body.Children.Add(PickRow(_project, "فتح مشروع", async () => await ChooseFileAsync(_project, "فتح مشروع .yssproj")));
        body.Children.Add(new TextBlock { Text = "محرك Whisper المحلي" });
        body.Children.Add(PickRow(_whisper, "اختيار المحرك", async () => await ChooseFileAsync(_whisper, "اختيار whisper-cli.exe")));
        body.Children.Add(new TextBlock { Text = "نموذج Whisper متعدد اللغات؛ لا تستخدم نموذجاً إنكليزياً فقط" });
        body.Children.Add(PickRow(_model, "اختيار النموذج", async () => await ChooseFileAsync(_model, "اختيار نموذج Whisper")));
        var tools = new Grid { ColumnDefinitions = new ColumnDefinitions("*,*"), ColumnSpacing = 8 };
        tools.Children.Add(_ffmpeg); Grid.SetColumn(_ffprobe, 1); tools.Children.Add(_ffprobe);
        body.Children.Add(new TextBlock { Text = "مسارا FFmpeg / FFprobe" });
        body.Children.Add(tools);
        var commands = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10 };
        commands.Children.Add(_start); commands.Children.Add(_cancel); commands.Children.Add(_export);
        body.Children.Add(commands);
        var translations = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10 };
        translations.Children.Add(_translate); translations.Children.Add(_exportArabic);
        body.Children.Add(translations);
        body.Children.Add(new TextBlock
        {
            Text = "الترجمة العربية تستخدم الإنترنت وخدمة MyMemory المجانية: يُرسل نص كل سطر فقط إلى جهة خارجية، وليس الفيديو أو الصوت. قد تنتهي الحصة المجانية. لا تضغط زر الترجمة إذا كانت النصوص سرية.",
            TextWrapping = Avalonia.Media.TextWrapping.Wrap
        });
        body.Children.Add(_message);
        body.Children.Add(new TextBlock { Text = "السطور المحفوظة — النص الأصلي والترجمة العربية إن وُجدت" });
        body.Children.Add(_captions);
        body.Children.Add(new TextBlock { Text = "ملاحظة: تحديد لغة الكلام تقديري، وقد يخطئ مع اللهجات أو المقاطع القصيرة. يمكن تصدير النص الأصلي دون ترجمة." });
        Content = new ScrollViewer { Content = body, HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Auto };

        _start.Click += async (_, _) => await StartAsync();
        _translate.Click += async (_, _) => await TranslateAsync();
        _cancel.Click += (_, _) => { _running?.Cancel(); _message.Text = "جاري إيقاف المعالجة؛ المقاطع المكتملة محفوظة."; };
        _export.Click += async (_, _) => await ExportAsync(arabic: false);
        _exportArabic.Click += async (_, _) => await ExportAsync(arabic: true);
        _project.LostFocus += (_, _) => ShowProjectIfAvailable();
        RestoreLastProject();
    }

    private static Control PickRow(TextBox input, string caption, Func<Task> picker)
    {
        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto"), ColumnSpacing = 8 };
        var button = new Button { Content = caption };
        button.Click += async (_, _) => await picker();
        grid.Children.Add(input); Grid.SetColumn(button, 1); grid.Children.Add(button);
        return grid;
    }

    private async Task ChooseFileAsync(TextBox target, string title, bool isMedia = false)
    {
        var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = title, AllowMultiple = false
        });
        if (files.Count == 0 || !files[0].Path.IsFile) return;
        target.Text = files[0].Path.LocalPath;
        if (isMedia)
        {
            _project.Text = Path.ChangeExtension(target.Text, ".yssproj");
            ShowProjectIfAvailable();
        }
        else if (ReferenceEquals(target, _project)) ShowProjectIfAvailable();
    }

    private void RestoreLastProject()
    {
        if (!File.Exists(_recentFile)) return;
        try
        {
            string projectPath = File.ReadAllText(_recentFile).Trim();
            if (File.Exists(projectPath)) { _project.Text = projectPath; ShowProjectIfAvailable(); }
        }
        catch (IOException) { /* The user can manually reopen any project. */ }
    }

    private void ShowProjectIfAvailable()
    {
        string file = _project.Text?.Trim() ?? "";
        if (string.IsNullOrEmpty(file) || !File.Exists(file))
        {
            _translate.IsEnabled = false; _export.IsEnabled = false; _exportArabic.IsEnabled = false;
            _captions.ItemsSource = Array.Empty<string>();
            return;
        }
        try
        {
            var saved = _store.Load(file);
            _media.Text = saved.VideoPath;
            int transcribed = saved.Work.Count(w => w.Stage == WorkStage.Transcription && w.Status == WorkStatus.Completed);
            int translated = saved.Cues.Count(c => !string.IsNullOrWhiteSpace(c.Translation));
            _message.Text = $"تم فتح {saved.Name} — مقاطع الصوت المكتملة: {transcribed} — السطور المترجمة: {translated} / {saved.Cues.Count}.";
            RefreshCaptions(saved);
            _translate.IsEnabled = _running is null && saved.Cues.Count > 0;
            _export.IsEnabled = transcribed > 0;
            _exportArabic.IsEnabled = translated > 0;
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException or System.Text.Json.JsonException)
        {
            _message.Text = "تعذر قراءة المشروع: " + ex.Message;
            _translate.IsEnabled = false; _exportArabic.IsEnabled = false;
        }
    }

    private void RefreshCaptions(SubtitleProject project)
    {
        _captions.ItemsSource = project.Cues.OrderBy(c => c.StartMilliseconds).Select(c =>
            $"{TimeSpan.FromMilliseconds(c.StartMilliseconds):hh\:mm\:ss} [{c.SourceLanguageCode ?? "?"}] {c.SourceText}" +
            (string.IsNullOrWhiteSpace(c.Translation) ? "" : "\n   العربية: " + c.Translation)).ToArray();
    }

    private void SetProcessing(bool processing)
    {
        _start.IsEnabled = !processing; _translate.IsEnabled = !processing &&
            File.Exists(_project.Text?.Trim()) && _store.Load(_project.Text!.Trim()).Cues.Count > 0;
        _cancel.IsEnabled = processing; _export.IsEnabled = !processing && _export.IsEnabled;
        _exportArabic.IsEnabled = !processing && _exportArabic.IsEnabled;
    }

    private async Task StartAsync()
    {
        if (_running is not null) return;
        string media = _media.Text?.Trim() ?? "";
        string projectFile = _project.Text?.Trim() ?? "";
        string engine = _whisper.Text?.Trim() ?? "";
        string model = _model.Text?.Trim() ?? "";
        if (string.IsNullOrWhiteSpace(projectFile) && !string.IsNullOrWhiteSpace(media))
        {
            projectFile = Path.ChangeExtension(media, ".yssproj"); _project.Text = projectFile;
        }
        if (string.IsNullOrWhiteSpace(media) || !File.Exists(media) || string.IsNullOrWhiteSpace(projectFile) ||
            !File.Exists(engine) || !File.Exists(model))
        {
            _message.Text = "تحقق من الفيديو والمشروع ومحرك Whisper والنموذج. يجب أن تكون الملفات موجودة.";
            return;
        }
        if (Path.GetFullPath(projectFile).Equals(Path.GetFullPath(media), StringComparison.OrdinalIgnoreCase))
        {
            _message.Text = "ملف المشروع يجب ألا يستبدل الفيديو."; return;
        }
        _running = new CancellationTokenSource();
        _start.IsEnabled = false; _translate.IsEnabled = false; _cancel.IsEnabled = true;
        _export.IsEnabled = false; _exportArabic.IsEnabled = false;
        var token = _running.Token;
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(_recentFile))!);
            File.WriteAllText(_recentFile, Path.GetFullPath(projectFile));
            _message.Text = "جاري تحديد مدة الفيديو وتقسيم الصوت وتحويله إلى نص...";
            var opts = new AutoLanguageOptions(
                string.IsNullOrWhiteSpace(_ffmpeg.Text) ? "ffmpeg" : _ffmpeg.Text.Trim(),
                string.IsNullOrWhiteSpace(_ffprobe.Text) ? "ffprobe" : _ffprobe.Text.Trim(), engine, model);
            var result = await new AutoLanguageTranscriber(_store).RunAsync(projectFile, media, opts,
                (chunk, language) => Dispatcher.UIThread.Post(() =>
                {
                    _message.Text = $"تم حفظ مقطع حتى {TimeSpan.FromMilliseconds(chunk.EndMilliseconds):hh\:mm\:ss} — اللغة: {language ?? "غير مؤكدة"}";
                    try { RefreshCaptions(_store.Load(projectFile)); }
                    catch (IOException) { /* Refresh on next checkpoint. */ }
                }), token);
            RefreshCaptions(result);
            _message.Text = $"اكتمل التفريغ. عدد السطور: {result.Cues.Count}. اضغط ترجمة النص إلى العربية إن رغبت.";
        }
        catch (OperationCanceledException) { _message.Text = "تم الإيقاف، والمقاطع المكتملة محفوظة للاستئناف."; }
        catch (Exception ex) { _message.Text = "تعذر إكمال التفريغ: " + ex.Message; }
        finally
        {
            _running.Dispose(); _running = null;
            string status = _message.Text ?? "";
            ShowProjectIfAvailable(); _message.Text = status;
            _start.IsEnabled = true; _cancel.IsEnabled = false;
        }
    }

    private async Task TranslateAsync()
    {
        if (_running is not null) return;
        string path = _project.Text?.Trim() ?? "";
        if (!File.Exists(path)) { _message.Text = "افتح المشروع المحفوظ أولاً."; return; }
        _running = new CancellationTokenSource();
        _start.IsEnabled = false; _translate.IsEnabled = false; _cancel.IsEnabled = true;
        _export.IsEnabled = false; _exportArabic.IsEnabled = false;
        try
        {
            using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(35) };
            http.DefaultRequestHeaders.UserAgent.ParseAdd("YasserSubtitleStudio/0.4");
            _message.Text = "جاري ترجمة النصوص للعربية عبر الإنترنت وحفظ كل سطر فور اكتماله...";
            var updated = await new OnlineTranslation(_store, http).TranslateToArabicAsync(path,
                (done, total) => Dispatcher.UIThread.Post(() =>
                {
                    _message.Text = $"تم حفظ {done} من {total} سطور تحتاج ترجمة. يمكنك الإيقاف والاستئناف.";
                    try { RefreshCaptions(_store.Load(path)); }
                    catch (IOException) { /* Refresh on next checkpoint. */ }
                }), _running.Token);
            RefreshCaptions(updated);
            _message.Text = "اكتملت ترجمة السطور المتاحة إلى العربية. يمكنك تصدير ملف SRT العربي.";
        }
        catch (OperationCanceledException) { _message.Text = "توقفت الترجمة، والسطور المكتملة محفوظة. اضغط الترجمة لاحقاً لاستئناف البقية."; }
        catch (Exception ex) { _message.Text = "توقفت الترجمة: " + ex.Message + " — السطور المكتملة محفوظة؛ يمكنك الاستئناف."; }
        finally
        {
            _running.Dispose(); _running = null;
            string status = _message.Text ?? "";
            ShowProjectIfAvailable(); _message.Text = status;
            _start.IsEnabled = true; _cancel.IsEnabled = false;
        }
    }

    private async Task ExportAsync(bool arabic)
    {
        string projectFile = _project.Text?.Trim() ?? "";
        if (!File.Exists(projectFile)) { _message.Text = "افتح المشروع المحفوظ أولاً."; return; }
        var destination = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = arabic ? "تصدير الترجمة العربية الحالية بصيغة SRT" : "حفظ النص الأصلي بصيغة SRT",
            SuggestedFileName = Path.GetFileNameWithoutExtension(projectFile) + (arabic ? ".ar.partial.srt" : ".original.partial.srt"),
            DefaultExtension = "srt"
        });
        if (destination is null || !destination.Path.IsFile) return;
        try
        {
            var service = new ProjectService(_store);
            if (arabic) service.ExportPartial(projectFile, destination.Path.LocalPath);
            else service.ExportTranscriptPartial(projectFile, destination.Path.LocalPath);
            _message.Text = "تم تصدير الملف: " + destination.Path.LocalPath;
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException or InvalidOperationException)
        {
            _message.Text = "تعذر التصدير: " + ex.Message;
        }
    }
}
