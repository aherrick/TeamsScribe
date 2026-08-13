using System.Diagnostics;
using System.Windows.Automation;
using NAudio.CoreAudioApi;
using NAudio.CoreAudioApi.Interfaces;

namespace TeamsScribe.Services;

static class TeamsDetector
{
    // A call is on when Teams has active microphone and speaker sessions and exposes an in-call
    // control. Teams can use audio for notifications or device monitoring while an ordinary chat
    // is open, so audio sessions alone are not enough to identify a meeting.
    public static bool IsInCall()
    {
        try
        {
            using var enumerator = new MMDeviceEnumerator();

            return HasActiveTeamsSession(enumerator, DataFlow.Capture)
                && HasActiveTeamsSession(enumerator, DataFlow.Render)
                && IsOnCallScreen();
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

    // Match by name only: Teams' WebView2 controls do not always surface as Buttons.
    private static readonly Condition InCallButton = new OrCondition(
        new PropertyCondition(AutomationElement.NameProperty, "Leave"),
        new PropertyCondition(AutomationElement.NameProperty, "Leave meeting"),
        new PropertyCondition(AutomationElement.NameProperty, "Hang up"),
        new PropertyCondition(AutomationElement.NameProperty, "End call"));

    private static bool IsOnCallScreen()
    {
        foreach (var process in TeamsProcesses())
        {
            using (process)
            {
                try
                {
                    if (process.MainWindowHandle != IntPtr.Zero
                        && AutomationElement.FromHandle(process.MainWindowHandle)
                            .FindFirst(TreeScope.Descendants, InCallButton) != null)
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
