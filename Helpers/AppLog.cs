namespace TeamsScribe.Helpers;

static class AppLog
{
    private static readonly object Gate = new();
    private static readonly string LogPath = Path.Combine(AppContext.BaseDirectory, "logs", "TeamsScribe.log");

    public static string Folder => Path.GetDirectoryName(LogPath)!;

    public static void Write(string message, Exception exception = null)
    {
        try
        {
            lock (Gate)
            {
                Directory.CreateDirectory(Folder);
                File.AppendAllText(LogPath, $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} {message}{Environment.NewLine}");

                if (exception != null)
                    File.AppendAllText(LogPath, exception + Environment.NewLine);
            }
        }
        catch
        {
        }
    }
}
