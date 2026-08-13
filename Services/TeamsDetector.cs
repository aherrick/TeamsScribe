using System.Diagnostics;
using Microsoft.Win32;

namespace TeamsScribe.Services;

static class TeamsDetector
{
    // Windows records per-app microphone usage here; a Stop time of 0 means "in use right now".
    private const string ConsentStore =
        @"SOFTWARE\Microsoft\Windows\CurrentVersion\CapabilityAccessManager\ConsentStore\microphone";

    // A call is on when Teams currently holds the microphone. This is far more stable than
    // inspecting Teams' UI, which can change or disappear while a call is still active.
    public static bool IsInCall()
    {
        try
        {
            using var root = Registry.CurrentUser.OpenSubKey(ConsentStore);

            foreach (var appKeyName in root?.GetSubKeyNames() ?? [])
            {
                using var appKey = root.OpenSubKey(appKeyName);

                if (appKey == null)
                    continue;

                if (appKeyName.Equals("NonPackaged", StringComparison.OrdinalIgnoreCase))
                {
                    foreach (var exeKeyName in appKey.GetSubKeyNames())
                    {
                        using var exeKey = appKey.OpenSubKey(exeKeyName);
                        if (IsTeamsInUse(exeKey, exeKeyName))
                            return true;
                    }
                }
                else if (IsTeamsInUse(appKey, appKeyName))
                {
                    return true;
                }
            }
        }
        catch
        {
        }

        return false;
    }

    private static bool IsTeamsInUse(RegistryKey key, string identifier)
    {
        if (key == null || !identifier.Contains("teams", StringComparison.OrdinalIgnoreCase))
            return false;

        return key.GetValue("LastUsedTimeStop") is long stop && stop == 0
            && key.GetValue("LastUsedTimeStart") is long start && start > 0;
    }

    public static Process FindProcess()
    {
        var processes = TeamsProcesses();
        return Array.Find(processes, p => p.MainWindowHandle != IntPtr.Zero) ?? processes.FirstOrDefault();
    }

    private static Process[] TeamsProcesses() =>
        [.. Process.GetProcessesByName("ms-teams"), .. Process.GetProcessesByName("Teams")];
}
