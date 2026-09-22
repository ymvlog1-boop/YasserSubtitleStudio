using System.Diagnostics;
using System.Globalization;
using System.Security.Cryptography;
using System.Text.Json;

namespace Yasser.ResumeCore;

/// <summary>One-click transcription using installed FFmpeg / FFprobe / multilingual whisper.cpp.
/// This backend returns original-language text; translation is deliberately a separate operation.</summary>
public sealed record AutoLanguageOptions(
    string FfmpegPath,
    string FfprobePath,
    string WhisperCliPath,
    string MultilingualModelPath,
    long ChunkMilliseconds = 30_000);

public sealed record RecognizedSegment(long StartMilliseconds, long EndMilliseconds, string Text);
public sealed record RecognizedChunk(string? LanguageCode, IReadOnlyList<RecognizedSegment> Segments);

/// <summary>Parse whisper.cpp --output-json-full, where offsets are milliseconds relative to the WAV.</summary>
public static class WhisperJsonParser
{
    public static RecognizedChunk Parse(string json)
    {
        using var document = JsonDocument.Parse(json);
        JsonElement root = document.RootElement;
        string? language = null;
        if (root.TryGetProperty("result", out var result) &&
            result.TryGetProperty("language", out var languageNode) &&
            languageNode.ValueKind == JsonValueKind.String)
        {
            language = languageNode.GetString();
            if (string.IsNullOrWhiteSpace(language) || language.Equals("auto", StringComparison.OrdinalIgnoreCase))
                language = null;
        }
        if (!root.TryGetProperty("transcription", out var transcription) ||
            transcription.ValueKind != JsonValueKind.Array)
            throw new InvalidDataException("Whisper did not return a transcription array.");
        var segments = new List<RecognizedSegment>();
        foreach (var item in transcription.EnumerateArray())
        {
            if (!item.TryGetProperty("offsets", out var offsets) ||
                !offsets.TryGetProperty("from", out var from) ||
                !offsets.TryGetProperty("to", out var to) ||
                !from.TryGetInt64(out long start) || !to.TryGetInt64(out long end) ||
                !item.TryGetProperty("text", out var textNode) ||
                textNode.ValueKind != JsonValueKind.String)
                throw new InvalidDataException("Malformed Whisper segment.");
            string text = textNode.GetString()?.Trim() ?? "";
            if (string.IsNullOrWhiteSpace(text)) continue;
            if (start < 0 || end <= start)
                throw new InvalidDataException("Invalid Whisper segment timing.");
            segments.Add(new RecognizedSegment(start, end, text));
        }
        return new RecognizedChunk(language, segments);
    }
}

internal static class ExternalCommand
{
    internal static async Task<string> RunAsync(string executable, IEnumerable<string> arguments,
        CancellationToken cancellationToken)
    {
        var info = new ProcessStartInfo(executable)
        {
            UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true,
            CreateNoWindow = true
        };
        foreach (string arg in arguments) info.ArgumentList.Add(arg);
        using var process = new Process { StartInfo = info };
        if (!process.Start()) throw new IOException($"Could not start {executable}.");
        using var registration = cancellationToken.Register(() =>
        {
            try { if (!process.HasExited) process.Kill(entireProcessTree: true); }
            catch (InvalidOperationException) { }
        });
        var stdout = process.StandardOutput.ReadToEndAsync(cancellationToken);
        var stderr = process.StandardError.ReadToEndAsync(cancellationToken);
        await process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);
        string output = await stdout.ConfigureAwait(false);
        string errors = await stderr.ConfigureAwait(false);
        if (process.ExitCode != 0)
            throw new IOException($"{Path.GetFileName(executable)} exited with {process.ExitCode}: {errors[..Math.Min(errors.Length, 3000)]}");
        return output;
    }
}

public sealed class AutoLanguageTranscriber
{
    private readonly ProjectStore _store;
    private readonly ResumeEngine _resume;
    public AutoLanguageTranscriber(ProjectStore store)
    {
        _store = store;
        _resume = new ResumeEngine(store);
    }

    /// <summary>Process an entire media file in bounded checkpoints; rerunning resumes remaining chunks.
    /// Only code/multilingual model provided by the caller is used: no model download or API upload.</summary>
    public async Task<SubtitleProject> RunAsync(string projectFile, string mediaFile,
        AutoLanguageOptions options, Action<WorkUnit, string?>? checkpoint = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(options);
        if (options.ChunkMilliseconds is < 1000 or > 30_000)
            throw new ArgumentOutOfRangeException(nameof(options), "Use audio chunks between 1 and 30 seconds.");
        string media = Path.GetFullPath(mediaFile);
        if (!File.Exists(media)) throw new FileNotFoundException("Media file not found.", media);
        if (!File.Exists(options.MultilingualModelPath))
            throw new FileNotFoundException("Whisper multilingual model not found.", options.MultilingualModelPath);
        if (Path.GetFileName(options.MultilingualModelPath).Contains(".en.", StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("An English-only Whisper model cannot auto-detect multiple languages.");

        SubtitleProject project;
        if (File.Exists(projectFile))
        {
            project = _store.Load(projectFile);
            if (!string.Equals(Path.GetFullPath(project.VideoPath), media, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("This project belongs to another media file. Choose a new project path.");
        }
        else
        {
            project = new SubtitleProject { Name = Path.GetFileNameWithoutExtension(media), VideoPath = media };
            _store.Save(projectFile, project);
        }
        string probe = await ExternalCommand.RunAsync(options.FfprobePath,
            ["-v", "error", "-show_entries", "format=duration", "-of", "default=noprint_wrappers=1:nokey=1", media],
            cancellationToken).ConfigureAwait(false);
        if (!double.TryParse(probe.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out double seconds) ||
            seconds <= 0 || seconds > long.MaxValue / 1000d)
            throw new InvalidDataException("Cannot determine media duration.");
        long duration = checked((long)Math.Ceiling(seconds * 1000));
        // Full media checksum protects against reusing saved captions for a modified video.
        string mediaHash;
        await using (var stream = File.OpenRead(media))
        {
            mediaHash = Convert.ToHexString(await SHA256.HashDataAsync(stream, cancellationToken).ConfigureAwait(false));
        }
        var modelInfo = new FileInfo(options.MultilingualModelPath);
        string engineFingerprint = ResumeEngine.Fingerprint($"whisper.cpp:auto:transcribe:{Path.GetFullPath(modelInfo.FullName)}:{modelInfo.Length}:{modelInfo.LastWriteTimeUtc.Ticks}:{options.ChunkMilliseconds}");
        var queued = _resume.QueueTranscription(projectFile, project, duration,
            options.ChunkMilliseconds, mediaHash, engineFingerprint);
        foreach (var work in queued)
        {
            cancellationToken.ThrowIfCancellationRequested();
            _resume.Begin(projectFile, project, work.Id);
            string tempDir = Path.Combine(Path.GetTempPath(), "yasser-stt-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(tempDir);
            try
            {
                string wav = Path.Combine(tempDir, "chunk.wav");
                string prefix = Path.Combine(tempDir, "result");
                await ExternalCommand.RunAsync(options.FfmpegPath,
                    ["-hide_banner", "-loglevel", "error", "-nostdin", "-y",
                     // Accurate input seeking avoids decoding an entire long video for each new chunk.
                     "-ss", (work.StartMilliseconds / 1000d).ToString("0.###", CultureInfo.InvariantCulture),
                     "-i", media,
                     "-t", ((work.EndMilliseconds - work.StartMilliseconds) / 1000d).ToString("0.###", CultureInfo.InvariantCulture),
                     "-vn", "-ac", "1", "-ar", "16000", "-c:a", "pcm_s16le", wav],
                    cancellationToken).ConfigureAwait(false);
                await ExternalCommand.RunAsync(options.WhisperCliPath,
                    ["-m", options.MultilingualModelPath, "-f", wav, "-l", "auto", "-ojf", "-of", prefix],
                    cancellationToken).ConfigureAwait(false);
                string jsonPath = prefix + ".json";
                if (!File.Exists(jsonPath)) throw new IOException("whisper-cli did not write JSON output.");
                var recognized = WhisperJsonParser.Parse(await File.ReadAllTextAsync(jsonPath, cancellationToken).ConfigureAwait(false));
                var cues = new List<SubtitleCue>();
                long availableDuration = work.EndMilliseconds - work.StartMilliseconds;
                foreach (var segment in recognized.Segments)
                {
                    long from = Math.Clamp(segment.StartMilliseconds, 0, availableDuration);
                    long to = Math.Clamp(segment.EndMilliseconds, 0, availableDuration);
                    if (to <= from) continue;
                    cues.Add(new SubtitleCue
                    {
                        StartMilliseconds = work.StartMilliseconds + from,
                        EndMilliseconds = work.StartMilliseconds + to,
                        SourceText = segment.Text,
                        SourceLanguageCode = recognized.LanguageCode,
                        SourceWorkId = work.Id
                    });
                }
                _resume.CompleteTranscription(projectFile, project, work.Id, cues, recognized.LanguageCode);
                checkpoint?.Invoke(work, recognized.LanguageCode);
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception ex)
            {
                _resume.Fail(projectFile, project, work.Id, ex.Message);
                throw;
            }
            finally
            {
                try { Directory.Delete(tempDir, recursive: true); }
                catch (IOException) { /* Keep work checkpoint even when temporary cleanup fails. */ }
            }
        }
        return project;
    }
}
