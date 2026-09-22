using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;

namespace Yasser.ResumeCore;

/// <summary>Online subtitle translation. Only a cue's text is sent to the service when the user starts translation.
/// Video, audio, project files and timecodes are never uploaded by this component.</summary>
public sealed class OnlineTranslation
{
    private const string ServiceUrl = "https://api.mymemory.translated.net/get";
    private readonly ProjectStore _store;
    private readonly ResumeEngine _resume;
    private readonly HttpClient _http;

    public OnlineTranslation(ProjectStore store, HttpClient http)
    {
        _store = store;
        _resume = new ResumeEngine(store);
        _http = http;
    }

    public static string NormalizeLanguage(string? language)
    {
        string code = (language ?? "").Trim().ToLowerInvariant().Replace('_', '-');
        return code switch
        {
            "arabic" => "ar", "turkish" => "tr", "english" => "en", "french" => "fr",
            "german" => "de", "spanish" => "es", "persian" or "farsi" => "fa",
            "italian" => "it", "russian" => "ru", "portuguese" => "pt", "korean" => "ko",
            "japanese" => "ja", "chinese" => "zh-CN", "kurdish" or "ku" => "ku",
            "" or "auto" or "unknown" => "",
            _ => code
        };
    }

    public Task<SubtitleProject> TranslateToArabicAsync(string projectFile,
        Action<int, int>? progress = null, CancellationToken cancellationToken = default) =>
        TranslateAsync(projectFile, "ar", progress, cancellationToken);

    public async Task<SubtitleProject> TranslateAsync(string projectFile, string targetLanguage,
        Action<int, int>? progress = null, CancellationToken cancellationToken = default)
    {
        if (!File.Exists(projectFile)) throw new FileNotFoundException("افتح مشروع الترجمة المحفوظ أولاً.", projectFile);
        var project = _store.Load(projectFile);
        if (project.Cues.Count == 0) throw new InvalidOperationException("حوّل الكلام إلى نص أولاً، ثم اضغط الترجمة.");
        string target = NormalizeLanguage(targetLanguage);
        if (target.Length == 0) throw new InvalidOperationException("اختر لغة ترجمة صالحة.");
        string fingerprint = "mymemory-public-api:v2:target=" + target;
        project.TranslationLanguageCode = target;
        _store.Save(projectFile, project);
        var tasks = _resume.QueueTranslation(projectFile, project, fingerprint);
        int completed = 0;
        progress?.Invoke(completed, tasks.Count);
        foreach (var work in tasks)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var cue = project.Cues.Single(c => c.Id == work.CueId);
            string source = NormalizeLanguage(cue.SourceLanguageCode);
            if (source.Length == 0)
            {
                _resume.Fail(projectFile, project, work.Id, "تعذر تحديد لغة هذا السطر. راجع لغة المقطع قبل الترجمة.");
                throw new InvalidOperationException("تعذر تحديد لغة سطر عند " + TimeSpan.FromMilliseconds(cue.StartMilliseconds).ToString(@"hh\:mm\:ss") + ". لم نفترض أنها إنكليزية ولم نغير النص الأصلي.");
            }
            if (string.IsNullOrWhiteSpace(cue.SourceText))
            {
                _resume.CompleteTranslation(projectFile, project, work.Id, "");
                progress?.Invoke(++completed, tasks.Count);
                continue;
            }
            _resume.Begin(projectFile, project, work.Id);
            try
            {
                string result = source == target ? cue.SourceText :
                    await TranslateCueAsync(cue.SourceText, source, target, cancellationToken).ConfigureAwait(false);
                _resume.CompleteTranslation(projectFile, project, work.Id, result);
                progress?.Invoke(++completed, tasks.Count);
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception ex)
            {
                _resume.Fail(projectFile, project, work.Id, ex.Message);
                throw;
            }
        }
        return project;
    }

    private async Task<string> TranslateCueAsync(string text, string source, string target, CancellationToken token)
    {
        // The public MyMemory endpoint caps a request at 500 UTF-8 bytes. Do not truncate subtitles.
        if (Encoding.UTF8.GetByteCount(text) > 500)
            throw new InvalidOperationException("هذا السطر يتجاوز الحد المسموح لخدمة الترجمة المجانية (500 بايت). قسّمه إلى سطرين ثم أعد المحاولة.");
        string url = ServiceUrl + "?q=" + Uri.EscapeDataString(text) +
            "&langpair=" + Uri.EscapeDataString(source + "|" + target);
        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        using var response = await _http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, token).ConfigureAwait(false);
        if (response.StatusCode == HttpStatusCode.TooManyRequests)
            throw new HttpRequestException("تم بلوغ حد خدمة الترجمة المجانية. انتظر ثم اضغط استئناف، أو استخدم محرك ترجمة آخر.");
        response.EnsureSuccessStatusCode();
        await using var stream = await response.Content.ReadAsStreamAsync(token).ConfigureAwait(false);
        using var json = await JsonDocument.ParseAsync(stream, cancellationToken: token).ConfigureAwait(false);
        var root = json.RootElement;
        if (!root.TryGetProperty("responseStatus", out var status) || !status.TryGetInt32(out int code) || code != 200)
        {
            string detail = root.TryGetProperty("responseDetails", out var d) ? d.ToString() : "خطأ غير معروف";
            throw new IOException("رفضت خدمة الترجمة الطلب أو تجاوزت الحصة المجانية: " + detail);
        }
        if (!root.TryGetProperty("responseData", out var data) ||
            !data.TryGetProperty("translatedText", out var node) || node.ValueKind != JsonValueKind.String ||
            string.IsNullOrWhiteSpace(node.GetString()))
            throw new InvalidDataException("لم ترجع خدمة الترجمة نصاً صالحاً؛ لم يتم تسجيل هذا السطر كمكتمل.");
        return WebUtility.HtmlDecode(node.GetString())!.Trim();
    }
}
