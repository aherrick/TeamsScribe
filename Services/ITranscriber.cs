using TeamsScribe.Models;

namespace TeamsScribe.Services;

// A swappable speech-to-text engine. Each implementation writes transcript.txt.
interface ITranscriber
{
    Task EnsureModelAsync();
    Task TranscribeAsync(string folder);
}

// Shared writer so every engine emits the same timestamped, speaker-labeled format.
static class Transcript
{
    public static async Task WriteAsync(string folder, IEnumerable<TranscriptSegment> segments)
    {
        var ordered = segments.OrderBy(s => s.Start).ToList();

        using var output = new StreamWriter(Path.Combine(folder, "transcript.txt"));

        foreach (var segment in ordered)
            await output.WriteLineAsync(
                $"[{segment.Start:hh\\:mm\\:ss}] {segment.Speaker}: {segment.Text}");
    }
}
