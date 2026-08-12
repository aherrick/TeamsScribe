using NAudio.Wave;
using SherpaOnnx;
using TeamsScribe.Helpers;
using TeamsScribe.Models;

namespace TeamsScribe.Services;

// NVIDIA Parakeet TDT 0.6B v3 (INT8) via Sherpa-ONNX. Offline transducer, CPU.
sealed class ParakeetTranscriber : ITranscriber
{
    private const string ModelBaseUrl =
        "https://huggingface.co/csukuangfj/sherpa-onnx-nemo-parakeet-tdt-0.6b-v3-int8/resolve/main/";

    private static readonly string[] ModelFiles =
        ["encoder.int8.onnx", "decoder.int8.onnx", "joiner.int8.onnx", "tokens.txt"];

    private static readonly string ModelDir =
        Path.Combine(AppDataPaths.ModelsFolder, "parakeet-nemo-tdt-0.6b-v3-int8");

    private static readonly HttpClient Http = new() { Timeout = Timeout.InfiniteTimeSpan };
    private OfflineRecognizer _recognizer;

    public async Task EnsureModelAsync()
    {
        await ModelInstaller.EnsureAsync(
            ModelDir,
            ModelFiles,
            name => Http.GetStreamAsync(ModelBaseUrl + name + "?download=true"));

        if (_recognizer != null)
            return;

        var config = new OfflineRecognizerConfig();
        config.FeatConfig.SampleRate = 16000;
        config.FeatConfig.FeatureDim = 80;
        config.ModelConfig.Tokens = Path.Combine(ModelDir, "tokens.txt");
        config.ModelConfig.Transducer.Encoder = Path.Combine(ModelDir, "encoder.int8.onnx");
        config.ModelConfig.Transducer.Decoder = Path.Combine(ModelDir, "decoder.int8.onnx");
        config.ModelConfig.Transducer.Joiner = Path.Combine(ModelDir, "joiner.int8.onnx");
        config.ModelConfig.ModelType = "nemo_transducer";
        config.ModelConfig.NumThreads = Math.Max(1, Environment.ProcessorCount / 2);
        config.ModelConfig.Debug = 0;

        _recognizer = new OfflineRecognizer(config);
    }

    public async Task TranscribeAsync(string folder)
    {
        await EnsureModelAsync();

        var segments = new List<TranscriptSegment>();

        // Parakeet decodes a whole track at once, so each speaker yields one block.
        foreach (var file in new[] { Tracks.Me, Tracks.Participants })
        {
            var path = Path.Combine(folder, file);

            if (!File.Exists(path))
                continue;

            var speaker = Path.GetFileNameWithoutExtension(file);
            var (sampleRate, samples) = ReadWave(path);

            using var stream = _recognizer.CreateStream();
            stream.AcceptWaveform(sampleRate, samples);
            _recognizer.Decode(stream);

            var text = stream.Result.Text?.Trim();

            if (!string.IsNullOrEmpty(text))
            {
                var start = stream.Result.Timestamps is { Length: > 0 } timestamps
                    ? TimeSpan.FromSeconds(timestamps[0])
                    : TimeSpan.Zero;

                segments.Add(new TranscriptSegment(start, speaker, text));
            }
        }

        await Transcript.WriteAsync(folder, segments);
    }

    // Sherpa expects mono float samples in [-1, 1]; our tracks are 16 kHz mono 16-bit PCM.
    private static (int SampleRate, float[] Samples) ReadWave(string path)
    {
        using var reader = new WaveFileReader(path);

        var samples = new List<float>();
        var frame = new byte[reader.WaveFormat.AverageBytesPerSecond];

        int bytes;
        while ((bytes = reader.Read(frame, 0, frame.Length)) > 0)
        {
            for (var i = 0; i + 1 < bytes; i += 2)
                samples.Add(BitConverter.ToInt16(frame, i) / 32768f);
        }

        return (reader.WaveFormat.SampleRate, samples.ToArray());
    }
}
