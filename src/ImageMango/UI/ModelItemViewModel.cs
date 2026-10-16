using System.IO;
using ImageMango.Core;

namespace ImageMango.UI;

public enum FitLevel { Good, Offload, TooLarge }

/// <summary>One catalog entry, evaluated against this machine and the local models folder.</summary>
public sealed class ModelItemViewModel : Observable
{
    public CatalogModel Model { get; }

    public ModelItemViewModel(CatalogModel model) => Model = model;

    public string Id => Model.Id;
    public string Name => Model.Name;
    public string Subtitle => $"{Model.Publisher} · {Model.Released} · {Model.License}";
    public string Description => Model.Description;
    public string Tips => Model.Tips;
    public IReadOnlyList<string> SamplePrompts => Model.SamplePrompts;
    public string PageUrl => Model.PageUrl;
    public string Category => Model.Category;

    public string CategoryLabel => Model.Category switch
    {
        "filtered" => "Filtered",
        "nfaa" => "Not for all audiences",
        _ => "Unfiltered",
    };

    /// <summary>Sort key so groups appear Filtered, Unfiltered, NFAA.</summary>
    public int CategoryOrder => Model.Category switch { "filtered" => 0, "nfaa" => 2, _ => 1 };

    private CatalogTier _tier = null!;
    public CatalogTier Tier { get => _tier; private set => Set(ref _tier, value); }

    private IReadOnlyList<CatalogFile> _files = Array.Empty<CatalogFile>();
    public IReadOnlyList<CatalogFile> Files => _files;

    private string _sizeText = "";
    public string SizeText { get => _sizeText; private set => Set(ref _sizeText, value); }

    private FitLevel _fit;
    public FitLevel Fit { get => _fit; private set => Set(ref _fit, value); }

    private string _fitText = "";
    public string FitText { get => _fitText; private set => Set(ref _fitText, value); }

    private bool _isReady;
    public bool IsReady { get => _isReady; private set { if (Set(ref _isReady, value)) Raise(nameof(StatusText)); } }

    private double _missingGb;
    public double MissingGb { get => _missingGb; private set { if (Set(ref _missingGb, value)) Raise(nameof(StatusText)); } }

    public string StatusText => IsDownloading ? DownloadText
        : IsReady ? "Downloaded"
        : MissingGb < TotalGb - 0.01 ? $"Partly downloaded · {MissingGb:0.0} GB to go"
        : $"Not downloaded · {TotalGb:0.0} GB";

    public double TotalGb { get; private set; }

    private bool _isDownloading;
    public bool IsDownloading { get => _isDownloading; set { if (Set(ref _isDownloading, value)) Raise(nameof(StatusText)); } }

    private double _downloadPercent;
    public double DownloadPercent { get => _downloadPercent; set => Set(ref _downloadPercent, value); }

    private string _downloadText = "";
    public string DownloadText { get => _downloadText; set { if (Set(ref _downloadText, value)) Raise(nameof(StatusText)); } }

    public CancellationTokenSource? DownloadCts { get; set; }

    public void Evaluate(MachineProfile machine, string modelsDir)
    {
        Tier = Model.SelectTier(machine.EffectiveVramGb);
        _files = Model.FilesFor(Tier);
        Raise(nameof(Files));

        TotalGb = _files.Sum(f => f.SizeGb);
        var rec = Model.Recommended(machine.EffectiveVramGb);
        double mainGb = _files.Where(f => f.Role is "diffusion" or "model").Sum(f => f.SizeGb);
        if (!string.IsNullOrEmpty(rec.WeightType))
            mainGb *= rec.WeightType.StartsWith("q8") ? 0.55 : rec.WeightType.StartsWith("q4") ? 0.3 : 1.0;
        double vram = machine.EffectiveVramGb, ram = machine.RamGb;

        if (!rec.OffloadToCpu && TotalGb + 1.5 <= vram)
        {
            Fit = FitLevel.Good;
            FitText = "Fits in VRAM";
        }
        else if (mainGb + 1.5 <= vram && TotalGb <= vram + ram * 0.75)
        {
            Fit = FitLevel.Good;
            FitText = "Fits (text encoder swaps via RAM)";
        }
        else if (TotalGb <= vram + ram * 0.75)
        {
            Fit = FitLevel.Offload;
            FitText = "Runs with RAM offload (slower)";
        }
        else
        {
            Fit = FitLevel.TooLarge;
            FitText = "Too large for this PC";
        }

        SizeText = $"{TotalGb:0.0} GB · tier {Tier.Label}";
        RefreshStatus(modelsDir);
    }

    public void RefreshStatus(string modelsDir)
    {
        MissingGb = _files.Where(f => !File.Exists(f.LocalPath(modelsDir))).Sum(f => f.SizeGb);
        IsReady = _files.All(f => File.Exists(f.LocalPath(modelsDir)));
        Raise(nameof(StatusText));
    }
}
