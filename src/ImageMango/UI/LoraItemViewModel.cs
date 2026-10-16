using ImageMango.Core;

namespace ImageMango.UI;

/// <summary>A LoRA switched on for the selected model: one row under the prompt with its weight slider.</summary>
public sealed class LoraItemViewModel : Observable
{
    private readonly Action _changed;

    public LoraItemViewModel(LoraInfo info, double weight, string? modelFamily, Action changed)
    {
        Info = info;
        _weight = weight;
        _changed = changed;
        Fit = LoraFamilies.Fit(info.Family, modelFamily);
        _baseText = FitDescription(info, Fit);
        _fitText = _baseText;
    }

    private readonly string _baseText;

    public LoraInfo Info { get; }
    public string Name => Info.Name;
    public string Path => Info.Path;
    public LoraFit Fit { get; }
    public bool HasTrigger => !string.IsNullOrWhiteSpace(Info.TriggerWords);
    public string TriggerWords => Info.TriggerWords;

    private double _weight;
    /// <summary>LoRA strength. 1 is full strength; most LoRAs look best between 0.6 and 1.</summary>
    public double Weight
    {
        get => _weight;
        set
        {
            if (Set(ref _weight, Math.Round(Math.Clamp(value, -2, 2), 2)))
                _changed();
        }
    }

    private string _fitText;
    /// <summary>Tooltip: what the LoRA is for and, after a run, how much of it applied.</summary>
    public string FitText { get => _fitText; private set => Set(ref _fitText, value); }

    private bool _warn;
    /// <summary>Shows the ⚠ badge: made for another model, or the last run applied little of it.</summary>
    public bool Warn { get => _warn || Fit == LoraFit.Mismatch; private set => Set(ref _warn, value); }

    /// <summary>Called with the engine's report after a run.</summary>
    public void ReportApplied(int applied, int total)
    {
        if (total <= 0) return;
        bool poor = applied * 2 < total;
        Warn = poor;
        FitText = _baseText + (poor
            ? $"\n\nLast run: only {applied} of {total} parts of this LoRA matched the model, so it had little or no effect. It was probably made for a different model type."
            : $"\n\nLast run: {applied} of {total} parts applied.");
        Raise(nameof(Warn));
    }

    private static string FitDescription(LoraInfo info, LoraFit fit)
    {
        var made = info.Family is null
            ? "ImageMango couldn't tell which model type this LoRA was made for."
            : $"Made for {LoraFamilies.NameOf(info.Family)} models (from {info.FamilySource ?? "its file"}).";
        var verdict = fit switch
        {
            LoraFit.Match => " It fits the selected model.",
            LoraFit.Maybe => " It may partly work with the selected model.",
            LoraFit.Mismatch => " It won't work with the selected model: switch it off or pick a matching model.",
            _ => " Try it: after a run, this tooltip says how much of it applied.",
        };
        var trigger = string.IsNullOrWhiteSpace(info.TriggerWords) ? "" : $"\n\nTrigger words: {info.TriggerWords}";
        return $"{info.Name}\n{info.FileName} · {info.SizeText}\n\n{made}{verdict}{trigger}";
    }
}
