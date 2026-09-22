using System.Text.Json;

namespace Yasser.ResumeCore;

/// <summary>Local-only, versioned project store. No API credentials should be saved here.</summary>
public sealed class ProjectStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = false
    };

    public void Save(string projectFile, SubtitleProject project)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(projectFile);
        ArgumentNullException.ThrowIfNull(project);
        Validate(project);
        string fullPath = Path.GetFullPath(projectFile);
        Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
        project.UpdatedUtc = DateTimeOffset.UtcNow;
        string temporaryPath = fullPath + ".tmp-" + Guid.NewGuid().ToString("N");
        try
        {
            using (var stream = new FileStream(temporaryPath, FileMode.CreateNew, FileAccess.Write,
                       FileShare.None, 4096, FileOptions.WriteThrough))
            {
                JsonSerializer.Serialize(stream, project, JsonOptions);
                stream.Flush(flushToDisk: true);
            }
            // Keep one last-known-good version. A power outage while updating the backup
            // still leaves the original intact until the new temporary file is ready.
            if (File.Exists(fullPath))
            {
                File.Copy(fullPath, fullPath + ".bak", overwrite: true);
            }
            File.Move(temporaryPath, fullPath, overwrite: true);
        }
        finally
        {
            if (File.Exists(temporaryPath)) File.Delete(temporaryPath);
        }
    }

    public SubtitleProject Load(string projectFile)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(projectFile);
        string fullPath = Path.GetFullPath(projectFile);
        try
        {
            return ReadValidated(fullPath);
        }
        catch (Exception ex) when (ex is JsonException or IOException or InvalidDataException)
        {
            string backup = fullPath + ".bak";
            if (!File.Exists(backup)) throw;
            // First validate the backup; never replace the only good copy with a corrupt
            // primary file on the next Save(). Restore via staging to avoid partial copy.
            var recovered = ReadValidated(backup);
            string staged = fullPath + ".recovery-" + Guid.NewGuid().ToString("N");
            try
            {
                File.Copy(backup, staged);
                File.Move(staged, fullPath, overwrite: true);
            }
            finally
            {
                if (File.Exists(staged)) File.Delete(staged);
            }
            return recovered;
        }
    }

    private static SubtitleProject ReadValidated(string path)
    {
        using var stream = File.OpenRead(path);
        var project = JsonSerializer.Deserialize<SubtitleProject>(stream, JsonOptions)
            ?? throw new InvalidDataException("Project file was empty.");
        Validate(project);
        // An interrupted process has not produced a checkpoint; its unit may be retried.
        foreach (var unit in project.Work)
        {
            if (unit.Status == WorkStatus.InProgress) unit.Status = WorkStatus.Pending;
        }
        return project;
    }

    private static void Validate(SubtitleProject project)
    {
        if (project.SchemaVersion != 1) throw new InvalidDataException("Unsupported project schema.");
        if (project.Cues is null || project.Work is null) throw new InvalidDataException("Missing project data.");
        if (project.VideoPositionMilliseconds < 0) throw new InvalidDataException("Invalid video position.");
        if (project.Cues.Any(c => c.StartMilliseconds < 0 || c.EndMilliseconds <= c.StartMilliseconds))
            throw new InvalidDataException("Invalid subtitle timing.");
        if (project.Cues.Select(c => c.Id).Distinct().Count() != project.Cues.Count)
            throw new InvalidDataException("Duplicate subtitle IDs.");
        if (project.Work.Select(w => w.Id).Distinct().Count() != project.Work.Count)
            throw new InvalidDataException("Duplicate work unit IDs.");
        if (project.Work.Any(w => w.StartMilliseconds < 0 || w.EndMilliseconds <= w.StartMilliseconds))
            throw new InvalidDataException("Invalid work unit timing.");
    }
}
