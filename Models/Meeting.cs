namespace TeamsScribe.Models;

// A single captured Teams meeting and where its artifacts live.
sealed record Meeting(string Title, string Folder, DateTime Start);
