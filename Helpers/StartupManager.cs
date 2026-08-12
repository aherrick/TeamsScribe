using Microsoft.Win32;

namespace TeamsScribe.Helpers;

static class StartupManager
{
    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueName = "TeamsScribe";

    public static bool IsEnabled()
    {
        using var key = Registry.CurrentUser.OpenSubKey(RunKey);
        return key?.GetValue(ValueName) != null;
    }

    public static void Set(bool enabled, string updateExePath)
    {
        using var key = Registry.CurrentUser.OpenSubKey(RunKey, writable: true);

        if (key == null)
            return;

        if (enabled)
            key.SetValue(ValueName, $"\"{updateExePath}\" start");
        else
            key.DeleteValue(ValueName, throwOnMissingValue: false);
    }
}
