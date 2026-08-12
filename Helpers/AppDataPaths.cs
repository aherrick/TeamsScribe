using System.IO;

namespace TeamsScribe.Helpers;

static class AppDataPaths
{
    // Velopack root (parent of the versioned "current" install dir), so data survives app updates.
    private static readonly string Root = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "TeamsScribe");

    public static readonly string ModelsFolder = Path.Combine(Root, "models");
    public static readonly string MeetingsFolder = Path.Combine(Root, "meetings");
    public static readonly string LogsFolder = Path.Combine(Root, "logs");
    public static readonly string SettingsFile = Path.Combine(Root, "settings.json");
}