using System.Diagnostics;
using TeamsScribe.Helpers;
using TeamsScribe.Models;
using TeamsScribe.Services;
using Velopack;
using Velopack.Sources;

namespace TeamsScribe;

// Runs headless in the system tray: watches for Teams calls, records, transcribes, summarizes.
sealed class TrayAppContext : ApplicationContext
{
    private const string RepoUrl = "https://github.com/aherrick/TeamsScribe";
    private const int MinMeetingSeconds = 30;
    private static readonly string AppVersion = typeof(TrayAppContext).Assembly.GetName().Version?.ToString(3) ?? "0.0.0";

    private static readonly string RecordingsFolder =
        Path.Combine(AppContext.BaseDirectory, "recordings");

    private readonly NotifyIcon _tray;
    private readonly Control _marshal = new();
    private readonly AppSettings _settings;
    private readonly CancellationTokenSource _cts = new();
    private readonly Task _watchLoop;
    private ToolStripMenuItem _statusItem;
    private ChatForm _chatForm;

    private readonly Dictionary<TranscriberEngine, ITranscriber> _transcribers = [];
    private readonly Dictionary<SummarizerModel, LocalChatClient> _chatClients = [];
    private readonly Dictionary<SummarizerModel, Summarizer> _summarizers = [];

    public TrayAppContext()
    {
        Directory.CreateDirectory(RecordingsFolder);
        _ = _marshal.Handle; // force handle creation on the UI thread
        _settings = AppSettings.Load();

        _tray = new NotifyIcon
        {
            Icon = SystemIcons.Application,
            Text = "TeamsScribe",
            Visible = true,
            ContextMenuStrip = BuildMenu(),
        };

        _ = CheckForUpdatesAsync();
        _watchLoop = Task.Run(async () =>
        {
            await DownloadDefaultModelsAsync();
            await WatchAsync(_cts.Token);
        });
    }

    private ContextMenuStrip BuildMenu()
    {
        var menu = new ContextMenuStrip();

        menu.Items.Add($"TeamsScribe v{AppVersion}", null, (_, _) => OpenRepo());
        _statusItem = new ToolStripMenuItem("Starting up...") { Enabled = false };
        menu.Items.Add(_statusItem);
        menu.Items.Add(new ToolStripSeparator());

        var transcriber = new ToolStripMenuItem("Transcriber");
        transcriber.DropDownItems.Add(TranscriberItem("Parakeet", TranscriberEngine.Parakeet));
        transcriber.DropDownItems.Add(TranscriberItem("Whisper", TranscriberEngine.Whisper));

        var summarizer = new ToolStripMenuItem("Summarizer");
        summarizer.DropDownItems.Add(SummarizerItem("Phi-4 Mini", SummarizerModel.Phi4Mini));
        summarizer.DropDownItems.Add(SummarizerItem("Qwen2.5 1.5B", SummarizerModel.Qwen25));

        var chatModel = new ToolStripMenuItem("Chat Model");
        chatModel.DropDownItems.Add(ChatModelItem("Phi-4 Mini", SummarizerModel.Phi4Mini));
        chatModel.DropDownItems.Add(ChatModelItem("Qwen2.5 1.5B", SummarizerModel.Qwen25));

        menu.Items.Add(transcriber);
        menu.Items.Add(summarizer);
        menu.Items.Add(chatModel);
        menu.Items.Add("Chat", null, (_, _) => OpenChat());

        var startup = new ToolStripMenuItem("Run at Startup")
        {
            CheckOnClick = true,
            Checked = StartupManager.IsEnabled(),
        };
        startup.Click += (_, _) => StartupManager.Set(startup.Checked);
        menu.Items.Add(startup);

        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("Exit", null, Exit);

        return menu;
    }

    private async Task CheckForUpdatesAsync()
    {
        try
        {
            var updater = new UpdateManager(new GithubSource(RepoUrl, null, false));

            var newVersion = await updater.CheckForUpdatesAsync();
            if (newVersion == null)
                return;

            if (MessageBox.Show(
                    "A new version of TeamsScribe is available. Download and install it now?",
                    "TeamsScribe update",
                    MessageBoxButtons.YesNo,
                    MessageBoxIcon.Information) != DialogResult.Yes)
                return;

            await updater.DownloadUpdatesAsync(newVersion);
            updater.ApplyUpdatesAndRestart(newVersion);
        }
        catch
        {
            // Update checks must not interfere with recording.
        }
    }

    private ToolStripMenuItem TranscriberItem(string text, TranscriberEngine engine)
    {
        var item = new ToolStripMenuItem(text) { Checked = _settings.Transcriber == engine };

        item.Click += (_, _) =>
        {
            _settings.Transcriber = engine;
            _settings.Save();

            foreach (ToolStripMenuItem sibling in item.Owner.Items)
                sibling.Checked = sibling == item;

            WarmUpModel(text, () => GetTranscriber(engine).EnsureModelAsync());
        };

        return item;
    }

    private ToolStripMenuItem SummarizerItem(string text, SummarizerModel model)
    {
        var item = new ToolStripMenuItem(text) { Checked = _settings.Summarizer == model };

        item.Click += (_, _) =>
        {
            _settings.Summarizer = model;
            _settings.Save();

            foreach (ToolStripMenuItem sibling in item.Owner.Items)
                sibling.Checked = sibling == item;

            WarmUpModel(text, () => GetChatClient(model).EnsureModelAsync());
        };

        return item;
    }

    private ToolStripMenuItem ChatModelItem(string text, SummarizerModel model)
    {
        var item = new ToolStripMenuItem(text) { Checked = _settings.ChatModel == model };

        item.Click += (_, _) =>
        {
            _settings.ChatModel = model;
            _settings.Save();

            foreach (ToolStripMenuItem sibling in item.Owner.Items)
                sibling.Checked = sibling == item;

            WarmUpModel(text, () => GetChatClient(model).EnsureModelAsync());
        };

        return item;
    }

    private ITranscriber GetTranscriber(TranscriberEngine engine) =>
        _transcribers.TryGetValue(engine, out var t)
            ? t
            : _transcribers[engine] = engine switch
            {
                TranscriberEngine.Whisper => new WhisperTranscriber(),
                _ => new ParakeetTranscriber(),
            };

    private LocalChatClient GetChatClient(SummarizerModel model) =>
        _chatClients.TryGetValue(model, out var client)
            ? client
            : _chatClients[model] = new LocalChatClient(model switch
            {
                SummarizerModel.Qwen25 => "qwen2.5-1.5b",
                _ => "phi-4-mini",
            });

    private Summarizer GetSummarizer(SummarizerModel model) =>
        _summarizers.TryGetValue(model, out var summarizer)
            ? summarizer
            : _summarizers[model] = new Summarizer(GetChatClient(model));

    private async Task DownloadDefaultModelsAsync()
    {
        SetStatus("Downloading models...");

        try
        {
            await Task.WhenAll(
                GetTranscriber(TranscriberEngine.Parakeet).EnsureModelAsync(),
                GetChatClient(SummarizerModel.Phi4Mini).EnsureModelAsync());
        }
        catch (Exception ex)
        {
            SetStatus("Model download failed: " + ex.Message, balloon: true);
        }
    }

    private void WarmUpModel(string name, Func<Task> ensureModel)
    {
        SetStatus($"Downloading {name}...", balloon: true);

        _ = Task.Run(async () =>
        {
            try
            {
                await ensureModel();
                SetStatus($"{name} ready", balloon: true);
            }
            catch (Exception ex)
            {
                SetStatus($"{name} failed: " + ex.Message, balloon: true);
            }
        });
    }

    private async Task WatchAsync(CancellationToken token)
    {
        Recorder recorder = null;
        Meeting meeting = null;
        var misses = 0;

        SetStatus("Watching for meetings...");

        while (!token.IsCancellationRequested)
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

                    SetStatus("Recording meeting...", balloon: true);
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
                SetStatus("Watching for meetings...");
            }

            try
            {
                await Task.Delay(2000, token);
            }
            catch (OperationCanceledException)
            {
            }
        }

        // Don't lose a meeting that was still recording when quitting.
        if (recorder != null)
        {
            SetStatus("Finishing recording...");
            await ProcessMeetingAsync(recorder, meeting);
        }
    }

    private async Task ProcessMeetingAsync(Recorder recorder, Meeting meeting)
    {
        try
        {
            await recorder.StopAsync();

            var end = DateTime.Now;
            var duration = end - meeting.Start;

            if (duration.TotalSeconds < MinMeetingSeconds)
            {
                MeetingNaming.TryDelete(meeting.Folder);
                return;
            }

            SetStatus("Transcribing meeting...");
            await GetTranscriber(_settings.Transcriber).TranscribeAsync(meeting.Folder);

            SetStatus("Summarizing meeting...");
            await GetSummarizer(_settings.Summarizer)
                .SummarizeAsync(meeting.Folder, meeting.Title, meeting.Start, end);

            SetStatus("Meeting complete", balloon: true);
        }
        catch (Exception ex)
        {
            SetStatus("Error: " + ex.Message, balloon: true);
        }
    }

    private void SetStatus(string status, bool balloon = false)
    {
        void Apply()
        {
            // NotifyIcon tooltip caps at 63 chars.
            var text = $"TeamsScribe - {status}";
            _tray.Text = text.Length <= 63 ? text : text[..63];
            _statusItem.Text = status;

            if (balloon)
                _tray.ShowBalloonTip(3000, "TeamsScribe", status, ToolTipIcon.Info);
        }

        if (_marshal.IsHandleCreated && _marshal.InvokeRequired)
            _marshal.BeginInvoke(Apply);
        else
            Apply();
    }

    private static void OpenRepo() =>
        Process.Start(new ProcessStartInfo(RepoUrl) { UseShellExecute = true });

    private void OpenChat()
    {
        if (_chatForm == null || _chatForm.IsDisposed)
            _chatForm = new ChatForm(GetChatClient, () => _settings.ChatModel);

        _chatForm.Show();
        _chatForm.Activate();
    }

    private async void Exit(object sender, EventArgs e)
    {
        _tray.Visible = false;
        _cts.Cancel();

        try
        {
            await _watchLoop; // finish an in-progress recording before quitting
        }
        catch
        {
            // Ignore shutdown errors.
        }

        _tray.Dispose();
        _marshal.Dispose();
        ExitThread();
    }
}
