using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using TeamsScribe.Helpers;

namespace TeamsScribe.Models;

enum SummarizerModel
{
    Phi4Mini,
    Phi4,
    Qwen25,
}

// Request budgets account for both context and local inference time.
static class SummarizerModels
{
    public static (string Alias, int ChunkChars) Get(SummarizerModel model) => model switch
    {
        SummarizerModel.Phi4 => ("phi-4", 40000),
        SummarizerModel.Qwen25 => ("qwen2.5-1.5b", 6000),
        _ => ("phi-4-mini", 6000),
    };
}

// User-chosen engines, persisted next to the exe so choices survive restarts.
sealed class AppSettings
{
    public SummarizerModel Summarizer { get; set; } = SummarizerModel.Phi4Mini;
    public SummarizerModel ChatModel { get; set; } = SummarizerModel.Phi4Mini;

    private static readonly string FilePath = AppDataPaths.SettingsFile;

    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() },
    };

    public static AppSettings Load()
    {
        try
        {
            if (File.Exists(FilePath))
                return JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(FilePath), Options)
                    ?? new AppSettings();
        }
        catch
        {
            // Corrupt or unreadable settings fall back to defaults.
        }

        return new AppSettings();
    }

    public void Save()
    {
        try
        {
            File.WriteAllText(FilePath, JsonSerializer.Serialize(this, Options));
        }
        catch
        {
            // Persisting settings is best-effort.
        }
    }
}
