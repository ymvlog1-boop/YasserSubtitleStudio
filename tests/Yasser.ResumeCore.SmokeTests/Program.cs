using Yasser.ResumeCore;

static void Check(bool condition, string message)
{
    if (!condition) throw new Exception("FAILED: " + message);
    Console.WriteLine("PASS: " + message);
}

string dir = Path.Combine(Path.GetTempPath(), "YasserResumeSmoke-" + Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(dir);
try
{
    string projectPath = Path.Combine(dir, "wedding.yssproj");
    string sourcePath = Path.Combine(dir, "source.srt");
    string mediaPath = Path.Combine(dir, "wedding.mp4");
    File.WriteAllText(sourcePath, "1\n00:00:01,000 --> 00:00:03,000\nFirst line\n\n" +
        "2\n00:00:04,000 --> 00:00:06,000\nSecond line\n\n" +
        "3\n00:00:07,000 --> 00:00:09,000\nThird line\n");
    var store = new ProjectStore();
    var service = new ProjectService(store);
    var engine = new ResumeEngine(store);
    var original = service.CreateFromSrt(projectPath, sourcePath, mediaPath);
    Check(original.Cues.Count == 3, "SRT import keeps three cues");
    engine.EditCue(projectPath, original, original.Cues[0].Id, "ترجمة يدوية");
    engine.SetPosition(projectPath, original, 5000, original.Cues[1].Id);
    var tasks = engine.QueueTranslation(projectPath, original, "mock-engine-v1");
    Check(tasks.Count == 2, "Manually edited cue is excluded from automatic translation");
    engine.Begin(projectPath, original, tasks[0].Id);
    engine.CompleteTranslation(projectPath, original, tasks[0].Id, "Translated two");
    engine.Begin(projectPath, original, tasks[1].Id);
    var reopened = service.Open(projectPath);
    Check(reopened.VideoPositionMilliseconds == 5000 &&
        reopened.SelectedCueId == original.Cues[1].Id, "Playback position and selected cue survive restart");
    Check(reopened.Work.Single(w => w.Id == tasks[1].Id).Status == WorkStatus.Pending,
        "Interrupted work becomes retryable");
    var remaining = engine.QueueTranslation(projectPath, reopened, "mock-engine-v1");
    Check(remaining.Count == 1 && remaining[0].Id == tasks[1].Id,
        "Resume schedules only one remaining translation");
    string partial = Path.Combine(dir, "partial.srt");
    service.ExportPartial(projectPath, partial);
    var exported = SrtCodec.Import(File.ReadAllText(partial));
    Check(exported.Count == 2 && exported[0].SourceText == "ترجمة يدوية" &&
        exported[1].SourceText == "Translated two", "Partial SRT preserves completed translations");
    engine.CompleteTranslation(projectPath, reopened, remaining[0].Id, "Translated three");
    Check(engine.QueueTranslation(projectPath, reopened, "mock-engine-v1").Count == 0,
        "Completed work is not reprocessed");
    var transcription = new SubtitleProject { Name = "audio" };
    string audioProject = Path.Combine(dir, "audio.yssproj");
    var chunks = engine.QueueTranscription(audioProject, transcription, 75000, 30000, "audio-v1", "whisper-v1");
    Check(chunks.Count == 3 && chunks[2].EndMilliseconds == 75000,
        "Audio transcription splits into three bounded chunks");
    engine.Begin(audioProject, transcription, chunks[0].Id);
    engine.CompleteTranscription(audioProject, transcription, chunks[0].Id,
        new[] { new SubtitleCue { StartMilliseconds = 1000, EndMilliseconds = 2000, SourceText = "Hello" } });
    var reread = service.Open(audioProject);
    var stillMissing = engine.QueueTranscription(audioProject, reread, 75000, 30000, "audio-v1", "whisper-v1");
    Check(stillMissing.Count == 2 && reread.Cues.Count == 1,
        "Transcription resumes remaining chunks without duplicate saved output");
    var sample = WhisperJsonParser.Parse("""{"result":{"language":"ar"},"transcription":[{"offsets":{"from":500,"to":1750},"text":" مرحبا بكم "}]}""");
    Check(sample.LanguageCode == "ar" && sample.Segments.Count == 1 &&
        sample.Segments[0].StartMilliseconds == 500 && sample.Segments[0].EndMilliseconds == 1750 &&
        sample.Segments[0].Text == "مرحبا بكم", "Whisper auto-language JSON and millisecond timestamps are parsed");
    var audioSaved = service.Open(audioProject);
    Check(audioSaved.Cues[0].SourceWorkId == chunks[0].Id,
        "Transcription cues retain a stable source work ID for safe retry");
    string transcribedPartial = Path.Combine(dir, "transcribed.partial.srt");
    service.ExportTranscriptPartial(audioProject, transcribedPartial);
    Check(SrtCodec.Import(File.ReadAllText(transcribedPartial)).Count == 1,
        "Only completed transcription chunks are exported as original-language SRT");
    var newOptionsWork = engine.QueueTranscription(audioProject, audioSaved, 75000, 30000, "audio-v1", "other-engine");
    Check(newOptionsWork.Count == 3 && audioSaved.Cues.Count == 0,
        "Changing recognition engine invalidates generated captions without duplicating them");
    File.WriteAllText(projectPath, "{INVALID JSON");
    var recovered = service.Open(projectPath);
    Check(recovered.Cues.Count == 3, "Corrupt primary project falls back to previous backup");
    Check(File.ReadAllText(projectPath).Contains("SchemaVersion"),
        "Valid backup restores corrupt primary before next save");
    engine.SetPosition(projectPath, recovered, 7000, recovered.Cues[2].Id);
    var secondRecovery = service.Open(projectPath);
    Check(secondRecovery.VideoPositionMilliseconds == 7000 &&
        service.Open(projectPath + ".bak").Cues.Count == 3,
        "Saving after backup recovery keeps the good backup and latest position");
    Console.WriteLine("ALL SMOKE TESTS PASSED");
}
finally
{
    Directory.Delete(dir, recursive: true);
}
