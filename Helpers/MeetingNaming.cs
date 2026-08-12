using System.Diagnostics;
using System.IO;
using System.Text.RegularExpressions;

namespace TeamsScribe.Helpers;

// Derives friendly meeting titles and filesystem-safe folder names.
static partial class MeetingNaming
{
    // Human-readable meeting title from the Teams window, or null if unavailable.
    public static string Title(Process teams)
    {
        string title = null;
        try { title = teams.MainWindowTitle; } catch { }

        if (string.IsNullOrWhiteSpace(title))
            return null;

        // Teams titles look like "Meeting name | Microsoft Teams".
        title = title.Split('|')[0].Trim();

        return string.IsNullOrWhiteSpace(title)
            || title.Equals("Microsoft Teams", StringComparison.OrdinalIgnoreCase)
            ? null
            : title;
    }

    public static string FolderName(DateTime start, string title)
    {
        var stamp = start.ToString("yyyy-MM-dd_HH-mm-ss");

        if (string.IsNullOrWhiteSpace(title))
            return stamp;

        var slug = InvalidFolderNameChars().Replace(title, "").Trim().Replace(' ', '-');

        if (slug.Length > 50)
            slug = slug[..50];

        return string.IsNullOrWhiteSpace(slug) ? stamp : $"{stamp}_{slug}";
    }

    public static void TryDelete(string folder)
    {
        try
        {
            if (Directory.Exists(folder))
                Directory.Delete(folder, true);
        }
        catch
        {
        }
    }

    [GeneratedRegex(@"[^\w\- ]")]
    private static partial Regex InvalidFolderNameChars();
}
