using System.IO;
using System.Windows.Threading;
using ImageMango.Core;

namespace ImageMango.UI;

/// <summary>
/// The prompt helper's engine: a llama-server with a small Qwen3 model, started on first use and stopped after
/// a few idle minutes, before each image run (to give the GPU memory back) and when the app closes.
/// </summary>
public sealed partial class MainViewModel
{
    private LlamaServer? _helperServer;
    private readonly SemaphoreSlim _helperGate = new(1, 1);
    private DispatcherTimer? _helperIdle;
    private static readonly TimeSpan HelperIdleTime = TimeSpan.FromMinutes(5);

    public HelperModel HelperModel
    {
        get => HelperModels.Get(_settings.PromptHelperModel);
        set
        {
            if (value.Id == _settings.PromptHelperModel) return;
            _settings.PromptHelperModel = value.Id;
            _settings.Save();
            Raise();
        }
    }

    public bool HelperAskFirst { get => _settings.PromptHelperAskFirst; set { _settings.PromptHelperAskFirst = value; Raise(); _settings.Save(); } }

    public string HelperModelPath(HelperModel m) => m.File.LocalPath(_settings.ModelsDir);
    public bool IsHelperModelReady(HelperModel m) => File.Exists(HelperModelPath(m));

    /// <summary>What still has to be downloaded before the helper can run, with the total size in GB.</summary>
    public (List<string> Parts, double Gb) HelperDownloadsNeeded(HelperModel m)
    {
        var parts = new List<string>();
        double gb = 0;
        if (!LlamaRuntime.IsInstalled)
        {
            parts.Add($"the helper engine (llama.cpp, {LlamaRuntime.DownloadBytes / 1e6:0} MB)");
            gb += LlamaRuntime.DownloadBytes / 1e9;
        }
        if (!IsHelperModelReady(m))
        {
            parts.Add($"{m.Name} ({m.File.SizeGb:0.0} GB)");
            gb += m.File.SizeGb;
        }
        return (parts, gb);
    }

    /// <summary>Downloads the engine and the model if missing. The model goes in the models folder, shared with image models that use it.</summary>
    public async Task InstallHelperAsync(HelperModel m, IProgress<string> status, CancellationToken ct)
    {
        CheckDiskSpace(HelperDownloadsNeeded(m).Gb);
        if (!LlamaRuntime.IsInstalled)
        {
            var p = new Progress<DownloadProgress>(d => status.Report(
                $"Helper engine: {d.Received / 1e6:0} / {d.Total / 1e6:0} MB · {d.BytesPerSecond / 1e6:0.0} MB/s"));
            await LlamaRuntime.InstallAsync(p, ct);
        }
        if (!IsHelperModelReady(m))
        {
            var p = new Progress<DownloadProgress>(d => status.Report(
                $"{m.Name}: {d.Received / 1e9:0.00} / {d.Total / 1e9:0.00} GB · {d.BytesPerSecond / 1e6:0} MB/s"));
            await Task.Run(() => Downloader.DownloadAsync(m.File.Url, HelperModelPath(m), _settings.HfToken, p, ct), ct);
            foreach (var x in Models) x.RefreshStatus(_settings.ModelsDir);
        }
    }

    /// <summary>Sends the conversation to the helper model, starting the engine first if needed.</summary>
    public async Task<HelperAnswer> AskHelperAsync(PromptHelperChat chat, bool allowQuestions, IProgress<string> status, CancellationToken ct)
    {
        await _helperGate.WaitAsync(ct);
        try
        {
            _helperIdle?.Stop();
            var path = HelperModelPath(HelperModel);
            if (_helperServer is null || !_helperServer.IsRunning || !string.Equals(_helperServer.ModelPath, path, StringComparison.OrdinalIgnoreCase))
            {
                StopHelperServer();
                status.Report($"Starting {HelperModel.Name}…");
                _helperServer = await LlamaServer.StartAsync(path, 8192, ct);
            }
            status.Report("Writing…");
            var server = _helperServer;
            var reply = await server.ChatJsonAsync(chat.Messages, chat.Schema(allowQuestions), PromptHelperChat.Sampling(Random.Shared.Next()), ct);
            return PromptHelperChat.Parse(reply);
        }
        finally
        {
            RestartHelperIdleTimer();
            _helperGate.Release();
        }
    }

    private void RestartHelperIdleTimer()
    {
        _helperIdle ??= new DispatcherTimer(HelperIdleTime, DispatcherPriority.Background, (_, _) =>
        {
            _helperIdle!.Stop();
            if (_helperGate.CurrentCount == 1) StopHelperServer();
        }, _ui);
        _helperIdle.Stop();
        _helperIdle.Start();
    }

    /// <summary>Ends the helper engine (frees its GPU memory). It starts again on the next request.</summary>
    private void StopHelperServer()
    {
        _helperServer?.Dispose();
        _helperServer = null;
    }

    /// <summary>Before an image run: give the GPU memory back, unless the helper is answering right now.</summary>
    private void StopHelperForImageRun()
    {
        if (_helperServer is not null && _helperGate.CurrentCount == 1) StopHelperServer();
    }
}
