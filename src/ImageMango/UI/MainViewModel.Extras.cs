using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media.Imaging;
using ImageMango.Core;
using ImageMango.Native;

namespace ImageMango.UI;

/// <summary>Image-to-image (start image + strength) and LoRA add-ons.</summary>
public sealed partial class MainViewModel
{
    public ICommand PickInitImageCommand { get; }
    public ICommand ClearInitImageCommand { get; }
    public ICommand UseCurrentAsInitCommand { get; }
    public ICommand RemoveLoraCommand { get; }
    public ICommand AddTriggerCommand { get; }
    public ICommand OpenLoraFolderCommand { get; }
    public ICommand CancelLoraDownloadCommand { get; }

    // ---------------------------------------------------------------- start image (image-to-image)

    private BitmapSource? _initImage;
    /// <summary>The picture generation starts from, held in memory. Null = text-to-image.</summary>
    public BitmapSource? InitImage
    {
        get => _initImage;
        private set
        {
            if (!Set(ref _initImage, value)) return;
            Raise(nameof(HasInitImage));
            Raise(nameof(InitStrengthText));
            CommandManager.InvalidateRequerySuggested();
        }
    }

    public bool HasInitImage => InitImage is not null;

    private string _initImageName = "";
    public string InitImageName { get => _initImageName; private set => Set(ref _initImageName, value); }

    /// <summary>0.05 = small touch-ups … 1 = a new image that only borrows rough colours.</summary>
    public double InitStrength
    {
        get => _settings.InitStrength;
        set
        {
            _settings.InitStrength = Math.Round(Math.Clamp(value, 0.05, 1.0), 2);
            Raise();
            Raise(nameof(InitStrengthText));
        }
    }

    /// <summary>What the strength does in words, and how many of the steps run.</summary>
    public string InitStrengthText
    {
        get
        {
            double v = InitStrength;
            string what = v < 0.35 ? "Small touch-ups; keeps the picture"
                : v < 0.6 ? "Restyles; keeps the layout and subjects"
                : v < 0.85 ? "Reimagines; keeps rough shapes and colours"
                : "Mostly new; keeps only a hint of the picture";
            int steps = Math.Max(1, CurrentSettings.Steps);
            return $"{what} · runs {GenerateRequest.SampledStepsFor(steps, v)} of {steps} steps";
        }
    }

    private void OnCurrentSettingChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(SettingsViewModel.Steps)) Raise(nameof(InitStrengthText));
    }

    private void PickInitImage()
    {
        var dlg = new Microsoft.Win32.OpenFileDialog { Title = "Start from an image", Filter = ImageStore.PictureFilter };
        if (dlg.ShowDialog() == true) SetInitImageFromFile(dlg.FileName);
    }

    public void SetInitImageFromFile(string path)
    {
        try
        {
            SetInitImage(ImageStore.LoadPicture(path), Path.GetFileName(path));
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Couldn't open that picture:\n{ex.Message}", AppPaths.AppName, MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    /// <summary>
    /// Uses <paramref name="bmp"/> as the start image and changes Width/Height to its shape at about the same pixel
    /// count, so the crop loses little and the model stays near the size it was trained for.
    /// </summary>
    public void SetInitImage(BitmapSource bmp, string name)
    {
        if (IsBusy) return;
        InitImage = bmp;
        InitImageName = name;
        double area = Math.Max(CurrentSettings.Width * CurrentSettings.Height, 512 * 512);
        double aspect = (double)bmp.PixelWidth / bmp.PixelHeight;
        int w = Math.Clamp((int)Math.Round(Math.Sqrt(area * aspect) / 64) * 64, 256, 2048);
        int h = Math.Clamp((int)Math.Round(Math.Sqrt(area / aspect) / 64) * 64, 256, 2048);
        bool resized = w != CurrentSettings.Width || h != CurrentSettings.Height;
        CurrentSettings.Width = w;
        CurrentSettings.Height = h;
        Status = $"Start image: {name}." + (resized ? $" Size set to {w}×{h} to match its shape." : "") +
                 " Describe what the result should look like, set Change, then Generate.";
    }

    private void ClearInitImage()
    {
        InitImage = null;
        InitImageName = "";
        Status = "Start image removed: back to text-to-image.";
    }

    /// <summary>"Use as start image" on the shown result: vary or refine it in the next run.</summary>
    private void UseCurrentAsInit()
    {
        if (CurrentImage is null || CurrentPath is null) return;
        SetInitImage(ImageStore.Freeze(CurrentImage), Path.GetFileName(CurrentPath));
    }

    public void UseGalleryItemAsInit(GalleryItem? item)
    {
        if (item is null) return;
        SetInitImageFromFile(item.Path);
    }

    /// <summary>Ctrl+V with a picture (or a copied picture file) on the clipboard.</summary>
    public bool TryPasteInitImage()
    {
        try
        {
            if (Clipboard.ContainsFileDropList())
            {
                var file = Clipboard.GetFileDropList().Cast<string>().FirstOrDefault(ImageStore.IsPictureFile);
                if (file is null) return false;
                SetInitImageFromFile(file);
                return true;
            }
            if (!Clipboard.ContainsImage()) return false;
            BitmapSource? bmp = null;
            // Browsers and the Snipping Tool also put a PNG on the clipboard, which keeps colours exact.
            if (Clipboard.GetData("PNG") is MemoryStream png)
                bmp = BitmapDecoder.Create(png, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad).Frames[0];
            // The plain bitmap often has an empty alpha channel: read it as opaque.
            bmp ??= Clipboard.GetImage() is { } raw
                ? new FormatConvertedBitmap(raw, System.Windows.Media.PixelFormats.Bgr32, null, 0)
                : null;
            if (bmp is null) return false;
            SetInitImage(ImageStore.Freeze(bmp), "pasted picture");
            return true;
        }
        catch (Exception ex)
        {
            Status = "Couldn't paste the picture: " + ex.Message;
            return false;
        }
    }

    // ---------------------------------------------------------------- LoRA library

    public string LorasDir => AppPaths.LorasDirFor(_settings.ModelsDir);

    /// <summary>Every LoRA file in the LoRA folder.</summary>
    public IReadOnlyList<LoraInfo> LoraLibrary { get; private set; } = Array.Empty<LoraInfo>();

    /// <summary>LoRAs switched on for the selected model, shown under the prompt.</summary>
    public ObservableCollection<LoraItemViewModel> ActiveLoras { get; } = new();

    public bool HasActiveLoras => ActiveLoras.Count > 0;

    public string? SelectedFamily => Selected?.Model.Family;
    public string SelectedFamilyName => LoraFamilies.NameOf(SelectedFamily);

    public void RefreshLoraLibrary()
    {
        try { LoraLibrary = Core.LoraLibrary.Scan(LorasDir); }
        catch { LoraLibrary = Array.Empty<LoraInfo>(); }
        Raise(nameof(LoraLibrary));
    }

    /// <summary>Library LoRAs grouped for the Add LoRA menu: those made for the selected model first.</summary>
    public (List<LoraInfo> Fits, List<LoraInfo> Others) LorasForSelected()
    {
        var fits = new List<LoraInfo>();
        var others = new List<LoraInfo>();
        foreach (var l in LoraLibrary)
            (LoraFamilies.Fit(l.Family, SelectedFamily) is LoraFit.Mismatch ? others : fits).Add(l);
        // Exact matches before "maybe" and "unknown".
        fits = fits.OrderBy(l => LoraFamilies.Fit(l.Family, SelectedFamily)).ThenBy(l => l.Name, StringComparer.CurrentCultureIgnoreCase).ToList();
        return (fits, others);
    }

    public bool IsLoraActive(LoraInfo info) => ActiveLoras.Any(a => SamePath(a.Path, info.Path));

    /// <summary>Base models to search for the selected model's LoRAs: Hugging Face repos and Civitai base-model names.</summary>
    public (IReadOnlyList<string> HuggingFace, IReadOnlyList<string> Civitai) LoraSearchBases()
    {
        var family = LoraFamilies.Get(SelectedFamily);
        if (family is null || Selected is null) return (Array.Empty<string>(), Array.Empty<string>());
        // The model's own list first (its closest relatives), then the rest of the family.
        return ((Selected.Model.LoraHuggingFace ?? new()).Concat(family.HuggingFace).Distinct(StringComparer.OrdinalIgnoreCase).ToList(),
                (Selected.Model.LoraCivitai ?? new()).Concat(family.Civitai).Distinct(StringComparer.OrdinalIgnoreCase).ToList());
    }

    /// <summary>Whether a search result is already in the LoRA folder (same source page or same file name).</summary>
    public bool IsLoraDownloaded(LoraSearchResult r) => LoraLibrary.Any(l =>
        (l.SourceUrl is { } s && (s.Equals(r.PageUrl, StringComparison.OrdinalIgnoreCase) ||
                                  (r.Source == "Civitai" && s.StartsWith(r.PageUrl.Split('?')[0] + "?", StringComparison.OrdinalIgnoreCase)))) ||
        (r.Candidate is { } c && string.Equals(l.FileName, c.FileName, StringComparison.OrdinalIgnoreCase)));

    private Task? _nsfwLoading;

    /// <summary>With the NSFW classifier on: whether a picture (a LoRA preview) scores at or above the threshold.</summary>
    public async Task<bool> IsPictureFlaggedAsync(BitmapSource bmp)
    {
        if (!NsfwFilterEnabled) return false;
        // Many previews arrive at once: load the classifier once for all of them.
        if (_nsfw is null)
        {
            _nsfwLoading ??= EnsureNsfwClassifierAsync();
            await _nsfwLoading;
            _nsfwLoading = null;
        }
        if (_nsfw is not { } clf) return false;
        var raw = ImageStore.ToRawImage(bmp, bmp.PixelWidth, bmp.PixelHeight);
        try { return await Task.Run(() => clf.Score(raw)) >= NsfwThreshold; }
        catch { return false; }
    }

    /// <summary>Adult content in search results only when "Not for all audiences" models are shown.</summary>
    public bool AllowMatureLoras => ShowNfaa;

    public string? HfTokenForSearch => _settings.HfToken;

    /// <summary>Links that open LoRA listings for the selected model's family on Hugging Face and Civitai.</summary>
    public IReadOnlyList<(string Label, string Url)> LoraBrowseLinks()
    {
        var family = LoraFamilies.Get(SelectedFamily);
        if (family is null || Selected is null) return Array.Empty<(string, string)>();
        var hf = Selected.Model.LoraHuggingFace is { Count: > 0 } h ? h : family.HuggingFace.ToList();
        var civ = Selected.Model.LoraCivitai is { Count: > 0 } c ? c : family.Civitai.Take(4).ToList();
        var links = new List<(string, string)>();
        foreach (var repo in hf.Take(2))
            links.Add(($"Hugging Face: LoRAs for {repo.Split('/')[^1]}", $"https://huggingface.co/models?other=base_model:adapter:{repo}&sort=likes"));
        if (civ.Count > 0)
            links.Add(($"Civitai: {string.Join(", ", civ)} LoRAs",
                "https://civitai.com/models?types=LORA&sort=Most%20Downloaded&" + string.Join("&", civ.Select(b => "baseModels=" + Uri.EscapeDataString(b)))));
        return links;
    }

    /// <summary>Switches a library LoRA on for the selected model (or off, if it already is).</summary>
    public void ToggleLora(LoraInfo info)
    {
        if (IsBusy) return;
        var existing = ActiveLoras.FirstOrDefault(a => SamePath(a.Path, info.Path));
        if (existing is not null)
        {
            RemoveLora(existing);
            return;
        }
        AddLora(info);
    }

    private void AddLora(LoraInfo info)
    {
        var row = new LoraItemViewModel(info, info.PreferredWeight ?? 0.8, SelectedFamily, SaveActiveLoras);
        ActiveLoras.Add(row);
        Raise(nameof(HasActiveLoras));
        SaveActiveLoras();
        bool addedTrigger = row.HasTrigger && AddTriggerWords(row, quiet: true);
        Status = row.Fit switch
        {
            LoraFit.Mismatch => $"{info.Name} is made for {LoraFamilies.NameOf(info.Family)} models, so it won't work with {Selected?.Name}. Remove it, or switch to a {LoraFamilies.NameOf(info.Family)} model.",
            LoraFit.Unknown => $"{info.Name} switched on. ImageMango couldn't tell which model type it was made for; the ⓘ next to it says how well it applied after a run.",
            _ => $"{info.Name} switched on at weight {row.Weight:0.0#}." + (addedTrigger ? $" Its trigger words were added to the prompt: {info.TriggerWords}" : ""),
        };
    }

    private void RemoveLora(LoraItemViewModel? row)
    {
        if (row is null) return;
        ActiveLoras.Remove(row);
        Raise(nameof(HasActiveLoras));
        SaveActiveLoras();
        Status = $"{row.Name} switched off." + (row.HasTrigger && Prompt.Contains(row.TriggerWords, StringComparison.OrdinalIgnoreCase)
            ? " Its trigger words are still in the prompt; delete them if you don't want them." : "");
    }

    /// <summary>Appends the LoRA's trigger words to the prompt unless they're already there.</summary>
    private bool AddTriggerWords(LoraItemViewModel? row, bool quiet = false)
    {
        if (row is null || !row.HasTrigger) return false;
        var words = row.TriggerWords.Trim();
        if (Prompt.Contains(words, StringComparison.OrdinalIgnoreCase))
        {
            if (!quiet) Status = "The trigger words are already in the prompt.";
            return false;
        }
        Prompt = string.IsNullOrWhiteSpace(Prompt) ? words : $"{Prompt.TrimEnd().TrimEnd(',')}, {words}";
        if (!quiet) Status = $"Added to the prompt: {words}";
        return true;
    }

    /// <summary>Rebuilds the LoRA rows for the selected model from settings.json.</summary>
    private void LoadActiveLoras()
    {
        ActiveLoras.Clear();
        if (Selected is not null && _settings.ModelLoras.TryGetValue(Selected.Id, out var saved))
        {
            foreach (var a in saved)
            {
                var info = LoraLibrary.FirstOrDefault(l => string.Equals(l.FileName, a.File, StringComparison.OrdinalIgnoreCase));
                if (info is not null) ActiveLoras.Add(new LoraItemViewModel(info, a.Weight, SelectedFamily, SaveActiveLoras));
            }
        }
        Raise(nameof(HasActiveLoras));
        Raise(nameof(SelectedFamilyName));
    }

    private void SaveActiveLoras()
    {
        if (Selected is null) return;
        if (ActiveLoras.Count == 0) _settings.ModelLoras.Remove(Selected.Id);
        else _settings.ModelLoras[Selected.Id] = ActiveLoras.Select(a => new ActiveLora { File = a.Info.FileName, Weight = a.Weight }).ToList();
    }

    /// <summary>Highest "applied / total" the engine reported per LoRA file during the current run.</summary>
    private readonly Dictionary<string, (int Applied, int Total)> _loraRun = new(StringComparer.OrdinalIgnoreCase);

    private void OnLoraApplied(string path, int applied, int total)
    {
        lock (_loraRun)
        {
            var key = Path.GetFullPath(path);
            if (!_loraRun.TryGetValue(key, out var r) || applied > r.Applied) _loraRun[key] = (applied, total);
        }
    }

    private void BeginLoraRun()
    {
        lock (_loraRun) _loraRun.Clear();
    }

    /// <summary>After a run: marks LoRAs that hardly applied (made for another model type).</summary>
    private void EndLoraRun()
    {
        lock (_loraRun)
            foreach (var row in ActiveLoras)
                if (_loraRun.TryGetValue(Path.GetFullPath(row.Path), out var r)) row.ReportApplied(r.Applied, r.Total);
    }

    private static bool SamePath(string a, string b) =>
        string.Equals(Path.GetFullPath(a), Path.GetFullPath(b), StringComparison.OrdinalIgnoreCase);

    // ---------------------------------------------------------------- adding LoRAs

    private CancellationTokenSource? _loraDownloadCts;

    private bool _isLoraDownloading;
    public bool IsLoraDownloading { get => _isLoraDownloading; private set { if (Set(ref _isLoraDownloading, value)) CommandManager.InvalidateRequerySuggested(); } }

    private double _loraDownloadPercent;
    public double LoraDownloadPercent { get => _loraDownloadPercent; private set => Set(ref _loraDownloadPercent, value); }

    private string _loraDownloadText = "";
    public string LoraDownloadText { get => _loraDownloadText; private set => Set(ref _loraDownloadText, value); }

    /// <summary>Looks up the LoRA files behind a Hugging Face / Civitai / direct link.</summary>
    public async Task<IReadOnlyList<LoraCandidate>?> ResolveLoraLinkAsync(string link)
    {
        Status = "Looking up the link…";
        try
        {
            var list = await LoraSources.ResolveAsync(link, _settings.HfToken, CancellationToken.None);
            Status = list.Count == 1 ? $"Found {list[0].FileName}." : $"Found {list.Count} LoRA files; pick one.";
            return list;
        }
        catch (Exception ex)
        {
            Status = "Couldn't use that link.";
            MessageBox.Show(ex.Message, "Download a LoRA", MessageBoxButton.OK, MessageBoxImage.Warning);
            return null;
        }
    }

    /// <summary>
    /// Downloads a LoRA into the LoRA folder, records where it came from, and switches it on if it fits.
    /// Returns null when it worked (or was already there), else what went wrong; <paramref name="showErrors"/>
    /// false leaves showing it to the caller (the LoRA browser lists errors per row).
    /// </summary>
    public async Task<string?> DownloadLoraAsync(LoraCandidate c, bool showErrors = true)
    {
        if (IsLoraDownloading) return "Another LoRA is downloading.";
        Directory.CreateDirectory(LorasDir);

        // Already downloaded (same file name and size): just switch it on.
        var existing = LoraLibrary.FirstOrDefault(l => string.Equals(l.FileName, c.FileName, StringComparison.OrdinalIgnoreCase)
                                                       && (c.SizeBytes is null || l.SizeBytes == c.SizeBytes));
        if (existing is not null)
        {
            if (!IsLoraActive(existing) && LoraFamilies.Fit(existing.Family, SelectedFamily) != LoraFit.Mismatch) AddLora(existing);
            Status = $"{existing.Name} was already downloaded" + (IsLoraActive(existing) ? " and is switched on." : ".");
            return null;
        }

        var dest = Core.LoraLibrary.UniquePath(LorasDir, c.FileName);
        IsLoraDownloading = true;
        LoraDownloadPercent = 0;
        _loraDownloadCts = new CancellationTokenSource();
        try
        {
            CheckDiskSpace((c.SizeBytes ?? 0) / 1e9);
            var progress = new Progress<DownloadProgress>(p =>
            {
                LoraDownloadPercent = p.Total > 0 ? p.Received * 100.0 / p.Total : 0;
                LoraDownloadText = $"Downloading {c.FileName}: {p.Received / 1e6:0} / {(p.Total > 0 ? $"{p.Total / 1e6:0}" : "?")} MB · {p.BytesPerSecond / 1e6:0.0} MB/s";
                Status = LoraDownloadText;
            });
            var token = _loraDownloadCts.Token;
            await Task.Run(() => Downloader.DownloadAsync(c.DownloadUrl, dest, _settings.HfToken, progress, token));

            var info = Core.LoraLibrary.Load(dest);
            info.Name = c.Name;
            info.SourceUrl = c.SourceUrl;
            if (!string.IsNullOrWhiteSpace(c.TriggerWords)) info.TriggerWords = c.TriggerWords;
            if (c.Family is not null) { info.Family = c.Family; info.FamilySource = c.FamilySource; }
            try { Core.LoraLibrary.SaveSidecar(info); } catch { /* the LoRA still works without its notes */ }
            FinishAdding(info);
            return null;
        }
        catch (OperationCanceledException)
        {
            TryDelete(dest + ".part");
            Status = "LoRA download cancelled.";
            return "Cancelled.";
        }
        catch (Exception ex)
        {
            Status = "LoRA download failed.";
            if (showErrors) MessageBox.Show(ex.Message, "Download a LoRA", MessageBoxButton.OK, MessageBoxImage.Warning);
            return ex.Message;
        }
        finally
        {
            IsLoraDownloading = false;
            LoraDownloadText = "";
            LoraDownloadPercent = 0;
            _loraDownloadCts.Dispose();
            _loraDownloadCts = null;
        }
    }

    /// <summary>Copies LoRA files from elsewhere on the PC (with their .json / .civitai.info notes) into the LoRA folder.</summary>
    public async Task ImportLoraFilesAsync(IEnumerable<string> paths)
    {
        Directory.CreateDirectory(LorasDir);
        foreach (var src in paths.Where(p => p.EndsWith(".safetensors", StringComparison.OrdinalIgnoreCase)))
        {
            try
            {
                string dest;
                if (string.Equals(Path.GetDirectoryName(Path.GetFullPath(src))?.TrimEnd('\\'), Path.GetFullPath(LorasDir).TrimEnd('\\'), StringComparison.OrdinalIgnoreCase))
                    dest = src;
                else
                {
                    dest = Core.LoraLibrary.UniquePath(LorasDir, Path.GetFileName(src));
                    Status = $"Copying {Path.GetFileName(src)} to the LoRA folder…";
                    var from = src;
                    await Task.Run(() => File.Copy(from, dest));
                    // Notes other apps keep next to the LoRA: trigger words, base model, preview.
                    foreach (var ext in new[] { ".json", ".civitai.info", ".preview.png" })
                    {
                        var side = Core.LoraLibrary.SidecarPath(src, ext);
                        if (File.Exists(side)) File.Copy(side, Core.LoraLibrary.SidecarPath(dest, ext), overwrite: false);
                    }
                }
                FinishAdding(Core.LoraLibrary.Load(dest));
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Couldn't add {Path.GetFileName(src)}:\n{ex.Message}", "Add a LoRA", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }
    }

    private void FinishAdding(LoraInfo info)
    {
        RefreshLoraLibrary();
        var lib = LoraLibrary.FirstOrDefault(l => SamePath(l.Path, info.Path)) ?? info;
        if (LoraFamilies.Fit(lib.Family, SelectedFamily) == LoraFit.Mismatch)
        {
            Status = $"Added {lib.Name}. It's made for {LoraFamilies.NameOf(lib.Family)} models, so it wasn't switched on for {Selected?.Name}. " +
                     $"Pick a {LoraFamilies.NameOf(lib.Family)} model and choose it under Add LoRA.";
            return;
        }
        if (!IsLoraActive(lib)) AddLora(lib);
        Status = "Added. " + Status;
    }

    private static void TryDelete(string path)
    {
        try { File.Delete(path); } catch { }
    }
}
