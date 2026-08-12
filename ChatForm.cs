using System.Runtime.InteropServices;
using System.Text;
using TeamsScribe.Models;
using TeamsScribe.Services;

namespace TeamsScribe;

// Simple local chat over the Foundry models (Phi-4 Mini / Qwen).
sealed class ChatForm : Form
{
    private const int WM_SETREDRAW = 0x000B;
    private const int WM_VSCROLL = 0x0115;
    private const int SB_BOTTOM = 7;

    [DllImport("user32.dll")]
    private static extern IntPtr SendMessage(IntPtr hWnd, int msg, IntPtr wParam, IntPtr lParam);

    private static readonly Color YouColor = Color.FromArgb(0, 99, 177);
    private static readonly Color BotColor = Color.FromArgb(16, 124, 16);
    private static readonly Color ErrColor = Color.FromArgb(197, 15, 31);

    private readonly Func<SummarizerModel, LocalChatClient> _getChatClient;
    private readonly Func<SummarizerModel> _getChatModel;
    private readonly CancellationTokenSource _cts = new();
    private readonly List<(string Role, string Content)> _conversation =
        [("system", "You are a helpful assistant.")];

    private readonly RichTextBox _transcript;
    private readonly TextBox _input;
    private readonly Button _send;

    public ChatForm(
        Func<SummarizerModel, LocalChatClient> getChatClient,
        Func<SummarizerModel> getChatModel)
    {
        _getChatClient = getChatClient;
        _getChatModel = getChatModel;

        Text = "TeamsScribe Chat";
        Icon = SystemIcons.Application;
        Width = 540;
        Height = 640;
        Font = new Font("Segoe UI", 9.75f);
        BackColor = Color.White;

        var transcriptHost = new Panel { Dock = DockStyle.Fill, Padding = new Padding(12) };
        _transcript = new RichTextBox
        {
            Dock = DockStyle.Fill,
            ReadOnly = true,
            TabStop = false,
            BorderStyle = BorderStyle.None,
            BackColor = Color.White,
        };
        transcriptHost.Controls.Add(_transcript);

        var inputPanel = new Panel { Dock = DockStyle.Bottom, Height = 76, Padding = new Padding(12, 12, 20, 12) };
        _input = new TextBox
        {
            Dock = DockStyle.Fill,
            Multiline = true,
            AcceptsReturn = true,
            ScrollBars = ScrollBars.Vertical,
        };
        _input.KeyDown += OnInputKeyDown;
        _send = new Button { Dock = DockStyle.Right, Text = "Send", Width = 80, FlatStyle = FlatStyle.System };
        _send.Click += async (_, _) => await SendAsync();
        inputPanel.Controls.Add(_input);
        inputPanel.Controls.Add(_send);

        Controls.Add(transcriptHost);
        Controls.Add(inputPanel);
        Shown += (_, _) => _input.Focus();
        FormClosing += (_, _) => _cts.Cancel();
    }

    private async void OnInputKeyDown(object sender, KeyEventArgs e)
    {
        if (e.KeyCode == Keys.Enter && !e.Shift)
        {
            e.SuppressKeyPress = true;
            await SendAsync();
        }
    }

    private async Task SendAsync()
    {
        var prompt = _input.Text.Trim();

        if (string.IsNullOrEmpty(prompt))
            return;

        _input.Clear();
        _input.Enabled = false;
        _send.Enabled = false;

        AppendMessage("You", prompt, YouColor);
        _conversation.Add(("user", prompt));

        try
        {
            var model = _getChatModel();
            AppendHeader(ModelName(model), BotColor);

            var reply = new StringBuilder();

            await foreach (var chunk in _getChatClient(model)
                .CompleteStreamingAsync(_conversation, _cts.Token))
            {
                reply.Append(chunk);
                AppendBody(chunk);
            }

            AppendBody("\n\n");
            _conversation.Add(("assistant", reply.ToString()));
        }
        catch (OperationCanceledException)
        {
            // Chat window closed mid-stream; nothing to show.
        }
        catch (Exception ex)
        {
            AppendMessage("Error", ex.Message, ErrColor);
        }
        finally
        {
            if (CanUpdateUi)
            {
                _input.Enabled = true;
                _send.Enabled = true;
                _input.Focus();
            }
        }
    }

    private static string ModelName(SummarizerModel model) =>
        model == SummarizerModel.Qwen25 ? "Qwen2.5 1.5B" : "Phi-4 Mini";

    private void AppendMessage(string author, string text, Color color)
    {
        AppendHeader(author, color);
        AppendBody(text + "\n\n");
    }

    private void AppendHeader(string author, Color color) =>
        Append($"{DateTime.Now:t}  {author}\n", color, new Font(_transcript.Font, FontStyle.Bold));

    private void AppendBody(string text) =>
        Append(text, _transcript.ForeColor, _transcript.Font);

    // Suspends painting while appending so the view doesn't bounce as chunks stream in.
    private void Append(string text, Color color, Font font)
    {
        if (!CanUpdateUi)
            return;

        SendMessage(_transcript.Handle, WM_SETREDRAW, IntPtr.Zero, IntPtr.Zero);

        _transcript.SelectionStart = _transcript.TextLength;
        _transcript.SelectionLength = 0;
        _transcript.SelectionColor = color;
        _transcript.SelectionFont = font;
        _transcript.AppendText(text);

        SendMessage(_transcript.Handle, WM_SETREDRAW, 1, IntPtr.Zero);
        SendMessage(_transcript.Handle, WM_VSCROLL, SB_BOTTOM, IntPtr.Zero);
        _transcript.Invalidate();
    }

    private bool CanUpdateUi =>
        !IsDisposed && !Disposing
        && !_input.IsDisposed && !_input.Disposing
        && !_send.IsDisposed && !_send.Disposing
        && !_transcript.IsDisposed && !_transcript.Disposing;
}
