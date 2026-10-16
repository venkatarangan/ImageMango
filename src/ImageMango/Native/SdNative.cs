// P/Invoke bindings for stable-diffusion.cpp, pinned to release master-964-1d0a58a
// (include/stable-diffusion.h at commit 1d0a58a). Struct and field names mirror the C
// header exactly so tools/InteropCheck can compare offsets against the native layout.
// If you upgrade the engine DLL, re-run tools/InteropCheck before shipping.
using System.Runtime.InteropServices;

namespace ImageMango.Native;

public static class SdVersion
{
    public const string PinnedRelease = "master-964-1d0a58a";
    public const string PinnedCommit = "1d0a58a";
}

/// <summary>C99 bool (1 byte). Keeps every struct blittable.</summary>
[StructLayout(LayoutKind.Sequential)]
public struct CBool
{
    private byte _value;
    public static implicit operator bool(CBool b) => b._value != 0;
    public static implicit operator CBool(bool b) => new() { _value = b ? (byte)1 : (byte)0 };
}

public enum SdLogLevel { Debug, Verbose, Info, Warn, Error }
public enum SdPreview { None, Proj, Tae, Vae }
public enum SdCancelMode { All, NewLatents, Reset }
public enum SdLoraApplyMode { Auto, Immediately, AtRuntime }

public static class SdConst
{
    public const int SAMPLE_METHOD_COUNT = 21;
    public const int SCHEDULER_COUNT = 17;
    public const int PREDICTION_COUNT = 8;
    public const int SD_TYPE_COUNT = 45;
    public const int RNG_TYPE_COUNT = 3;
    public const int CUDA_RNG = 1;
    public const int SD_VAE_FORMAT_AUTO = -1;
}

[StructLayout(LayoutKind.Sequential)]
public struct sd_image_t
{
    public uint width;
    public uint height;
    public uint channel;
    public nint data;
}

[StructLayout(LayoutKind.Sequential)]
public struct sd_tiling_params_t
{
    public CBool enabled;
    public CBool temporal_tiling;
    public int tile_size_w;
    public int tile_size_h;
    public float target_overlap;
    public float rel_size_w;
    public float rel_size_h;
    public nint extra_tiling_args;
}

[StructLayout(LayoutKind.Sequential)]
public struct sd_ctx_params_t
{
    public nint model_path;
    public nint clip_l_path;
    public nint clip_g_path;
    public nint clip_vision_path;
    public nint t5xxl_path;
    public nint llm_path;
    public nint llm_vision_path;
    public nint diffusion_model_path;
    public nint high_noise_diffusion_model_path;
    public nint uncond_diffusion_model_path;
    public nint embeddings_connectors_path;
    public nint vae_path;
    public nint audio_vae_path;
    public nint audio_encoder_path;
    public nint taesd_path;
    public nint control_net_path;
    public nint ip_adapter_path;
    public nint motion_module_path;
    public nint embeddings;
    public uint embedding_count;
    public nint photo_maker_path;
    public nint pulid_weights_path;
    public nint tensor_type_rules;
    public int n_threads;
    public int wtype;
    public int rng_type;
    public int sampler_rng_type;
    public int prediction;
    public int lora_apply_mode;
    public CBool enable_mmap;
    public CBool flash_attn;
    public CBool diffusion_flash_attn;
    public CBool tae_preview_only;
    public CBool diffusion_conv_direct;
    public CBool vae_conv_direct;
    public CBool force_sdxl_vae_conv_scale;
    public int vae_format;
    public nint max_vram;
    public CBool disable_prefetch;
    public CBool eager_load;
    public nint backend;
    public nint params_backend;
    public nint split_mode;
    public CBool auto_fit;
    public nint rpc_servers;
    public nint model_args;
    public CBool disable_segmented_compute;
    public CBool batched_cfg;
    public float linear_scale;
    public float attn_scale;
    public nint tokenizer;
    public CBool sage_attn;
    public int conditioning_cache_size;
    public nint flash_attn_mode;
    public nint conv_direct_mode;
}

[StructLayout(LayoutKind.Sequential)]
public struct sd_slg_params_t
{
    public nint layers;
    public nuint layer_count;
    public float layer_start;
    public float layer_end;
    public float scale;
}

[StructLayout(LayoutKind.Sequential)]
public struct sd_guidance_params_t
{
    public float txt_cfg;
    public float img_cfg;
    public float distilled_guidance;
    public sd_slg_params_t slg;
}

[StructLayout(LayoutKind.Sequential)]
public struct sd_sample_params_t
{
    public sd_guidance_params_t guidance;
    public int scheduler;
    public int sample_method;
    public int sample_steps;
    public float eta;
    public int shifted_timestep;
    public nint custom_sigmas;
    public int custom_sigmas_count;
    public float flow_shift;
    public nint extra_sample_args;
}

[StructLayout(LayoutKind.Sequential)]
public struct sd_pm_params_t
{
    public nint id_images;
    public int id_images_count;
    public nint id_embed_path;
    public float style_strength;
}

[StructLayout(LayoutKind.Sequential)]
public struct sd_pulid_params_t
{
    public nint id_embedding_path;
    public float id_weight;
}

[StructLayout(LayoutKind.Sequential)]
public struct sd_cache_params_t
{
    public int mode;
    public float reuse_threshold;
    public float start_percent;
    public float end_percent;
    public float error_decay_rate;
    public CBool use_relative_threshold;
    public CBool reset_error_on_compute;
    public int Fn_compute_blocks;
    public int Bn_compute_blocks;
    public float residual_diff_threshold;
    public int max_warmup_steps;
    public int max_cached_steps;
    public int max_continuous_cached_steps;
    public int taylorseer_n_derivatives;
    public int taylorseer_skip_interval;
    public nint scm_mask;
    public CBool scm_policy_dynamic;
    public float spectrum_w;
    public int spectrum_m;
    public float spectrum_lam;
    public int spectrum_window_size;
    public float spectrum_flex_window;
    public int spectrum_warmup_steps;
    public float spectrum_stop_percent;
}

[StructLayout(LayoutKind.Sequential)]
public struct sd_hires_params_t
{
    public CBool enabled;
    public int upscaler;
    public nint model_path;
    public float scale;
    public int target_width;
    public int target_height;
    public int steps;
    public float denoising_strength;
    public int upscale_tile_size;
    public nint custom_sigmas;
    public int custom_sigmas_count;
}

[StructLayout(LayoutKind.Sequential)]
public struct sd_lora_t
{
    public CBool is_high_noise;
    public float multiplier;
    public nint path;
}

[StructLayout(LayoutKind.Sequential)]
public struct sd_image_preprocess_params_t
{
    public nint rules;
}

[StructLayout(LayoutKind.Sequential)]
public struct sd_img_gen_params_t
{
    public nint loras;
    public uint lora_count;
    public nint prompt;
    public nint negative_prompt;
    public int clip_skip;
    public sd_image_t init_image;
    public nint ref_images;
    public int ref_images_count;
    public nint ref_image_args;
    public sd_image_t mask_image;
    public int width;
    public int height;
    public sd_sample_params_t sample_params;
    public float strength;
    public long seed;
    public int batch_count;
    public sd_image_t control_image;
    public float control_strength;
    public sd_image_t ip_adapter_image;
    public float ip_adapter_strength;
    public sd_pm_params_t pm_params;
    public sd_pulid_params_t pulid_params;
    public sd_tiling_params_t vae_tiling_params;
    public sd_cache_params_t cache;
    public sd_hires_params_t hires;
    public int qwen_image_layers;
    public CBool circular_x;
    public CBool circular_y;
    public sd_image_preprocess_params_t image_preprocess;
}

[UnmanagedFunctionPointer(CallingConvention.Cdecl)]
public delegate void SdLogCallback(SdLogLevel level, nint text, nint data);

[UnmanagedFunctionPointer(CallingConvention.Cdecl)]
public delegate void SdProgressCallback(int step, int steps, float time, nint data);

[UnmanagedFunctionPointer(CallingConvention.Cdecl)]
public delegate void SdPreviewCallback(int step, int frameCount, nint frames, byte isNoisy, nint data);

public static unsafe class SdNative
{
    // Resolves to stable-diffusion.dll on Windows and libstable-diffusion.so on Linux.
    private const string Lib = "stable-diffusion";

    [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
    public static extern void sd_set_log_callback(SdLogCallback? cb, nint data);

    [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
    public static extern void sd_set_progress_callback(SdProgressCallback? cb, nint data);

    [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
    public static extern void sd_set_preview_callback(SdPreviewCallback? cb, SdPreview mode, int interval,
        CBool denoised, CBool noisy, nint data);

    [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
    public static extern int sd_get_num_physical_cores();

    [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
    public static extern nint sd_get_system_info();

    [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
    public static extern nint sd_commit();

    [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
    public static extern nint sd_version();

    [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
    public static extern int str_to_sd_type([MarshalAs(UnmanagedType.LPUTF8Str)] string str);

    [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
    public static extern int str_to_sample_method([MarshalAs(UnmanagedType.LPUTF8Str)] string str);

    [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
    public static extern int str_to_scheduler([MarshalAs(UnmanagedType.LPUTF8Str)] string str);

    [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
    public static extern int str_to_prediction([MarshalAs(UnmanagedType.LPUTF8Str)] string str);

    [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
    public static extern nint sd_sample_method_name(int method);

    [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
    public static extern nint sd_scheduler_name(int scheduler);

    [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
    public static extern void sd_ctx_params_init(sd_ctx_params_t* p);

    [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
    public static extern nint new_sd_ctx(sd_ctx_params_t* p);

    [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
    public static extern void free_sd_ctx(nint ctx);

    [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
    public static extern nint sd_get_model_version_name(nint ctx);

    [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
    public static extern int sd_get_default_sample_method(nint ctx);

    [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
    public static extern int sd_get_default_scheduler(nint ctx, int sampleMethod);

    [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
    public static extern void sd_img_gen_params_init(sd_img_gen_params_t* p);

    [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
    [return: MarshalAs(UnmanagedType.U1)]
    public static extern bool generate_image(nint ctx, sd_img_gen_params_t* p, sd_image_t** imagesOut, int* numImagesOut);

    [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
    public static extern void sd_cancel_generation(nint ctx, SdCancelMode mode);

    [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
    public static extern void free_sd_images(sd_image_t* images, int count);

    public static string? Str(nint p) => p == 0 ? null : Marshal.PtrToStringUTF8(p);
}
