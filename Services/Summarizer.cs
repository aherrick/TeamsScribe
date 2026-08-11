using System.Text;
using Betalgo.Ranul.OpenAI.ObjectModels.RequestModels;
using Microsoft.AI.Foundry.Local;
using Microsoft.Extensions.Logging.Abstractions;

namespace TeamsScribe.Services;

internal static class Summarizer
{
    private const string Alias = "phi-4-mini";

    // Keep well under the model's context window; long meetings get chunked.
    private const int MaxChars = 12000;

    private static IModel _model;

    public static async Task EnsureModelAsync()
    {
        if (_model != null)
            return;

        await FoundryLocalManager.CreateAsync(
            new Configuration { AppName = "TeamsScribe" },
            NullLogger.Instance
        );

        var catalog = await FoundryLocalManager.Instance.GetCatalogAsync();

        _model =
            await catalog.GetModelAsync(Alias)
            ?? throw new Exception($"Model '{Alias}' not found.");

        await _model.DownloadAsync();
        await _model.LoadAsync();
    }

    public static async Task SummarizeAsync(string folder, string title, DateTime start, DateTime end)
    {
        await EnsureModelAsync();

        var transcript = await File.ReadAllTextAsync(Path.Combine(folder, "transcript.txt"));

        if (string.IsNullOrWhiteSpace(transcript))
            return;

        string summary;

        if (transcript.Length <= MaxChars)
        {
            summary = await RecapAsync(transcript);
        }
        else
        {
            // Condense each chunk to notes, then recap the combined notes.
            var notes = new StringBuilder();

            foreach (var chunk in Chunk(transcript, MaxChars))
                notes.AppendLine(await CondenseAsync(chunk));

            summary = await RecapAsync(notes.ToString());
        }

        var frontMatter = FrontMatter(title, start, end);

        await File.WriteAllTextAsync(Path.Combine(folder, "summary.md"), frontMatter + summary);

        // One artifact to paste into Teams/email: properties + recap on top, transcript below.
        var meeting = frontMatter + summary + "\n\n---\n\n## Full transcript\n\n" + transcript;
        await File.WriteAllTextAsync(Path.Combine(folder, "meeting.md"), meeting);

        Console.WriteLine(summary);
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

    private static Task<string> RecapAsync(string text) =>
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

    private static Task<string> CondenseAsync(string chunk) =>
        ChatAsync(
            "You condense meeting transcript excerpts into terse factual notes.",
            "Summarize the key points, decisions, and action items in this excerpt as short bullets:\n\n" + chunk);

    private static async Task<string> ChatAsync(string system, string user)
    {
        var chat = await _model.GetChatClientAsync();

        var messages = new List<ChatMessage>
        {
            new() { Role = "system", Content = system },
            new() { Role = "user", Content = user },
        };

        var response = await chat.CompleteChatAsync(messages);

        return response.Choices[0].Message.Content;
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