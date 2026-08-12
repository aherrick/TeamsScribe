using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace TeamsScribe.Models;

enum SummarizerModel
{
    Phi4Mini,
    Qwen25,
}

// User-chosen engines, persisted next to the exe so choices survive restarts.
sealed class AppSettings
{
    public SummarizerModel Summarizer { get; set; } = SummarizerModel.Phi4Mini;
    public SummarizerModel ChatModel { get; set; } = SummarizerModel.Phi4Mini;

    private static readonly string FilePath =
        Path.Combine(AppContext.BaseDirectory, "settings.json");

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
