using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace ImageMango.Native;

/// <summary>Everything that requires (re)creating the native context when it changes.</summary>
public sealed record EngineLoadOptions
{
    public string? ModelPath { get; init; }
    public string? DiffusionModelPath { get; init; }
    public string? ClipLPath { get; init; }
    public string? ClipGPath { get; init; }
    public string? T5xxlPath { get; init; }
    public string? LlmPath { get; init; }
    public string? VaePath { get; init; }
    public string? TaesdPath { get; init; }

    /// <summary>Keep weights in system RAM and stream them to the GPU when needed.</summary>
    public bool OffloadToCpu { get; init; }
    /// <summary>Run the text encoder (CLIP/T5/LLM) on the CPU.</summary>
    public bool TextEncoderOnCpu { get; init; }
    /// <summary>Run the VAE on the CPU.</summary>
    public bool VaeOnCpu { get; init; }
    /// <summary>on / off / auto.</summary>
    public string FlashAttention { get; init; } = "auto";
    /// <summary>Quantize weights while loading (e.g. "q8_0"); null keeps the file's type.</summary>
    public string? WeightType { get; init; }
    public string? Prediction { get; init; }
    public string? ModelArgs { get; init; }
    /// <summary>Per-device VRAM budget in GiB as the engine expects it ("-1" reserves ~1 GiB); null = automatic.</summary>
    public string? MaxVram { get; init; }
    /// <summary>0 = engine default (physical cores).</summary>
    public int Threads { get; init; }

    [JsonIgnore]
    public string Key => JsonSerializer.Serialize(this);
}

public sealed record GenerateRequest
{
    public string Prompt { get; init; } = "";
    public string NegativePrompt { get; init; } = "";
    public int Width { get; init; } = 1024;
    public int Height { get; init; } = 1024;
    public int Steps { get; init; } = 20;
    public float CfgScale { get; init; } = 7f;
    /// <summary>Distilled guidance (Flux-dev style); null = engine default.</summary>
    public float? Guidance { get; init; }
    /// <summary>Sampler name as the engine spells it (euler, euler_a, dpm++2m, ...) or null/"default".</summary>
    public string? Sampler { get; init; }
    public string? Scheduler { get; init; }
    public float? FlowShift { get; init; }
    public long Seed { get; init; } = -1;
    public int BatchCount { get; init; } = 1;
    public int ClipSkip { get; init; } = -1;
    public bool VaeTiling { get; init; }
    public bool LivePreview { get; init; } = true;

    /// <summary>Image-to-image: the starting picture, RGB and exactly Width×Height. Null = text-to-image.</summary>
    public RawImage? InitImage { get; init; }
    /// <summary>Image-to-image: how much of the starting picture is replaced (0..1).</summary>
    public float Strength { get; init; } = 0.75f;
    /// <summary>LoRA files applied for this run only (no model reload).</summary>
    public IReadOnlyList<LoraSpec> Loras { get; init; } = Array.Empty<LoraSpec>();

    /// <summary>
    /// The sampler steps that actually run. With a starting image the engine skips the first part of the
    /// schedule: it runs floor(steps × strength) steps (at most steps - 1), plus one.
    /// </summary>
    public int SampledSteps => InitImage is null || Strength >= 1f ? Steps : SampledStepsFor(Steps, Strength);

    public static int SampledStepsFor(int steps, double strength)
    {
        int tEnc = (int)(steps * (float)strength);
        if (tEnc >= steps) tEnc = steps - 1;
        return Math.Max(0, tEnc) + 1;
    }
}

public sealed record LoraSpec(string Path, float Weight);

public sealed class RawImage
{
    public required int Width { get; init; }
    public required int Height { get; init; }
    public required int Channels { get; init; }
    public required byte[] Pixels { get; init; }
    public long Seed { get; init; }

    internal static unsafe RawImage Copy(sd_image_t* img, long seed)
    {
        int len = checked((int)(img->width * img->height * img->channel));
        var pixels = new byte[len];
        Marshal.Copy(img->data, pixels, 0, len);
        return new RawImage { Width = (int)img->width, Height = (int)img->height, Channels = (int)img->channel, Pixels = pixels, Seed = seed };
    }
}

public sealed class GenerationResult
{
    public required IReadOnlyList<RawImage> Images { get; init; }
    public required TimeSpan LoadTime { get; init; }
    public required TimeSpan GenerateTime { get; init; }
    public required string Sampler { get; init; }
    public required string Scheduler { get; init; }
    public bool Cancelled { get; init; }
}

/// <summary>
/// Owns the single native stable-diffusion.cpp context. The native library keeps its
/// callbacks in globals, so there must only be one instance per process. All calls are
/// serialized; call from a background thread (they block for seconds to minutes).
/// </summary>
public sealed unsafe class SdEngine : IDisposable
{
    private readonly object _gate = new();
    private nint _ctx;
    private NativeStrings? _ctxStrings;
    private string? _loadedKey;
    private readonly Queue<string> _recentErrors = new();

    // Delegates must stay referenced while native code holds the function pointers.
    private readonly SdLogCallback _logCb;
    private readonly SdProgressCallback _progressCb;
    private readonly SdPreviewCallback _previewCb;

    public event Action<SdLogLevel, string>? Log;
    /// <summary>step, total steps, seconds per step.</summary>
    public event Action<int, int, float>? Progress;
    public event Action<RawImage>? Preview;
    /// <summary>LoRA file path, parts applied, parts in the file (from the engine's per-run LoRA report).</summary>
    public event Action<string, int, int>? LoraApplied;

    public bool IsLoaded => _ctx != 0;
    public string? LoadedKey => _loadedKey;
    public string? ModelVersion { get; private set; }

    public SdEngine()
    {
        _logCb = OnLog;
        _progressCb = OnProgress;
        _previewCb = OnPreview;
        SdNative.sd_set_log_callback(_logCb, 0);
        SdNative.sd_set_progress_callback(_progressCb, 0);
    }

    public static string EngineInfo()
    {
        var commit = SdNative.Str(SdNative.sd_commit()) ?? "?";
        var version = SdNative.Str(SdNative.sd_version()) ?? "?";
        return $"stable-diffusion.cpp {version} ({commit})";
    }

    public static string SystemInfo() => SdNative.Str(SdNative.sd_get_system_info()) ?? "";

    /// <summary>Creates the native context unless one with identical options is already loaded.</summary>
    /// <returns>Time spent loading (zero if reused).</returns>
    public TimeSpan EnsureLoaded(EngineLoadOptions o)
    {
        lock (_gate)
        {
            if (_ctx != 0 && _loadedKey == o.Key) return TimeSpan.Zero;
            UnloadLocked();

            var sw = Stopwatch.StartNew();
            // Kept alive with the context in case the engine holds on to any of these pointers.
            var s = new NativeStrings();
            sd_ctx_params_t p;
            SdNative.sd_ctx_params_init(&p);
            p.model_path = s.Get(o.ModelPath);
            p.diffusion_model_path = s.Get(o.DiffusionModelPath);
            p.clip_l_path = s.Get(o.ClipLPath);
            p.clip_g_path = s.Get(o.ClipGPath);
            p.t5xxl_path = s.Get(o.T5xxlPath);
            p.llm_path = s.Get(o.LlmPath);
            p.vae_path = s.Get(o.VaePath);
            p.taesd_path = s.Get(o.TaesdPath);
            if (o.Threads > 0) p.n_threads = o.Threads;

            if (!string.IsNullOrWhiteSpace(o.WeightType))
            {
                int t = SdNative.str_to_sd_type(o.WeightType.Trim());
                if (t >= SdConst.SD_TYPE_COUNT) throw new ArgumentException($"Unknown weight type '{o.WeightType}'.");
                p.wtype = t;
            }
            if (!string.IsNullOrWhiteSpace(o.Prediction))
            {
                int pr = SdNative.str_to_prediction(o.Prediction.Trim());
                if (pr >= SdConst.PREDICTION_COUNT) throw new ArgumentException($"Unknown prediction type '{o.Prediction}'.");
                p.prediction = pr;
            }

            // Same mapping as the engine's CLI: --offload-to-cpu => params "*=cpu",
            // --clip-on-cpu => backend "te=cpu", --vae-on-cpu => backend "vae=cpu".
            var backend = new List<string>();
            if (o.TextEncoderOnCpu) backend.Add("te=cpu");
            if (o.VaeOnCpu) backend.Add("vae=cpu");
            if (backend.Count > 0) p.backend = s.Get(string.Join(",", backend));
            if (o.OffloadToCpu) p.params_backend = s.Get("*=cpu");

            p.flash_attn_mode = s.Get(string.IsNullOrWhiteSpace(o.FlashAttention) ? "auto" : o.FlashAttention);
            p.model_args = s.Get(o.ModelArgs);
            p.max_vram = s.Get(o.MaxVram);

            ClearErrors();
            nint ctx = SdNative.new_sd_ctx(&p);
            if (ctx == 0)
            {
                s.Dispose();
                throw new InvalidOperationException("The engine could not load this model." + RecentErrorText());
            }

            _ctx = ctx;
            _ctxStrings = s;
            _loadedKey = o.Key;
            ModelVersion = SdNative.Str(SdNative.sd_get_model_version_name(ctx));
            return sw.Elapsed;
        }
    }

    public GenerationResult Generate(GenerateRequest r, CancellationToken token)
    {
        lock (_gate)
        {
            if (_ctx == 0) throw new InvalidOperationException("No model is loaded.");
            var ctx = _ctx;

            using var s = new NativeStrings();
            sd_img_gen_params_t g;
            SdNative.sd_img_gen_params_init(&g);
            g.prompt = s.Get(r.Prompt ?? "", allowEmpty: true);
            g.negative_prompt = s.Get(r.NegativePrompt ?? "", allowEmpty: true);
            g.width = r.Width;
            g.height = r.Height;
            g.seed = r.Seed;
            g.batch_count = Math.Max(1, r.BatchCount);
            g.clip_skip = r.ClipSkip;
            g.vae_tiling_params.enabled = r.VaeTiling;

            g.sample_params.sample_steps = r.Steps;
            g.sample_params.guidance.txt_cfg = r.CfgScale;
            if (r.Guidance is float dg) g.sample_params.guidance.distilled_guidance = dg;
            if (r.FlowShift is float fs) g.sample_params.flow_shift = fs;

            int method = ResolveName(r.Sampler, SdNative.str_to_sample_method, SdConst.SAMPLE_METHOD_COUNT);
            if (method >= SdConst.SAMPLE_METHOD_COUNT) method = SdNative.sd_get_default_sample_method(ctx);
            int scheduler = ResolveName(r.Scheduler, SdNative.str_to_scheduler, SdConst.SCHEDULER_COUNT);
            if (scheduler >= SdConst.SCHEDULER_COUNT) scheduler = SdNative.sd_get_default_scheduler(ctx, method);
            g.sample_params.sample_method = method;
            g.sample_params.scheduler = scheduler;

            // LoRAs: an array of sd_lora_t in native memory, freed after the run.
            nint loraArray = 0;
            if (r.Loras.Count > 0)
            {
                loraArray = Marshal.AllocHGlobal(sizeof(sd_lora_t) * r.Loras.Count);
                var loras = (sd_lora_t*)loraArray;
                for (int i = 0; i < r.Loras.Count; i++)
                    loras[i] = new sd_lora_t { is_high_noise = false, multiplier = r.Loras[i].Weight, path = s.Get(r.Loras[i].Path) };
                g.loras = loraArray;
                g.lora_count = (uint)r.Loras.Count;
            }

            // Image-to-image: the engine reads the pixels during the call, so pin them until it returns.
            GCHandle initPin = default;
            if (r.InitImage is { } init)
            {
                if (init.Channels != 3 || init.Width != r.Width || init.Height != r.Height)
                    throw new ArgumentException("The starting image must be RGB and the same size as the output.");
                initPin = GCHandle.Alloc(init.Pixels, GCHandleType.Pinned);
                g.init_image = new sd_image_t { width = (uint)init.Width, height = (uint)init.Height, channel = 3, data = initPin.AddrOfPinnedObject() };
                g.strength = Math.Clamp(r.Strength, 0.01f, 1f);
            }

            if (r.LivePreview)
                SdNative.sd_set_preview_callback(_previewCb, SdPreview.Proj, 1, true, false, 0);
            else
                SdNative.sd_set_preview_callback(null, SdPreview.None, 0, false, false, 0);

            SdNative.sd_cancel_generation(ctx, SdCancelMode.Reset);
            using var reg = token.Register(() => SdNative.sd_cancel_generation(ctx, SdCancelMode.All));

            ClearErrors();
            var sw = Stopwatch.StartNew();
            sd_image_t* images = null;
            int count = 0;
            bool ok;
            try
            {
                ok = SdNative.generate_image(ctx, &g, &images, &count);
            }
            finally
            {
                if (initPin.IsAllocated) initPin.Free();
                if (loraArray != 0) Marshal.FreeHGlobal(loraArray);
            }
            var elapsed = sw.Elapsed;

            var list = new List<RawImage>();
            try
            {
                for (int i = 0; i < count; i++)
                    if (images[i].data != 0)
                        list.Add(RawImage.Copy(&images[i], r.Seed + i));
            }
            finally
            {
                if (images != null) SdNative.free_sd_images(images, count);
            }

            if (token.IsCancellationRequested)
                return new GenerationResult { Images = list, LoadTime = TimeSpan.Zero, GenerateTime = elapsed, Sampler = Name(SdNative.sd_sample_method_name(method)), Scheduler = Name(SdNative.sd_scheduler_name(scheduler)), Cancelled = true };
            if (!ok || list.Count == 0)
                throw new InvalidOperationException("Generation failed." + RecentErrorText());

            return new GenerationResult
            {
                Images = list,
                LoadTime = TimeSpan.Zero,
                GenerateTime = elapsed,
                Sampler = Name(SdNative.sd_sample_method_name(method)),
                Scheduler = Name(SdNative.sd_scheduler_name(scheduler)),
            };
        }
    }

    public void Unload()
    {
        lock (_gate) UnloadLocked();
    }

    private void UnloadLocked()
    {
        if (_ctx != 0)
        {
            SdNative.free_sd_ctx(_ctx);
            _ctx = 0;
        }
        _ctxStrings?.Dispose();
        _ctxStrings = null;
        _loadedKey = null;
        ModelVersion = null;
    }

    public void Dispose() => Unload();

    private static string Name(nint p) => SdNative.Str(p) ?? "?";

    private static int ResolveName(string? name, Func<string, int> lookup, int count)
    {
        if (string.IsNullOrWhiteSpace(name) || name.Equals("default", StringComparison.OrdinalIgnoreCase)) return count;
        return lookup(name.Trim());
    }

    // "(480 / 480) LoRA tensors have been applied, lora_file_path = … --- lora.hpp:1004", or "Only (0 / 480) …".
    // The engine reports each LoRA several times per run (a sizing pass that applies 0, then once per model part),
    // so callers should keep the highest count of a run.
    private static readonly System.Text.RegularExpressions.Regex LoraReport =
        new(@"\((\d+) / (\d+)\) LoRA tensors have been applied, lora_file_path = (.+?)(?: --- \S+:\d+)?$");

    /// <summary>
    /// A LoRA made for another model type produces one warning per tensor (hundreds); they're summarised
    /// by the applied-count report, so they're kept out of the UI log and the error summary.
    /// </summary>
    public static bool IsLoraTensorNoise(string msg) =>
        msg.StartsWith("unused lora tensor", StringComparison.Ordinal) ||
        msg.StartsWith("skip incompatible LoRA tensor", StringComparison.Ordinal);

    private void OnLog(SdLogLevel level, nint text, nint data)
    {
        var msg = (SdNative.Str(text) ?? "").TrimEnd();
        if (msg.Length == 0) return;
        if (msg.Contains("LoRA tensors have been applied", StringComparison.Ordinal) && LoraReport.Match(msg) is { Success: true } m)
            LoraApplied?.Invoke(m.Groups[3].Value.Trim(), int.Parse(m.Groups[1].Value), int.Parse(m.Groups[2].Value));
        if (level >= SdLogLevel.Warn && !IsLoraTensorNoise(msg))
        {
            lock (_recentErrors)
            {
                _recentErrors.Enqueue(msg);
                while (_recentErrors.Count > 8) _recentErrors.Dequeue();
            }
        }
        Log?.Invoke(level, msg);
    }

    private void OnProgress(int step, int steps, float time, nint data) => Progress?.Invoke(step, steps, time);

    private void OnPreview(int step, int frameCount, nint frames, byte isNoisy, nint data)
    {
        if (frameCount < 1 || frames == 0 || Preview is null) return;
        var img = (sd_image_t*)frames;
        if (img->data == 0) return;
        Preview?.Invoke(RawImage.Copy(img, -1));
    }

    private void ClearErrors()
    {
        lock (_recentErrors) _recentErrors.Clear();
    }

    private string RecentErrorText()
    {
        lock (_recentErrors)
        {
            if (_recentErrors.Count == 0) return " See the log for details.";
            var sb = new StringBuilder("\n\nEngine said:\n");
            foreach (var e in _recentErrors) sb.AppendLine(e);
            return sb.ToString();
        }
    }

    /// <summary>UTF-8 copies of managed strings that live until disposed.</summary>
    private sealed class NativeStrings : IDisposable
    {
        private readonly List<nint> _ptrs = new();

        public nint Get(string? value, bool allowEmpty = false)
        {
            if (value is null || (value.Length == 0 && !allowEmpty)) return 0;
            var p = Marshal.StringToCoTaskMemUTF8(value);
            _ptrs.Add(p);
            return p;
        }

        public void Dispose()
        {
            foreach (var p in _ptrs) Marshal.FreeCoTaskMem(p);
            _ptrs.Clear();
        }
    }
}
