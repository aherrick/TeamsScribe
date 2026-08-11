namespace TeamsScribe.Models;

// A single captured Teams meeting and where its artifacts live.
sealed class Meeting(string title, string folder, DateTime start)
{
    public string Title { get; } = title;
    public string Folder { get; } = folder;
    public DateTime Start { get; } = start;
}
