// Usage:
//   InteropCheck layout <c_layout.txt>                  compare C# struct layout with the native dump
//   InteropCheck gen <diffusion.gguf> <llm.gguf> <vae> <out.ppm> [nsfw.onnx] [size] [steps]
//                                                       Z-Image style smoke test (CPU or GPU)
using System.Reflection;
using System.Runtime.InteropServices;
using ImageMango.Core;
using ImageMango.Native;

if (args.Length >= 2 && args[0] == "layout") return CheckLayout(args[1]);
if (args.Length >= 5 && args[0] == "gen") return Gen(args);
if (args.Length >= 3 && args[0] == "catalog") return await CheckCatalog(args[1], double.Parse(args[2]), args.Length > 3 && args[3] == "--urls");
if (args.Length >= 1 && args[0] == "lora") return await CheckLoras(args.Skip(1).ToArray());
if (args.Length >= 1 && args[0] == "paths")
{
    // Shows where the app keeps its data after the LocalImageGen → ImageMango move (run with a fake HOME/TMPDIR).
    var s = UserSettings.Load();
    Console.WriteLine($"DataRoot={AppPaths.DataRoot} migrated={AppPaths.MigratedFromOldName}\nModelsDir={s.ModelsDir}\nOutputDir={s.OutputDir}\nPending={string.Join(";", s.IncognitoPending)}");
    return 0;
}
if (args.Length >= 3 && args[0] == "meta") return CheckMetadata(args[1], args[2]);
if (args.Length >= 6 && args[0] == "helper") return await CheckHelper(args);
if (args.Length >= 2 && args[0] == "lorasearch") return await CheckLoraSearch(args[1], args.Length > 2 ? args[2] : "", args.Length > 3 && args[3] == "mature");
Console.Error.WriteLine("usage: InteropCheck layout <file> | gen <diffusion> <llm> <vae> <out.ppm> [nsfw.onnx] | catalog <models.json> <vramGb> [--urls] | lora [local.safetensors…] | meta <in.png> <out.png>");
return 2;

// Prompt helper on Windows: installs the pinned llama.cpp build if needed (download, checksum, unpack), starts it with
// the given GGUF, and asks for a prompt for each listed model id. "ask" lets it ask questions first; answers to them
// are "you choose".
//   InteropCheck helper <models.json> <llm.gguf> <id,id,…> improve|idea "<text>" [ask]
static async Task<int> CheckHelper(string[] args)
{
    var catalog = System.Text.Json.JsonSerializer.Deserialize<CatalogDocument>(File.ReadAllText(args[1]), Json.Options)!;
    var task = args[4] == "idea" ? HelperTask.FromIdea : HelperTask.Improve;
    bool ask = args.Length > 6 && args[6] == "ask";
    var sw = System.Diagnostics.Stopwatch.StartNew();
    if (!LlamaRuntime.IsInstalled)
    {
        Console.WriteLine($"installing llama.cpp {LlamaRuntime.Tag} ({LlamaRuntime.Flavour}) into {LlamaRuntime.Dir}");
        await LlamaRuntime.InstallAsync(null, CancellationToken.None);
        Console.WriteLine($"installed in {sw.Elapsed.TotalSeconds:0.0} s");
    }
    sw.Restart();
    using var server = await LlamaServer.StartAsync(args[2], 8192, CancellationToken.None);
    Console.WriteLine($"server ready in {sw.Elapsed.TotalSeconds:0.0} s ({LlamaRuntime.Flavour})");
    int failures = 0;
    foreach (var id in args[3].Split(','))
    {
        var model = catalog.Models.First(m => m.Id == id);
        double cfg = model.Defaults?["cfgScale"]?.GetValue<double>() ?? 7;
        var chat = new PromptHelperChat(model, cfg);
        chat.Begin(task, args[5], model.Defaults?["negativePrompt"]?.GetValue<string>() ?? "", ask);
        sw.Restart();
        var answer = PromptHelperChat.Parse(await server.ChatJsonAsync(chat.Messages, chat.Schema(ask), PromptHelperChat.Sampling(42), CancellationToken.None));
        if (answer.IsQuestion)
        {
            Console.WriteLine($"\n== {model.Name}: asks ({sw.Elapsed.TotalSeconds:0.0} s)\n  ? " + string.Join("\n  ? ", answer.Questions));
            chat.Answer(answer, Array.Empty<string>());
            answer = PromptHelperChat.Parse(await server.ChatJsonAsync(chat.Messages, chat.Schema(false), PromptHelperChat.Sampling(42), CancellationToken.None));
        }
        bool ok = answer.Prompt.Length > 0 && (chat.UsesNegative || answer.NegativePrompt.Length == 0);
        if (!ok) failures++;
        Console.WriteLine($"\n== {model.Name} ({sw.Elapsed.TotalSeconds:0.0} s, {answer.Prompt.Length} chars){(ok ? "" : "  ** CHECK **")}\n" +
                          $"PROMPT: {answer.Prompt}\nNEG: {answer.NegativePrompt}\nNOTES: {answer.Notes}");
    }
    return failures == 0 ? 0 : 1;
}

// LoRA browser search for a family (sdxl, z-image, …) on both sites; "mature" includes adult content.
static async Task<int> CheckLoraSearch(string familyId, string query, bool mature)
{
    var family = LoraFamilies.Get(familyId) ?? throw new ArgumentException("unknown family " + familyId);
    var hf = await LoraSearch.HuggingFaceAsync(family.HuggingFace, family.Id, query, LoraSort.Downloads, mature, null, CancellationToken.None);
    var (civ, cursor) = await LoraSearch.CivitaiAsync(family.Civitai, family.Id, query, LoraSort.Downloads, mature, null, CancellationToken.None);
    foreach (var r in civ.Take(12).Concat(hf.Take(12)))
        Console.WriteLine($"{r.Source,-12} {r.Name,-48} {r.BaseModel,-14} {(r.SizeBytes is long b ? $"{b / 1e6:0} MB" : ""),7} dl {r.Downloads,8} img {(r.ImageUrl is null ? "-" : r.ImageMature ? "blur" : "ok")} trig [{r.TriggerWords}]");
    Console.WriteLine($"Civitai {civ.Count} (more: {cursor is not null}), Hugging Face {hf.Count}");
    return civ.Count + hf.Count > 0 ? 0 : 1;
}

// Writes the app's PNG metadata into a copy of a PNG, reads it back and checks the prompt survives the round trip.
// Check the result in Explorer (Properties › Details) or with exiftool.
static int CheckMetadata(string input, string output)
{
    var d = new ImageDescription
    {
        Prompt = "a mango tree at dusk, \"golden\" light & <soft> haze, மாம்பழம் 🥭",
        NegativePrompt = "blurry, lowres",
        ModelName = "NoobAI-XL",
        Settings = new (string, string)[]
        {
            ("Steps", "28"), ("Sampler", "euler_a"), ("Schedule type", "karras"), ("CFG scale", "5.5"), ("Seed", "1234"),
            ("Size", "1024x1024"), ("Model", "NoobAI-XL"), ("Denoising strength", "0.55"), ("LoRAs", "pixel-art-xl 1, own LoRA 0.8"),
            ("Version", "ImageMango"),
        },
        Tags = new[] { "NoobAI-XL", "AI generated" },
        CreatedAt = DateTimeOffset.UtcNow,
    };
    File.WriteAllBytes(output, PngMetadata.Embed(File.ReadAllBytes(input), d));
    var text = PngMetadata.ReadText(output);
    foreach (var (k, v) in text) Console.WriteLine($"--- {k} ({v.Length} chars)\n{(v.Length > 400 ? v[..400] + "…" : v)}");
    var p = PngMetadata.ParseParameters(text["parameters"]);
    bool ok = p.Prompt == d.Prompt && p.NegativePrompt == d.NegativePrompt && p.Settings["Seed"] == "1234"
              && p.Settings["LoRAs"] == "pixel-art-xl 1, own LoRA 0.8" && p.Settings["Size"] == "1024x1024" && text["Description"] == d.Prompt;
    Console.WriteLine(ok ? "round trip OK" : $"ROUND TRIP FAILED: prompt=[{p.Prompt}] neg=[{p.NegativePrompt}] settings={string.Join("; ", p.Settings)}");
    return ok ? 0 : 1;
}

// LoRA family detection on real files (header only, via HTTP Range) and link resolution for Hugging Face and
// Civitai pages. With file arguments, only those local files are checked.
static async Task<int> CheckLoras(string[] files)
{
    if (files.Length > 0)
    {
        foreach (var f in files)
        {
            var (fam, src) = LoraLibrary.DetectFromFile(f);
            Console.WriteLine($"{fam ?? "?",-16} ({src ?? "-"})  {f}");
        }
        return 0;
    }

    // (repo, file, accepted families) — samples taken from Hugging Face on 2026-10-16.
    var samples = new (string Repo, string File, string[] Expect)[]
    {
        ("Shakker-Labs/AWPortrait-Z", "AWPortrait-Z.safetensors", new[] { "z-image" }),
        ("fal/flux-2-klein-4b-spritesheet-lora", "flux-spritesheet-lora.safetensors", new[] { "flux2-klein-4b" }),
        ("fal/flux-2-klein-4B-outpaint-lora", "LyNiaZ53Tudg0J6sT8Xbx_pytorch_lora_weights_comfy_converted.safetensors", new[] { "flux2-klein-4b" }),
        ("Limbicnation/pixel-art-lora", "pytorch_lora_weights.comfyui.safetensors", new[] { "flux2-klein-4b" }),
        ("dx8152/Flux2-Klein-9B-Consistency", "Flux2-Klein-9B-consistency-V2.safetensors", new[] { "flux2-klein-9b" }),
        ("Viggle/Qwen-Image-2.1-viggle-turbo", "Qwen-Image-2.1-viggle-turbo-4step-lora-r64.safetensors", new[] { "qwen-image-2.1" }),
        ("lilylilith/QI_2.1_AnyAngle", "QI2.1_AnyAngle.safetensors", new[] { "qwen-image-2.1" }),
        ("flymy-ai/qwen-image-realism-lora", "flymy_realism.safetensors", new[] { "qwen-image" }),
        ("lightx2v/Qwen-Image-Lightning", "Qwen-Image-Edit-2509/Qwen-Image-Edit-2509-Lightning-4steps-V1.0-bf16.safetensors", new[] { "qwen-image" }),
        ("codeShare/Drills3D_Chroma_LoRa", "drills3d_chroma_lora_e39.safetensors", new[] { "chroma" }),
        ("semiosphere/the_artist_for_chromaHD", "The_Artist.safetensors", new[] { "chroma", "flux1" }),
        ("latent-consistency/lcm-lora-sdxl", "pytorch_lora_weights.safetensors", new[] { "sdxl" }),
        ("nerijs/pixel-art-xl", "pixel-art-xl.safetensors", new[] { "sdxl" }),
        ("fofr/sdxl-emoji", "lora.safetensors", new[] { "sdxl" }),
        ("XLabs-AI/flux-RealismLora", "lora.safetensors", new[] { "flux1" }),
        ("ByteDance/Hyper-SD", "Hyper-FLUX.1-dev-16steps-lora.safetensors", new[] { "flux1" }),
        ("latent-consistency/lcm-lora-sdv1-5", "pytorch_lora_weights.safetensors", new[] { "sd1" }),
    };
    using var http = new HttpClient();
    http.DefaultRequestHeaders.UserAgent.ParseAdd("ImageMango-check/1.0");
    int bad = 0;
    foreach (var (repo, file, expect) in samples)
    {
        var url = $"https://huggingface.co/{repo}/resolve/main/{file}";
        try
        {
            var len = BitConverter.ToInt64(await RangeAsync(http, url, 0, 7));
            using var doc = System.Text.Json.JsonDocument.Parse(await RangeAsync(http, url, 8, 8 + len - 1));
            var (fam, src) = LoraLibrary.DetectFromHeader(doc.RootElement);
            bool ok = fam is not null && expect.Contains(fam);
            if (!ok) bad++;
            Console.WriteLine($"{(ok ? "ok " : "BAD")} {fam ?? "?",-16} ({src ?? "-"}) expected {string.Join("/", expect)}  {repo}");
        }
        catch (Exception ex)
        {
            bad++;
            Console.WriteLine($"ERR {repo}: {ex.Message}");
        }
    }

    // Link resolution: family and trigger words from the Hugging Face card and the Civitai API.
    var links = new (string Link, string Expect)[]
    {
        ("https://huggingface.co/Shakker-Labs/AWPortrait-Z", "z-image"),
        ("https://huggingface.co/nerijs/pixel-art-xl/blob/main/pixel-art-xl.safetensors", "sdxl"),
        ("https://huggingface.co/dx8152/Flux2-Klein-9B-Consistency", "flux2-klein-9b"),
        ("https://civitai.com/models/122359/detail-tweaker-xl", "sdxl"),
    };
    foreach (var (link, expect) in links)
    {
        try
        {
            var list = await LoraSources.ResolveAsync(link, null, CancellationToken.None);
            bool ok = list.Count > 0 && list.All(c => c.Family == expect);
            if (!ok) bad++;
            Console.WriteLine($"{(ok ? "ok " : "BAD")} {link}");
            foreach (var c in list.Take(4))
                Console.WriteLine($"      {c.Label}  trigger='{c.TriggerWords}'  {c.DownloadUrl}");
        }
        catch (Exception ex)
        {
            bad++;
            Console.WriteLine($"ERR {link}: {ex.Message}");
        }
    }
    Console.WriteLine(bad == 0 ? "All LoRA checks OK." : $"{bad} LoRA check problems.");
    return bad == 0 ? 0 : 1;

    static async Task<byte[]> RangeAsync(HttpClient http, string url, long from, long to)
    {
        using var req = new HttpRequestMessage(HttpMethod.Get, url);
        req.Headers.Range = new System.Net.Http.Headers.RangeHeaderValue(from, to);
        using var resp = await http.SendAsync(req);
        resp.EnsureSuccessStatusCode();
        return await resp.Content.ReadAsByteArrayAsync();
    }
}

// Validates the catalog, prints what each model resolves to for the given VRAM, and
// optionally checks every download URL exists with the expected size.
static async Task<int> CheckCatalog(string path, double vram, bool urls)
{
    var doc = Json.Load<CatalogDocument>(path) ?? throw new Exception("cannot read " + path);
    foreach (var m in doc.Models) m.Validate();
    Console.WriteLine($"catalog {doc.CatalogVersion}: {doc.Models.Count} models, VRAM {vram} GB");
    foreach (var m in doc.Models)
    {
        var tier = m.SelectTier(vram);
        var s = m.Recommended(vram);
        var files = m.FilesFor(tier);
        Console.WriteLine($"- {m.Id,-20} [{m.Category,-10}] tier {tier.Label,-10} {files.Sum(f => f.SizeGb),5:0.0} GB  " +
                          $"{s.Width}x{s.Height} steps={s.Steps} cfg={s.CfgScale} sampler={s.Sampler} offload={s.OffloadToCpu} wtype={s.WeightType ?? "-"}");
        Console.WriteLine($"    {string.Join(", ", files.Select(f => f.Path.Split('/')[^1]))}");
    }
    if (!urls) return 0;

    using var http = new HttpClient();
    http.DefaultRequestHeaders.UserAgent.ParseAdd("ImageMango-check/1.0");
    int bad = 0;
    var all = doc.Models.SelectMany(m => m.Files.Values).GroupBy(f => f.Url).Select(g => g.First())
        .Append(new CatalogFile { Repo = NsfwClassifier.Repo, Path = NsfwClassifier.RepoPath, SizeGb = NsfwClassifier.ApproxSizeGb });
    foreach (var f in all)
    {
        using var req = new HttpRequestMessage(HttpMethod.Head, f.Url);
        using var resp = await http.SendAsync(req);
        long len = resp.Content.Headers.ContentLength ?? -1;
        double gb = len / 1e9;
        bool ok = resp.IsSuccessStatusCode && Math.Abs(gb - f.SizeGb) < 0.02 + f.SizeGb * 0.01;
        if (!ok) bad++;
        Console.WriteLine($"{(ok ? "ok " : "BAD")} {(int)resp.StatusCode} {gb,6:0.00} GB (catalog {f.SizeGb:0.00})  {f.Repo}/{f.Path}");
    }
    Console.WriteLine(bad == 0 ? "All URLs OK." : $"{bad} URL problems.");
    return bad == 0 ? 0 : 1;
}

static int CheckLayout(string path)
{
    var asm = typeof(SdNative).Assembly;
    int bad = 0, checkedCount = 0;
    foreach (var line in File.ReadAllLines(path))
    {
        var parts = line.Split(' ');
        if (parts.Length == 3 && parts[1] == "size")
        {
            var t = asm.GetType("ImageMango.Native." + parts[0]) ?? throw new Exception("missing type " + parts[0]);
            int cs = Marshal.SizeOf(t), c = int.Parse(parts[2]);
            checkedCount++;
            if (cs != c) { bad++; Console.WriteLine($"SIZE MISMATCH {parts[0]}: C={c} C#={cs}"); }
        }
        else if (parts.Length == 2)
        {
            var dot = parts[0].IndexOf('.');
            var t = asm.GetType("ImageMango.Native." + parts[0][..dot]) ?? throw new Exception("missing type " + parts[0]);
            var field = parts[0][(dot + 1)..];
            int cs = (int)Marshal.OffsetOf(t, field), c = int.Parse(parts[1]);
            checkedCount++;
            if (cs != c) { bad++; Console.WriteLine($"OFFSET MISMATCH {parts[0]}: C={c} C#={cs}"); }
        }
    }
    Console.WriteLine(bad == 0 ? $"OK: {checkedCount} sizes/offsets match." : $"{bad} mismatches.");
    return bad == 0 ? 0 : 1;
}

static int Gen(string[] a)
{
    Console.WriteLine(SdEngine.EngineInfo());
    using var engine = new SdEngine();
    engine.Log += (lvl, msg) => { if (lvl >= SdLogLevel.Info) Console.WriteLine($"[{lvl}] {msg}"); };
    engine.Progress += (s, n, t) => Console.Write($"\rstep {s}/{n} {t:F2}s/it   ");
    int previews = 0;
    engine.Preview += _ => previews++;

    var load = engine.EnsureLoaded(new EngineLoadOptions
    {
        DiffusionModelPath = a[1], LlmPath = a[2], VaePath = a[3],
        OffloadToCpu = Environment.GetEnvironmentVariable("IC_OFFLOAD") != "0",
    });
    Console.WriteLine($"loaded in {load.TotalSeconds:F1}s, model version: {engine.ModelVersion}");

    int size = a.Length >= 7 ? int.Parse(a[6]) : 256;
    int steps = a.Length >= 8 ? int.Parse(a[7]) : 4;
    var res = engine.Generate(new GenerateRequest
    {
        Prompt = "a red apple on a wooden table, studio photo",
        Width = size, Height = size, Steps = steps, CfgScale = 1f, Seed = 42, LivePreview = true,
    }, CancellationToken.None);
    // Second identical-size run: the model and conditioning cache are warm, this is the steady-state speed.
    var warm = engine.Generate(new GenerateRequest
    {
        Prompt = "a green pear on a wooden table, studio photo",
        Width = size, Height = size, Steps = steps, CfgScale = 1f, Seed = 43, LivePreview = false,
    }, CancellationToken.None);
    Console.WriteLine($"\nwarm run: {warm.GenerateTime.TotalSeconds:F1}s");
    var img = res.Images[0];
    Console.WriteLine($"\ngenerated {res.Images.Count} image {img.Width}x{img.Height}x{img.Channels} in {res.GenerateTime.TotalSeconds:F1}s ({res.Sampler}/{res.Scheduler}), previews={previews}");

    using (var f = File.Create(a[4]))
    {
        var header = System.Text.Encoding.ASCII.GetBytes($"P6\n{img.Width} {img.Height}\n255\n");
        f.Write(header);
        if (img.Channels == 3) f.Write(img.Pixels);
    }

    // Second run reuses the loaded context and checks cancellation does not break later runs.
    using var cts = new CancellationTokenSource();
    engine.Progress += (s, n, t) => { if (s >= 1) cts.Cancel(); };
    var cancelled = engine.Generate(new GenerateRequest { Prompt = "a cat", Width = 256, Height = 256, Steps = 6, CfgScale = 1f, Seed = 7 }, cts.Token);
    Console.WriteLine($"\ncancel test: cancelled={cancelled.Cancelled}, images={cancelled.Images.Count}, after {cancelled.GenerateTime.TotalSeconds:F1}s");

    if (a.Length >= 6 && a[5].Length > 0)
    {
        using var clf = new NsfwClassifier(a[5]);
        Console.WriteLine($"nsfw score: {clf.Score(img):F3}");
    }
    return 0;
}
