// Prints sizeof/offsetof for every struct the C# interop mirrors.
#include <stdio.h>
#include <stddef.h>
#include "stable-diffusion.h"

#define S(T) printf("%s size %zu\n", #T, sizeof(T))
#define F(T, f) printf("%s.%s %zu\n", #T, #f, offsetof(T, f))

int main(void) {
    S(sd_image_t);
    F(sd_image_t, data);

    S(sd_lora_t);
    F(sd_lora_t, multiplier);
    F(sd_lora_t, path);

    S(sd_tiling_params_t);
    F(sd_tiling_params_t, tile_size_w);
    F(sd_tiling_params_t, target_overlap);
    F(sd_tiling_params_t, extra_tiling_args);

    S(sd_ctx_params_t);
    F(sd_ctx_params_t, clip_l_path);
    F(sd_ctx_params_t, t5xxl_path);
    F(sd_ctx_params_t, llm_path);
    F(sd_ctx_params_t, diffusion_model_path);
    F(sd_ctx_params_t, vae_path);
    F(sd_ctx_params_t, taesd_path);
    F(sd_ctx_params_t, embeddings);
    F(sd_ctx_params_t, embedding_count);
    F(sd_ctx_params_t, photo_maker_path);
    F(sd_ctx_params_t, n_threads);
    F(sd_ctx_params_t, wtype);
    F(sd_ctx_params_t, lora_apply_mode);
    F(sd_ctx_params_t, enable_mmap);
    F(sd_ctx_params_t, flash_attn);
    F(sd_ctx_params_t, force_sdxl_vae_conv_scale);
    F(sd_ctx_params_t, vae_format);
    F(sd_ctx_params_t, max_vram);
    F(sd_ctx_params_t, disable_prefetch);
    F(sd_ctx_params_t, eager_load);
    F(sd_ctx_params_t, backend);
    F(sd_ctx_params_t, split_mode);
    F(sd_ctx_params_t, auto_fit);
    F(sd_ctx_params_t, rpc_servers);
    F(sd_ctx_params_t, model_args);
    F(sd_ctx_params_t, disable_segmented_compute);
    F(sd_ctx_params_t, batched_cfg);
    F(sd_ctx_params_t, linear_scale);
    F(sd_ctx_params_t, attn_scale);
    F(sd_ctx_params_t, tokenizer);
    F(sd_ctx_params_t, sage_attn);
    F(sd_ctx_params_t, conditioning_cache_size);
    F(sd_ctx_params_t, flash_attn_mode);
    F(sd_ctx_params_t, conv_direct_mode);

    S(sd_slg_params_t);
    S(sd_guidance_params_t);
    F(sd_guidance_params_t, slg);
    S(sd_sample_params_t);
    F(sd_sample_params_t, scheduler);
    F(sd_sample_params_t, sample_steps);
    F(sd_sample_params_t, custom_sigmas);
    F(sd_sample_params_t, flow_shift);
    F(sd_sample_params_t, extra_sample_args);

    S(sd_pm_params_t);
    S(sd_pulid_params_t);
    S(sd_cache_params_t);
    F(sd_cache_params_t, use_relative_threshold);
    F(sd_cache_params_t, Fn_compute_blocks);
    F(sd_cache_params_t, scm_mask);
    F(sd_cache_params_t, scm_policy_dynamic);
    F(sd_cache_params_t, spectrum_w);
    F(sd_cache_params_t, spectrum_stop_percent);
    S(sd_hires_params_t);
    F(sd_hires_params_t, model_path);
    F(sd_hires_params_t, custom_sigmas);

    S(sd_img_gen_params_t);
    F(sd_img_gen_params_t, lora_count);
    F(sd_img_gen_params_t, prompt);
    F(sd_img_gen_params_t, clip_skip);
    F(sd_img_gen_params_t, init_image);
    F(sd_img_gen_params_t, ref_images);
    F(sd_img_gen_params_t, ref_image_args);
    F(sd_img_gen_params_t, mask_image);
    F(sd_img_gen_params_t, width);
    F(sd_img_gen_params_t, sample_params);
    F(sd_img_gen_params_t, strength);
    F(sd_img_gen_params_t, seed);
    F(sd_img_gen_params_t, batch_count);
    F(sd_img_gen_params_t, control_image);
    F(sd_img_gen_params_t, control_strength);
    F(sd_img_gen_params_t, ip_adapter_image);
    F(sd_img_gen_params_t, pm_params);
    F(sd_img_gen_params_t, pulid_params);
    F(sd_img_gen_params_t, vae_tiling_params);
    F(sd_img_gen_params_t, cache);
    F(sd_img_gen_params_t, hires);
    F(sd_img_gen_params_t, qwen_image_layers);
    F(sd_img_gen_params_t, circular_x);
    F(sd_img_gen_params_t, image_preprocess);
    return 0;
}
