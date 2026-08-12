using Velopack.Windows;

namespace TeamsScribe.Helpers;

static class StartupManager
{
    private static readonly Shortcuts Shortcuts = new();

    public static bool IsEnabled() =>
        Shortcuts.FindShortcuts(Path.GetFileName(Environment.ProcessPath)!, ShortcutLocation.Startup).Any();

    public static void Set(bool enabled)
    {
        if (enabled)
            Shortcuts.CreateShortcutForThisExe(ShortcutLocation.Startup);
        else
            Shortcuts.RemoveShortcutForThisExe(ShortcutLocation.Startup);
    }
}
