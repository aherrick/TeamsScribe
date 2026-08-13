using Betalgo.Ranul.OpenAI.ObjectModels.RequestModels;
using Microsoft.AI.Foundry.Local;
using Microsoft.Extensions.Logging.Abstractions;

namespace TeamsScribe.Services;

// Generic Foundry Local chat client for one-off prompts and multi-turn conversations.
internal sealed class LocalChatClient
{
    private static readonly SemaphoreSlim ManagerGate = new(1, 1);
    private static bool _managerReady;

    private readonly string _alias;
    private IModel _model;

    public LocalChatClient(string alias) => _alias = alias;

    public async Task EnsureModelAsync(Action<float> downloadProgress = null)
    {
        if (_model != null)
            return;

        if (!_managerReady)
        {
            await ManagerGate.WaitAsync();

            try
            {
                if (!_managerReady)
                {
                    await FoundryLocalManager.CreateAsync(
                        new Configuration { AppName = "TeamsScribe" },
                        NullLogger.Instance);

                    _managerReady = true;
                }
            }
            finally
            {
                ManagerGate.Release();
            }
        }

        var catalog = await FoundryLocalManager.Instance.GetCatalogAsync();
        _model = await catalog.GetModelAsync(_alias)
            ?? throw new Exception($"Model '{_alias}' not found.");

        await _model.DownloadAsync(downloadProgress);
        await _model.LoadAsync();
    }

    public Task<string> CompleteAsync(string system, string user) =>
        CompleteAsync([("system", system), ("user", user)]);

    public IAsyncEnumerable<string> CompleteStreamingAsync(
        string system,
        string user,
        CancellationToken cancellationToken = default) =>
        CompleteStreamingAsync([("system", system), ("user", user)], cancellationToken);

    public async Task<string> CompleteAsync(IReadOnlyList<(string Role, string Content)> conversation)
    {
        await EnsureModelAsync();

        var chat = await _model.GetChatClientAsync();
        var messages = conversation
            .Select(turn => new ChatMessage { Role = turn.Role, Content = turn.Content })
            .ToList();

        var response = await chat.CompleteChatAsync(messages);
        return response.Choices[0].Message.Content;
    }

    public async IAsyncEnumerable<string> CompleteStreamingAsync(
        IReadOnlyList<(string Role, string Content)> conversation,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        await EnsureModelAsync();

        var chat = await _model.GetChatClientAsync();
        var messages = conversation
            .Select(turn => new ChatMessage { Role = turn.Role, Content = turn.Content })
            .ToList();

        await foreach (var response in chat.CompleteChatStreamingAsync(messages, cancellationToken))
        {
            var content = response.Choices[0].Delta.Content;

            if (!string.IsNullOrEmpty(content))
                yield return content;
        }
    }
}
