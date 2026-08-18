using System.IO;
using System.Text.RegularExpressions;

namespace TeamsScribe.Helpers;

// Turns a meeting title into a filesystem-safe folder name.
static partial class MeetingNaming
{
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
