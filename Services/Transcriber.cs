using Whisper.net;
using Whisper.net.Ggml;
using TeamsScribe.Models;

namespace TeamsScribe.Services;

static class Transcriber
{
    private const string ModelFile = "ggml-base-en.bin";

    public static async Task EnsureModelAsync()
    {
        if (File.Exists(ModelFile))
            return;

        Console.WriteLine("Downloading Whisper model...");

        using var model = await WhisperGgmlDownloader.Default.GetGgmlModelAsync(GgmlType.BaseEn);
        using var file = File.Create(ModelFile);
        await model.CopyToAsync(file);
    }

    public static async Task TranscribeAsync(string folder)
    {
        using var factory = WhisperFactory.FromPath(ModelFile);
        using var processor = factory.CreateBuilder().WithLanguage("en").Build();

        var segments = await CollectTracks(processor, folder);

        using var output = new StreamWriter(Path.Combine(folder, "transcript.txt"));

        foreach (var segment in segments)
        {
            var line = $"[{segment.Start:hh\\:mm\\:ss}] {segment.Speaker}: {segment.Text}";
            Console.WriteLine(line);
            await output.WriteLineAsync(line);
        }
    }

    private static async Task<List<TranscriptSegment>> CollectTracks(
        WhisperProcessor processor, string folder)
    {
        var segments = new List<TranscriptSegment>();

        foreach (var file in new[] { Tracks.Me, Tracks.Participants })
        {
            var path = Path.Combine(folder, file);

            if (!File.Exists(path))
                continue;

            var speaker = Path.GetFileNameWithoutExtension(file);

            using var audio = File.OpenRead(path);

            await foreach (var segment in processor.ProcessAsync(audio))
            {
                var text = segment.Text.Trim();

                if (text.Length > 0)
                    segments.Add(new TranscriptSegment(segment.Start, speaker, text));
            }
        }

        segments.Sort((a, b) => a.Start.CompareTo(b.Start));
        return segments;
    }
}
