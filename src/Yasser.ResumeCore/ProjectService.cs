namespace Yasser.ResumeCore;

public sealed class ProjectService
{
    private readonly ProjectStore _store;
    public ProjectService(ProjectStore store) => _store = store;

    public SubtitleProject CreateFromSrt(string projectFile, string subtitleFile, string videoFile)
    {
        var project = new SubtitleProject
        {
            Name = Path.GetFileNameWithoutExtension(videoFile),
            VideoPath = Path.GetFullPath(videoFile),
            SubtitlePath = Path.GetFullPath(subtitleFile),
            Cues = SrtCodec.Import(File.ReadAllText(subtitleFile))
        };
        _store.Save(projectFile, project);
        return project;
    }

    public SubtitleProject Open(string projectFile) => _store.Load(projectFile);

    private static void EnsureSafeExportPath(string projectFile, SubtitleProject project, string outputSrtFile)
    {
        string output = Path.GetFullPath(outputSrtFile);
        foreach (string protectedFile in new[] { projectFile, project.SubtitlePath, project.VideoPath })
            if (!string.IsNullOrWhiteSpace(protectedFile) &&
                string.Equals(output, Path.GetFullPath(protectedFile), StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("Export must not replace source media, source subtitles, or project.");
    }

    public void ExportTranscriptPartial(string projectFile, string outputSrtFile)
    {
        var project = Open(projectFile);
        EnsureSafeExportPath(projectFile, project, outputSrtFile);
        var completedWork = project.Work.Where(w => w.Stage == WorkStage.Transcription &&
            w.Status == WorkStatus.Completed).Select(w => w.Id).ToHashSet();
        var cues = project.Cues.Where(c => c.SourceWorkId.HasValue &&
            completedWork.Contains(c.SourceWorkId.Value));
        string output = Path.GetFullPath(outputSrtFile);
        Directory.CreateDirectory(Path.GetDirectoryName(output)!);
        File.WriteAllText(output, SrtCodec.Export(cues, onlyCompletedTranslation: false),
            new System.Text.UTF8Encoding(false));
    }

    /// <summary>Exports only finished Arabic translations and manual translations. Never overwrite the source.</summary>
    public void ExportPartial(string projectFile, string outputSrtFile)
    {
        var project = Open(projectFile);
        EnsureSafeExportPath(projectFile, project, outputSrtFile);
        string output = Path.GetFullPath(outputSrtFile);
        Directory.CreateDirectory(Path.GetDirectoryName(output)!);
        File.WriteAllText(output, SrtCodec.Export(project.Cues, onlyCompletedTranslation: true),
            new System.Text.UTF8Encoding(false));
    }
}
