using System.IO;

namespace TeamsScribe.Helpers;

static class AppDataPaths
{
    public static readonly string ModelsFolder = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "TeamsScribe",
        "models");
}