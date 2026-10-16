using System.Globalization;
using System.IO;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Windows.Input;
using ImageMango.Core;
using ImageMango.Native;

namespace ImageMango.UI;

/// <summary>Side panels, the metadata written into images, and reusing an image's prompt and settings.</summary>
public sealed partial class MainViewModel
{
    public ICommand ToggleModelsPanelCommand { get; }
    public ICommand ToggleSettingsPanelCommand { get; }
    public ICommand TogglePreviewFocusCommand { get; }
    public ICommand ReuseCurrentCommand { get; }

    // ---------------------------------------------------------------- side panels

    /// <summary>The model list on the left. Hiding it gives the preview the width (the window applies the column sizes).</summary>
    public bool ModelsPanelOpen
    {
        get => _settings.ModelsPanelOpen;
        set
        {
            if (_settings.ModelsPanelOpen == value) return;
            _settings.ModelsPanelOpen = value;
            Raise();
            Raise(nameof(PreviewFocused));
        }
    }

    /// <summary>The settings panel on the right.</summary>
    public bool SettingsPanelOpen
    {
        get => _settings.SettingsPanelOpen;
        set
        {
            if (_settings.SettingsPanelOpen == value) return;
            _settings.SettingsPanelOpen = value;
            Raise();
            Raise(nameof(PreviewFocused));
        }
    }

    /// <summary>Both side panels hidden: the preview has the whole width.</summary>
    public bool PreviewFocused => !ModelsPanelOpen && !SettingsPanelOpen;

    public double ModelsPanelWidth { get => _settings.ModelsPanelWidth; set => _settings.ModelsPanelWidth = Math.Max(300, value); }
    public double SettingsPanelWidth { get => _settings.SettingsPanelWidth; set => _settings.SettingsPanelWidth = Math.Max(320, value); }

    /// <summary>Hides both panels, or shows both again when they're already hidden.</summary>
    private void TogglePreviewFocus()
    {
        bool open = PreviewFocused;
        ModelsPanelOpen = open;
        SettingsPanelOpen = open;
    }

    // ---------------------------------------------------------------- metadata inside the PNG

    public bool EmbedMetadata { get => _settings.EmbedMetadata; set { _settings.EmbedMetadata = value; Raise(); _settings.Save(); } }

    /// <summary>
    /// What goes inside the PNG. Nothing about the user or the PC: no names, folders or file paths, no time zone
    /// (the time is UTC), no start-image file name, and LoRAs added from this PC are listed without their file names.
    /// </summary>
    private ImageDescription DescribeImage(ModelItemViewModel m, GenSettings s, RawImage img, string sampler, string scheduler,
        bool fromStartImage, IReadOnlyList<LoraItemViewModel> loras)
    {
        var inv = CultureInfo.InvariantCulture;
        var settings = new List<(string, string)>
        {
            ("Steps", s.Steps.ToString(inv)),
            ("Sampler", sampler),
            ("Schedule type", scheduler),
            ("CFG scale", s.CfgScale.ToString("0.##", inv)),
            ("Seed", img.Seed.ToString(inv)),
            ("Size", $"{img.Width}x{img.Height}"),
            ("Model", m.Name),
        };
        if (s.Guidance is double g) settings.Add(("Distilled CFG Scale", g.ToString("0.##", inv)));
        if (s.FlowShift is double f) settings.Add(("Shift", f.ToString("0.##", inv)));
        if (s.ClipSkip > 0) settings.Add(("Clip skip", s.ClipSkip.ToString(inv)));
        if (fromStartImage) settings.Add(("Denoising strength", InitStrength.ToString("0.00", inv)));
        if (loras.Count > 0)
            settings.Add(("LoRAs", string.Join(", ", loras.Select(l =>
                $"{(string.IsNullOrWhiteSpace(l.Info.SourceUrl) ? "own LoRA" : l.Name)} {l.Weight.ToString("0.##", inv)}"))));
        settings.Add(("Version", AppPaths.AppName));
        return new ImageDescription
        {
            Prompt = Prompt,
            NegativePrompt = s.NegativePrompt,
            ModelName = m.Name,
            Settings = settings,
            Tags = new[] { m.Name, "AI generated" },
            CreatedAt = DateTimeOffset.UtcNow,
            Software = AppPaths.AppName,
        };
    }

    /// <summary>The prompt stored with an image: its .json file, else the text inside the PNG. Null when it has none.</summary>
    private static string? StoredPrompt(string path)
    {
        try
        {
            var json = Path.ChangeExtension(path, ".json");
            if (File.Exists(json) && JsonNode.Parse(File.ReadAllText(json))?["prompt"]?.GetValue<string>() is { } p) return p;
        }
        catch { }
        var text = PngMetadata.ReadText(path);
        if (text.TryGetValue("parameters", out var parameters)) return PngMetadata.ParseParameters(parameters).Prompt;
        return text.TryGetValue("Description", out var d) ? d : null;
    }

    // ---------------------------------------------------------------- reuse an image's prompt and settings

    /// <summary>
    /// Puts back what made an image: its model, prompt, negative prompt, seed, size, steps, CFG, sampler and LoRAs.
    /// Reads the .json file when there is one, else the "parameters" text inside the PNG (also from other SD apps).
    /// Memory settings stay as they are; they belong to this PC, not the image.
    /// </summary>
    public void ReuseImage(string? path)
    {
        if (path is null || IsBusy) return;
        JsonObject? meta = null;
        try
        {
            var json = Path.ChangeExtension(path, ".json");
            if (File.Exists(json)) meta = JsonNode.Parse(File.ReadAllText(json)) as JsonObject;
        }
        catch { }
        if (meta?["prompt"] is not null)
        {
            ReuseFromSidecar(meta);
            return;
        }
        var text = PngMetadata.ReadText(path);
        if (text.TryGetValue("parameters", out var parameters))
        {
            ReuseFromParameters(PngMetadata.ParseParameters(parameters));
            return;
        }
        Status = "This image has no saved prompt (made in incognito, with \"Save the prompt and model inside images\" off, or by another app).";
    }

    private void ReuseFromSidecar(JsonObject meta)
    {
        var notes = new List<string>();
        if (meta["model"]?.GetValue<string>() is { } id && id != Selected?.Id)
        {
            if (Models.FirstOrDefault(x => x.Id == id) is { } model) Selected = model;
            else notes.Add($"its model ({meta["modelName"]?.GetValue<string>() ?? id}) isn't in the catalog");
        }
        var s = meta["settings"]?.Deserialize<GenSettings>(Json.Options);
        if (s is not null)
        {
            var c = CurrentSettings;
            c.Steps = s.Steps; c.CfgScale = s.CfgScale; c.Guidance = s.Guidance; c.FlowShift = s.FlowShift;
            c.Sampler = s.Sampler; c.Scheduler = s.Scheduler; c.ClipSkip = s.ClipSkip; c.NegativePrompt = s.NegativePrompt;
        }
        if (meta["width"]?.GetValue<int>() is int w && meta["height"]?.GetValue<int>() is int h)
        {
            CurrentSettings.Width = w;
            CurrentSettings.Height = h;
        }
        Prompt = meta["prompt"]?.GetValue<string>() ?? Prompt;
        if (meta["seed"]?.GetValue<long>() is long seed) Seed = seed;

        if (meta["loras"] is JsonArray loras)
        {
            RefreshLoraLibrary();
            ActiveLoras.Clear();
            foreach (var l in loras.OfType<JsonObject>())
            {
                var file = l["file"]?.GetValue<string>();
                var info = LoraLibrary.FirstOrDefault(x => string.Equals(x.FileName, file, StringComparison.OrdinalIgnoreCase));
                if (info is null) notes.Add($"LoRA {l["name"]?.GetValue<string>() ?? file} isn't in the LoRA folder");
                else ActiveLoras.Add(new LoraItemViewModel(info, l["weight"]?.GetValue<double>() ?? 0.8, SelectedFamily, SaveActiveLoras));
            }
            Raise(nameof(HasActiveLoras));
            SaveActiveLoras();
        }
        if (meta["startImage"]?["strength"]?.GetValue<double>() is double strength)
        {
            InitStrength = strength;
            if (!HasInitImage) notes.Add($"it was made from a start image (Change {strength:0.00}); choose that picture again to redo it");
        }
        Status = "Prompt and settings reused, with the same seed (🎲 for a new one)." + (notes.Count > 0 ? " Note: " + string.Join("; ", notes) + "." : "");
    }

    private void ReuseFromParameters(PngMetadata.ParsedParameters p)
    {
        var inv = CultureInfo.InvariantCulture;
        var notes = new List<string>();
        var set = p.Settings;
        if (set.TryGetValue("Model", out var modelName) && !string.Equals(modelName, Selected?.Name, StringComparison.OrdinalIgnoreCase))
        {
            if (Models.FirstOrDefault(x => string.Equals(x.Name, modelName, StringComparison.OrdinalIgnoreCase)) is { } model) Selected = model;
            else notes.Add($"it was made with {modelName}, which isn't in the catalog; the selected model is kept");
        }
        var c = CurrentSettings;
        Prompt = p.Prompt;
        c.NegativePrompt = p.NegativePrompt;
        if (set.TryGetValue("Seed", out var seed) && long.TryParse(seed, inv, out var sv)) Seed = sv;
        if (set.TryGetValue("Steps", out var steps) && int.TryParse(steps, inv, out var st)) c.Steps = st;
        if (set.TryGetValue("CFG scale", out var cfg) && double.TryParse(cfg, inv, out var cv)) c.CfgScale = cv;
        if (set.TryGetValue("Size", out var size) && size.Split('x') is [var ws, var hs] &&
            int.TryParse(ws, inv, out var w) && int.TryParse(hs, inv, out var h))
        {
            c.Width = w;
            c.Height = h;
        }
        if (set.TryGetValue("Sampler", out var sampler) && GenSettings.Samplers.Contains(sampler)) c.Sampler = sampler;
        if (set.TryGetValue("Schedule type", out var sched) && GenSettings.Schedulers.Contains(sched)) c.Scheduler = sched;
        if (set.ContainsKey("LoRAs") || set.ContainsKey("Lora hashes")) notes.Add("it used LoRAs; switch them on under Add LoRA");
        Status = "Prompt and settings reused from the text inside the image." + (notes.Count > 0 ? " Note: " + string.Join("; ", notes) + "." : "");
    }

    // ---------------------------------------------------------------- incognito

    /// <summary>
    /// After an incognito image is deleted by hand: also clear the prompt that made it, and the start image if it was
    /// that image, so nothing of it stays on screen.
    /// </summary>
    private void ForgetIncognitoInputs(IEnumerable<string> deletedImages)
    {
        Prompt = "";
        var names = deletedImages.Select(Path.GetFileName).ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (HasInitImage && names.Contains(InitImageName))
        {
            InitImage = null;
            InitImageName = "";
        }
    }
}
