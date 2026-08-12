using Whisper.net;
using Whisper.net.Ggml;
using TeamsScribe.Helpers;
using TeamsScribe.Models;

namespace TeamsScribe.Services;

// Whisper (ggml-base-en) via Whisper.net — proven default, splits each track into utterances.
sealed class WhisperTranscriber : ITranscriber
{
    private const string ModelName = "ggml-base-en.bin";

    private static readonly string ModelDir =
        Path.Combine(AppDataPaths.ModelsFolder, "whisper-ggml-base-en");

    private static readonly string ModelFile =
        Path.Combine(ModelDir, ModelName);

    public async Task EnsureModelAsync()
    {
        await ModelInstaller.EnsureAsync(
            ModelDir,
            [ModelName],
            _ => WhisperGgmlDownloader.Default.GetGgmlModelAsync(GgmlType.BaseEn));
    }

    public async Task TranscribeAsync(string folder)
    {
        await EnsureModelAsync();

        using var factory = WhisperFactory.FromPath(ModelFile);
        using var processor = factory.CreateBuilder().WithLanguage("en").Build();

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

        await Transcript.WriteAsync(folder, segments);
    }
}
