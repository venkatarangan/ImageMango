using ImageMango.Core;

namespace ImageMango.UI;

/// <summary>Bindable copy of <see cref="GenSettings"/> for the selected model.</summary>
public sealed class SettingsViewModel : Observable
{
    public SettingsViewModel(GenSettings s, Dictionary<string, string>? help = null)
    {
        Help = help ?? SettingHelp.For(null, null);
        _width = s.Width; _height = s.Height; _steps = s.Steps; _cfgScale = s.CfgScale;
        _guidance = s.Guidance; _sampler = s.Sampler; _scheduler = s.Scheduler; _flowShift = s.FlowShift;
        _negativePrompt = s.NegativePrompt; _clipSkip = s.ClipSkip;
        _offloadToCpu = s.OffloadToCpu; _textEncoderOnCpu = s.TextEncoderOnCpu; _vaeOnCpu = s.VaeOnCpu;
        _vaeTiling = s.VaeTiling; _flashAttention = s.FlashAttention; _weightType = s.WeightType ?? "";
        _prediction = s.Prediction; _modelArgs = s.ModelArgs ?? ""; _maxVram = s.MaxVram ?? "";
    }

    public GenSettings ToGenSettings() => new()
    {
        Width = RoundTo16(Width), Height = RoundTo16(Height), Steps = Math.Max(1, Steps), CfgScale = CfgScale,
        Guidance = Guidance, Sampler = Sampler, Scheduler = Scheduler, FlowShift = FlowShift,
        NegativePrompt = NegativePrompt ?? "", ClipSkip = ClipSkip,
        OffloadToCpu = OffloadToCpu, TextEncoderOnCpu = TextEncoderOnCpu, VaeOnCpu = VaeOnCpu,
        VaeTiling = VaeTiling, FlashAttention = FlashAttention,
        WeightType = string.IsNullOrWhiteSpace(WeightType) ? null : WeightType.Trim(),
        Prediction = _prediction,
        ModelArgs = string.IsNullOrWhiteSpace(ModelArgs) ? null : ModelArgs.Trim(),
        MaxVram = string.IsNullOrWhiteSpace(MaxVram) ? null : MaxVram.Trim(),
    };

    private static int RoundTo16(int v) => Math.Clamp((int)Math.Round(v / 16.0) * 16, 64, 4096);

    private int _width;
    public int Width { get => _width; set => Set(ref _width, value); }
    private int _height;
    public int Height { get => _height; set => Set(ref _height, value); }
    private int _steps;
    public int Steps { get => _steps; set => Set(ref _steps, value); }
    private double _cfgScale;
    public double CfgScale { get => _cfgScale; set => Set(ref _cfgScale, value); }
    private double? _guidance;
    public double? Guidance { get => _guidance; set => Set(ref _guidance, value); }
    private string _sampler;
    public string Sampler { get => _sampler; set => Set(ref _sampler, value); }
    private string _scheduler;
    public string Scheduler { get => _scheduler; set => Set(ref _scheduler, value); }
    private double? _flowShift;
    public double? FlowShift { get => _flowShift; set => Set(ref _flowShift, value); }
    private string _negativePrompt;
    public string NegativePrompt { get => _negativePrompt; set => Set(ref _negativePrompt, value); }
    private int _clipSkip;
    public int ClipSkip { get => _clipSkip; set => Set(ref _clipSkip, value); }

    private bool _offloadToCpu;
    public bool OffloadToCpu { get => _offloadToCpu; set => Set(ref _offloadToCpu, value); }
    private bool _textEncoderOnCpu;
    public bool TextEncoderOnCpu { get => _textEncoderOnCpu; set => Set(ref _textEncoderOnCpu, value); }
    private bool _vaeOnCpu;
    public bool VaeOnCpu { get => _vaeOnCpu; set => Set(ref _vaeOnCpu, value); }
    private bool _vaeTiling;
    public bool VaeTiling { get => _vaeTiling; set => Set(ref _vaeTiling, value); }
    private string _flashAttention;
    public string FlashAttention { get => _flashAttention; set => Set(ref _flashAttention, value); }
    private string _weightType;
    public string WeightType { get => _weightType; set => Set(ref _weightType, value); }
    private string? _prediction;
    private string _modelArgs;
    public string ModelArgs { get => _modelArgs; set => Set(ref _modelArgs, value); }
    private string _maxVram;
    public string MaxVram { get => _maxVram; set => Set(ref _maxVram, value); }

    /// <summary>Tooltip text per setting name, bound as Help[Steps] etc.</summary>
    public Dictionary<string, string> Help { get; }

    public IReadOnlyList<string> Samplers => GenSettings.Samplers;
    public IReadOnlyList<string> Schedulers => GenSettings.Schedulers;
    public IReadOnlyList<string> FlashAttentionModes => GenSettings.FlashAttentionModes;
    public IReadOnlyList<string> WeightTypes => GenSettings.WeightTypes;
}
