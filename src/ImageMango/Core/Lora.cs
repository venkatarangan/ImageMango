using System.IO;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace ImageMango.Core;

/// <summary>
/// A model architecture that LoRAs are trained for. A LoRA only works with models of its own family
/// (an SDXL LoRA works with every SDXL fine-tune, but not with Z-Image or FLUX).
/// </summary>
public sealed record LoraFamily(string Id, string Name, string[] HuggingFace, string[] Civitai);

public enum LoraFit { Match, Maybe, Unknown, Mismatch }

public static class LoraFamilies
{
    /// <summary>
    /// HuggingFace: base repos whose "adapter" models fit (the Hugging Face browse link uses the first ones).
    /// Civitai: Civitai's base-model names (checked against its API on 2026-10-16).
    /// </summary>
    public static readonly IReadOnlyList<LoraFamily> All = new[]
    {
        // Illustrious, NoobAI and Pony are SDXL fine-tunes: their LoRAs are SDXL LoRAs.
        new LoraFamily("sdxl", "SDXL",
            new[] { "stabilityai/stable-diffusion-xl-base-1.0", "OnomaAIResearch/Illustrious-xl-early-release-v0", "OnomaAIResearch/Illustrious-XL-v2.0", "Laxhar/noobai-XL-1.1", "SG161222/RealVisXL_V5.0" },
            new[] { "SDXL 1.0", "Illustrious", "NoobAI", "Pony", "SDXL 0.9", "SDXL Turbo", "SDXL Lightning", "SDXL Hyper" }),
        new LoraFamily("z-image", "Z-Image",
            new[] { "Tongyi-MAI/Z-Image-Turbo", "Tongyi-MAI/Z-Image" },
            new[] { "ZImageTurbo", "ZImageBase" }),
        new LoraFamily("flux2-klein-4b", "FLUX.2 [klein] 4B",
            new[] { "black-forest-labs/FLUX.2-klein-4B", "black-forest-labs/FLUX.2-klein-base-4B" },
            new[] { "Flux.2 Klein 4B", "Flux.2 Klein 4B-base" }),
        new LoraFamily("flux2-klein-9b", "FLUX.2 [klein] 9B",
            new[] { "black-forest-labs/FLUX.2-klein-9B", "black-forest-labs/FLUX.2-klein-base-9B" },
            new[] { "Flux.2 Klein 9B", "Flux.2 Klein 9B-base" }),
        new LoraFamily("qwen-image-2.1", "Qwen-Image 2.1",
            new[] { "Qwen/Qwen-Image-2.1" },
            new[] { "Qwen 2.1" }),
        new LoraFamily("chroma", "Chroma",
            new[] { "lodestones/Chroma1-HD", "lodestones/Chroma", "lodestones/Chroma1-Base" },
            new[] { "Chroma" }),
        // Families with no model in the catalog: recognised so the app can say what a LoRA is for.
        new LoraFamily("flux1", "FLUX.1",
            new[] { "black-forest-labs/FLUX.1-dev", "black-forest-labs/FLUX.1-schnell", "black-forest-labs/FLUX.1-Krea-dev" },
            new[] { "Flux.1 D", "Flux.1 S", "Flux.1 Krea", "Flux.1 Kontext" }),
        new LoraFamily("flux2-dev", "FLUX.2 [dev]",
            new[] { "black-forest-labs/FLUX.2-dev" },
            new[] { "Flux.2 D" }),
        new LoraFamily("qwen-image", "Qwen-Image (first generation)",
            new[] { "Qwen/Qwen-Image", "Qwen/Qwen-Image-2512", "Qwen/Qwen-Image-Edit", "Qwen/Qwen-Image-Edit-2509" },
            new[] { "Qwen" }),
        new LoraFamily("sd1", "Stable Diffusion 1.5",
            new[] { "stable-diffusion-v1-5/stable-diffusion-v1-5", "runwayml/stable-diffusion-v1-5" },
            new[] { "SD 1.5", "SD 1.4", "SD 1.5 LCM", "SD 1.5 Hyper" }),
    };

    public static LoraFamily? Get(string? id) => id is null ? null : All.FirstOrDefault(f => f.Id == id);

    public static string NameOf(string? id) => Get(id)?.Name ?? id ?? "unknown";

    public static string? FromHuggingFaceBase(string? repo) =>
        repo is null ? null : All.FirstOrDefault(f => f.HuggingFace.Contains(repo.Trim(), StringComparer.OrdinalIgnoreCase))?.Id;

    public static string? FromCivitaiBase(string? baseModel) =>
        baseModel is null ? null : All.FirstOrDefault(f => f.Civitai.Contains(baseModel.Trim(), StringComparer.OrdinalIgnoreCase))?.Id;

    /// <summary>How well a LoRA of <paramref name="loraFamily"/> fits a model of <paramref name="modelFamily"/>.</summary>
    public static LoraFit Fit(string? loraFamily, string? modelFamily)
    {
        if (loraFamily is null || modelFamily is null) return LoraFit.Unknown;
        if (loraFamily == modelFamily) return LoraFit.Match;
        // Chroma is a retrained FLUX.1-schnell: FLUX.1 LoRAs load, with mixed results.
        if ((loraFamily, modelFamily) is ("flux1", "chroma") or ("chroma", "flux1")) return LoraFit.Maybe;
        return LoraFit.Mismatch;
    }
}

/// <summary>One LoRA file in the library folder, with what is known about it.</summary>
public sealed class LoraInfo
{
    public required string Path { get; init; }
    public string FileName => System.IO.Path.GetFileName(Path);
    public string Name { get; set; } = "";
    /// <summary>Family id (see <see cref="LoraFamilies"/>) or null when it couldn't be worked out.</summary>
    public string? Family { get; set; }
    /// <summary>Where <see cref="Family"/> came from: "Civitai", "Hugging Face", "file metadata", "file contents".</summary>
    public string? FamilySource { get; set; }
    /// <summary>Words the LoRA was trained on; put them in the prompt to switch it on.</summary>
    public string TriggerWords { get; set; } = "";
    public double? PreferredWeight { get; set; }
    public string? SourceUrl { get; set; }
    public string? Description { get; set; }
    public long SizeBytes { get; set; }

    public string SizeText => SizeBytes >= 1_000_000_000 ? $"{SizeBytes / 1e9:0.0} GB" : $"{SizeBytes / 1e6:0} MB";
}

/// <summary>
/// The LoRA folder (models\loras). Each LoRA may have a "{name}.json" sidecar in the format AUTOMATIC1111/Forge
/// use ("activation text", "preferred weight", "sd version", "description"), plus an "imagemango" object with
/// the family and download source. "{name}.civitai.info" files from Civitai Helper are read too.
/// </summary>
public static class LoraLibrary
{
    private static readonly Dictionary<string, (DateTime Stamp, string? Family, string? Source)> HeaderCache = new(StringComparer.OrdinalIgnoreCase);

    public static List<LoraInfo> Scan(string dir)
    {
        var list = new List<LoraInfo>();
        if (!Directory.Exists(dir)) return list;
        foreach (var path in Directory.EnumerateFiles(dir, "*.safetensors", SearchOption.AllDirectories))
        {
            try { list.Add(Load(path)); }
            catch { /* unreadable file: skip it */ }
        }
        return list.OrderBy(l => l.Name, StringComparer.CurrentCultureIgnoreCase).ToList();
    }

    public static LoraInfo Load(string path)
    {
        var fi = new FileInfo(path);
        var info = new LoraInfo { Path = fi.FullName, SizeBytes = fi.Length, Name = PrettyName(fi.Name) };

        var civitai = SidecarPath(path, ".civitai.info");
        if (File.Exists(civitai))
        {
            try
            {
                var c = JsonNode.Parse(File.ReadAllText(civitai)) as JsonObject;
                info.TriggerWords = JoinWords(c?["trainedWords"]);
                info.Family = LoraFamilies.FromCivitaiBase(Str(c?["baseModel"]));
                if (info.Family is not null) info.FamilySource = "Civitai";
                if (Str(c?["model"]?["name"]) is { Length: > 0 } n) info.Name = n;
            }
            catch { }
        }

        var json = SidecarPath(path, ".json");
        if (File.Exists(json))
        {
            try
            {
                var j = JsonNode.Parse(File.ReadAllText(json)) as JsonObject;
                if (Str(j?["activation text"]) is { Length: > 0 } t) info.TriggerWords = t;
                if (j?["preferred weight"] is JsonValue w && w.TryGetValue<double>(out var pw) && pw != 0) info.PreferredWeight = pw;
                if (Str(j?["description"]) is { Length: > 0 } d) info.Description = d;
                var im = j?["imagemango"] as JsonObject;
                if (Str(im?["name"]) is { Length: > 0 } n) info.Name = n;
                if (Str(im?["source"]) is { Length: > 0 } s) info.SourceUrl = s;
                if (Str(im?["family"]) is { Length: > 0 } f)
                {
                    info.Family = f;
                    info.FamilySource = Str(im?["familySource"]) ?? "you";
                }
                else if (info.Family is null)
                {
                    info.Family = Str(j?["sd version"]) switch { "SDXL" => "sdxl", "SD1" => "sd1", _ => null };
                    if (info.Family is not null) info.FamilySource = "sidecar";
                }
            }
            catch { }
        }

        if (info.Family is null)
        {
            var (family, source) = DetectCached(fi);
            info.Family = family;
            info.FamilySource = source;
        }
        return info;
    }

    /// <summary>Writes what we know about a LoRA to its "{name}.json" sidecar, keeping keys other tools wrote.</summary>
    public static void SaveSidecar(LoraInfo info)
    {
        var path = SidecarPath(info.Path, ".json");
        JsonObject j;
        try { j = File.Exists(path) ? JsonNode.Parse(File.ReadAllText(path)) as JsonObject ?? new() : new(); }
        catch { j = new(); }
        j["activation text"] = info.TriggerWords;
        if (info.PreferredWeight is double w) j["preferred weight"] = w;
        j["sd version"] = info.Family switch { "sdxl" => "SDXL", "sd1" => "SD1", _ => "Unknown" };
        if (!string.IsNullOrEmpty(info.Description)) j["description"] = info.Description;
        var im = j["imagemango"] as JsonObject ?? new JsonObject();
        im["name"] = info.Name;
        if (info.Family is not null) { im["family"] = info.Family; im["familySource"] = info.FamilySource; }
        if (info.SourceUrl is not null) im["source"] = info.SourceUrl;
        j["imagemango"] = im;
        File.WriteAllText(path, j.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
    }

    public static string SidecarPath(string loraPath, string extension) =>
        System.IO.Path.Combine(System.IO.Path.GetDirectoryName(loraPath)!, System.IO.Path.GetFileNameWithoutExtension(loraPath) + extension);

    /// <summary>"my_cool-style_v2.safetensors" → "my cool-style v2".</summary>
    public static string PrettyName(string fileName) =>
        System.IO.Path.GetFileNameWithoutExtension(fileName).Replace('_', ' ').Trim();

    /// <summary>A file name that is safe on Windows and not taken yet in <paramref name="dir"/>.</summary>
    public static string UniquePath(string dir, string fileName)
    {
        var clean = string.Concat(fileName.Select(c => System.IO.Path.GetInvalidFileNameChars().Contains(c) ? '_' : c)).Trim();
        if (!clean.EndsWith(".safetensors", StringComparison.OrdinalIgnoreCase)) clean += ".safetensors";
        var stem = System.IO.Path.GetFileNameWithoutExtension(clean);
        var path = System.IO.Path.Combine(dir, clean);
        for (int i = 2; File.Exists(path) || File.Exists(path + ".part"); i++)
            path = System.IO.Path.Combine(dir, $"{stem} ({i}).safetensors");
        return path;
    }

    private static (string? Family, string? Source) DetectCached(FileInfo fi)
    {
        lock (HeaderCache)
            if (HeaderCache.TryGetValue(fi.FullName, out var c) && c.Stamp == fi.LastWriteTimeUtc) return (c.Family, c.Source);
        var r = DetectFromFile(fi.FullName);
        lock (HeaderCache) HeaderCache[fi.FullName] = (fi.LastWriteTimeUtc, r.Family, r.Source);
        return r;
    }

    /// <summary>Reads the safetensors header (tensor names, shapes, training metadata) and guesses the family.</summary>
    public static (string? Family, string? Source) DetectFromFile(string path)
    {
        try
        {
            using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
            var lenBytes = new byte[8];
            fs.ReadExactly(lenBytes);
            long len = BitConverter.ToInt64(lenBytes);
            if (len <= 0 || len > 100_000_000) return (null, null);
            var header = new byte[len];
            fs.ReadExactly(header);
            using var doc = JsonDocument.Parse(header);
            return DetectFromHeader(doc.RootElement);
        }
        catch
        {
            return (null, null);
        }
    }

    public static (string? Family, string? Source) DetectFromHeader(JsonElement root)
    {
        if (root.TryGetProperty("__metadata__", out var meta) && meta.ValueKind == JsonValueKind.Object)
        {
            string M(string key) => meta.TryGetProperty(key, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString()!.ToLowerInvariant() : "";
            var fromMeta = FamilyFromTrainingTag(M("ss_base_model_version"), M("modelspec.architecture"), M("ss_model_type"));
            if (fromMeta is not null) return (fromMeta, "file metadata");
        }

        var tensors = new List<(string Key, int[] Shape)>();
        foreach (var p in root.EnumerateObject())
        {
            if (p.Name == "__metadata__" || p.Value.ValueKind != JsonValueKind.Object) continue;
            int[] shape = p.Value.TryGetProperty("shape", out var s) && s.ValueKind == JsonValueKind.Array
                ? s.EnumerateArray().Select(e => e.TryGetInt32(out var n) ? n : 0).ToArray()
                : Array.Empty<int>();
            tensors.Add((p.Name.ToLowerInvariant(), shape));
        }
        var family = FamilyFromTensors(tensors);
        return (family, family is null ? null : "file contents");
    }

    /// <summary>Training tools write the base model into the metadata (kohya: ss_base_model_version, modelspec.architecture).</summary>
    private static string? FamilyFromTrainingTag(string ssBase, string arch, string modelType)
    {
        string t = ssBase.Length > 0 ? ssBase : modelType;
        if (t.Contains("klein")) return t.Contains("9b") ? "flux2-klein-9b" : t.Contains("4b") ? "flux2-klein-4b" : null;
        if (t.StartsWith("sdxl")) return "sdxl";
        if (t.StartsWith("sd_v1") || t.StartsWith("sd_1") || t == "sd1") return "sd1";
        if (t.Contains("zimage") || t.Contains("z_image") || t.Contains("z-image")) return "z-image";
        if (t.Contains("chroma")) return "chroma";
        if (t.StartsWith("flux2")) return "flux2-dev";
        if (t.StartsWith("flux1") || t == "flux") return "flux1";
        // "qwen_image" (first generation) vs "qwen_image_2" are told apart by tensor size below.
        if (arch.StartsWith("stable-diffusion-xl")) return "sdxl";
        if (arch.StartsWith("stable-diffusion-v1")) return "sd1";
        if (arch.StartsWith("flux-1")) return "flux1";
        if (arch.Contains("chroma")) return "chroma";
        return null;
    }

    private static readonly Regex Index = new(@"(?:double_blocks|single_blocks|single_transformer_blocks|transformer_blocks)[._](\d+)[._]", RegexOptions.Compiled);

    /// <summary>Recognises the architecture from LoRA tensor names and widths (samples from Hugging Face, 2026-10).</summary>
    private static string? FamilyFromTensors(List<(string Key, int[] Shape)> t)
    {
        if (t.Count == 0) return null;
        bool Has(string s) => t.Any(x => x.Key.Contains(s, StringComparison.Ordinal));
        bool IsDown(string k) => k.Contains("lora_a") || k.Contains("lora_down") || k.Contains("lora.down") || k.EndsWith(".down.weight");
        // The input width of the first LoRA "down" matrix on a tensor matching the filter.
        int InWidth(Func<string, bool> filter) =>
            t.Where(x => filter(x.Key) && IsDown(x.Key) && x.Shape.Length == 2).Select(x => x.Shape[1]).FirstOrDefault();
        int MaxIndex(string block) =>
            t.Select(x => Index.Match(x.Key)).Where(m => m.Success && m.Value.StartsWith(block)).Select(m => int.Parse(m.Groups[1].Value)).DefaultIfEmpty(-1).Max();

        // Stable Diffusion UNets: SDXL has a second text encoder and 2048-wide cross-attention.
        if (Has("lora_te2_") || Has("text_encoder_2")) return "sdxl";
        if (Has("input_blocks") || Has("down_blocks") || Has("output_blocks") || Has("up_blocks"))
        {
            int cross = InWidth(k => k.Contains("attn2") && k.Contains("to_k"));
            return cross switch { 2048 => "sdxl", 768 => "sd1", _ => cross == 0 ? "sdxl" : null };
        }

        // Z-Image (Lumina-style DiT): layers.N.attention / feed_forward / adaLN_modulation.
        if (Has("layers.") && (Has("adaln_modulation") || Has(".attention.to_") || Has("feed_forward.w"))) return "z-image";

        // FLUX-style: double/single stream blocks (BFL or kohya naming) or diffusers single_transformer_blocks.
        bool bfl = Has("double_blocks") || Has("single_blocks");
        bool diffusersFlux = Has("single_transformer_blocks");
        if (bfl || diffusersFlux)
        {
            // Attention/projection inputs are the model width; MLP outputs and single-block linear2 are wider.
            int width = bfl
                ? InWidth(k => (k.Contains("double_blocks") && !k.Contains("mlp")) || (k.Contains("single_blocks") && k.Contains("linear1")))
                : InWidth(k => (k.Contains("transformer_blocks") && (k.Contains("to_q") || k.Contains("to_qkv") || k.Contains("add_q_proj"))));
            int doubles = bfl ? MaxIndex("double_blocks") + 1 : t.Select(x => Index.Match(x.Key))
                .Where(m => m.Success && m.Value.StartsWith("transformer_blocks")).Select(m => int.Parse(m.Groups[1].Value)).DefaultIfEmpty(-1).Max() + 1;
            bool flux2Names = Has("to_qkv_mlp_proj") || Has("double_stream_modulation");
            if (width == 6144) return "flux2-dev";
            if (width == 4096) return "flux2-klein-9b";
            if (width == 3072)
            {
                if (flux2Names || (doubles > 0 && doubles <= 6)) return "flux2-klein-4b";
                return "flux1";
            }
            return null;
        }

        // Qwen-Image (MMDiT with transformer_blocks only): 3072 wide for the first generation, 4096 for 2.1.
        if (Has("transformer_blocks") && (Has("img_mlp") || Has("txt_mlp") || Has("attn.to_q") || Has("attn.add_q_proj") || Has("img_mod")))
        {
            int width = InWidth(k => k.Contains("transformer_blocks") && (k.Contains("to_q") || k.Contains("to_k") || k.Contains("add_q_proj")));
            return width switch { 3072 => "qwen-image", 4096 => "qwen-image-2.1", _ => null };
        }
        return null;
    }

    private static string? Str(JsonNode? n) => n is JsonValue v && v.TryGetValue<string>(out var s) ? s : null;

    internal static string JoinWords(JsonNode? n) => n switch
    {
        JsonArray a => string.Join(", ", a.Select(Str).Where(s => !string.IsNullOrWhiteSpace(s)).Select(s => s!.Trim().TrimEnd(','))),
        JsonValue => Str(n)?.Trim() ?? "",
        _ => "",
    };
}

/// <summary>A downloadable LoRA file found behind a link the user pasted.</summary>
public sealed class LoraCandidate
{
    public required string Name { get; init; }
    public required string FileName { get; init; }
    public required string DownloadUrl { get; init; }
    public long? SizeBytes { get; init; }
    public string? Family { get; init; }
    public string? FamilySource { get; init; }
    public string TriggerWords { get; init; } = "";
    public string? Description { get; init; }
    public required string SourceUrl { get; init; }

    public string Label => $"{Name}  ·  {FileName}" +
                           (SizeBytes is long b ? (b >= 1_000_000_000 ? $"  ·  {b / 1e9:0.0} GB" : $"  ·  {b / 1e6:0} MB") : "") +
                           (Family is not null ? $"  ·  for {LoraFamilies.NameOf(Family)}" : "");
}

/// <summary>
/// Turns a pasted link into downloadable LoRA files: Hugging Face repo or file links, Civitai model pages,
/// or a direct link to a .safetensors file. Only the site in the link is contacted.
/// </summary>
public static class LoraSources
{
    public static async Task<IReadOnlyList<LoraCandidate>> ResolveAsync(string link, string? hfToken, CancellationToken ct)
    {
        if (!Uri.TryCreate(link.Trim(), UriKind.Absolute, out var uri) || (uri.Scheme != "https" && uri.Scheme != "http"))
            throw new ArgumentException("That doesn't look like a web link. Paste the address of a Hugging Face or Civitai page, or of a .safetensors file.");

        if (Downloader.IsHuggingFace(uri)) return await FromHuggingFaceAsync(uri, hfToken, ct);
        if (uri.Host.EndsWith("civitai.com", StringComparison.OrdinalIgnoreCase) || uri.Host.EndsWith("civitai.green", StringComparison.OrdinalIgnoreCase))
            return await FromCivitaiAsync(uri, ct);
        if (uri.AbsolutePath.EndsWith(".safetensors", StringComparison.OrdinalIgnoreCase))
        {
            var file = Uri.UnescapeDataString(uri.Segments[^1]);
            return new[] { new LoraCandidate { Name = LoraLibrary.PrettyName(file), FileName = file, DownloadUrl = uri.ToString(), SourceUrl = uri.ToString() } };
        }
        throw new ArgumentException("ImageMango can download LoRAs from Hugging Face and Civitai links, or from a direct link to a .safetensors file.");
    }

    // ------------------------------------------------------------------ Hugging Face

    private static async Task<IReadOnlyList<LoraCandidate>> FromHuggingFaceAsync(Uri uri, string? token, CancellationToken ct)
    {
        var seg = uri.AbsolutePath.Trim('/').Split('/').Select(Uri.UnescapeDataString).ToArray();
        if (seg.Length < 2 || seg[0] is "spaces" or "datasets" or "docs" or "models" or "blog")
            throw new ArgumentException("Paste the link of a Hugging Face model page (huggingface.co/owner/name) or of a file in it.");
        string repo = $"{seg[0]}/{seg[1]}";
        string? filePath = null, rev = "main";
        if (seg.Length >= 5 && seg[2] is "blob" or "resolve")
        {
            rev = seg[3];
            filePath = string.Join('/', seg.Skip(4));
        }

        // The repo's card says which base model the LoRA is for and its trigger words ("instance_prompt").
        JsonObject? info = null;
        try { info = JsonNode.Parse(await Downloader.GetStringAsync($"https://huggingface.co/api/models/{repo}?blobs=true", token, ct)) as JsonObject; }
        catch when (filePath is not null) { /* a direct file link still works without the card */ }

        var card = info?["cardData"] as JsonObject;
        var bases = new List<string>();
        switch (card?["base_model"])
        {
            case JsonArray a: bases.AddRange(a.Select(n => n?.GetValue<string>() ?? "")); break;
            case JsonValue v when v.TryGetValue<string>(out var b): bases.Add(b); break;
        }
        if (info?["tags"] is JsonArray tags)
            foreach (var tag in tags.Select(n => n?.GetValue<string>() ?? ""))
                if (tag.StartsWith("base_model:adapter:")) bases.Add(tag["base_model:adapter:".Length..]);
                else if (tag.StartsWith("base_model:") && !tag.StartsWith("base_model:finetune:") && !tag.StartsWith("base_model:quantized:") && !tag.StartsWith("base_model:merge:"))
                    bases.Add(tag["base_model:".Length..]);
        string? family = bases.Select(LoraFamilies.FromHuggingFaceBase).FirstOrDefault(f => f is not null);
        string trigger = LoraLibrary.JoinWords(card?["instance_prompt"]);
        string repoName = seg[1].Replace('_', ' ').Replace('-', ' ');

        // Diffusers-trained LoRAs are all called pytorch_lora_weights.safetensors: save them under the repo's name.
        static bool Generic(string file) => file.ToLowerInvariant() is "pytorch_lora_weights.safetensors" or "lora.safetensors"
            or "adapter_model.safetensors" or "diffusion_pytorch_model.safetensors" or "model.safetensors";

        LoraCandidate Make(string path, long? size) => new()
        {
            Name = repoName,
            FileName = Generic(path.Split('/')[^1]) ? $"{seg[1]}.safetensors" : path.Split('/')[^1],
            DownloadUrl = $"https://huggingface.co/{repo}/resolve/{Uri.EscapeDataString(rev)}/{string.Join('/', path.Split('/').Select(Uri.EscapeDataString))}",
            SizeBytes = size,
            Family = family,
            FamilySource = family is null ? null : "Hugging Face",
            TriggerWords = trigger,
            SourceUrl = $"https://huggingface.co/{repo}",
        };

        if (filePath is not null)
        {
            if (!filePath.EndsWith(".safetensors", StringComparison.OrdinalIgnoreCase))
                throw new ArgumentException("That file isn't a .safetensors LoRA.");
            long? size = (info?["siblings"] as JsonArray)?.OfType<JsonObject>()
                .FirstOrDefault(s => s["rfilename"]?.GetValue<string>() == filePath)?["size"]?.GetValue<long>();
            return new[] { Make(filePath, size) };
        }

        var files = (info?["siblings"] as JsonArray ?? new JsonArray()).OfType<JsonObject>()
            .Select(s => (Path: s["rfilename"]?.GetValue<string>() ?? "", Size: s["size"] is JsonValue sz && sz.TryGetValue<long>(out var l) ? l : (long?)null))
            .Where(f => f.Path.EndsWith(".safetensors", StringComparison.OrdinalIgnoreCase)
                        && !f.Path.Contains("text_encoder") && !f.Path.Contains("vae/") && !f.Path.Contains("unet/") && !f.Path.Contains("transformer/"))
            .ToList();
        if (files.Count == 0)
            throw new InvalidOperationException($"No .safetensors file found in {repo}. It may not be a LoRA, or it may need a Hugging Face token (Settings › Storage).");
        return files.Select(f => Make(f.Path, f.Size)).ToList();
    }

    // ------------------------------------------------------------------ Civitai

    private static readonly Regex CivitaiModel = new(@"/models/(\d+)", RegexOptions.IgnoreCase);
    private static readonly Regex CivitaiDownload = new(@"/api/download/models/(\d+)", RegexOptions.IgnoreCase);

    private static async Task<IReadOnlyList<LoraCandidate>> FromCivitaiAsync(Uri uri, CancellationToken ct)
    {
        string? versionId = System.Web.HttpUtility.ParseQueryString(uri.Query)["modelVersionId"];
        if (CivitaiDownload.Match(uri.AbsolutePath) is { Success: true } d) versionId = d.Groups[1].Value;

        JsonObject model;
        List<JsonObject> versions;
        if (versionId is not null)
        {
            var v = JsonNode.Parse(await Downloader.GetStringAsync($"https://civitai.com/api/v1/model-versions/{versionId}", null, ct)) as JsonObject
                    ?? throw new InvalidOperationException("Civitai returned nothing for that link.");
            model = v["model"] as JsonObject ?? new JsonObject();
            versions = new() { v };
        }
        else if (CivitaiModel.Match(uri.AbsolutePath) is { Success: true } m)
        {
            model = JsonNode.Parse(await Downloader.GetStringAsync($"https://civitai.com/api/v1/models/{m.Groups[1].Value}", null, ct)) as JsonObject
                    ?? throw new InvalidOperationException("Civitai returned nothing for that link.");
            versions = (model["modelVersions"] as JsonArray ?? new JsonArray()).OfType<JsonObject>().ToList();
        }
        else throw new ArgumentException("Paste the link of a Civitai model page (civitai.com/models/…).");

        string type = model["type"]?.GetValue<string>() ?? "LORA";
        if (type is not ("LORA" or "LoCon" or "DoRA" or "LyCORIS"))
            throw new InvalidOperationException($"That Civitai page is a {type}, not a LoRA.");
        string modelName = model["name"]?.GetValue<string>() ?? "Civitai LoRA";
        string page = versionId is null ? uri.GetLeftPart(UriPartial.Path) : $"{uri.GetLeftPart(UriPartial.Path)}?modelVersionId={versionId}";

        var list = new List<LoraCandidate>();
        foreach (var v in versions)
        {
            var file = (v["files"] as JsonArray ?? new JsonArray()).OfType<JsonObject>()
                .Where(f => (f["name"]?.GetValue<string>() ?? "").EndsWith(".safetensors", StringComparison.OrdinalIgnoreCase))
                .OrderByDescending(f => f["primary"] is JsonValue p && p.TryGetValue<bool>(out var b) && b)
                .FirstOrDefault();
            if (file is null) continue;
            string baseModel = v["baseModel"]?.GetValue<string>() ?? "";
            string? family = LoraFamilies.FromCivitaiBase(baseModel);
            double kb = file["sizeKB"] is JsonValue s && s.TryGetValue<double>(out var k) ? k : 0;
            list.Add(new LoraCandidate
            {
                Name = versions.Count > 1 ? $"{modelName} ({v["name"]?.GetValue<string>()}, {baseModel})" : modelName,
                FileName = file["name"]!.GetValue<string>(),
                DownloadUrl = file["downloadUrl"]?.GetValue<string>() ?? $"https://civitai.com/api/download/models/{v["id"]}",
                SizeBytes = kb > 0 ? (long)(kb * 1024) : null,
                Family = family,
                FamilySource = family is null ? null : "Civitai",
                TriggerWords = LoraLibrary.JoinWords(v["trainedWords"]),
                SourceUrl = page,
            });
        }
        if (list.Count == 0) throw new InvalidOperationException("No .safetensors file found on that Civitai page.");
        return list;
    }
}
