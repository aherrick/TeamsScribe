using System.Diagnostics;
using System.Text.RegularExpressions;
using TeamsScribe;

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
string meetingFolder = null;
string meetingTitle = null;
DateTime meetingStart = default;
int misses = 0;

while (!cts.IsCancellationRequested)
{
    var inCall = TeamsDetector.IsInCall();

    if (inCall && recorder == null)
    {
        var teams = TeamsDetector.FindProcess();

        if (teams != null)
        {
            meetingStart = DateTime.Now;
            meetingTitle = MeetingTitle(teams);
            meetingFolder = Path.Combine(RecordingsFolder, MeetingName(meetingStart, meetingTitle));
            Directory.CreateDirectory(meetingFolder);

            recorder = new Recorder();
            await recorder.StartAsync(meetingFolder, teams.Id);

            Console.WriteLine("Teams meeting detected. Recording...");
        }
    }

    if (inCall)
    {
        misses = 0;
    }
    else if (recorder != null && ++misses >= 5) // ~10s of silence = meeting ended
    {
        await ProcessMeetingAsync(recorder, meetingFolder, meetingTitle, meetingStart);
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
    await ProcessMeetingAsync(recorder, meetingFolder, meetingTitle, meetingStart);
}

static async Task ProcessMeetingAsync(Recorder recorder, string folder, string title, DateTime start)
{
    const int MinMeetingSeconds = 30;

    try
    {
        await recorder.StopAsync();

        var end = DateTime.Now;
        var duration = end - start;

        if (duration.TotalSeconds < MinMeetingSeconds)
        {
            Console.WriteLine($"Skipping short meeting ({duration.TotalSeconds:F0}s).");
            TryDelete(folder);
            return;
        }

        Console.WriteLine("Meeting ended. Transcribing...");
        await Transcriber.TranscribeAsync(folder);
        await Summarizer.SummarizeAsync(folder, title, start, end);
        Console.WriteLine("Done: " + folder);
    }
    catch (Exception ex)
    {
        Console.WriteLine("Failed to process meeting: " + ex.Message);
    }
}

// Human-readable meeting title from the Teams window, or null if unavailable.
static string MeetingTitle(Process teams)
{
    string title = null;
    try { title = teams.MainWindowTitle; } catch { }

    if (string.IsNullOrWhiteSpace(title))
        return null;

    // Teams titles look like "Meeting name | Microsoft Teams".
    title = title.Split('|')[0].Trim();

    return string.IsNullOrWhiteSpace(title)
        || title.Equals("Microsoft Teams", StringComparison.OrdinalIgnoreCase)
        ? null
        : title;
}

static string MeetingName(DateTime start, string title)
{
    var stamp = start.ToString("yyyy-MM-dd_HH-mm-ss");

    if (string.IsNullOrWhiteSpace(title))
        return stamp;

    var slug = Regex.Replace(title, @"[^\w\- ]", "").Trim().Replace(' ', '-');

    if (slug.Length > 50)
        slug = slug[..50];

    return string.IsNullOrWhiteSpace(slug) ? stamp : $"{stamp}_{slug}";
}

static void TryDelete(string folder)
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
