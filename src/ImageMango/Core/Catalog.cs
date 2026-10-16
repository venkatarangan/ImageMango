using System.IO;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;

namespace ImageMango.Core;

public sealed class CatalogDocument
{
    public string CatalogVersion { get; set; } = "";
    public string? EngineRelease { get; set; }
    public string? Notes { get; set; }
    public List<CatalogModel> Models { get; set; } = new();

    [JsonIgnore] public string SourcePath { get; set; } = "";

    /// <summary>Loads the user's override in the config folder if present, else the bundled catalog.</summary>
    public static CatalogDocument Load()
    {
        var path = File.Exists(AppPaths.CatalogOverrideFile) ? AppPaths.CatalogOverrideFile : AppPaths.BundledCatalogFile;
        var doc = Json.Load<CatalogDocument>(path)
                  ?? throw new FileNotFoundException("Model catalog not found.", path);
        doc.SourcePath = path;
        foreach (var m in doc.Models) m.Validate();
        if (path != AppPaths.BundledCatalogFile) FillFromBundled(doc);
        return doc;
    }

    /// <summary>
    /// An override copied before 2026-10-16 has no LoRA family or prompt guide; take them from the bundled catalog by model id.
    /// </summary>
    private static void FillFromBundled(CatalogDocument doc)
    {
        if (doc.Models.All(m => m.Family is not null && m.PromptGuide is not null)) return;
        try
        {
            var bundled = Json.Load<CatalogDocument>(AppPaths.BundledCatalogFile);
            if (bundled is null) return;
            foreach (var m in doc.Models.Where(m => m.Family is null || m.PromptGuide is null))
                if (bundled.Models.FirstOrDefault(b => b.Id == m.Id) is { } b)
                {
                    m.Family ??= b.Family;
                    m.LoraCivitai ??= b.LoraCivitai;
                    m.LoraHuggingFace ??= b.LoraHuggingFace;
                    m.PromptGuide ??= b.PromptGuide;
                }
        }
        catch { /* LoRA matching just shows "unknown" */ }
    }
}

public sealed class CatalogModel
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public string Publisher { get; set; } = "";
    public string Released { get; set; } = "";
    /// <summary>filtered | unfiltered | nfaa</summary>
    public string Category { get; set; } = "unfiltered";
    public string License { get; set; } = "";
    public string PageUrl { get; set; } = "";
    public string Description { get; set; } = "";
    public string Tips { get; set; } = "";
    /// <summary>Example prompts that show off what this model does best (the "Sample prompts" button).</summary>
    public List<string> SamplePrompts { get; set; } = new();
    /// <summary>How to write prompts for this model: given to the prompt helper's language model.</summary>
    public string? PromptGuide { get; set; }
    /// <summary>
    /// Model architecture for LoRA matching (sdxl, z-image, flux2-klein-4b, qwen-image-2.1, chroma, …; see
    /// <see cref="LoraFamilies"/>). A LoRA works only with models of the family it was trained for.
    /// </summary>
    public string? Family { get; set; }
    /// <summary>Optional: Civitai base-model names to browse for this model, best first (default: the family's list).</summary>
    public List<string>? LoraCivitai { get; set; }
    /// <summary>Optional: Hugging Face base repos whose adapters fit this model, best first (default: the family's list).</summary>
    public List<string>? LoraHuggingFace { get; set; }
    public Dictionary<string, CatalogFile> Files { get; set; } = new();
    public JsonObject? Defaults { get; set; }
    public List<CatalogTier> Tiers { get; set; } = new();

    public void Validate()
    {
        if (Tiers.Count == 0) throw new InvalidDataException($"Model '{Id}' has no tiers.");
        foreach (var t in Tiers)
            foreach (var f in t.Files)
                if (!Files.ContainsKey(f))
                    throw new InvalidDataException($"Model '{Id}' tier {t.MinVramGb} GB references unknown file '{f}'.");
    }

    /// <summary>The tier with the largest minVramGb not above this machine's VRAM (0.5 GB tolerance).</summary>
    public CatalogTier SelectTier(double vramGb) =>
        Tiers.Where(t => t.MinVramGb <= vramGb + 0.5).OrderByDescending(t => t.MinVramGb).FirstOrDefault()
        ?? Tiers.OrderBy(t => t.MinVramGb).First();

    public IReadOnlyList<CatalogFile> FilesFor(CatalogTier tier) => tier.Files.Select(f => Files[f]).ToList();

    public GenSettings Recommended(double vramGb) => GenSettings.FromLayers(Defaults, SelectTier(vramGb).Settings);
}

public sealed class CatalogFile
{
    /// <summary>model | diffusion | clip_l | clip_g | t5xxl | llm | vae | taesd</summary>
    public string Role { get; set; } = "";
    public string Repo { get; set; } = "";
    public string Path { get; set; } = "";
    public double SizeGb { get; set; }

    public string Url => $"https://huggingface.co/{Repo}/resolve/main/{Uri.EscapeDataString(Path).Replace("%2F", "/")}";

    /// <summary>models\{owner}\{repo}\{path} — identical files shared by several models are stored once.</summary>
    public string LocalPath(string modelsDir) =>
        System.IO.Path.Combine(new[] { modelsDir }.Concat(Repo.Split('/')).Concat(Path.Split('/')).ToArray());
}

public sealed class CatalogTier
{
    public double MinVramGb { get; set; }
    public List<string> Files { get; set; } = new();
    public JsonObject? Settings { get; set; }

    [JsonIgnore] public string Label => MinVramGb <= 0 ? "under 8 GB" : $"{MinVramGb:0}+ GB";
}
