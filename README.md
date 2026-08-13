# TeamsScribe

[![Build](https://github.com/aherrick/TeamsScribe/actions/workflows/build.yml/badge.svg)](https://github.com/aherrick/TeamsScribe/actions/workflows/build.yml)
[![Release](https://img.shields.io/github/v/release/aherrick/TeamsScribe)](https://github.com/aherrick/TeamsScribe/releases/latest)

Automatically records, transcribes, and summarizes your Microsoft Teams meetings — entirely on your local machine.

TeamsScribe runs in the background watching for Teams calls. When a meeting starts it captures both sides of the audio, and when it ends it produces a speaker-labeled transcript and an AI-generated summary. Transcription (Whisper) and summarization (Phi-4 via Foundry Local) run locally, so audio never leaves your PC.

## How it works

1. **Detect** — [`TeamsDetector`](TeamsDetector.cs) watches the Windows microphone consent store to tell when Teams is actively in a call.
2. **Record** — [`Recorder`](Recorder.cs) captures two 16 kHz mono tracks via NAudio: the default system output (`participants.wav`) and your mic (`me.wav`).
3. **Transcribe** — [`Transcriber`](Transcriber.cs) runs Whisper (`ggml-base-en`) over each track and merges them into a timestamped, speaker-labeled `transcript.txt`.
4. **Summarize** — [`Summarizer`](Summarizer.cs) uses the `phi-4-mini` model through Foundry Local to write a recap; long meetings are chunked to fit the context window.

Each meeting is saved to its own folder under `recordings/`, named by timestamp and meeting title.

## Requirements

- Windows 10 (19041+) / Windows 11
- [.NET 10 SDK](https://dotnet.microsoft.com/download)
- Microsoft Teams
- Enough disk space for the Whisper and Phi-4 models (downloaded automatically on first run)

## Usage

```powershell
dotnet run
```

The app downloads the required models on first launch, then prints:

```
Watching for Teams meetings...
Press Ctrl+C to quit.
```

Leave it running. It will start recording automatically when you join a Teams call and process the meeting once the call ends. Press `Ctrl+C` to quit — if a meeting is still in progress, its recording is finished and processed before exit.

Meetings shorter than 30 seconds are discarded.

## Output

For each meeting, `recordings/<timestamp>_<title>/` contains:

| File | Description |
| --- | --- |
| `me.wav` | Your microphone audio |
| `participants.wav` | Other participants' audio |
| `transcript.txt` | Timestamped, speaker-labeled transcript |
| `summary.md` | AI-generated meeting summary with metadata |
| `meeting.md` | Summary plus the full transcript — ready to paste into Teams or email |

## Notes

- All processing is local — no audio or transcript data is sent to the cloud.
- Always follow your organization's policies and applicable laws regarding recording meetings, and obtain consent from participants where required.
