using System.Diagnostics;
using System.Windows.Automation;
using NAudio.CoreAudioApi;
using NAudio.CoreAudioApi.Interfaces;

namespace TeamsScribe.Services;

static class TeamsDetector
{
    // A call is on when Teams has active microphone and speaker sessions, but isn't at pre-join.
    // Teams can retain a microphone session after a call ends, but its speaker session ends.
    public static bool IsInCall()
    {
        try
        {
            using var enumerator = new MMDeviceEnumerator();

            return HasActiveTeamsSession(enumerator, DataFlow.Capture)
                && HasActiveTeamsSession(enumerator, DataFlow.Render)
                && !IsOnPreJoinScreen();
        }
        catch
        {
            return false;
        }
    }

    private static bool HasActiveTeamsSession(MMDeviceEnumerator enumerator, DataFlow dataFlow)
    {
        foreach (var device in enumerator.EnumerateAudioEndPoints(dataFlow, DeviceState.Active))
        {
            using (device)
            {
                var sessions = device.AudioSessionManager.Sessions;

                for (var i = 0; i < sessions.Count; i++)
                {
                    using var session = sessions[i];

                    if (session.State != AudioSessionState.AudioSessionStateActive)
                        continue;

                    try
                    {
                        using var owner = Process.GetProcessById((int)session.GetProcessID);
                        if (owner.ProcessName.Contains("teams", StringComparison.OrdinalIgnoreCase))
                            return true;
                    }
                    catch
                    {
                    }
                }
            }
        }

        return false;
    }

    // Match by name only: the new Teams pre-join button lives in WebView2 and may not
    // surface as a Button control. Safe because this only runs when Teams audio is active.
    private static readonly Condition JoinButton = new OrCondition(
        new PropertyCondition(AutomationElement.NameProperty, "Join now"),
        new PropertyCondition(AutomationElement.NameProperty, "Join"));

    private static bool IsOnPreJoinScreen()
    {
        foreach (var process in TeamsProcesses())
        {
            using (process)
            {
                try
                {
                    if (process.MainWindowHandle != IntPtr.Zero
                        && AutomationElement.FromHandle(process.MainWindowHandle)
                            .FindFirst(TreeScope.Descendants, JoinButton) != null)
                        return true;
                }
                catch
                {
                }
            }
        }

        return false;
    }

    public static Process FindProcess()
    {
        var processes = TeamsProcesses();
        return Array.Find(processes, p => p.MainWindowHandle != IntPtr.Zero) ?? processes.FirstOrDefault();
    }

    private static Process[] TeamsProcesses() =>
        [.. Process.GetProcessesByName("ms-teams"), .. Process.GetProcessesByName("Teams")];
}
