using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using Yasser.ResumeCore;

namespace Yasser.SubtitleDesktop;

/// <summary>
/// A runnable one-click desktop PREVIEW for the independently developed checkpoint engine.
/// It is NOT wired into upstream Subtitle Edit's existing main window yet.
/// </summary>
public sealed class MainWindow : Window
{
    private readonly TextBox _media = new() { Watermark = "ملف الفيديو أو الصوت" };
    private readonly TextBox _project = new() { Watermark = "ملف المشروع .yssproj" };
    private readonly TextBox _whisper = new() { Watermark = "المسار إلى whisper-cli.exe" };
    private readonly TextBox _model = new() { Watermark = "المسار إلى نموذج Whisper متعدد اللغات (.bin)" };
    private readonly TextBox _ffmpeg = new() { Text = "ffmpeg" };
    private readonly TextBox _ffprobe = new() { Text = "ffprobe" };
    private readonly Button _start = new() { Content = "تحويل الكلام إلى نص / استئناف", MinWidth = 240 };
    private readonly Button _cancel = new() { Content = "إيقاف بعد حفظ المقاطع المكتملة", IsEnabled = false };
    private readonly Button _export = new() { Content = "تصدير SRT جزئي", IsEnabled = false };
    private readonly TextBlock _message = new() { Text = "اختر فيديو ومحرك Whisper ونموذجاً متعدد اللغات." };
    private readonly ListBox _captions = new() { Height = 230 };
    private readonly ProjectStore _store = new();
    private readonly string _recentFile = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "YasserSubtitleStudio", "last-project.txt");
    private CancellationTokenSource? _running;

    public MainWindow()
    {
        Title = "Yasser Subtitle Studio — معاينة التطوير v0.3";
        Width = 900; Height = 760; MinWidth = 620; MinHeight = 580;
        var body = new StackPanel { Spacing = 10, Margin = new Thickness(18) };
        body.Children.Add(new TextBlock { Text = "Yasser Subtitle Studio", FontSize = 27 });
        body.Children.Add(new TextBlock { Text = "تحويل الصوت إلى نص بلغته الأصلية مع حفظ واستئناف كل مقطع — نسخة معاينة مستقلة عن واجهة Subtitle Edit" });
        body.Children.Add(new TextBlock { Text = "الفيديو / الصوت" });
        body.Children.Add(PickRow(_media, "اختيار فيديو", async () => await ChooseFileAsync(_media, "اختيار فيديو أو صوت", isMedia: true)));
        body.Children.Add(new TextBlock { Text = "مشروعك المحفوظ (افتحه لاحقاً للاستئناف)" });
        body.Children.Add(PickRow(_project, "فتح مشروع", async () => await ChooseFileAsync(_project, "فتح مشروع .yssproj")));
        body.Children.Add(new TextBlock { Text = "محرك Whisper المحلي" });
        body.Children.Add(PickRow(_whisper, "اختيار المحرك", async () => await ChooseFileAsync(_whisper, "اختيار whisper-cli.exe")));
        body.Children.Add(new TextBlock { Text = "نموذج Whisper متعدد اللغات؛ لا تستخدم نموذجاً إنكليزياً فقط" });
        body.Children.Add(PickRow(_model, "اختيار النموذج", async () => await ChooseFileAsync(_model, "اختيار نموذج Whisper")));
        var tools = new Grid { ColumnDefinitions = new ColumnDefinitions("*,*"), ColumnSpacing = 8 };
        tools.Children.Add(_ffmpeg);
        Grid.SetColumn(_ffprobe, 1); tools.Children.Add(_ffprobe);
        body.Children.Add(new TextBlock { Text = "مسارا FFmpeg / FFprobe (يمكن تركهما كما هما إذا كانا متاحين في PATH)" });
        body.Children.Add(tools);
        var commands = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10 };
        commands.Children.Add(_start); commands.Children.Add(_cancel); commands.Children.Add(_export);
        body.Children.Add(commands);
        body.Children.Add(_message);
        body.Children.Add(new TextBlock { Text = "السطور التي تم حفظها بالفعل" });
        body.Children.Add(_captions);
        body.Children.Add(new TextBlock {
            Text = "ملاحظة: تحديد اللغة تقديري وقد يخطئ مع اللهجات أو المقاطع القصيرة. التحويل إلى نص ليس ترجمة إلى لغة أخرى."
        });
        Content = new ScrollViewer { Content = body, HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Auto };

        _start.Click += async (_, _) => await StartAsync();
        _cancel.Click += (_, _) => { _running?.Cancel(); _message.Text = "يتم إيقاف المعالجة الجارية؛ المقاطع المحفوظة ستبقى بالمشروع."; };
        _export.Click += async (_, _) => await ExportAsync();
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
            var projectPath = File.ReadAllText(_recentFile).Trim();
            if (File.Exists(projectPath)) { _project.Text = projectPath; ShowProjectIfAvailable(); }
        }
        catch (IOException) { /* User may select another project manually. */ }
    }

    private void ShowProjectIfAvailable()
    {
        string file = _project.Text?.Trim() ?? "";
        if (string.IsNullOrEmpty(file) || !File.Exists(file)) return;
        try
        {
            var saved = _store.Load(file);
            _media.Text = saved.VideoPath;
            var finished = saved.Work.Count(w => w.Stage == WorkStage.Transcription && w.Status == WorkStatus.Completed);
            _message.Text = $"تم فتح {saved.Name} — المقاطع المكتملة: {finished}. اضغط تحويل / استئناف لإكمال الباقي.";
            RefreshCaptions(saved);
            _export.IsEnabled = finished > 0;
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException or System.Text.Json.JsonException)
        {
            _message.Text = $"تعذر قراءة المشروع: {ex.Message}";
        }
    }

    private void RefreshCaptions(SubtitleProject project)
    {
        _captions.ItemsSource = project.Cues.OrderBy(c => c.StartMilliseconds).Select(c =>
            $"{TimeSpan.FromMilliseconds(c.StartMilliseconds):hh\\:mm\\:ss} [{c.SourceLanguageCode ?? "?"}] {c.SourceText}").ToArray();
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
            string.IsNullOrWhiteSpace(engine) || string.IsNullOrWhiteSpace(model) || !File.Exists(engine) || !File.Exists(model))
        {
            _message.Text = "تحقق من مسار الفيديو والمشروع ومحرك whisper-cli ونموذج Whisper. يجب أن تكون الملفات موجودة.";
            return;
        }
        if (Path.GetFullPath(projectFile).Equals(Path.GetFullPath(media), StringComparison.OrdinalIgnoreCase))
        {
            _message.Text = "ملف المشروع يجب ألا يستبدل الفيديو."; return;
        }
        _running = new CancellationTokenSource();
        _start.IsEnabled = false; _cancel.IsEnabled = true; _export.IsEnabled = false;
        var token = _running.Token;
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(_recentFile))!);
            File.WriteAllText(_recentFile, Path.GetFullPath(projectFile));
            _message.Text = "جاري تحديد مدة الفيديو، ثم تقسيم الصوت وتحويله إلى نص...";
            var opts = new AutoLanguageOptions(
                string.IsNullOrWhiteSpace(_ffmpeg.Text) ? "ffmpeg" : _ffmpeg.Text.Trim(),
                string.IsNullOrWhiteSpace(_ffprobe.Text) ? "ffprobe" : _ffprobe.Text.Trim(), engine, model);
            var result = await new AutoLanguageTranscriber(_store).RunAsync(projectFile, media, opts,
                (chunk, language) => Dispatcher.UIThread.Post(() =>
                {
                    _message.Text = $"تم حفظ مقطع حتى {TimeSpan.FromMilliseconds(chunk.EndMilliseconds):hh\\:mm\\:ss} — اللغة المكتشفة: {language ?? "غير مؤكدة"}";
                    try { RefreshCaptions(_store.Load(projectFile)); }
                    catch (IOException) { /* Next checkpoint can refresh preview. */ }
                }), token);
            RefreshCaptions(result);
            _message.Text = $"اكتمل العمل المحفوظ. عدد سطور النص: {result.Cues.Count}. يمكنك تصدير ملف SRT.";
        }
        catch (OperationCanceledException)
        {
            _message.Text = "تم الإيقاف. المقاطع المكتملة محفوظة ويمكن استئناف البقية بالزر نفسه.";
        }
        catch (Exception ex)
        {
            _message.Text = $"تعذر إكمال التفريغ: {ex.Message}. يمكنك إصلاح السبب ثم الاستئناف.";
        }
        finally
        {
            _running.Dispose(); _running = null;
            _start.IsEnabled = true; _cancel.IsEnabled = false;
            ShowProjectIfAvailable();
        }
    }

    private async Task ExportAsync()
    {
        string projectFile = _project.Text?.Trim() ?? "";
        if (!File.Exists(projectFile)) { _message.Text = "افتح المشروع المحفوظ أولاً."; return; }
        var destination = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = "حفظ النص الحالي بصيغة SRT",
            SuggestedFileName = Path.GetFileNameWithoutExtension(projectFile) + ".partial.srt",
            DefaultExtension = "srt"
        });
        if (destination is null || !destination.Path.IsFile) return;
        try
        {
            new ProjectService(_store).ExportTranscriptPartial(projectFile, destination.Path.LocalPath);
            _message.Text = "تم تصدير الترجمة الجزئية: " + destination.Path.LocalPath;
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException or InvalidOperationException)
        {
            _message.Text = "تعذر التصدير: " + ex.Message;
        }
    }
}
