using System.Diagnostics;
using Microsoft.Win32;

namespace TeamsScribe;

static class TeamsDetector
{
    private const string ConsentStorePath =
        @"SOFTWARE\Microsoft\Windows\CurrentVersion\CapabilityAccessManager\ConsentStore\microphone";

    // Windows sets LastUsedTimeStop == 0 while an app is actively using the mic.
    public static bool IsInCall()
    {
        try
        {
            using var root = Registry.CurrentUser.OpenSubKey(ConsentStorePath);
            if (root == null)
                return false;

            foreach (var name in root.GetSubKeyNames())
            {
                using var key = root.OpenSubKey(name);
                if (key == null)
                    continue;

                if (name.Equals("NonPackaged", StringComparison.OrdinalIgnoreCase))
                {
                    foreach (var child in key.GetSubKeyNames())
                    {
                        using var childKey = key.OpenSubKey(child);
                        if (childKey != null && IsTeamsUsingMic(childKey, child))
                            return true;
                    }
                }
                else if (IsTeamsUsingMic(key, name))
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

    public static Process FindProcess()
    {
        var processes = Process.GetProcessesByName("ms-teams")
            .Concat(Process.GetProcessesByName("Teams"))
            .ToArray();

        foreach (var process in processes)
        {
            try
            {
                if (process.MainWindowHandle != IntPtr.Zero)
                    return process;
            }
            catch
            {
            }
        }

        return processes.FirstOrDefault();
    }

    private static bool IsTeamsUsingMic(RegistryKey key, string name)
    {
        return name.Contains("teams", StringComparison.OrdinalIgnoreCase)
            && key.GetValue("LastUsedTimeStop") is long stop && stop == 0
            && key.GetValue("LastUsedTimeStart") is long start && start > 0;
    }
}
