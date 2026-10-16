using System.Text.Json;
using System.Text.Json.Nodes;

namespace ImageMango.Core;

/// <summary>
/// Per-model generation and memory settings. Catalog defaults, the machine tier and the
/// user's saved overrides are layered as JSON objects, so any subset of keys can be set.
/// </summary>
public sealed class GenSettings
{
    public int Width { get; set; } = 1024;
    public int Height { get; set; } = 1024;
    public int Steps { get; set; } = 20;
    public double CfgScale { get; set; } = 7.0;
    /// <summary>Distilled guidance (FLUX.1-dev). Null = engine default.</summary>
    public double? Guidance { get; set; }
    public string Sampler { get; set; } = "default";
    public string Scheduler { get; set; } = "default";
    public double? FlowShift { get; set; }
    public string NegativePrompt { get; set; } = "";
    public int ClipSkip { get; set; } = -1;

    // Memory / context settings (changing these reloads the model).
    public bool OffloadToCpu { get; set; }
    public bool TextEncoderOnCpu { get; set; }
    public bool VaeOnCpu { get; set; }
    public bool VaeTiling { get; set; }
    public string FlashAttention { get; set; } = "auto";
    public string? WeightType { get; set; }
    public string? Prediction { get; set; }
    public string? ModelArgs { get; set; }
    public string? MaxVram { get; set; }

    public JsonObject ToJsonObject() =>
        JsonSerializer.SerializeToNode(this, Json.Options)!.AsObject();

    public static GenSettings FromLayers(params JsonObject?[] layers)
    {
        var merged = new GenSettings().ToJsonObject();
        foreach (var layer in layers) Json.Overlay(merged, layer);
        return merged.Deserialize<GenSettings>(Json.Options) ?? new GenSettings();
    }

    public GenSettings Clone() => FromLayers(ToJsonObject());

    public static readonly string[] Samplers =
    {
        "default", "euler", "euler_a", "heun", "dpm2", "dpm++2s_a", "dpm++2m", "dpm++2mv2", "ipndm", "ipndm_v",
        "lcm", "ddim_trailing", "tcd", "res_multistep", "res_2s", "er_sde", "euler_cfg_pp", "euler_a_cfg_pp",
        "euler_ge", "dpm++2m_sde", "dpm++2m_sde_bt", "lms",
    };

    public static readonly string[] Schedulers =
    {
        "default", "discrete", "karras", "exponential", "ays", "gits", "sgm_uniform", "simple", "smoothstep",
        "kl_optimal", "lcm", "bong_tangent", "logit_normal", "flux2", "flux", "beta",
    };

    public static readonly string[] FlashAttentionModes = { "auto", "on", "off" };

    public static readonly string[] WeightTypes = { "", "f16", "q8_0", "q6_K", "q5_K", "q5_0", "q4_K", "q4_0", "q3_K", "q2_K" };
}
