using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using ImageMango.Core;
using ImageMango.Native;

namespace ImageMango.UI;

public sealed class GalleryItem
{
    public required string Path { get; init; }
    public BitmapSource? Thumb { get; init; }
    public bool Flagged { get; init; }
}

public sealed record SizePreset(string Label, int Width, int Height)
{
    public override string ToString() => Label;
}

public sealed partial class MainViewModel : Observable, IDisposable
{
    private readonly Dispatcher _ui = Application.Current.Dispatcher;
    private readonly UserSettings _settings;
    private MachineProfile _machine;
    private CatalogDocument _catalog;
    private readonly SdEngine? _engine;
    private readonly Dictionary<string, GenSettings> _session = new();
    private readonly SemaphoreSlim _downloadGate = new(1, 1);
    /// <summary>engine.log; null while incognito.</summary>
    private StreamWriter? _log;
    private NsfwClassifier? _nsfw;
    private CancellationTokenSource? _genCts;
    private int _previewPending;

    public ObservableCollection<ModelItemViewModel> Models { get; } = new();
    public ICollectionView ModelsView { get; }
    public ObservableCollection<GalleryItem> Gallery { get; } = new();
    public ObservableCollection<string> LogLines { get; } = new();

    public string EngineInfo { get; }
    public string? EngineError { get; }

    public MainViewModel()
    {
        AppPaths.EnsureCreated();
        _settings = UserSettings.Load();
        Downloader.CivitaiToken = _settings.CivitaiToken;
        _machine = MachineProfile.LoadOrDetect();
        _catalog = CatalogDocument.Load();
        _prompt = _settings.Incognito ? "" : _settings.LastPrompt;

        // Images left over from an incognito session that ended without a clean exit.
        DeleteIncognitoImages();
        if (_settings.Incognito) DeleteLogFile();
        else OpenLog();

        try
        {
            _engine = new SdEngine();
            EngineInfo = SdEngine.EngineInfo();
            _engine.Log += OnEngineLog;
            _engine.Progress += OnEngineProgress;
            _engine.Preview += OnEnginePreview;
            _engine.LoraApplied += OnLoraApplied;
        }
        catch (Exception ex) when (ex is DllNotFoundException or BadImageFormatException or EntryPointNotFoundException)
        {
            EngineInfo = "Engine not found";
            EngineError = "The stable-diffusion.cpp engine DLLs were not found next to ImageMango.exe.\n" +
                          "Run setup-engine.ps1 from the repository root, then rebuild.\n\n" + ex.Message;
        }

        ModelsView = CollectionViewSource.GetDefaultView(Models);
        ModelsView.GroupDescriptions.Add(new PropertyGroupDescription(nameof(ModelItemViewModel.CategoryLabel)));
        ModelsView.SortDescriptions.Add(new SortDescription(nameof(ModelItemViewModel.CategoryOrder), ListSortDirection.Ascending));
        ModelsView.Filter = o => o is ModelItemViewModel m && m.Category switch
        {
            "filtered" => ShowFiltered,
            "nfaa" => ShowNfaa,
            _ => ShowUnfiltered,
        };

        LoadCatalogIntoList();
        _selected = Models.FirstOrDefault(m => m.Id == _settings.LastModelId)
                    ?? Models.FirstOrDefault(m => m.IsReady)
                    ?? Models.FirstOrDefault();
        _currentSettings = NewSettingsVm(EffectiveSettings(_selected));
        _currentSettings.PropertyChanged += OnCurrentSettingChanged;
        LoadGallery();
        RefreshLoraLibrary();
        LoadActiveLoras();

        GenerateCommand = new RelayCommand(async () => await GenerateAsync(), () => !IsBusy && Selected is not null && _engine is not null);
        CancelCommand = new RelayCommand(() => _genCts?.Cancel(), () => IsBusy);
        DownloadCommand = new RelayCommand(async p => await DownloadAsync(p as ModelItemViewModel ?? Selected), p => (p as ModelItemViewModel ?? Selected) is { IsReady: false, IsDownloading: false });
        CancelDownloadCommand = new RelayCommand(p => (p as ModelItemViewModel ?? Selected)?.DownloadCts?.Cancel());
        DeleteCommand = new RelayCommand(p => DeleteModel(p as ModelItemViewModel ?? Selected), p => !IsBusy && (p as ModelItemViewModel ?? Selected) is { IsDownloading: false });
        SaveDefaultsCommand = new RelayCommand(SaveModelDefaults, () => Selected is not null);
        ResetDefaultsCommand = new RelayCommand(ResetModelDefaults, () => Selected is not null);
        DetectMachineCommand = new RelayCommand(DetectMachine, () => !IsBusy);
        ReloadConfigCommand = new RelayCommand(ReloadConfig, () => !IsBusy);
        EditCatalogCommand = new RelayCommand(EditCatalog);
        OpenConfigFolderCommand = new RelayCommand(() => OpenPath(AppPaths.ConfigDir));
        OpenOutputFolderCommand = new RelayCommand(() => OpenPath(_settings.OutputDir));
        OpenModelsFolderCommand = new RelayCommand(() => OpenPath(_settings.ModelsDir));
        ChangeModelsFolderCommand = new RelayCommand(ChangeModelsFolder, () => !IsBusy);
        ChangeOutputFolderCommand = new RelayCommand(ChangeOutputFolder, () => !IsBusy);
        ClearOutputFolderCommand = new RelayCommand(ClearOutputFolder, () => !IsBusy);
        DeleteCurrentCommand = new RelayCommand(DeleteCurrentImage, () => !IsBusy && CurrentPath is not null);
        ResetOutputFolderCommand = new RelayCommand(ResetOutputFolder, () => !IsBusy && !IsDefaultOutput(_settings.OutputDir));
        OpenModelPageCommand = new RelayCommand(p => OpenPath((p as ModelItemViewModel ?? Selected)?.PageUrl));
        UnloadCommand = new RelayCommand(() => { _engine?.Unload(); Raise(nameof(LoadedText)); Status = "Model unloaded; VRAM freed."; }, () => !IsBusy && _engine?.IsLoaded == true);
        RandomSeedCommand = new RelayCommand(() => Seed = -1);
        ReuseSeedCommand = new RelayCommand(() => { if (LastSeed is long s) Seed = s; }, () => LastSeed is not null);
        RevealCommand = new RelayCommand(() => CurrentRevealed = !CurrentRevealed);
        ShowGalleryItemCommand = new RelayCommand(p => ShowGalleryItem(p as GalleryItem));
        OpenCurrentCommand = new RelayCommand(() => OpenPath(CurrentPath), () => CurrentPath is not null);
        ApplySizePresetCommand = new RelayCommand(p => { if (p is SizePreset s) { CurrentSettings.Width = s.Width; CurrentSettings.Height = s.Height; } });
        PickInitImageCommand = new RelayCommand(PickInitImage, () => !IsBusy);
        ClearInitImageCommand = new RelayCommand(ClearInitImage, () => !IsBusy && HasInitImage);
        UseCurrentAsInitCommand = new RelayCommand(UseCurrentAsInit, () => !IsBusy && CurrentImage is not null && CurrentPath is not null);
        RemoveLoraCommand = new RelayCommand(p => RemoveLora(p as LoraItemViewModel), _ => !IsBusy);
        AddTriggerCommand = new RelayCommand(p => AddTriggerWords(p as LoraItemViewModel), p => p is LoraItemViewModel { HasTrigger: true });
        OpenLoraFolderCommand = new RelayCommand(() => OpenPath(LorasDir));
        CancelLoraDownloadCommand = new RelayCommand(() => _loraDownloadCts?.Cancel(), () => IsLoraDownloading);
        ToggleModelsPanelCommand = new RelayCommand(() => ModelsPanelOpen = !ModelsPanelOpen);
        ToggleSettingsPanelCommand = new RelayCommand(() => SettingsPanelOpen = !SettingsPanelOpen);
        TogglePreviewFocusCommand = new RelayCommand(TogglePreviewFocus);
        ReuseCurrentCommand = new RelayCommand(() => ReuseImage(CurrentPath), () => !IsBusy && CurrentPath is not null);

        Status = EngineError is not null ? "Engine missing — see the message."
            : AppPaths.MigratedFromOldName ? "LocalImageGen is now ImageMango. Your models, settings and images were moved to the new ImageMango folders."
            : _selected?.IsReady == true ? "Ready."
            : "Pick a model and click Download.";
    }

    // ---------------------------------------------------------------- commands

    public ICommand GenerateCommand { get; }
    public ICommand CancelCommand { get; }
    public ICommand DownloadCommand { get; }
    public ICommand CancelDownloadCommand { get; }
    public ICommand DeleteCommand { get; }
    public ICommand SaveDefaultsCommand { get; }
    public ICommand ResetDefaultsCommand { get; }
    public ICommand DetectMachineCommand { get; }
    public ICommand ReloadConfigCommand { get; }
    public ICommand EditCatalogCommand { get; }
    public ICommand OpenConfigFolderCommand { get; }
    public ICommand OpenOutputFolderCommand { get; }
    public ICommand OpenModelsFolderCommand { get; }
    public ICommand ChangeModelsFolderCommand { get; }
    public ICommand ChangeOutputFolderCommand { get; }
    public ICommand ResetOutputFolderCommand { get; }
    public ICommand ClearOutputFolderCommand { get; }
    public ICommand DeleteCurrentCommand { get; }
    public ICommand OpenModelPageCommand { get; }
    public ICommand UnloadCommand { get; }
    public ICommand RandomSeedCommand { get; }
    public ICommand ReuseSeedCommand { get; }
    public ICommand RevealCommand { get; }
    public ICommand ShowGalleryItemCommand { get; }
    public ICommand OpenCurrentCommand { get; }
    public ICommand ApplySizePresetCommand { get; }

    // ---------------------------------------------------------------- state

    public string MachineSummary => _machine.Summary;
    public string MachineDetail => $"Detected {_machine.DetectedAt:g} via {(_machine.DetectedBy == "" ? "defaults" : _machine.DetectedBy)}. Edit machine.json to override.";
    public string CatalogInfo => $"Catalog {_catalog.CatalogVersion} · {Path.GetFileName(Path.GetDirectoryName(_catalog.SourcePath))}\\{Path.GetFileName(_catalog.SourcePath)}";
    public string ModelsDir => _settings.ModelsDir;
    public string OutputDir => _settings.OutputDir;
    public string LoadedText => _engine?.IsLoaded == true ? $"Loaded: {_loadedModelName} ({_engine.ModelVersion})" : "No model loaded";
    private string _loadedModelName = "";

    private ModelItemViewModel? _selected;
    public ModelItemViewModel? Selected
    {
        get => _selected;
        set
        {
            if (_selected == value) return;
            if (_selected is not null) _session[_selected.Id] = CurrentSettings.ToGenSettings();
            _selected = value;
            Raise();
            CurrentSettings = NewSettingsVm(EffectiveSettings(value));
            _settings.LastModelId = value?.Id;
            LoadActiveLoras();
        }
    }

    private SettingsViewModel _currentSettings;
    public SettingsViewModel CurrentSettings
    {
        get => _currentSettings;
        private set
        {
            _currentSettings.PropertyChanged -= OnCurrentSettingChanged;
            Set(ref _currentSettings, value);
            value.PropertyChanged += OnCurrentSettingChanged;
            Raise(nameof(SettingsSource));
            Raise(nameof(SizePresets));
            Raise(nameof(InitStrengthText));
        }
    }

    public string SettingsSource => Selected is null ? ""
        : _settings.ModelOverrides.ContainsKey(Selected.Id) ? "Using your saved defaults for this model."
        : $"Recommended for this PC (tier {Selected.Tier.Label}).";

    public IReadOnlyList<SizePreset> SizePresets
    {
        get
        {
            var rec = Selected is null ? 1024 : Math.Max(Selected.Model.Recommended(_machine.EffectiveVramGb).Width, 512);
            double k = rec / 1024.0;
            int R(double v) => (int)Math.Round(v * k / 64.0) * 64;
            return new[]
            {
                new SizePreset("1:1 square", R(1024), R(1024)),
                new SizePreset("3:4 portrait", R(896), R(1152)),
                new SizePreset("2:3 portrait", R(832), R(1216)),
                new SizePreset("9:16 tall", R(768), R(1344)),
                new SizePreset("4:3 landscape", R(1152), R(896)),
                new SizePreset("3:2 landscape", R(1216), R(832)),
                new SizePreset("16:9 wide", R(1344), R(768)),
            }.Select(p => p with { Label = $"{p.Label}  {p.Width}×{p.Height}" }).ToList();
        }
    }

    private string _prompt;
    public string Prompt { get => _prompt; set => Set(ref _prompt, value); }

    private long _seed = -1;
    public long Seed { get => _seed; set => Set(ref _seed, value); }

    private long? _lastSeed;
    public long? LastSeed { get => _lastSeed; private set => Set(ref _lastSeed, value); }

    private int _batchCount = 1;
    public int BatchCount { get => _batchCount; set => Set(ref _batchCount, Math.Clamp(value, 1, 16)); }

    private bool _isBusy;
    public bool IsBusy { get => _isBusy; private set { if (Set(ref _isBusy, value)) CommandManager.InvalidateRequerySuggested(); } }

    private string _status = "";
    public string Status { get => _status; set => Set(ref _status, value); }

    private double _progressValue;
    public double ProgressValue { get => _progressValue; set => Set(ref _progressValue, value); }

    private double _progressMax = 1;
    public double ProgressMax { get => _progressMax; set => Set(ref _progressMax, value); }

    private string _progressText = "";
    public string ProgressText { get => _progressText; set => Set(ref _progressText, value); }

    private BitmapSource? _currentImage;
    public BitmapSource? CurrentImage { get => _currentImage; set => Set(ref _currentImage, value); }

    private string? _currentPath;
    public string? CurrentPath { get => _currentPath; set => Set(ref _currentPath, value); }

    private bool _currentFlagged;
    public bool CurrentFlagged { get => _currentFlagged; set { if (Set(ref _currentFlagged, value)) Raise(nameof(CurrentBlurred)); } }

    private bool _currentRevealed;
    public bool CurrentRevealed { get => _currentRevealed; set { if (Set(ref _currentRevealed, value)) Raise(nameof(CurrentBlurred)); } }

    public bool CurrentBlurred => CurrentFlagged && !CurrentRevealed;

    private string _currentInfo = "";
    public string CurrentInfo { get => _currentInfo; set => Set(ref _currentInfo, value); }

    public bool ShowFiltered { get => _settings.ShowFiltered; set { _settings.ShowFiltered = value; Raise(); ModelsView.Refresh(); } }
    public bool ShowUnfiltered { get => _settings.ShowUnfiltered; set { _settings.ShowUnfiltered = value; Raise(); ModelsView.Refresh(); } }
    public bool ShowNfaa { get => _settings.ShowNfaa; set { _settings.ShowNfaa = value; Raise(); ModelsView.Refresh(); } }

    public bool LivePreview { get => _settings.LivePreview; set { _settings.LivePreview = value; Raise(); _settings.Save(); } }

    public bool NsfwFilterEnabled
    {
        get => _settings.NsfwFilterEnabled;
        set
        {
            _settings.NsfwFilterEnabled = value;
            Raise();
            _settings.Save();
            if (value) _ = EnsureNsfwClassifierAsync();
        }
    }

    public double NsfwThreshold
    {
        get => _settings.NsfwThreshold;
        set { _settings.NsfwThreshold = Math.Clamp(value, 0.05, 0.99); Raise(); }
    }

    public string HfToken { get => _settings.HfToken ?? ""; set { _settings.HfToken = string.IsNullOrWhiteSpace(value) ? null : value.Trim(); Raise(); _settings.Save(); } }

    public string CivitaiToken
    {
        get => _settings.CivitaiToken ?? "";
        set
        {
            _settings.CivitaiToken = string.IsNullOrWhiteSpace(value) ? null : value.Trim();
            Downloader.CivitaiToken = _settings.CivitaiToken;
            Raise();
            _settings.Save();
        }
    }

    // ---------------------------------------------------------------- catalog / settings

    private void LoadCatalogIntoList()
    {
        var selectedId = _selected?.Id;
        Models.Clear();
        foreach (var m in _catalog.Models)
        {
            var vm = new ModelItemViewModel(m);
            vm.Evaluate(_machine, _settings.ModelsDir);
            Models.Add(vm);
        }
        if (selectedId is not null) _selected = Models.FirstOrDefault(m => m.Id == selectedId);
        Raise(nameof(CatalogInfo));
    }

    /// <summary>Settings panel for the selected model, with tooltips that quote its recommended values.</summary>
    private SettingsViewModel NewSettingsVm(GenSettings s) =>
        new(s, SettingHelp.For(_selected?.Model, _selected?.Model.Recommended(_machine.EffectiveVramGb)));

    private GenSettings EffectiveSettings(ModelItemViewModel? m)
    {
        if (m is null) return new GenSettings();
        if (_session.TryGetValue(m.Id, out var s)) return s.Clone();
        if (_settings.ModelOverrides.TryGetValue(m.Id, out var o)) return GenSettings.FromLayers(o);
        return m.Model.Recommended(_machine.EffectiveVramGb);
    }

    private void SaveModelDefaults()
    {
        if (Selected is null) return;
        var s = CurrentSettings.ToGenSettings();
        _settings.ModelOverrides[Selected.Id] = s.ToJsonObject();
        _session[Selected.Id] = s;
        _settings.Save();
        Raise(nameof(SettingsSource));
        Status = $"Saved these settings as your defaults for {Selected.Name}.";
    }

    private void ResetModelDefaults()
    {
        if (Selected is null) return;
        _settings.ModelOverrides.Remove(Selected.Id);
        _session.Remove(Selected.Id);
        _settings.Save();
        CurrentSettings = NewSettingsVm(Selected.Model.Recommended(_machine.EffectiveVramGb));
        Status = $"{Selected.Name}: back to the recommended settings for this PC.";
    }

    private void DetectMachine()
    {
        var old = _machine;
        _machine = MachineProfile.Detect();
        _machine.Save();

        bool resetOverrides = false;
        if (_settings.ModelOverrides.Count > 0)
        {
            resetOverrides = MessageBox.Show(
                $"Detected: {_machine.Summary}\n\nYou have saved custom settings for {_settings.ModelOverrides.Count} model(s). " +
                "Replace them with the recommended settings for this PC?",
                "Detect this PC", MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes;
        }
        if (resetOverrides) _settings.ModelOverrides.Clear();
        _settings.Save();
        ApplyMachineChange();
        Status = old.EffectiveVramGb == _machine.EffectiveVramGb
            ? $"Detected {_machine.Summary}. Recommendations refreshed."
            : $"Detected {_machine.Summary}. Recommendations switched from {old.EffectiveVramGb:0.#} GB to {_machine.EffectiveVramGb:0.#} GB tier.";
    }

    /// <summary>Re-reads machine.json, settings.json and the catalog (after editing them by hand).</summary>
    private void ReloadConfig()
    {
        try
        {
            _machine = MachineProfile.LoadOrDetect();
            _catalog = CatalogDocument.Load();
            var fresh = UserSettings.Load();
            _settings.ModelOverrides = fresh.ModelOverrides;
            _settings.NsfwThreshold = fresh.NsfwThreshold;
            _settings.HfToken = fresh.HfToken;
            _settings.CivitaiToken = fresh.CivitaiToken;
            Downloader.CivitaiToken = fresh.CivitaiToken;
            Raise(nameof(CivitaiToken));
            LoadCatalogIntoList();
            ApplyMachineChange();
            Raise(nameof(NsfwThreshold));
            Raise(nameof(HfToken));
            Status = "Configuration reloaded.";
        }
        catch (Exception ex)
        {
            MessageBox.Show("Could not reload configuration:\n" + ex.Message, AppPaths.AppName, MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void ApplyMachineChange()
    {
        _session.Clear();
        foreach (var m in Models) m.Evaluate(_machine, _settings.ModelsDir);
        if (_selected is not null && !Models.Contains(_selected))
            _selected = Models.FirstOrDefault();
        Raise(nameof(Selected));
        CurrentSettings = NewSettingsVm(EffectiveSettings(_selected));
        Raise(nameof(MachineSummary));
        Raise(nameof(MachineDetail));
    }

    private void EditCatalog()
    {
        if (!File.Exists(AppPaths.CatalogOverrideFile))
            File.Copy(AppPaths.BundledCatalogFile, AppPaths.CatalogOverrideFile);
        _catalog = CatalogDocument.Load();
        Raise(nameof(CatalogInfo));
        OpenPath(AppPaths.CatalogOverrideFile);
        Status = "Editing config\\models.json. Save it, then click Reload config.";
    }

    private void ChangeModelsFolder()
    {
        var dlg = new Microsoft.Win32.OpenFolderDialog { Title = "Folder for downloaded models", InitialDirectory = _settings.ModelsDir };
        if (dlg.ShowDialog() != true) return;
        _settings.ModelsDir = dlg.FolderName;
        _settings.Save();
        foreach (var m in Models) m.RefreshStatus(_settings.ModelsDir);
        Raise(nameof(ModelsDir));
    }

    private void ChangeOutputFolder()
    {
        var dlg = new Microsoft.Win32.OpenFolderDialog { Title = "Folder for generated images", InitialDirectory = _settings.OutputDir };
        if (dlg.ShowDialog() != true) return;
        if (!CanWriteTo(dlg.FolderName, out var error))
        {
            MessageBox.Show($"ImageMango can't save images in\n{dlg.FolderName}\n\n{error}\n\nPick another folder.",
                "Output folder", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }
        SetOutputDir(dlg.FolderName);
        Status = $"Images will be saved to {dlg.FolderName}.";
    }

    private void ResetOutputFolder()
    {
        SetOutputDir(AppPaths.DefaultOutputDir);
        Status = $"Images will be saved to the default folder, {AppPaths.DefaultOutputDir}.";
    }

    private void SetOutputDir(string dir)
    {
        _settings.OutputDir = dir;
        _settings.Save();
        Raise(nameof(OutputDir));
        Gallery.Clear();
        LoadGallery();
    }

    /// <summary>Creates the folder and writes a probe file; OneDrive or Controlled Folder Access can block either step.</summary>
    private static bool CanWriteTo(string dir, out string error)
    {
        try
        {
            Directory.CreateDirectory(dir);
            var probe = Path.Combine(dir, $".write-test-{Environment.ProcessId}");
            File.WriteAllText(probe, "");
            File.Delete(probe);
            error = "";
            return true;
        }
        catch (Exception ex)
        {
            error = ex.Message;
            return false;
        }
    }

    // ---------------------------------------------------------------- downloads

    private async Task DownloadAsync(ModelItemViewModel? m)
    {
        if (m is null || m.IsDownloading) return;
        m.IsDownloading = true;
        m.DownloadCts = new CancellationTokenSource();
        m.DownloadText = "Queued…";
        try
        {
            await _downloadGate.WaitAsync(m.DownloadCts.Token);
            try
            {
                var missing = m.Files.Where(f => !File.Exists(f.LocalPath(_settings.ModelsDir)))
                                     .GroupBy(f => f.LocalPath(_settings.ModelsDir)).Select(g => g.First()).ToList();
                double totalGb = missing.Sum(f => f.SizeGb);
                CheckDiskSpace(totalGb);
                double doneGb = 0;
                foreach (var f in missing)
                {
                    var dest = f.LocalPath(_settings.ModelsDir);
                    var progress = new Progress<DownloadProgress>(p =>
                    {
                        double gb = doneGb + p.Received / 1e9;
                        m.DownloadPercent = totalGb > 0 ? Math.Min(100, gb / totalGb * 100) : 0;
                        m.DownloadText = $"{p.FileName}: {gb:0.00} / {totalGb:0.00} GB · {p.BytesPerSecond / 1e6:0} MB/s";
                    });
                    await Task.Run(() => Downloader.DownloadAsync(f.Url, dest, _settings.HfToken, progress, m.DownloadCts.Token));
                    doneGb += f.SizeGb;
                }
                Status = $"{m.Name} downloaded.";
            }
            finally
            {
                _downloadGate.Release();
            }
        }
        catch (OperationCanceledException)
        {
            Status = $"{m.Name}: download paused. Click Download to resume.";
        }
        catch (Exception ex)
        {
            Status = $"{m.Name}: download failed.";
            MessageBox.Show(ex.Message, "Download failed", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
        finally
        {
            m.IsDownloading = false;
            m.DownloadCts = null;
            m.DownloadPercent = 0;
            foreach (var x in Models) x.RefreshStatus(_settings.ModelsDir);
            CommandManager.InvalidateRequerySuggested();
        }
    }

    private void CheckDiskSpace(double neededGb)
    {
        Directory.CreateDirectory(_settings.ModelsDir);
        var root = Path.GetPathRoot(Path.GetFullPath(_settings.ModelsDir));
        if (root is null) return;
        var free = new DriveInfo(root).AvailableFreeSpace / 1e9;
        if (free < neededGb + 1)
            throw new IOException($"Not enough disk space on {root}: need {neededGb:0.0} GB, {free:0.0} GB free. Change the models folder in Settings.");
    }

    private void DeleteModel(ModelItemViewModel? m)
    {
        if (m is null) return;
        // Keep files that another fully downloaded model still needs (shared VAEs / text encoders).
        var keep = Models.Where(x => x != m && x.IsReady)
                         .SelectMany(x => x.Model.Files.Values)
                         .Select(f => f.LocalPath(_settings.ModelsDir))
                         .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var targets = m.Model.Files.Values.Select(f => f.LocalPath(_settings.ModelsDir))
                       .Distinct(StringComparer.OrdinalIgnoreCase)
                       .SelectMany(p => new[] { p, p + ".part" })
                       .Where(p => File.Exists(p) && !keep.Contains(p.EndsWith(".part") ? p[..^5] : p))
                       .ToList();
        if (targets.Count == 0)
        {
            Status = $"{m.Name}: nothing to delete (shared files are kept for other downloaded models).";
            return;
        }
        double gb = targets.Sum(p => new FileInfo(p).Length) / 1e9;
        if (MessageBox.Show($"Delete {targets.Count} file(s), {gb:0.0} GB, for {m.Name}?\nFiles shared with other downloaded models are kept.",
                "Delete model files", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return;

        _engine?.Unload();
        Raise(nameof(LoadedText));
        foreach (var p in targets)
        {
            try { File.Delete(p); } catch (Exception ex) { LogLines.Add("Delete failed: " + ex.Message); }
        }
        foreach (var x in Models) x.RefreshStatus(_settings.ModelsDir);
        Status = $"Deleted {gb:0.0} GB for {m.Name}.";
    }

    // ---------------------------------------------------------------- generation

    private async Task GenerateAsync()
    {
        var m = Selected;
        if (m is null || _engine is null) return;
        if (!m.IsReady)
        {
            if (MessageBox.Show($"{m.Name} is not downloaded yet ({m.MissingGb:0.0} GB). Download now?", AppPaths.AppName,
                    MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes)
                await DownloadAsync(m);
            return;
        }
        if (string.IsNullOrWhiteSpace(Prompt))
        {
            Status = "Type a prompt first.";
            return;
        }

        // Total time is measured from the Generate press to the last image being saved.
        var clock = Stopwatch.StartNew();

        // The previous incognito run's images go as soon as the next prompt runs.
        DeleteIncognitoImages();
        StopHelperForImageRun();

        var s = CurrentSettings.ToGenSettings();
        _session[m.Id] = s;
        bool incognito = Incognito;
        _settings.LastPrompt = incognito ? "" : Prompt;
        long seed = Seed >= 0 ? Seed : Random.Shared.NextInt64(0, int.MaxValue);
        LastSeed = seed;

        string? PathFor(string role) => m.Files.FirstOrDefault(f => f.Role == role)?.LocalPath(_settings.ModelsDir);
        var load = new EngineLoadOptions
        {
            ModelPath = PathFor("model"),
            DiffusionModelPath = PathFor("diffusion"),
            ClipLPath = PathFor("clip_l"),
            ClipGPath = PathFor("clip_g"),
            T5xxlPath = PathFor("t5xxl"),
            LlmPath = PathFor("llm"),
            VaePath = PathFor("vae"),
            TaesdPath = PathFor("taesd"),
            OffloadToCpu = s.OffloadToCpu,
            TextEncoderOnCpu = s.TextEncoderOnCpu,
            VaeOnCpu = s.VaeOnCpu,
            FlashAttention = s.FlashAttention,
            WeightType = s.WeightType,
            Prediction = s.Prediction,
            ModelArgs = s.ModelArgs,
            MaxVram = s.MaxVram,
        };

        // Image-to-image: the start picture, cropped to the output size (needs the UI thread).
        RawImage? initImage = InitImage is { } start ? ImageStore.ToRawImage(start, s.Width, s.Height) : null;
        var activeLoras = ActiveLoras.Where(l => l.Weight != 0 && File.Exists(l.Path)).ToList();
        var loras = activeLoras.Select(l => new LoraSpec(l.Path, (float)l.Weight)).ToList();
        SaveActiveLoras();

        var req = new GenerateRequest
        {
            Prompt = Prompt,
            NegativePrompt = s.NegativePrompt,
            Width = s.Width,
            Height = s.Height,
            Steps = s.Steps,
            CfgScale = (float)s.CfgScale,
            Guidance = s.Guidance is double g ? (float)g : null,
            Sampler = s.Sampler,
            Scheduler = s.Scheduler,
            FlowShift = s.FlowShift is double fs ? (float)fs : null,
            Seed = seed,
            BatchCount = BatchCount,
            ClipSkip = s.ClipSkip,
            VaeTiling = s.VaeTiling,
            // Previews are never checked by the classifier, so they are off while the filter is on.
            LivePreview = LivePreview && !NsfwFilterEnabled,
            InitImage = initImage,
            Strength = (float)InitStrength,
            Loras = loras,
        };

        IsBusy = true;
        StartElapsedTimer(clock);
        BeginLoraRun();
        Volatile.Write(ref _expectedSteps, req.SampledSteps);
        _genCts = new CancellationTokenSource();
        var token = _genCts.Token;
        ProgressValue = 0;
        ProgressText = "";
        CurrentRevealed = false;
        CurrentFlagged = false;
        try
        {
            bool needsLoad = _engine.LoadedKey != load.Key;
            var what = (initImage is null ? "Generating" : $"Generating from the start image (change {InitStrength:0.00})") +
                       (loras.Count > 0 ? $" with {loras.Count} LoRA{(loras.Count > 1 ? "s" : "")}…" : "…");
            Status = needsLoad ? $"Loading {m.Name}… (first run after switching models takes longer)" : what;
            var (result, loadTime) = await Task.Run(() =>
            {
                var lt = _engine.EnsureLoaded(load);
                _ui.BeginInvoke(() => { Status = what; _loadedModelName = m.Name; Raise(nameof(LoadedText)); });
                token.ThrowIfCancellationRequested();
                return (_engine.Generate(req, token), lt);
            }, token);

            EndLoraRun();
            if (NsfwFilterEnabled) await EnsureNsfwClassifierAsync();

            foreach (var img in result.Images)
            {
                float? score = null;
                if (NsfwFilterEnabled && _nsfw is not null)
                {
                    var clf = _nsfw;
                    score = await Task.Run(() => clf.Score(img));
                }
                bool flagged = score >= NsfwThreshold;
                var bmp = ImageStore.ToBitmap(img);
                var folder = flagged ? Path.Combine(_settings.OutputDir, "flagged") : _settings.OutputDir;
                object? meta = incognito ? null : new
                {
                    model = m.Id,
                    modelName = m.Name,
                    files = m.Files.Select(f => $"{f.Repo}/{f.Path}"),
                    prompt = Prompt,
                    negativePrompt = s.NegativePrompt,
                    seed = img.Seed,
                    width = img.Width,
                    height = img.Height,
                    steps = s.Steps,
                    cfgScale = s.CfgScale,
                    guidance = s.Guidance,
                    flowShift = s.FlowShift,
                    sampler = result.Sampler,
                    scheduler = result.Scheduler,
                    settings = s,
                    // Strength only: the start picture's file name can be personal.
                    startImage = initImage is null ? null : new { strength = Math.Round(InitStrength, 2) },
                    loras = activeLoras.Count == 0 ? null : activeLoras.Select(l => new { name = l.Name, file = l.Info.FileName, weight = l.Weight, family = l.Info.Family, source = l.Info.SourceUrl }),
                    nsfwScore = score,
                    generationSeconds = Math.Round(result.GenerateTime.TotalSeconds, 2),
                    modelLoadSeconds = Math.Round(loadTime.TotalSeconds, 2),
                    totalSeconds = Math.Round(clock.Elapsed.TotalSeconds, 2),
                    engine = EngineInfo,
                    // UTC: a local time would give away the time zone.
                    createdAt = DateTime.UtcNow,
                };
                var embedded = incognito || !EmbedMetadata ? null
                    : DescribeImage(m, s, img, result.Sampler, result.Scheduler, initImage is not null, activeLoras);
                string path;
                try
                {
                    path = ImageStore.Save(bmp, folder, m.Id, img.Seed, meta, embedded);
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException && !IsDefaultOutput(folder))
                {
                    // Don't lose the image: save it to the default (temp) folder and say so.
                    var fallback = flagged ? Path.Combine(AppPaths.DefaultOutputDir, "flagged") : AppPaths.DefaultOutputDir;
                    path = ImageStore.Save(bmp, fallback, m.Id, img.Seed, meta, embedded);
                    MessageBox.Show($"Couldn't save to the output folder:\n{ex.Message}\n\nThe image was saved to\n{fallback}\n\n" +
                                    "Use Change… under Storage on the right to pick another folder.",
                        "Output folder", MessageBoxButton.OK, MessageBoxImage.Warning);
                }
                if (incognito)
                {
                    _settings.IncognitoPending.Add(path);
                    _settings.Save();
                }
                Gallery.Insert(0, new GalleryItem { Path = path, Thumb = ImageStore.LoadThumbnail(path), Flagged = flagged });
                CurrentImage = bmp;
                CurrentPath = path;
                CurrentFlagged = flagged;
                CurrentInfo = $"{m.Name} · {img.Width}×{img.Height} · seed {img.Seed} · {FormatDuration(clock.Elapsed)}" +
                              (initImage is not null ? $" · from start image ({InitStrength:0.00})" : "") +
                              (activeLoras.Count > 0 ? $" · LoRA: {string.Join(", ", activeLoras.Select(l => l.Name))}" : "") +
                              (score is float sc ? $" · NSFW score {sc:0.00}" : "");
            }

            clock.Stop();
            var loadPart = loadTime > TimeSpan.Zero ? $"model load {FormatDuration(loadTime)}, " : "";
            var poorLoras = activeLoras.Where(l => l.Warn).Select(l => l.Name).ToList();
            Status = result.Cancelled
                ? $"Cancelled after {FormatDuration(clock.Elapsed)}."
                : poorLoras.Count > 0
                    ? $"Done in {FormatDuration(clock.Elapsed)}, but {string.Join(", ", poorLoras)} didn't fit {m.Name} and had little or no effect. Hover the ⚠ for details."
                    : $"Done: {result.Images.Count} image(s) in {FormatDuration(clock.Elapsed)} from pressing Generate " +
                      $"({loadPart}sampling and decoding {FormatDuration(result.GenerateTime)}) · {result.Sampler}/{result.Scheduler} · seed {seed}";
        }
        catch (OperationCanceledException)
        {
            Status = $"Cancelled after {FormatDuration(clock.Elapsed)}.";
        }
        catch (Exception ex)
        {
            Status = "Failed — see the log.";
            var tip = ex is IOException or UnauthorizedAccessException
                ? "\n\nTip: check the output folder under Storage on the right, or pick another one with Change…."
                : "\n\nTip: if this is an out-of-memory error, tick 'Offload weights to RAM', lower the size, or pick a smaller quantization tier.";
            MessageBox.Show(ex.Message + tip, "Generation failed", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            clock.Stop();
            StopElapsedTimer(clock);
            IsBusy = false;
            ProgressText = "";
            ProgressValue = 0;
            _genCts.Dispose();
            _genCts = null;
            Raise(nameof(LoadedText));
            _settings.Save();
        }
    }

    private async Task EnsureNsfwClassifierAsync()
    {
        if (_nsfw is not null) return;
        var path = Path.Combine(AppPaths.ToolsDir, "nsfw_image_detection.onnx");
        try
        {
            if (!File.Exists(path))
            {
                Status = "Downloading NSFW classifier (90 MB)…";
                var url = new CatalogFile { Repo = NsfwClassifier.Repo, Path = NsfwClassifier.RepoPath }.Url;
                await Task.Run(() => Downloader.DownloadAsync(url, path, _settings.HfToken, null, CancellationToken.None));
                Status = "NSFW classifier ready.";
            }
            _nsfw ??= await Task.Run(() => new NsfwClassifier(path));
        }
        catch (Exception ex)
        {
            Status = "NSFW classifier unavailable: " + ex.Message;
        }
    }

    // ---------------------------------------------------------------- engine callbacks (worker thread)

    private void OnEngineLog(SdLogLevel level, string msg)
    {
        // The engine's verbose lines include the prompt text, so nothing is written while incognito.
        var log = _log;
        if (log is not null)
            lock (log)
            {
                try { log.WriteLine($"{DateTime.Now:HH:mm:ss} [{level}] {msg}"); } catch { }
            }
        if (level < SdLogLevel.Info) return;
        _ui.BeginInvoke(() =>
        {
            LogLines.Add($"[{level}] {msg}");
            while (LogLines.Count > 400) LogLines.RemoveAt(0);
        });
    }

    private void OnEngineProgress(int step, int steps, float secondsPerStep)
    {
        // The engine reports tensor loading and VAE tiles through the same callback; only a
        // count equal to the requested steps is sampling.
        bool sampling = steps == Volatile.Read(ref _expectedSteps);
        _ui.BeginInvoke(() =>
        {
            // Callbacks queued before the run finished arrive after the bar was reset; ignore them.
            if (!IsBusy) return;
            ProgressMax = Math.Max(1, steps);
            ProgressValue = step;
            if (!sampling)
                ProgressText = steps > 0 ? $"Working {step * 100 / steps}%" : "";
            else if (step == 0 || secondsPerStep <= 0 || secondsPerStep > 900)
                ProgressText = $"Step {step}/{steps}";
            else
                ProgressText = $"Step {step}/{steps} · {(secondsPerStep >= 1 ? $"{secondsPerStep:0.00} s/it" : $"{1 / secondsPerStep:0.0} it/s")}";
        });
    }

    private int _expectedSteps;

    private void OnEnginePreview(RawImage img)
    {
        // Drop frames while the UI is still drawing the previous one.
        if (Interlocked.Exchange(ref _previewPending, 1) == 1) return;
        var bmp = ImageStore.ToBitmap(img);
        _ui.BeginInvoke(() =>
        {
            CurrentImage = bmp;
            Interlocked.Exchange(ref _previewPending, 0);
        });
    }

    // ---------------------------------------------------------------- gallery / misc

    private void LoadGallery()
    {
        try
        {
            if (!Directory.Exists(_settings.OutputDir)) return;
            var files = Directory.EnumerateFiles(_settings.OutputDir, "*.png", SearchOption.AllDirectories)
                .Select(p => new FileInfo(p)).OrderByDescending(f => f.LastWriteTimeUtc).Take(60);
            foreach (var f in files)
            {
                bool flagged = string.Equals(f.Directory?.Name, "flagged", StringComparison.OrdinalIgnoreCase);
                Gallery.Add(new GalleryItem { Path = f.FullName, Thumb = ImageStore.LoadThumbnail(f.FullName), Flagged = flagged });
            }
        }
        catch { /* gallery is optional */ }
    }

    private void ShowGalleryItem(GalleryItem? item)
    {
        if (item is null || IsBusy) return;
        CurrentImage = ImageStore.LoadFull(item.Path);
        CurrentPath = item.Path;
        CurrentRevealed = false;
        CurrentFlagged = item.Flagged;
        CurrentInfo = Path.GetFileName(item.Path);
        try
        {
            var meta = Path.ChangeExtension(item.Path, ".json");
            if (File.Exists(meta))
            {
                using var doc = System.Text.Json.JsonDocument.Parse(File.ReadAllText(meta));
                var r = doc.RootElement;
                if (r.TryGetProperty("totalSeconds", out var t) && t.TryGetDouble(out var secs))
                    CurrentInfo = $"{CurrentInfo} · took {FormatDuration(TimeSpan.FromSeconds(secs))}";
            }
        }
        catch { }
        if (StoredPrompt(item.Path) is { Length: > 0 } prompt) CurrentInfo = $"{CurrentInfo}\n{prompt}";
    }

    private static bool IsDefaultOutput(string folder)
    {
        var f = Path.GetFullPath(folder).TrimEnd('\\', '/');
        var d = Path.GetFullPath(AppPaths.DefaultOutputDir).TrimEnd('\\', '/');
        return string.Equals(f, d, StringComparison.OrdinalIgnoreCase) ||
               string.Equals(f, Path.Combine(d, "flagged"), StringComparison.OrdinalIgnoreCase);
    }

    private static void OpenPath(string? path)
    {
        if (string.IsNullOrEmpty(path)) return;
        try
        {
            if (!path.StartsWith("http") && !File.Exists(path)) Directory.CreateDirectory(path);
            Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message, AppPaths.AppName);
        }
    }

    public void OnClosing()
    {
        _genCts?.Cancel();
        foreach (var m in Models) m.DownloadCts?.Cancel();
        _settings.LastPrompt = Incognito ? "" : Prompt;
        if (Selected is not null) _settings.LastModelId = Selected.Id;
        SaveActiveLoras();
        _loraDownloadCts?.Cancel();
        DeleteIncognitoImages();
        StopHelperServer();
        _settings.Save();
    }

    public void Dispose()
    {
        StopHelperServer();
        if (!IsBusy) _engine?.Dispose();
        _nsfw?.Dispose();
        CloseLog();
    }

    // ---------------------------------------------------------------- run timer

    private DispatcherTimer? _elapsedTimer;

    private string _elapsedText = "";
    /// <summary>Time since Generate was pressed (live while running, then the last run's total).</summary>
    public string ElapsedText { get => _elapsedText; private set => Set(ref _elapsedText, value); }

    private void StartElapsedTimer(Stopwatch clock)
    {
        _elapsedTimer?.Stop();
        ElapsedText = "⏱ 0.0 s";
        _elapsedTimer = new DispatcherTimer(TimeSpan.FromMilliseconds(200), DispatcherPriority.Background, (_, _) =>
            ElapsedText = $"⏱ {FormatDuration(clock.Elapsed)}", _ui);
        _elapsedTimer.Start();
    }

    private void StopElapsedTimer(Stopwatch clock)
    {
        _elapsedTimer?.Stop();
        _elapsedTimer = null;
        ElapsedText = $"⏱ last run {FormatDuration(clock.Elapsed)}";
    }

    private static string FormatDuration(TimeSpan t) =>
        t.TotalSeconds < 60 ? $"{t.TotalSeconds:0.0} s" : $"{(int)t.TotalMinutes} min {t.Seconds:00} s";

    // ---------------------------------------------------------------- incognito / clearing images

    public bool Incognito
    {
        get => _settings.Incognito;
        set
        {
            if (_settings.Incognito == value) return;
            _settings.Incognito = value;
            if (value)
            {
                // Forget what this session already recorded: the log holds this session's prompts.
                CloseLog();
                DeleteLogFile();
                _settings.LastPrompt = "";
                Status = "Incognito on: no log, no prompt history, no .json files. Each run's images are deleted when you run the next prompt or close the app.";
            }
            else
            {
                OpenLog();
                Status = "Incognito off. Images are kept, and the log and last prompt are saved again.";
            }
            _settings.Save();
            Raise();
        }
    }

    private void OpenLog()
    {
        try
        {
            _log = new StreamWriter(AppPaths.LogFile, append: false) { AutoFlush = true };
        }
        catch { /* another instance has the log open */ }
    }

    private void CloseLog()
    {
        var log = _log;
        _log = null;
        if (log is null) return;
        lock (log) log.Dispose();
    }

    private static void DeleteLogFile()
    {
        try { File.Delete(AppPaths.LogFile); } catch { /* another instance has it open */ }
    }

    /// <summary>Deletes the images from the previous incognito run and drops them from the gallery.</summary>
    private void DeleteIncognitoImages()
    {
        if (_settings.IncognitoPending.Count == 0) return;
        var gone = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var p in _settings.IncognitoPending.ToList())
        {
            try
            {
                File.Delete(p);
                File.Delete(Path.ChangeExtension(p, ".json"));
                gone.Add(p);
            }
            catch { /* still open in a viewer: keep it on the list and try again next time */ }
        }
        _settings.IncognitoPending.RemoveAll(gone.Contains);
        RemoveFromView(gone);
        _settings.Save();
    }

    private void RemoveFromView(HashSet<string> deleted)
    {
        foreach (var g in Gallery.Where(g => deleted.Contains(g.Path)).ToList()) Gallery.Remove(g);
        if (CurrentPath is not null && deleted.Contains(CurrentPath))
        {
            CurrentPath = null;
            CurrentImage = null;
            CurrentInfo = "";
        }
    }

    /// <summary>
    /// Removes the shown image from the preview and the gallery and deletes it with its .json sidecar:
    /// to the Recycle Bin normally, permanently for incognito images (the Recycle Bin would keep a copy).
    /// </summary>
    private void DeleteCurrentImage()
    {
        var path = CurrentPath;
        if (path is null) return;
        bool permanent = Incognito || _settings.IncognitoPending.Contains(path, StringComparer.OrdinalIgnoreCase);
        try
        {
            foreach (var f in new[] { path, Path.ChangeExtension(path, ".json") }.Where(File.Exists))
            {
                if (permanent)
                    File.Delete(f);
                else
                    Microsoft.VisualBasic.FileIO.FileSystem.DeleteFile(f,
                        Microsoft.VisualBasic.FileIO.UIOption.OnlyErrorDialogs, Microsoft.VisualBasic.FileIO.RecycleOption.SendToRecycleBin);
            }
        }
        catch (Exception ex)
        {
            Status = $"Couldn't delete the image: {ex.Message}";
            return;
        }
        if (_settings.IncognitoPending.RemoveAll(p => string.Equals(p, path, StringComparison.OrdinalIgnoreCase)) > 0)
            _settings.Save();
        RemoveFromView(new HashSet<string>(StringComparer.OrdinalIgnoreCase) { path });
        if (permanent) ForgetIncognitoInputs(new[] { path });
        Status = permanent ? "Image deleted, and its prompt cleared." : "Image moved to the Recycle Bin.";
    }

    /// <summary>Deletes every image and .json sidecar the app saved in the output folder (and its flagged subfolder).</summary>
    private void ClearOutputFolder()
    {
        var dir = _settings.OutputDir;
        var files = new[] { dir, Path.Combine(dir, "flagged") }
            .Where(Directory.Exists)
            .SelectMany(d => Directory.EnumerateFiles(d))
            .Where(f => ImageStore.OwnFileName.IsMatch(Path.GetFileName(f)))
            .ToList();
        if (files.Count == 0)
        {
            Status = $"No generated images in {dir}.";
            return;
        }
        int images = files.Count(f => f.EndsWith(".png", StringComparison.OrdinalIgnoreCase));
        double mb = files.Sum(f => new FileInfo(f).Length) / 1e6;
        if (MessageBox.Show($"Permanently delete {images} image(s) and their .json files ({mb:0.#} MB) from\n{dir}?\n\n" +
                            "Only files ImageMango created are deleted. They don't go to the Recycle Bin.",
                "Clear output folder", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes) return;

        var deleted = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        int failed = 0;
        foreach (var f in files)
        {
            try { File.Delete(f); deleted.Add(f); }
            catch { failed++; }
        }
        try
        {
            var flagged = Path.Combine(dir, "flagged");
            if (Directory.Exists(flagged) && !Directory.EnumerateFileSystemEntries(flagged).Any()) Directory.Delete(flagged);
        }
        catch { }
        var incognitoGone = _settings.IncognitoPending.Where(deleted.Contains).ToList();
        _settings.IncognitoPending.RemoveAll(deleted.Contains);
        _settings.Save();
        RemoveFromView(deleted);
        if (incognitoGone.Count > 0 || Incognito) ForgetIncognitoInputs(incognitoGone);
        Status = failed == 0
            ? $"Deleted {images} image(s) from {dir}."
            : $"Deleted {deleted.Count} file(s); {failed} couldn't be deleted (open in another app?).";
    }
}
