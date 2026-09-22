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

    public void ExportTranscriptPartial(string projectFile, string outputSrtFile)
    {
        var project = Open(projectFile);
        string outputFullPath = Path.GetFullPath(outputSrtFile);
        if (string.Equals(outputFullPath, Path.GetFullPath(projectFile), StringComparison.OrdinalIgnoreCase) ||
            (!string.IsNullOrWhiteSpace(project.SubtitlePath) &&
             string.Equals(outputFullPath, project.SubtitlePath, StringComparison.OrdinalIgnoreCase)) ||
            (!string.IsNullOrWhiteSpace(project.VideoPath) &&
             string.Equals(outputFullPath, project.VideoPath, StringComparison.OrdinalIgnoreCase)))
            throw new InvalidOperationException("Transcript export must not replace source media, source subtitles, or project.");
        var completedWork = project.Work.Where(w => w.Stage == WorkStage.Transcription &&
            w.Status == WorkStatus.Completed).Select(w => w.Id).ToHashSet();
        var cues = project.Cues.Where(c => c.SourceWorkId.HasValue &&
            completedWork.Contains(c.SourceWorkId.Value));
        Directory.CreateDirectory(Path.GetDirectoryName(outputFullPath)!);
        File.WriteAllText(outputFullPath, SrtCodec.Export(cues, onlyCompletedTranslation: false),
            new System.Text.UTF8Encoding(false));
    }


    public void ExportPartial(string projectFile, string outputSrtFile)
    {
        var project = Open(projectFile);
        string srt = SrtCodec.Export(project.Cues, onlyCompletedTranslation: true);
        string outputFullPath = Path.GetFullPath(outputSrtFile);
        // Never overwrite a project's original subtitle source on partial export.
        if (string.Equals(outputFullPath, project.SubtitlePath, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Partial export must not replace the original SRT.");
        Directory.CreateDirectory(Path.GetDirectoryName(outputFullPath)!);
        File.WriteAllText(outputFullPath, srt, new System.Text.UTF8Encoding(false));
    }
}
