using System.Diagnostics;
using System.IO;
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
    private const string WatchingStatus = "Watching for meetings...";
    private static readonly TimeSpan MinMeetingLength = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan CallPollInterval = TimeSpan.FromSeconds(2);

    // Teams can briefly drop the microphone mid-call (device switches, reconnects), so wait a
    // little before deciding a call is really over.
    private static readonly TimeSpan CallEndGrace = TimeSpan.FromSeconds(60);

    private static readonly string AppVersion = typeof(TrayAppContext).Assembly.GetName().Version?.ToString(3) ?? "0.0.0";

    private static readonly string MeetingsFolder = AppDataPaths.MeetingsFolder;

    private static readonly string UpdateExePath = Path.Combine(
        Directory.GetParent(AppContext.BaseDirectory.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar))?.FullName
            ?? AppContext.BaseDirectory,
        "Update.exe");

    private readonly NotifyIcon _tray;
    private readonly Control _marshal = new();
    private readonly AppSettings _settings;
    private readonly CancellationTokenSource _cts = new();
    private readonly Task _watchLoop;
    private readonly UpdateManager _updater = new(new GithubSource(RepoUrl, null, false));
    private ToolStripMenuItem _statusItem;
    private ChatForm _chatForm;

    private readonly Dictionary<WhisperModel, WhisperTranscriber> _transcribers = [];
    private readonly Dictionary<SummarizerModel, LocalChatClient> _chatClients = [];
    private readonly Dictionary<SummarizerModel, Summarizer> _summarizers = [];

    private readonly Icon _idleIcon = LoadIcon("teamsscribe_record.png");
    private readonly Icon _recordingIcon = LoadIcon("teamsscribe_recording.png");

    public TrayAppContext()
    {
        Directory.CreateDirectory(MeetingsFolder);
        _ = _marshal.Handle; // force handle creation on the UI thread
        _settings = AppSettings.Load();

        _tray = new NotifyIcon
        {
            Icon = _idleIcon,
            Text = "TeamsScribe",
            Visible = true,
            ContextMenuStrip = BuildMenu(),
        };

        _tray.ShowBalloonTip(3000, "TeamsScribe", "TeamsScribe is running in your system tray.", ToolTipIcon.Info);
        AppLog.Write($"TeamsScribe v{AppVersion} started.");
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

        menu.Items.Add("Chat", null, (_, _) => OpenChat());

        var folders = new ToolStripMenuItem("Open Folder");
        folders.DropDownItems.Add("Meetings", null, (_, _) => OpenFolder(MeetingsFolder));
        folders.DropDownItems.Add("Logs", null, (_, _) => OpenFolder(AppLog.Folder));
        menu.Items.Add(folders);
        menu.Items.Add(new ToolStripSeparator());

        var models = new ToolStripMenuItem("Models");

        var transcriber = new ToolStripMenuItem("Transcription Model");
        transcriber.DropDownItems.Add(TranscriberItem("Fast", WhisperModel.Fast));
        transcriber.DropDownItems.Add(TranscriberItem("Balanced", WhisperModel.Balanced));
        transcriber.DropDownItems.Add(TranscriberItem("Accurate", WhisperModel.Accurate));
        transcriber.DropDownItems.Add(TranscriberItem("Most Accurate", WhisperModel.MostAccurate));

        var summarizer = new ToolStripMenuItem("Summary Model");
        summarizer.DropDownItems.Add(SummarizerItem("Phi-4 Mini", SummarizerModel.Phi4Mini));
        summarizer.DropDownItems.Add(SummarizerItem("Phi-4", SummarizerModel.Phi4));
        summarizer.DropDownItems.Add(SummarizerItem("Qwen2.5 1.5B", SummarizerModel.Qwen25));

        var chatModel = new ToolStripMenuItem("Chat Model");
        chatModel.DropDownItems.Add(ChatModelItem("Phi-4 Mini", SummarizerModel.Phi4Mini));
        chatModel.DropDownItems.Add(ChatModelItem("Phi-4", SummarizerModel.Phi4));
        chatModel.DropDownItems.Add(ChatModelItem("Qwen2.5 1.5B", SummarizerModel.Qwen25));

        models.DropDownItems.Add(chatModel);
        models.DropDownItems.Add(summarizer);
        models.DropDownItems.Add(transcriber);
        menu.Items.Add(models);
        menu.Items.Add(new ToolStripSeparator());

        var startup = new ToolStripMenuItem("Run at Startup")
        {
            CheckOnClick = true,
            Checked = StartupManager.IsEnabled(),
        };
        startup.Click += (_, _) => StartupManager.Set(startup.Checked, UpdateExePath);
        menu.Items.Add(startup);
        menu.Items.Add("Check for Updates", null, async (_, _) => await CheckForUpdatesAsync());
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("Exit", null, Exit);

        return menu;
    }

    private async Task CheckForUpdatesAsync()
    {
        try
        {
            var newVersion = await _updater.CheckForUpdatesAsync();
            if (newVersion == null)
            {
                MessageBox.Show(
                    "TeamsScribe is up to date.",
                    "TeamsScribe updates",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Information);
                return;
            }

            if (MessageBox.Show(
                    "A new version of TeamsScribe is available. Download and install it now?",
                    "TeamsScribe update",
                    MessageBoxButtons.YesNo,
                    MessageBoxIcon.Information) != DialogResult.Yes)
                return;

            await _updater.DownloadUpdatesAsync(newVersion);
            _updater.ApplyUpdatesAndRestart(newVersion);
        }
        catch (Exception ex)
        {
            AppLog.Write("Update check failed.", ex);
            SetStatus("Update check failed", balloon: true);
        }
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

            WarmUpModel(text, progress => GetChatClient(model).EnsureModelAsync(progress));
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

            WarmUpModel(text, progress => GetChatClient(model).EnsureModelAsync(progress));
        };

        return item;
    }

    private ToolStripMenuItem TranscriberItem(string text, WhisperModel model)
    {
        var item = new ToolStripMenuItem(text) { Checked = _settings.Transcriber == model };

        item.Click += (_, _) =>
        {
            _settings.Transcriber = model;
            _settings.Save();

            foreach (ToolStripMenuItem sibling in item.Owner.Items)
                sibling.Checked = sibling == item;

            WarmUpModel($"{text} transcriber", _ => GetTranscriber(model).EnsureModelAsync());
        };

        return item;
    }

    private WhisperTranscriber GetTranscriber(WhisperModel model) =>
        _transcribers.TryGetValue(model, out var transcriber)
            ? transcriber
            : _transcribers[model] = new WhisperTranscriber(model);

    private LocalChatClient GetChatClient(SummarizerModel model) =>
        _chatClients.TryGetValue(model, out var client)
            ? client
            : _chatClients[model] = new LocalChatClient(SummarizerModels.Get(model).Alias);

    private Summarizer GetSummarizer(SummarizerModel model) =>
        _summarizers.TryGetValue(model, out var summarizer)
            ? summarizer
            : _summarizers[model] = new Summarizer(
                GetChatClient(model),
                SummarizerModels.Get(model).ChunkChars);

    private async Task DownloadDefaultModelsAsync()
    {
        SetStatus("Downloading models...");

        try
        {
            await Task.WhenAll(
                GetTranscriber(_settings.Transcriber).EnsureModelAsync(),
                GetChatClient(SummarizerModel.Phi4Mini).EnsureModelAsync());
        }
        catch (Exception ex)
        {
            AppLog.Write("Default model download failed.", ex);
            SetStatus("Model download failed: " + ex.Message, balloon: true);
        }
    }

    private void WarmUpModel(string name, Func<Action<float>, Task> ensureModel)
    {
        SetStatus($"Downloading {name}...", balloon: true);

        _ = Task.Run(async () =>
        {
            try
            {
                var lastPercent = -1;
                await ensureModel(progress =>
                {
                    var percent = (int)Math.Round(progress * 100);

                    if (percent == lastPercent)
                        return;

                    lastPercent = percent;
                    SetStatus($"Downloading {name}: {percent}%");
                });
                SetStatus($"{name} ready", balloon: true);
            }
            catch (Exception ex)
            {
                AppLog.Write($"{name} model download failed.", ex);
                SetStatus($"{name} failed: " + ex.Message, balloon: true);
            }
        });
    }

    private async Task WatchAsync(CancellationToken token)
    {
        Recorder recorder = null;
        Meeting meeting = null;
        DateTime? callEnded = null;
        var startFailed = false;

        SetStatus(WatchingStatus);

        while (!token.IsCancellationRequested)
        {
            if (TeamsDetector.IsInCall())
            {
                if (callEnded != null)
                    AppLog.Write("Teams call detected again; continuing recording.");

                callEnded = null;

                if (recorder == null && !startFailed)
                {
                    try
                    {
                        (recorder, meeting) = await StartMeetingAsync();
                    }
                    catch (Exception ex)
                    {
                        startFailed = true; // don't retry until the current call ends
                        recorder = null;
                        AppLog.Write("Failed to start recording.", ex);
                        SetStatus("Recording failed: " + ex.Message, balloon: true);
                    }
                }
            }
            else
            {
                startFailed = false;

                if (recorder != null)
                {
                    callEnded ??= LogCallEnded();

                    // Teams being gone is final; no need to wait out the grace period.
                    if (DateTime.Now - callEnded >= CallEndGrace || !TeamsDetector.IsRunning())
                    {
                        await ProcessMeetingAsync(recorder, meeting, callEnded.Value);
                        recorder = null;
                        meeting = null;
                        callEnded = null;
                        SetStatus(WatchingStatus);
                    }
                }
            }

            try
            {
                await Task.Delay(CallPollInterval, token);
            }
            catch (OperationCanceledException)
            {
            }
        }

        // Don't lose a meeting that was still recording when quitting.
        if (recorder != null)
        {
            SetStatus("Finishing recording...");
            await ProcessMeetingAsync(recorder, meeting, callEnded ?? DateTime.Now);
        }
    }

    private async Task<(Recorder Recorder, Meeting Meeting)> StartMeetingAsync()
    {
        var start = DateTime.Now;
        var title = TeamsDetector.MeetingTitle();
        var folder = Path.Combine(
            MeetingsFolder,
            start.ToString("yyyy-MM-dd"),
            MeetingNaming.FolderName(start, title));
        Directory.CreateDirectory(folder);

        var recorder = new Recorder();
        await recorder.StartAsync(folder);

        SetStatus($"Recording {title ?? "meeting"}...", balloon: true);
        SetRecording(true);

        return (recorder, new Meeting(title, folder, start));
    }

    private static DateTime LogCallEnded()
    {
        AppLog.Write($"Teams call no longer detected; waiting {CallEndGrace.TotalSeconds:0}s before stopping recording.");
        return DateTime.Now;
    }

    private async Task ProcessMeetingAsync(Recorder recorder, Meeting meeting, DateTime end)
    {
        try
        {
            await recorder.StopAsync();
            SetRecording(false);

            if (end - meeting.Start < MinMeetingLength)
            {
                AppLog.Write("Meeting was too short to keep; discarding recording.");
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
            SetRecording(false);
            AppLog.Write("Meeting processing failed.", ex);
            SetStatus("Error: " + ex.Message, balloon: true);
        }
    }

    private void SetStatus(string status, bool balloon = false)
    {
        AppLog.Write(status);

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

    private void SetRecording(bool recording)
    {
        void Apply() => _tray.Icon = recording ? _recordingIcon : _idleIcon;

        if (_marshal.IsHandleCreated && _marshal.InvokeRequired)
            _marshal.BeginInvoke(Apply);
        else
            Apply();
    }

    private static Icon LoadIcon(string file)
    {
        try
        {
            using var bitmap = new Bitmap(Path.Combine(AppContext.BaseDirectory, "icons", file));
            return Icon.FromHandle(bitmap.GetHicon());
        }
        catch
        {
            return SystemIcons.Application;
        }
    }

    private static void OpenRepo() =>
        Process.Start(new ProcessStartInfo(RepoUrl) { UseShellExecute = true });

    private static void OpenFolder(string folder)
    {
        Directory.CreateDirectory(folder);
        Process.Start(new ProcessStartInfo(folder) { UseShellExecute = true });
    }

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
        _idleIcon.Dispose();
        _recordingIcon.Dispose();
        _marshal.Dispose();
        ExitThread();
    }
}
