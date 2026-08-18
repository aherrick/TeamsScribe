using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Win32;

namespace TeamsScribe.Services;

// A call is on when Teams currently holds the microphone. This is far more stable than
// inspecting Teams' UI, which can change or disappear while a call is still active.
static class TeamsDetector
{
    // Windows records per-app microphone usage here; a Stop time of 0 means "in use right now".
    private const string ConsentStore =
        @"SOFTWARE\Microsoft\Windows\CurrentVersion\CapabilityAccessManager\ConsentStore\microphone";

    private const string NonPackagedKey = "NonPackaged";
    private const uint GW_OWNER = 4;

    private static readonly string[] ProcessNames = ["ms-teams", "Teams"];

    // Matched exactly against the executable / package family name, so unrelated apps that merely
    // have "teams" in their path (TeamsScribe itself, above all) are never mistaken for Teams.
    private static readonly string[] Executables = ["ms-teams.exe", "Teams.exe"];
    private static readonly string[] PackagePrefixes = ["MSTeams_", "MicrosoftTeams_"];

    // Hub windows that stay open outside of calls; never useful as a meeting name.
    private static readonly string[] GenericWindows =
    [
        "Microsoft Teams", "Teams", "Chat", "Calendar", "Activity", "Calls",
        "Files", "Apps", "Communities", "Search", "Settings",
    ];

    public static bool IsInCall() => HoldsMicrophone() && IsRunning();

    public static bool IsRunning() => TeamsProcessIds().Count > 0;

    // Friendly name of the current meeting window, or null when only hub windows are open.
    public static string MeetingTitle()
    {
        var teams = TeamsProcessIds();

        if (teams.Count == 0)
            return null;

        string title = null;
        var text = new StringBuilder(512);

        EnumWindows(
            (window, _) =>
            {
                if (!IsWindowVisible(window) || GetWindow(window, GW_OWNER) != IntPtr.Zero)
                    return true;

                GetWindowThreadProcessId(window, out var processId);

                if (!teams.Contains((int)processId) || GetWindowText(window, text, text.Capacity) == 0)
                    return true;

                title = MeetingName(text.ToString());
                text.Clear();

                return title == null; // stop at the first window that looks like a meeting
            },
            IntPtr.Zero);

        return title;
    }

    // Teams titles look like "Meeting name | Microsoft Teams".
    private static string MeetingName(string windowText)
    {
        var name = windowText.Split('|')[0].Trim();

        return name.Length == 0 || GenericWindows.Contains(name, StringComparer.OrdinalIgnoreCase)
            ? null
            : name;
    }

    private static HashSet<int> TeamsProcessIds()
    {
        HashSet<int> ids = [];

        foreach (var name in ProcessNames)
        {
            foreach (var process in Process.GetProcessesByName(name))
            {
                using (process)
                    ids.Add(process.Id);
            }
        }

        return ids;
    }

    private static bool HoldsMicrophone()
    {
        try
        {
            using var store = Registry.CurrentUser.OpenSubKey(ConsentStore);

            foreach (var name in store?.GetSubKeyNames() ?? [])
            {
                using var key = store.OpenSubKey(name);

                if (key == null)
                    continue;

                if (name.Equals(NonPackagedKey, StringComparison.OrdinalIgnoreCase))
                {
                    foreach (var exeName in key.GetSubKeyNames())
                    {
                        if (!IsTeamsExecutable(exeName))
                            continue;

                        using var exeKey = key.OpenSubKey(exeName);

                        if (InUse(exeKey))
                            return true;
                    }
                }
                else if (IsTeamsPackage(name) && InUse(key))
                {
                    return true;
                }
            }
        }
        catch
        {
            // A registry read failure just means "no call detected" on this tick.
        }

        return false;
    }

    // NonPackaged subkeys are full executable paths with '#' in place of '\'.
    private static bool IsTeamsExecutable(string keyName) =>
        Executables.Contains(keyName[(keyName.LastIndexOf('#') + 1)..], StringComparer.OrdinalIgnoreCase);

    private static bool IsTeamsPackage(string keyName) =>
        PackagePrefixes.Any(prefix => keyName.StartsWith(prefix, StringComparison.OrdinalIgnoreCase));

    private static bool InUse(RegistryKey key) =>
        key?.GetValue("LastUsedTimeStart") is long start && start > 0
        && key.GetValue("LastUsedTimeStop") is long stop && stop == 0;

    private delegate bool EnumWindowsProc(IntPtr window, IntPtr parameter);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool EnumWindows(EnumWindowsProc callback, IntPtr parameter);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetWindowText(IntPtr window, StringBuilder text, int maxLength);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsWindowVisible(IntPtr window);

    [DllImport("user32.dll")]
    private static extern IntPtr GetWindow(IntPtr window, uint command);

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr window, out uint processId);
}
