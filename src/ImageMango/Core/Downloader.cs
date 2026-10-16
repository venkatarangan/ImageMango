using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;

namespace ImageMango.Core;

public readonly record struct DownloadProgress(string FileName, long Received, long Total, double BytesPerSecond);

/// <summary>Resumable Hugging Face downloads (writes to *.part, renames when complete).</summary>
public static class Downloader
{
    private static readonly HttpClient Http = CreateClient();

    private static HttpClient CreateClient()
    {
        var handler = new SocketsHttpHandler
        {
            AutomaticDecompression = DecompressionMethods.None,
            AllowAutoRedirect = true,
            MaxAutomaticRedirections = 10,
            ConnectTimeout = TimeSpan.FromSeconds(30),
        };
        var http = new HttpClient(handler) { Timeout = Timeout.InfiniteTimeSpan };
        http.DefaultRequestHeaders.UserAgent.ParseAdd("ImageMango/1.1");
        return http;
    }

    /// <summary>The Hugging Face token is only ever sent to huggingface.co, never to other download hosts.</summary>
    public static bool IsHuggingFace(Uri uri) =>
        uri.Host.Equals("huggingface.co", StringComparison.OrdinalIgnoreCase) ||
        uri.Host.EndsWith(".huggingface.co", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Optional Civitai API key (Settings › Storage): many Civitai LoRAs can only be downloaded by a logged-in user.
    /// Sent only with downloads from civitai.com itself; its redirect to the file host doesn't carry it.
    /// </summary>
    public static string? CivitaiToken { get; set; }

    public static bool IsCivitai(Uri uri) =>
        uri.Host.Equals("civitai.com", StringComparison.OrdinalIgnoreCase) ||
        uri.Host.EndsWith(".civitai.com", StringComparison.OrdinalIgnoreCase);

    /// <summary>GET a small JSON document (Hugging Face / Civitai APIs). The token goes only to huggingface.co.</summary>
    public static async Task<string> GetStringAsync(string url, string? hfToken, CancellationToken ct)
    {
        using var req = new HttpRequestMessage(HttpMethod.Get, url);
        if (!string.IsNullOrWhiteSpace(hfToken) && IsHuggingFace(req.RequestUri!))
            req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", hfToken.Trim());
        using var resp = await Http.SendAsync(req, ct);
        if (resp.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
            throw new HttpRequestException($"Access denied ({(int)resp.StatusCode}) for {req.RequestUri!.Host}.", null, resp.StatusCode);
        if (resp.StatusCode == HttpStatusCode.NotFound)
            throw new HttpRequestException($"Not found: {url}", null, resp.StatusCode);
        resp.EnsureSuccessStatusCode();
        return await resp.Content.ReadAsStringAsync(ct);
    }

    /// <summary>GET a small binary file (LoRA preview images), at most <paramref name="maxBytes"/>.</summary>
    public static async Task<byte[]> GetBytesAsync(string url, string? hfToken, long maxBytes, CancellationToken ct)
    {
        using var req = new HttpRequestMessage(HttpMethod.Get, url);
        if (!string.IsNullOrWhiteSpace(hfToken) && IsHuggingFace(req.RequestUri!))
            req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", hfToken.Trim());
        using var resp = await Http.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, ct);
        resp.EnsureSuccessStatusCode();
        if (resp.Content.Headers.ContentLength > maxBytes) throw new IOException("Image too large.");
        await using var src = await resp.Content.ReadAsStreamAsync(ct);
        using var ms = new MemoryStream();
        var buffer = new byte[81920];
        int n;
        while ((n = await src.ReadAsync(buffer, ct)) > 0)
        {
            ms.Write(buffer, 0, n);
            if (ms.Length > maxBytes) throw new IOException("Image too large.");
        }
        return ms.ToArray();
    }

    public static async Task DownloadAsync(string url, string destination, string? hfToken,
        IProgress<DownloadProgress>? progress, CancellationToken ct)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
        var part = destination + ".part";
        long existing = File.Exists(part) ? new FileInfo(part).Length : 0;
        var name = Path.GetFileName(destination);

        using var req = new HttpRequestMessage(HttpMethod.Get, url);
        if (!string.IsNullOrWhiteSpace(hfToken) && IsHuggingFace(req.RequestUri!))
            req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", hfToken.Trim());
        else if (CivitaiToken is { Length: > 0 } civitai && IsCivitai(req.RequestUri!))
            req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", civitai.Trim());
        if (existing > 0)
            req.Headers.Range = new RangeHeaderValue(existing, null);

        using var resp = await Http.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, ct);
        if (resp.StatusCode == HttpStatusCode.RequestedRangeNotSatisfiable && existing > 0)
        {
            // The .part file already holds the whole file.
            File.Move(part, destination, overwrite: true);
            return;
        }
        if (resp.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
            throw new HttpRequestException(IsHuggingFace(req.RequestUri!)
                ? $"{name}: access denied ({(int)resp.StatusCode}). This repo may be gated: accept its licence on huggingface.co and set a token in Settings."
                : IsCivitai(req.RequestUri!)
                    ? string.IsNullOrWhiteSpace(CivitaiToken)
                        ? $"Civitai only lets logged-in users download this one. Add your free Civitai API key (civitai.com › Account settings › API Keys) in Settings › Storage or in Find LoRAs, then try again."
                        : $"Civitai refused the download ({(int)resp.StatusCode}) even with your API key. The LoRA may be early access or restricted; check its page."
                : $"{name}: access denied ({(int)resp.StatusCode}) by {req.RequestUri!.Host}. The site may require you to log in: download the file in your browser, then add it with \"Add a LoRA file from this PC\".",
                null, resp.StatusCode);
        resp.EnsureSuccessStatusCode();

        bool resumed = resp.StatusCode == HttpStatusCode.PartialContent;
        if (!resumed) existing = 0;
        long total = (resp.Content.Headers.ContentLength ?? 0) + existing;

        await using (var src = await resp.Content.ReadAsStreamAsync(ct))
        await using (var dst = new FileStream(part, resumed ? FileMode.Append : FileMode.Create, FileAccess.Write, FileShare.None, 1 << 20, useAsync: true))
        {
            var buffer = new byte[1 << 20];
            long received = existing;
            var sw = Stopwatch.StartNew();
            long lastBytes = received;
            double lastTime = 0, speed = 0;
            int n;
            while ((n = await src.ReadAsync(buffer, ct)) > 0)
            {
                await dst.WriteAsync(buffer.AsMemory(0, n), ct);
                received += n;
                double t = sw.Elapsed.TotalSeconds;
                if (t - lastTime >= 0.25)
                {
                    speed = (received - lastBytes) / (t - lastTime);
                    lastBytes = received;
                    lastTime = t;
                    progress?.Report(new DownloadProgress(name, received, total, speed));
                }
            }
            progress?.Report(new DownloadProgress(name, received, total, speed));
            if (total > 0 && received != total)
                throw new IOException($"{name}: download ended early ({received:N0} of {total:N0} bytes). Click Download again to resume.");
        }
        File.Move(part, destination, overwrite: true);
    }
}
