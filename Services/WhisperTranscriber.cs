using System.IO;
using Whisper.net;
using Whisper.net.Ggml;
using TeamsScribe.Helpers;
using TeamsScribe.Models;

namespace TeamsScribe.Services;

// Whisper via Whisper.net — splits each track into timestamped utterances.
sealed class WhisperTranscriber
{
    private const string ModelName = "model.bin";

    private readonly GgmlType _type;
    private readonly QuantizationType _quantization;
    private readonly string _modelDir;

    public WhisperTranscriber(WhisperModel model)
    {
        // English-only weights where they exist; the large models have no .en variant but
        // still beat them. Medium is skipped: turbo is more accurate and faster.
        (_type, _quantization) = model switch
        {
            WhisperModel.Balanced => (GgmlType.SmallEn, QuantizationType.NoQuantization),
            WhisperModel.Accurate => (GgmlType.LargeV3Turbo, QuantizationType.Q5_0),
            WhisperModel.MostAccurate => (GgmlType.LargeV3, QuantizationType.Q5_0),
            _ => (GgmlType.BaseEn, QuantizationType.NoQuantization),
        };

        _modelDir = Path.Combine(AppDataPaths.ModelsFolder, $"whisper-{model}".ToLowerInvariant());
    }

    public async Task EnsureModelAsync()
    {
        await ModelInstaller.EnsureAsync(
            _modelDir,
            [ModelName],
            _ => WhisperGgmlDownloader.Default.GetGgmlModelAsync(_type, _quantization));
    }

    public async Task TranscribeAsync(string folder)
    {
        await EnsureModelAsync();

        using var factory = WhisperFactory.FromPath(Path.Combine(_modelDir, ModelName));
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

        using var output = new StreamWriter(Path.Combine(folder, "transcript.txt"));

        foreach (var segment in segments.OrderBy(s => s.Start))
            await output.WriteLineAsync($"[{segment.Start:hh\\:mm\\:ss}] {segment.Speaker}: {segment.Text}");
    }
}
