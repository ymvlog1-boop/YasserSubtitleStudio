using System.Security.Cryptography;
using System.Text;

namespace Yasser.ResumeCore;

/// <summary>Idempotent checkpointing of individual SRT cues and timed transcription chunks.</summary>
public sealed class ResumeEngine
{
    private readonly ProjectStore _store;
    public ResumeEngine(ProjectStore store) => _store = store;

    public static string Fingerprint(string input) => Convert.ToHexString(
        SHA256.HashData(Encoding.UTF8.GetBytes(input)));

    public void SetPosition(string path, SubtitleProject project, long milliseconds, Guid? selectedCueId)
    {
        if (milliseconds < 0) throw new ArgumentOutOfRangeException(nameof(milliseconds));
        project.VideoPositionMilliseconds = milliseconds;
        project.SelectedCueId = selectedCueId;
        _store.Save(path, project);
    }

    public void EditCue(string path, SubtitleProject project, Guid cueId, string translatedText)
    {
        var cue = GetCue(project, cueId);
        cue.Translation = translatedText ?? throw new ArgumentNullException(nameof(translatedText));
        cue.ManuallyEdited = true;
        _store.Save(path, project);
    }

    /// <summary>Reconciles work to actual source text + selected engine/settings.
    /// Completed, matching units are reused. Human-edited text is never overwritten.
    /// </summary>
    public IReadOnlyList<WorkUnit> QueueTranslation(string path, SubtitleProject project, string engineFingerprint)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(engineFingerprint);
        foreach (var cue in project.Cues)
        {
            if (cue.ManuallyEdited) continue;
            string sourceHash = Fingerprint(cue.SourceText);
            var old = project.Work.LastOrDefault(w => w.Stage == WorkStage.Translation && w.CueId == cue.Id);
            if (old is not null && old.Status == WorkStatus.Completed &&
                old.InputHash == sourceHash && old.EngineFingerprint == engineFingerprint) continue;
            if (old is not null && old.Status == WorkStatus.InProgress) old.Status = WorkStatus.Pending;
            if (old is null)
            {
                old = new WorkUnit { Stage = WorkStage.Translation, CueId = cue.Id,
                    StartMilliseconds = cue.StartMilliseconds, EndMilliseconds = cue.EndMilliseconds };
                project.Work.Add(old);
            }
            else if (old.InputHash != sourceHash || old.EngineFingerprint != engineFingerprint)
            {
                // Invalidate stale generated text, not a human correction.
                cue.Translation = "";
            }
            old.InputHash = sourceHash;
            old.EngineFingerprint = engineFingerprint;
            old.StartMilliseconds = cue.StartMilliseconds;
            old.EndMilliseconds = cue.EndMilliseconds;
            old.Status = WorkStatus.Pending;
            old.Error = null;
            old.CompletedUtc = null;
        }
        _store.Save(path, project);
        return project.Work.Where(w => w.Stage == WorkStage.Translation &&
            w.Status != WorkStatus.Completed &&
            w.CueId.HasValue && !GetCue(project, w.CueId.Value).ManuallyEdited).ToList();
    }

    public IReadOnlyList<WorkUnit> QueueTranscription(string path, SubtitleProject project,
        long durationMilliseconds, long chunkMilliseconds, string audioFingerprint, string engineFingerprint)
    {
        if (durationMilliseconds <= 0 || chunkMilliseconds <= 0)
            throw new ArgumentOutOfRangeException(nameof(durationMilliseconds));
        ArgumentException.ThrowIfNullOrWhiteSpace(audioFingerprint);
        ArgumentException.ThrowIfNullOrWhiteSpace(engineFingerprint);
        // Each chunk is a half-open interval. The adapter can add audio context, but must
        // return timestamps clipped to this interval to avoid duplicate overlapping cues.
        for (long start = 0; start < durationMilliseconds; start += chunkMilliseconds)
        {
            long end = Math.Min(durationMilliseconds, start + chunkMilliseconds);
            string hash = Fingerprint($"{audioFingerprint}:{start}:{end}");
            var old = project.Work.FirstOrDefault(w => w.Stage == WorkStage.Transcription &&
                w.StartMilliseconds == start && w.EndMilliseconds == end);
            if (old is null)
            {
                old = new WorkUnit { Stage = WorkStage.Transcription,
                    StartMilliseconds = start, EndMilliseconds = end };
                project.Work.Add(old);
            }
            if (old.Status == WorkStatus.Completed && old.InputHash == hash &&
                old.EngineFingerprint == engineFingerprint) continue;
            if (old.Status == WorkStatus.Completed)
            {
                var generated = project.Cues.Where(c => c.SourceWorkId == old.Id).ToList();
                if (generated.Any(c => c.ManuallyEdited))
                    throw new InvalidOperationException("Cannot replace a completed chunk containing human edits. Export or copy your edits first.");
                if (generated.Count == 0 && project.Cues.Any(c =>
                    c.StartMilliseconds >= old.StartMilliseconds && c.EndMilliseconds <= old.EndMilliseconds))
                    throw new InvalidOperationException("Cannot safely replace an old untagged transcription chunk.");
                foreach (var cue in generated) project.Cues.Remove(cue);
                project.Work.RemoveAll(w => w.Stage == WorkStage.Translation &&
                    w.CueId.HasValue && generated.Any(c => c.Id == w.CueId.Value));
            }
            old.DetectedLanguageCode = null;
            old.InputHash = hash;
            old.EngineFingerprint = engineFingerprint;
            old.Status = WorkStatus.Pending;
            old.CompletedUtc = null;
            old.Error = null;
        }
        _store.Save(path, project);
        return project.Work.Where(w => w.Stage == WorkStage.Transcription &&
            w.Status != WorkStatus.Completed && w.StartMilliseconds < durationMilliseconds &&
            w.EndMilliseconds <= durationMilliseconds).ToList();
    }

    public void Begin(string path, SubtitleProject project, Guid workId)
    {
        var work = GetWork(project, workId);
        if (work.Status == WorkStatus.Completed) return;
        work.Status = WorkStatus.InProgress;
        work.Error = null;
        _store.Save(path, project);
    }

    public void CompleteTranslation(string path, SubtitleProject project, Guid workId, string text)
    {
        var work = GetWork(project, workId);
        if (work.Stage != WorkStage.Translation || !work.CueId.HasValue)
            throw new InvalidOperationException("Not a translation task.");
        var cue = GetCue(project, work.CueId.Value);
        if (cue.ManuallyEdited) return;
        // A delayed result from the old source or engine must not overwrite newer work.
        if (work.InputHash != Fingerprint(cue.SourceText))
            throw new InvalidOperationException("Source changed during translation; queue again.");
        if (work.Status == WorkStatus.Completed) return;
        cue.Translation = text ?? throw new ArgumentNullException(nameof(text));
        work.Status = WorkStatus.Completed;
        work.Error = null;
        work.CompletedUtc = DateTimeOffset.UtcNow;
        _store.Save(path, project);
    }

    public void CompleteTranscription(string path, SubtitleProject project, Guid workId,
        IReadOnlyList<SubtitleCue> recognizedCues, string? detectedLanguageCode = null)
    {
        ArgumentNullException.ThrowIfNull(recognizedCues);
        var work = GetWork(project, workId);
        if (work.Stage != WorkStage.Transcription) throw new InvalidOperationException("Not a transcription task.");
        if (work.Status == WorkStatus.Completed) return;
        if (recognizedCues.Any(c => c.StartMilliseconds < work.StartMilliseconds ||
                                    c.EndMilliseconds > work.EndMilliseconds ||
                                    c.EndMilliseconds <= c.StartMilliseconds))
            throw new InvalidDataException("Transcription timestamps must be inside the chunk.");
        // Chunk is committed together with its output; retries cannot duplicate its cues.
        foreach (var cue in recognizedCues)
        {
            if (project.Cues.Any(existing => existing.Id == cue.Id))
                throw new InvalidDataException("Recognized cue ID already exists.");
        }
        foreach (var cue in recognizedCues)
        {
            cue.SourceWorkId = work.Id;
            cue.SourceLanguageCode = detectedLanguageCode;
        }
        project.Cues.AddRange(recognizedCues);
        work.DetectedLanguageCode = detectedLanguageCode;
        work.Status = WorkStatus.Completed;
        work.CompletedUtc = DateTimeOffset.UtcNow;
        work.Error = null;
        _store.Save(path, project);
    }

    public void Fail(string path, SubtitleProject project, Guid workId, string error)
    {
        var work = GetWork(project, workId);
        if (work.Status == WorkStatus.Completed) return;
        work.Status = WorkStatus.Failed;
        work.Error = error;
        _store.Save(path, project);
    }

    private static WorkUnit GetWork(SubtitleProject project, Guid id) =>
        project.Work.Single(w => w.Id == id);
    private static SubtitleCue GetCue(SubtitleProject project, Guid id) =>
        project.Cues.Single(c => c.Id == id);
}
