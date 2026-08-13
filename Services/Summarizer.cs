using System.IO;
using System.Text;

namespace TeamsScribe.Services;

internal sealed class Summarizer
{
    private const int MaxAttempts = 2;

    private readonly LocalChatClient _chat;
    private readonly int _chunkChars;

    public Summarizer(LocalChatClient chat, int chunkChars)
    {
        _chat = chat;
        _chunkChars = chunkChars;
    }

    public async Task SummarizeAsync(string folder, string title, DateTime start, DateTime end)
    {
        var transcript = await File.ReadAllTextAsync(Path.Combine(folder, "transcript.txt"));

        if (string.IsNullOrWhiteSpace(transcript))
            return;

        string summary;

        if (transcript.Length <= _chunkChars)
        {
            summary = await RecapAsync(transcript);
        }
        else
        {
            // Condense each chunk to notes, then recap the combined notes.
            var notes = new StringBuilder();

            foreach (var chunk in Chunk(transcript, _chunkChars))
                notes.AppendLine(await RetryAsync(() => CondenseAsync(chunk)));

            summary = await RetryAsync(() => RecapAsync(notes.ToString()));
        }

        var frontMatter = FrontMatter(title, start, end);

        await File.WriteAllTextAsync(Path.Combine(folder, "summary.md"), frontMatter + summary);

        // One artifact to paste into Teams/email: properties + recap on top, transcript below.
        var meeting = frontMatter + summary + "\n\n---\n\n## Full transcript\n\n" + transcript;
        await File.WriteAllTextAsync(Path.Combine(folder, "meeting.md"), meeting);
    }


    private static string FrontMatter(string title, DateTime start, DateTime end)
    {
        var subject = string.IsNullOrWhiteSpace(title) ? "Teams meeting" : title;
        var duration = end - start;

        var builder = new StringBuilder();
        builder.AppendLine("---");
        builder.AppendLine($"title: {subject}");
        builder.AppendLine($"date: {start:yyyy-MM-dd}");
        builder.AppendLine($"start: {start:yyyy-MM-dd HH:mm:ss}");
        builder.AppendLine($"end: {end:yyyy-MM-dd HH:mm:ss}");
        builder.AppendLine($"duration: {duration:hh\\:mm\\:ss}");
        builder.AppendLine("---");
        builder.AppendLine();
        builder.AppendLine($"# {subject}");
        builder.AppendLine();

        return builder.ToString();
    }

    private Task<string> RecapAsync(string text) =>
        ChatAsync(
            "Write concise, action-oriented Teams meeting summaries.",
            $"""
            Summarize this meeting using:

            ## TL;DR
            ## Key Points
            ## Decisions
            ## Action Items

            Transcript:
            {text}
            """);

    private Task<string> CondenseAsync(string chunk) =>
        ChatAsync(
            "You condense meeting transcript excerpts into terse factual notes.",
            "Summarize the key points, decisions, and action items in this excerpt as short bullets:\n\n" + chunk);

    private Task<string> ChatAsync(string system, string user) =>
        _chat.CompleteAsync(system, user);

    private static async Task<string> RetryAsync(Func<Task<string>> operation)
    {
        Exception lastError = null;

        for (var attempt = 1; attempt <= MaxAttempts; attempt++)
        {
            try
            {
                return await operation();
            }
            catch (Exception ex) when (attempt < MaxAttempts)
            {
                lastError = ex;
            }
        }

        throw lastError!;
    }

    private static IEnumerable<string> Chunk(string text, int size)
    {
        var builder = new StringBuilder();

        foreach (var line in text.Split('\n'))
        {
            if (builder.Length + line.Length > size && builder.Length > 0)
            {
                yield return builder.ToString();
                builder.Clear();
            }

            builder.Append(line).Append('\n');
        }

        if (builder.Length > 0)
            yield return builder.ToString();
    }
}