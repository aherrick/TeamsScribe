using NAudio.Wave;

namespace TeamsScribe;

// Records Teams (per-process loopback) and the mic as two 16 kHz mono tracks.
sealed class Recorder
{
    private WasapiRecorder _teams;
    private WasapiRecorder _mic;
    private WaveFileWriter _teamsWriter;
    private WaveFileWriter _micWriter;

    public async Task StartAsync(string folder, int teamsProcessId)
    {
        var format = new WaveFormat(16000, 16, 1);

        _teams = await new WasapiRecorderBuilder()
            .WithProcessLoopback((uint)teamsProcessId)
            .WithFormat(format)
            .BuildAsync();

        _mic = new WasapiRecorderBuilder()
            .WithFormat(format)
            .Build();

        _teamsWriter = new WaveFileWriter(Path.Combine(folder, Tracks.Participants), _teams.WaveFormat);
        _micWriter = new WaveFileWriter(Path.Combine(folder, Tracks.Me), _mic.WaveFormat);

        _teams.DataAvailable += (buffer, _, _, _) => _teamsWriter?.Write(buffer);
        _mic.DataAvailable += (buffer, _, _, _) => _micWriter?.Write(buffer);

        _teams.StartRecording();
        _mic.StartRecording();
    }

    public async Task StopAsync()
    {
        await _teams.DisposeAsync();
        await _mic.DisposeAsync();
        _teamsWriter.Dispose();
        _micWriter.Dispose();
    }
}
