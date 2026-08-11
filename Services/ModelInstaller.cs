using System.Collections.Concurrent;

namespace TeamsScribe.Services;

// Installs an entire model directory atomically: incomplete downloads are never usable.
static class ModelInstaller
{
    private static readonly ConcurrentDictionary<string, SemaphoreSlim> Gates = [];

    public static async Task EnsureAsync(
        string modelDir,
        IReadOnlyList<string> files,
        Func<string, Task<Stream>> openStreamAsync)
    {
        var gate = Gates.GetOrAdd(modelDir, _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync();

        try
        {
            if (IsInstalled(modelDir, files))
                return;

            var stagingDir = modelDir + ".downloading";
            Directory.CreateDirectory(Path.GetDirectoryName(modelDir)!);

            if (Directory.Exists(stagingDir))
                Directory.Delete(stagingDir, recursive: true);

            Directory.CreateDirectory(stagingDir);

            try
            {
                foreach (var name in files)
                {
                    await using var source = await openStreamAsync(name);
                    await using var destination = File.Create(Path.Combine(stagingDir, name));
                    await source.CopyToAsync(destination);
                }

                await File.WriteAllTextAsync(Path.Combine(stagingDir, ".complete"), "");

                if (Directory.Exists(modelDir))
                    Directory.Delete(modelDir, recursive: true);

                Directory.Move(stagingDir, modelDir);
            }
            catch
            {
                if (Directory.Exists(stagingDir))
                    Directory.Delete(stagingDir, recursive: true);

                throw;
            }
        }
        finally
        {
            gate.Release();
        }
    }

    private static bool IsInstalled(string modelDir, IReadOnlyList<string> files) =>
        File.Exists(Path.Combine(modelDir, ".complete")) &&
        files.All(file => File.Exists(Path.Combine(modelDir, file)));
}