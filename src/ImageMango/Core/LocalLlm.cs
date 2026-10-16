using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;

namespace ImageMango.Core;

/// <summary>
/// The pinned llama.cpp build that runs the prompt helper's language model (llama-server, OpenAI-style API on localhost).
/// Downloaded on first use into tools\llama-{tag}-{flavour}. The CUDA build reuses the CUDA 12 runtime DLLs that already
/// sit next to ImageMango.exe for the image engine, so only llama.cpp itself is downloaded (267 MB); without them the
/// CPU build is used (20 MB).
/// </summary>
public static class LlamaRuntime
{
    public const string Tag = "b11683";
    private const string Base = "https://github.com/ggml-org/llama.cpp/releases/download/" + Tag + "/";

    private static readonly (string Zip, string Sha256, long Size) Cuda =
        ($"llama-{Tag}-bin-win-cuda-12.4-x64.zip", "e416c97fcab725b8a5405fd50483abd3448336a762393a0cde4997d7651e2d0e", 266_609_546);
    private static readonly (string Zip, string Sha256, long Size) Cpu =
        ($"llama-{Tag}-bin-win-cpu-x64.zip", "69921e204b856c0e01b71361273eaa4158863e15dcc603022a8dbfef611e640e", 19_564_815);

    /// <summary>The image engine's CUDA 12 runtime, next to the exe (absent with the Vulkan or CPU engine).</summary>
    public static bool CanUseCuda =>
        new[] { "cudart64_12.dll", "cublas64_12.dll", "cublasLt64_12.dll" }.All(f => File.Exists(Path.Combine(AppContext.BaseDirectory, f)));

    private static (string Zip, string Sha256, long Size) Package => CanUseCuda ? Cuda : Cpu;

    public static string Flavour => CanUseCuda ? "cuda" : "cpu";
    public static string Dir => Path.Combine(AppPaths.ToolsDir, $"llama-{Tag}-{Flavour}");
    public static string ServerExe => Path.Combine(Dir, "llama-server.exe");
    public static bool IsInstalled => File.Exists(ServerExe);
    public static long DownloadBytes => Package.Size;

    /// <summary>Downloads the pinned zip, checks its SHA-256 and unpacks llama-server.exe with its DLLs.</summary>
    public static async Task InstallAsync(IProgress<DownloadProgress>? progress, CancellationToken ct)
    {
        if (IsInstalled) return;
        var (zipName, sha, _) = Package;
        Directory.CreateDirectory(AppPaths.ToolsDir);
        var zip = Path.Combine(AppPaths.ToolsDir, zipName);
        await Downloader.DownloadAsync(Base + zipName, zip, null, progress, ct);
        try
        {
            string actual;
            await using (var fs = File.OpenRead(zip)) actual = Convert.ToHexStringLower(await SHA256.HashDataAsync(fs, ct));
            if (actual != sha)
                throw new InvalidDataException($"{zipName} doesn't match the expected checksum, so it wasn't installed. Try again later.");

            var tmp = Dir + ".tmp";
            if (Directory.Exists(tmp)) Directory.Delete(tmp, true);
            Directory.CreateDirectory(tmp);
            using (var archive = ZipFile.OpenRead(zip))
            {
                // The server and the libraries it loads; the other tools in the zip aren't needed.
                foreach (var e in archive.Entries.Where(e => e.FullName == e.Name &&
                             (e.Name == "llama-server.exe" || e.Name.EndsWith(".dll", StringComparison.OrdinalIgnoreCase) ||
                              e.Name.StartsWith("LICENSE", StringComparison.OrdinalIgnoreCase))))
                    e.ExtractToFile(Path.Combine(tmp, e.Name), overwrite: true);
            }
            if (Directory.Exists(Dir)) Directory.Delete(Dir, true);
            Directory.Move(tmp, Dir);
        }
        finally
        {
            try { File.Delete(zip); } catch { }
        }
    }
}

/// <summary>
/// One llama-server process serving one model on 127.0.0.1 (a free port, no web UI, offline). Its output is kept only
/// in memory for error messages, never written to disk. The process belongs to a job object, so Windows ends it if
/// ImageMango exits without stopping it.
/// </summary>
public sealed class LlamaServer : IDisposable
{
    private static readonly HttpClient Http = new(new SocketsHttpHandler { UseProxy = false, ConnectTimeout = TimeSpan.FromSeconds(5) })
    {
        Timeout = TimeSpan.FromMinutes(5),
    };

    private readonly Process _process;
    private readonly string _baseUrl;
    private readonly Queue<string> _output = new();

    public string ModelPath { get; }
    public bool IsRunning => !_process.HasExited;

    private LlamaServer(Process process, int port, string modelPath)
    {
        _process = process;
        _baseUrl = $"http://127.0.0.1:{port}";
        ModelPath = modelPath;
    }

    /// <summary>
    /// Starts the server and waits until the model is loaded. "--fit" (on by default) puts as many layers on the GPU as
    /// its free memory allows and the rest on the CPU, so it works next to a loaded image model.
    /// </summary>
    public static async Task<LlamaServer> StartAsync(string modelPath, int contextTokens, CancellationToken ct)
    {
        if (!LlamaRuntime.IsInstalled) throw new FileNotFoundException("The prompt helper engine isn't installed.", LlamaRuntime.ServerExe);
        int port = FreePort();
        var psi = new ProcessStartInfo(LlamaRuntime.ServerExe)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            WorkingDirectory = LlamaRuntime.Dir,
        };
        foreach (var a in new[]
                 {
                     "-m", modelPath, "--host", "127.0.0.1", "--port", port.ToString(), "-c", contextTokens.ToString(), "-np", "1",
                     "--reasoning", "off", "--no-webui", "--offline", "--no-slots",
                 })
            psi.ArgumentList.Add(a);
        // The CUDA build finds the CUDA runtime next to ImageMango.exe; its own ggml DLLs come first from its folder.
        psi.Environment["PATH"] = LlamaRuntime.Dir + ";" + AppContext.BaseDirectory.TrimEnd('\\') + ";" + Environment.GetEnvironmentVariable("PATH");
        // Settings in LLAMA_ARG_* variables would override ours.
        foreach (var key in psi.Environment.Keys.Where(k => k.StartsWith("LLAMA_ARG_", StringComparison.OrdinalIgnoreCase)).ToList())
            psi.Environment.Remove(key);

        SuppressErrorDialogs();
        var process = Process.Start(psi) ?? throw new InvalidOperationException("Couldn't start llama-server.");
        var server = new LlamaServer(process, port, modelPath);
        KillWithThisApp(process);
        process.OutputDataReceived += (_, e) => server.Keep(e.Data);
        process.ErrorDataReceived += (_, e) => server.Keep(e.Data);
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();

        try
        {
            var deadline = DateTime.UtcNow.AddMinutes(4);
            while (true)
            {
                ct.ThrowIfCancellationRequested();
                if (process.HasExited)
                    throw new InvalidOperationException($"The prompt helper engine stopped while loading (exit code {process.ExitCode}).\n\n{server.LastOutput()}");
                try
                {
                    using var resp = await Http.GetAsync(server._baseUrl + "/health", ct);
                    if (resp.IsSuccessStatusCode) return server;
                }
                catch (HttpRequestException) { /* not listening yet */ }
                if (DateTime.UtcNow > deadline) throw new TimeoutException("The prompt helper model took too long to load.");
                await Task.Delay(300, ct);
            }
        }
        catch
        {
            server.Dispose();
            throw;
        }
    }

    /// <summary>
    /// One chat completion whose reply is forced to match <paramref name="schema"/> (llama.cpp turns the JSON schema
    /// into a grammar). Returns the reply text.
    /// </summary>
    public async Task<string> ChatJsonAsync(JsonArray messages, JsonObject schema, JsonObject sampling, CancellationToken ct)
    {
        var body = new JsonObject
        {
            ["messages"] = messages.DeepClone(),
            ["response_format"] = new JsonObject
            {
                ["type"] = "json_schema",
                ["json_schema"] = new JsonObject { ["name"] = "answer", ["schema"] = schema.DeepClone() },
            },
        };
        foreach (var (k, v) in sampling) body[k] = v?.DeepClone();
        using var content = new StringContent(body.ToJsonString(), Encoding.UTF8, "application/json");
        using var resp = await Http.PostAsync(_baseUrl + "/v1/chat/completions", content, ct);
        var text = await resp.Content.ReadAsStringAsync(ct);
        if (!resp.IsSuccessStatusCode)
            throw new InvalidOperationException($"The prompt helper engine returned an error ({(int)resp.StatusCode}): {Shorten(text)}");
        var reply = JsonNode.Parse(text)?["choices"]?[0]?["message"]?["content"]?.GetValue<string>();
        return reply ?? throw new InvalidOperationException("The prompt helper engine sent an empty answer.");
    }

    private void Keep(string? line)
    {
        if (line is null) return;
        lock (_output)
        {
            _output.Enqueue(line);
            while (_output.Count > 30) _output.Dequeue();
        }
    }

    private string LastOutput()
    {
        lock (_output) return string.Join("\n", _output.TakeLast(12));
    }

    private static string Shorten(string s) => s.Length > 400 ? s[..400] + "…" : s;

    private static int FreePort()
    {
        var l = new TcpListener(IPAddress.Loopback, 0);
        l.Start();
        int port = ((IPEndPoint)l.LocalEndpoint).Port;
        l.Stop();
        return port;
    }

    public void Dispose()
    {
        try
        {
            if (!_process.HasExited)
            {
                _process.Kill(entireProcessTree: true);
                _process.WaitForExit(5000);
            }
        }
        catch { /* already gone */ }
        _process.Dispose();
    }

    // ---------------------------------------------------------------- Windows plumbing

    /// <summary>
    /// No "DLL not found" system dialogs on the desktop: the child inherits this error mode, and a failed start shows
    /// up as an exit code and a message in the app instead.
    /// </summary>
    private static void SuppressErrorDialogs()
    {
        if (OperatingSystem.IsWindows()) SetErrorMode(SEM_FAILCRITICALERRORS | SEM_NOOPENFILEERRORBOX);
    }

    private static IntPtr _job;

    /// <summary>Adds the process to a job that Windows closes, ending the process, when ImageMango exits for any reason.</summary>
    private static void KillWithThisApp(Process process)
    {
        if (!OperatingSystem.IsWindows()) return;
        try
        {
            if (_job == IntPtr.Zero)
            {
                var job = CreateJobObject(IntPtr.Zero, null);
                var info = new JOBOBJECT_EXTENDED_LIMIT_INFORMATION { BasicLimitInformation = { LimitFlags = JOB_OBJECT_LIMIT_KILL_ON_JOB_CLOSE } };
                if (!SetInformationJobObject(job, JobObjectExtendedLimitInformation, ref info, Marshal.SizeOf<JOBOBJECT_EXTENDED_LIMIT_INFORMATION>()))
                    return;
                _job = job;
            }
            AssignProcessToJobObject(_job, process.Handle);
        }
        catch { /* best effort: Stop and Dispose still end it */ }
    }

    private const uint SEM_FAILCRITICALERRORS = 0x0001, SEM_NOOPENFILEERRORBOX = 0x8000;
    private const int JobObjectExtendedLimitInformation = 9;
    private const uint JOB_OBJECT_LIMIT_KILL_ON_JOB_CLOSE = 0x2000;

    [DllImport("kernel32.dll")] private static extern uint SetErrorMode(uint mode);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] private static extern IntPtr CreateJobObject(IntPtr attributes, string? name);
    [DllImport("kernel32.dll")] private static extern bool SetInformationJobObject(IntPtr job, int infoClass, ref JOBOBJECT_EXTENDED_LIMIT_INFORMATION info, int length);
    [DllImport("kernel32.dll")] private static extern bool AssignProcessToJobObject(IntPtr job, IntPtr process);

    [StructLayout(LayoutKind.Sequential)]
    private struct JOBOBJECT_BASIC_LIMIT_INFORMATION
    {
        public long PerProcessUserTimeLimit, PerJobUserTimeLimit;
        public uint LimitFlags;
        public UIntPtr MinimumWorkingSetSize, MaximumWorkingSetSize;
        public uint ActiveProcessLimit;
        public UIntPtr Affinity;
        public uint PriorityClass, SchedulingClass;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct IO_COUNTERS
    {
        public ulong ReadOperationCount, WriteOperationCount, OtherOperationCount, ReadTransferCount, WriteTransferCount, OtherTransferCount;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct JOBOBJECT_EXTENDED_LIMIT_INFORMATION
    {
        public JOBOBJECT_BASIC_LIMIT_INFORMATION BasicLimitInformation;
        public IO_COUNTERS IoInfo;
        public UIntPtr ProcessMemoryLimit, JobMemoryLimit, PeakProcessMemoryUsed, PeakJobMemoryUsed;
    }
}
