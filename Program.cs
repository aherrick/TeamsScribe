using TeamsScribe.Helpers;
using TeamsScribe.Models;
using TeamsScribe.Services;

const string RecordingsFolder = "recordings";

Directory.CreateDirectory(RecordingsFolder);

await Transcriber.EnsureModelAsync();
await Summarizer.EnsureModelAsync();

using var cts = new CancellationTokenSource();
Console.CancelKeyPress += (_, e) =>
{
    e.Cancel = true; // unwind cleanly instead of killing the process
    cts.Cancel();
};

Console.WriteLine("Watching for Teams meetings...");
Console.WriteLine("Press Ctrl+C to quit.");

Recorder recorder = null;
Meeting meeting = null;
int misses = 0;

while (!cts.IsCancellationRequested)
{
    var inCall = TeamsDetector.IsInCall();

    if (inCall && recorder == null)
    {
        var teams = TeamsDetector.FindProcess();

        if (teams != null)
        {
            var start = DateTime.Now;
            var title = MeetingNaming.Title(teams);
            var folder = Path.Combine(RecordingsFolder, MeetingNaming.FolderName(start, title));
            Directory.CreateDirectory(folder);

            meeting = new Meeting(title, folder, start);

            recorder = new Recorder();
            await recorder.StartAsync(folder, teams.Id);

            Console.WriteLine("Teams meeting detected. Recording...");
        }
    }

    if (inCall)
    {
        misses = 0;
    }
    else if (recorder != null && ++misses >= 5) // ~10s of silence = meeting ended
    {
        await ProcessMeetingAsync(recorder, meeting);
        recorder = null;

        Console.WriteLine();
        Console.WriteLine("Watching for Teams meetings...");
    }

    try
    {
        await Task.Delay(2000, cts.Token);
    }
    catch (OperationCanceledException)
    {
    }
}

// Don't lose a meeting that was still recording when quitting.
if (recorder != null)
{
    Console.WriteLine("Quitting - finishing current recording...");
    await ProcessMeetingAsync(recorder, meeting);
}

static async Task ProcessMeetingAsync(Recorder recorder, Meeting meeting)
{
    const int MinMeetingSeconds = 30;

    try
    {
        await recorder.StopAsync();

        var end = DateTime.Now;
        var duration = end - meeting.Start;

        if (duration.TotalSeconds < MinMeetingSeconds)
        {
            Console.WriteLine($"Skipping short meeting ({duration.TotalSeconds:F0}s).");
            MeetingNaming.TryDelete(meeting.Folder);
            return;
        }

        Console.WriteLine("Meeting ended. Transcribing...");
        await Transcriber.TranscribeAsync(meeting.Folder);
        await Summarizer.SummarizeAsync(meeting.Folder, meeting.Title, meeting.Start, end);
        Console.WriteLine("Done: " + meeting.Folder);
    }
    catch (Exception ex)
    {
        Console.WriteLine("Failed to process meeting: " + ex.Message);
    }
}
