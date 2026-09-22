using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace Yasser.ResumeCore;

public static partial class SrtCodec
{
    [GeneratedRegex(@"\A\s*(?:(?:\d+)\s*\r?\n)?(?<from>\d{2,}:\d{2}:\d{2}[,.]\d{3})\s*-->\s*(?<to>\d{2,}:\d{2}:\d{2}[,.]\d{3})(?:[^\r\n]*)\r?\n(?<text>[\s\S]*)\z")]
    private static partial Regex BlockRegex();

    public static List<SubtitleCue> Import(string srt)
    {
        ArgumentNullException.ThrowIfNull(srt);
        var cues = new List<SubtitleCue>();
        string normalized = srt.TrimStart('\uFEFF').Replace("\r\n", "\n").Trim();
        if (normalized.Length == 0) return cues;
        foreach (string block in Regex.Split(normalized, @"\n[ \t]*\n+"))
        {
            var match = BlockRegex().Match(block.Trim());
            if (!match.Success) throw new InvalidDataException("Invalid SRT cue.");
            long start = ParseTime(match.Groups["from"].Value);
            long end = ParseTime(match.Groups["to"].Value);
            if (end <= start) throw new InvalidDataException("End must follow start.");
            cues.Add(new SubtitleCue { StartMilliseconds = start, EndMilliseconds = end,
                SourceText = match.Groups["text"].Value.TrimEnd() });
        }
        return cues;
    }

    public static string Export(IEnumerable<SubtitleCue> cues, bool onlyCompletedTranslation)
    {
        var builder = new StringBuilder();
        int n = 0;
        foreach (var cue in cues.OrderBy(c => c.StartMilliseconds).ThenBy(c => c.EndMilliseconds))
        {
            string content = onlyCompletedTranslation ? cue.Translation :
                (string.IsNullOrWhiteSpace(cue.Translation) ? cue.SourceText : cue.Translation);
            if (string.IsNullOrWhiteSpace(content)) continue;
            if (cue.EndMilliseconds <= cue.StartMilliseconds || cue.StartMilliseconds < 0)
                throw new InvalidDataException("Invalid cue timing.");
            builder.AppendLine((++n).ToString(CultureInfo.InvariantCulture));
            builder.Append(FormatTime(cue.StartMilliseconds)).Append(" --> ")
                .AppendLine(FormatTime(cue.EndMilliseconds));
            builder.AppendLine(content.Replace("\r\n", "\n").Replace('\r', '\n').Replace("\n", Environment.NewLine));
            builder.AppendLine();
        }
        return builder.ToString();
    }

    private static long ParseTime(string value)
    {
        string[] parts = value.Replace('.', ',').Split(':', ',');
        if (parts.Length != 4) throw new InvalidDataException("Invalid timecode.");
        long h = long.Parse(parts[0], CultureInfo.InvariantCulture);
        long m = long.Parse(parts[1], CultureInfo.InvariantCulture);
        long s = long.Parse(parts[2], CultureInfo.InvariantCulture);
        long ms = long.Parse(parts[3], CultureInfo.InvariantCulture);
        if (m > 59 || s > 59 || ms > 999) throw new InvalidDataException("Invalid timecode.");
        return checked((((h * 60) + m) * 60 + s) * 1000 + ms);
    }

    private static string FormatTime(long milliseconds)
    {
        long h = milliseconds / 3_600_000;
        long m = (milliseconds / 60_000) % 60;
        long s = (milliseconds / 1000) % 60;
        long ms = milliseconds % 1000;
        return $"{h:00}:{m:00}:{s:00},{ms:000}";
    }
}
