using System.Net;
using System.Net.Http;
using System.Text;
using Yasser.ResumeCore;

static void Check(bool condition, string description)
{
    if (!condition) throw new Exception("FAIL: " + description);
    Console.WriteLine("PASS: " + description);
}

string dir = Path.Combine(Path.GetTempPath(), "YasserOnlineTest-" + Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(dir);
try
{
    var store = new ProjectStore();
    string path = Path.Combine(dir, "turkish.yssproj");
    var project = new SubtitleProject { Name = "turkish", VideoPath = Path.Combine(dir, "video.mp4") };
    project.Cues.AddRange(new[]
    {
        new SubtitleCue { StartMilliseconds = 0, EndMilliseconds = 1000, SourceText = "Merhaba", SourceLanguageCode = "tr" },
        new SubtitleCue { StartMilliseconds = 1000, EndMilliseconds = 2000, SourceText = "Günaydın", SourceLanguageCode = "tr" },
        new SubtitleCue { StartMilliseconds = 2000, EndMilliseconds = 3000, SourceText = "Teşekkür ederim", SourceLanguageCode = "tr" }
    });
    store.Save(path, project);
    int requests = 0;
    using (var client = new HttpClient(new FakeHandler(request =>
    {
        requests++;
        Check(request.RequestUri!.Host == "api.mymemory.translated.net" &&
            request.RequestUri.Query.Contains("langpair=tr%7Car", StringComparison.OrdinalIgnoreCase),
            "Only Turkish cue text and language pair reach the configured translation endpoint");
        return requests == 2 ? new HttpResponseMessage(HttpStatusCode.TooManyRequests) :
            new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("{\"responseStatus\":200,\"responseData\":{\"translatedText\":\"مرحبا\"}}", Encoding.UTF8, "application/json")
            };
    })))
    {
        bool interrupted = false;
        try { await new OnlineTranslation(store, client).TranslateToArabicAsync(path); }
        catch (HttpRequestException) { interrupted = true; }
        Check(interrupted && requests == 2, "Provider quota interruption is surfaced without losing progress");
    }
    Check(store.Load(path).Cues[0].Translation == "مرحبا" &&
        string.IsNullOrEmpty(store.Load(path).Cues[1].Translation),
        "First finished cue remains saved after the second request fails");
    int resumedRequests = 0;
    using (var client = new HttpClient(new FakeHandler(_ =>
    {
        resumedRequests++;
        return new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("{\"responseStatus\":200,\"responseData\":{\"translatedText\":\"ترجمة\"}}", Encoding.UTF8, "application/json")
        };
    })))
    {
        await new OnlineTranslation(store, client).TranslateAsync(path, "en");
        Check(resumedRequests == 2, "Resume sends only the two unfinished cues");
        await new OnlineTranslation(store, client).TranslateToArabicAsync(path);
        Check(resumedRequests == 2, "Fully completed translation makes no additional network requests");
    }
    int englishRequests = 0;
    using (var client = new HttpClient(new FakeHandler(request =>
    {
        englishRequests++;
        Check(request.RequestUri!.Query.Contains("langpair=tr%7Cen", StringComparison.OrdinalIgnoreCase),
            "Changing target language sends the selected target code");
        return new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("{\"responseStatus\":200,\"responseData\":{\"translatedText\":\"English translation\"}}", Encoding.UTF8, "application/json")
        };
    })))
    {
        await new OnlineTranslation(store, client).TranslateAsync(path, "en");
    }
    Check(englishRequests == 3 && store.Load(path).TranslationLanguageCode == "en",
        "Changing target language invalidates generated translations and persists the new target");

    string srt = Path.Combine(dir, "translated.srt");
    new ProjectService(store).ExportPartial(path, srt);
    Check(SrtCodec.Import(File.ReadAllText(srt)).Count == 3, "Translated SRT export retains all subtitle timings");
    bool refusedOverwrite = false;
    try { new ProjectService(store).ExportPartial(path, path); }
    catch (InvalidOperationException) { refusedOverwrite = true; }
    Check(refusedOverwrite, "Translated export cannot overwrite the project file");
    var edited = store.Load(path);
    new ResumeEngine(store).EditCue(path, edited, edited.Cues[0].Id, "تصحيح بشري");
    using (var client = new HttpClient(new FakeHandler(_ => throw new Exception("Manual edit was resent"))))
    {
        await new OnlineTranslation(store, client).TranslateToArabicAsync(path);
        Check(store.Load(path).Cues[0].Translation == "تصحيح بشري", "Manual subtitle correction is preserved without retranslating it");
    }
    Console.WriteLine("ALL ONLINE TRANSLATION SMOKE TESTS PASSED");
}
finally
{
    Directory.Delete(dir, recursive: true);
}

sealed class FakeHandler(Func<HttpRequestMessage, HttpResponseMessage> answer) : HttpMessageHandler
{
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
        Task.FromResult(answer(request));
}
