using System.Diagnostics;
using NAudio.CoreAudioApi;
using NAudio.CoreAudioApi.Interfaces;

namespace TeamsScribe.Services;

static class TeamsDetector
{
    // A call is on when a Teams process holds an active session on a mic (capture) device.
    public static bool IsInCall()
    {
        try
        {
            using var enumerator = new MMDeviceEnumerator();

            foreach (var device in enumerator.EnumerateAudioEndPoints(DataFlow.Capture, DeviceState.Active))
            {
                using (device)
                {
                    var sessions = device.AudioSessionManager.Sessions;

                    for (var i = 0; i < sessions.Count; i++)
                    {
                        using var session = sessions[i];

                        if (session.State == AudioSessionState.AudioSessionStateActive
                            && IsTeamsProcess((int)session.GetProcessID))
                            return true;
                    }
                }
            }
        }
        catch
        {
        }

        return false;
    }

    private static bool IsTeamsProcess(int processId)
    {
        if (processId == 0)
            return false;

        try
        {
            using var process = Process.GetProcessById(processId);
            return process.ProcessName.Contains("teams", StringComparison.OrdinalIgnoreCase);
        }
        catch
        {
            return false;
        }
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
}
