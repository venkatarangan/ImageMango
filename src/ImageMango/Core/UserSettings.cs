using System.IO;
using System.Text.Json.Nodes;

namespace ImageMango.Core;

/// <summary>A LoRA picked for a model: the file name in the LoRA folder and its weight.</summary>
public sealed class ActiveLora
{
    public string File { get; set; } = "";
    public double Weight { get; set; } = 0.8;
}

public sealed class UserSettings
{
    public string ModelsDir { get; set; } = AppPaths.DefaultModelsDir;
    public string OutputDir { get; set; } = AppPaths.DefaultOutputDir;
    /// <summary>Optional Hugging Face token (only needed for gated repos or rate limits).</summary>
    public string? HfToken { get; set; }
    /// <summary>Optional Civitai API key: many Civitai LoRAs download only for logged-in users. Sent only to civitai.com.</summary>
    public string? CivitaiToken { get; set; }

    /// <summary>Post-generation NSFW classifier. Off by default.</summary>
    public bool NsfwFilterEnabled { get; set; }
    /// <summary>Images scoring at or above this probability are blurred and saved to a "flagged" subfolder.</summary>
    public double NsfwThreshold { get; set; } = 0.7;

    public bool LivePreview { get; set; } = true;
    public bool ShowFiltered { get; set; } = true;
    public bool ShowUnfiltered { get; set; } = true;
    public bool ShowNfaa { get; set; } = true;

    /// <summary>
    /// Write the prompt, model and settings inside each PNG, where Explorer (Properties › Details), macOS, Linux viewers
    /// and A1111/Civitai can read them. Never done in incognito.
    /// </summary>
    public bool EmbedMetadata { get; set; } = true;

    /// <summary>The side panels (model list, settings): shown or hidden, and their widths when shown.</summary>
    public bool ModelsPanelOpen { get; set; } = true;
    public bool SettingsPanelOpen { get; set; } = true;
    public double ModelsPanelWidth { get; set; } = 400;
    public double SettingsPanelWidth { get; set; } = 430;

    /// <summary>The prompt helper's language model (<see cref="HelperModels"/> id) and whether it may ask questions first.</summary>
    public string PromptHelperModel { get; set; } = "qwen3-4b";
    public bool PromptHelperAskFirst { get; set; } = true;

    public string? LastModelId { get; set; }
    public string LastPrompt { get; set; } = "";

    /// <summary>Image-to-image: how much of the starting picture is changed (0.05..1). Remembered between runs.</summary>
    public double InitStrength { get; set; } = 0.6;

    /// <summary>The LoRAs switched on for each model id, remembered between starts.</summary>
    public Dictionary<string, List<ActiveLora>> ModelLoras { get; set; } = new();

    /// <summary>
    /// Incognito: no engine.log, no .json sidecars, the prompt isn't remembered, and each run's images are
    /// deleted when the next run starts or the app closes.
    /// </summary>
    public bool Incognito { get; set; }
    /// <summary>Incognito images not yet deleted (file paths only), so a crash doesn't leave them behind.</summary>
    public List<string> IncognitoPending { get; set; } = new();

    /// <summary>Saved "my defaults" per model id. When absent the machine's recommended settings apply.</summary>
    public Dictionary<string, JsonObject> ModelOverrides { get; set; } = new();

    public static UserSettings Load()
    {
        try
        {
            var s = Json.Load<UserSettings>(AppPaths.SettingsFile) ?? new UserSettings();
            s.MigrateOldName();
            if (string.IsNullOrWhiteSpace(s.OutputDir) ||
                string.Equals(s.OutputDir.TrimEnd('\\', '/'), AppPaths.LegacyOutputDir, StringComparison.OrdinalIgnoreCase))
                s.OutputDir = AppPaths.DefaultOutputDir;
            return s;
        }
        catch
        {
            return new UserSettings();
        }
    }

    public void Save() => Json.Save(AppPaths.SettingsFile, this);

    /// <summary>
    /// After the LocalImageGen → ImageMango rename: paths saved under the old data folder or the old
    /// %TEMP%\LocalImageGen output folder point to the new folders. The old temp folder is renamed too,
    /// so Recent images keep working.
    /// </summary>
    private void MigrateOldName()
    {
        // A portable folder (IMAGEMANGO_HOME) never touches the normal profile folders.
        if (AppPaths.IsPortable) return;
        var oldRoot = AppPaths.OldDataRoot;
        var root = AppPaths.DataRoot;
        if (!string.Equals(oldRoot, root, StringComparison.OrdinalIgnoreCase))
        {
            ModelsDir = AppPaths.Rebase(ModelsDir, oldRoot, root);
            OutputDir = AppPaths.Rebase(OutputDir, oldRoot, root);
        }

        var oldTemp = AppPaths.OldDefaultOutputDir;
        var newTemp = AppPaths.DefaultOutputDir;
        if (string.Equals(OutputDir.TrimEnd('\\', '/'), oldTemp, StringComparison.OrdinalIgnoreCase))
        {
            try
            {
                if (Directory.Exists(oldTemp) && !Directory.Exists(newTemp)) Directory.Move(oldTemp, newTemp);
            }
            catch { /* a viewer has a file open: start with an empty folder, the old images stay where they are */ }
            OutputDir = newTemp;
        }
        IncognitoPending = IncognitoPending.Select(p => AppPaths.Rebase(AppPaths.Rebase(p, oldTemp, newTemp), oldRoot, root)).ToList();
    }
}
