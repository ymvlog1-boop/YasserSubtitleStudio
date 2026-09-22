namespace Yasser.ResumeCore;

public enum WorkStage { Transcription, Translation }
public enum WorkStatus { Pending, InProgress, Completed, Failed }

public sealed class SubtitleCue
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public long StartMilliseconds { get; set; }
    public long EndMilliseconds { get; set; }
    public string SourceText { get; set; } = "";
    public string Translation { get; set; } = "";
    // An explicit flag: even an empty human edit must not be overwritten by automation.
    public string? SourceLanguageCode { get; set; }
    public Guid? SourceWorkId { get; set; }
    public bool ManuallyEdited { get; set; }
}

public sealed class WorkUnit
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public WorkStage Stage { get; set; }
    public WorkStatus Status { get; set; } = WorkStatus.Pending;
    public Guid? CueId { get; set; }
    public long StartMilliseconds { get; set; }
    public long EndMilliseconds { get; set; }
    public string InputHash { get; set; } = "";
    public string EngineFingerprint { get; set; } = "";
    public string? DetectedLanguageCode { get; set; }
    public string? Error { get; set; }
    public DateTimeOffset? CompletedUtc { get; set; }
}

public sealed class SubtitleProject
{
    public int SchemaVersion { get; set; } = 1;
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Name { get; set; } = "Untitled";
    public string VideoPath { get; set; } = "";
    public string SubtitlePath { get; set; } = "";
    public long VideoPositionMilliseconds { get; set; }
    public Guid? SelectedCueId { get; set; }
    public DateTimeOffset UpdatedUtc { get; set; } = DateTimeOffset.UtcNow;
    public List<SubtitleCue> Cues { get; set; } = new();
    public List<WorkUnit> Work { get; set; } = new();
}
