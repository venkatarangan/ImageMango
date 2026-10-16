using System.Windows.Media.Imaging;
using ImageMango.Core;

namespace ImageMango.UI;

/// <summary>One row in the LoRA browser: a search result, its preview, whether it's ticked, and its download state.</summary>
public sealed class LoraResultItem : Observable
{
    public LoraResultItem(LoraSearchResult result, bool downloaded)
    {
        Result = result;
        _isDownloaded = downloaded;
        _status = downloaded ? "Downloaded" : "";
    }

    public LoraSearchResult Result { get; }
    public string Name => Result.Name;

    public string Details => string.Join(" · ", new[]
    {
        Result.Author.Length > 0 ? $"by {Result.Author}" : "",
        Result.Source,
        Result.BaseModel,
        Result.SizeBytes is long b ? (b >= 1_000_000_000 ? $"{b / 1e9:0.0} GB" : $"{b / 1e6:0} MB") : "",
        Result.Downloads > 0 ? $"⬇ {Result.Downloads:N0}" + (Result.Source == "Hugging Face" ? " last month" : "") : "",
        Result.Likes > 0 ? $"♥ {Result.Likes:N0}" : "",
    }.Where(s => s.Length > 0));

    public string TriggerText => string.IsNullOrWhiteSpace(Result.TriggerWords) ? "" : $"Trigger words: {Result.TriggerWords}";
    public bool HasTrigger => TriggerText.Length > 0;

    private bool _isChecked;
    public bool IsChecked { get => _isChecked; set => Set(ref _isChecked, value && !IsDownloaded); }

    private bool _isDownloaded;
    public bool IsDownloaded
    {
        get => _isDownloaded;
        set
        {
            if (!Set(ref _isDownloaded, value)) return;
            if (value) IsChecked = false;
            Raise(nameof(CanTick));
        }
    }

    public bool CanTick => !IsDownloaded;

    private string _status;
    public string Status { get => _status; set { if (Set(ref _status, value)) Raise(nameof(HasStatus)); } }
    public bool HasStatus => Status.Length > 0;

    private double _progress;
    public double Progress { get => _progress; set => Set(ref _progress, value); }

    private bool _isDownloading;
    public bool IsDownloading { get => _isDownloading; set => Set(ref _isDownloading, value); }

    private BitmapSource? _thumb;
    public BitmapSource? Thumb { get => _thumb; set => Set(ref _thumb, value); }

    private bool _blurThumb;
    /// <summary>Preview rated as adult content (Civitai) or flagged by the NSFW classifier.</summary>
    public bool BlurThumb { get => _blurThumb; set => Set(ref _blurThumb, value); }
}
