using System.Globalization;
using ImageMango.Core;

namespace ImageMango.UI;

/// <summary>
/// Tooltip text for every setting: what it does, how to set it, and the recommendation for the
/// selected model on this PC. Keys match the setting names used in MainWindow.xaml.
/// </summary>
public static class SettingHelp
{
    public static Dictionary<string, string> For(CatalogModel? model, GenSettings? rec)
    {
        rec ??= new GenSettings();
        string name = model?.Name ?? "this model";
        // Turbo/distilled models run at CFG 1 with few steps; a full model can also be set to CFG 1 for speed.
        bool distilled = rec.CfgScale <= 1.0 && rec.Steps <= 12;
        bool sdxl = model?.Files.Values.Any(f => f.Role == "model") == true;
        string R(string text) => model is null ? "" : $"\n\nFor {name} on this PC: {text}";
        string F(double v) => v.ToString("0.##", CultureInfo.CurrentCulture);
        string OnOff(bool v) => v ? "on" : "off";

        return new Dictionary<string, string>
        {
            ["Width"] = SizeText + R($"{rec.Width}×{rec.Height}."),
            ["Height"] = SizeText + R($"{rec.Width}×{rec.Height}."),

            ["Steps"] =
                "How many denoising steps the sampler runs. More steps refine the image further but take longer, " +
                "with diminishing returns.\n\n" +
                "How to set it: distilled \"turbo\" models are trained for a small fixed count (4–8) and don't improve " +
                "with more. Full models usually want 20–35. Halve the steps to draft ideas quickly, then go back up." +
                R(distilled ? $"{rec.Steps} (distilled model, so stay close to this)." : $"{rec.Steps} (try {Math.Max(1, rec.Steps - 6)}–{rec.Steps + 10})."),

            ["CfgScale"] =
                "Classifier-free guidance: how hard each step pushes the image towards the prompt and away from the " +
                "negative prompt. Above 1, the model runs twice per step (about 2× slower) and the negative prompt is used.\n\n" +
                "How to set it: too low looks loose and washed out, too high looks oversaturated and burnt. " +
                "Guidance-distilled models must stay at 1." +
                R(distilled ? "1. It's guidance-distilled, so higher values only slow it down and burn the image."
                            : $"{F(rec.CfgScale)} (useful range {F(Math.Max(1.5, rec.CfgScale - 1.5))}–{F(rec.CfgScale + 1.5)})."),

            ["Guidance"] =
                "Built-in (distilled) guidance for models trained with it, such as FLUX.1 [dev]. It isn't CFG and costs " +
                "no extra time.\n\n" +
                "How to set it: higher follows the prompt more literally with more contrast, lower looks softer and more " +
                "natural (2–5 is the useful range). Leave it empty to use the model default; models without it ignore it." +
                R(rec.Guidance is double g ? $"{F(g)}." : "leave empty, this model doesn't use it."),

            ["FlowShift"] =
                "Shifts the noise schedule of flow-matching models (Qwen-Image, Z-Image, FLUX, Chroma) towards the noisy " +
                "early steps. Not used by SDXL models.\n\n" +
                "How to set it: higher spends more effort on overall composition, which helps large images and complex " +
                "layouts. Lower spends more on fine detail. Leave it empty for the model's own default." +
                R(rec.FlowShift is double fs ? $"{F(fs)}." : "leave empty (model default)."),

            ["Sampler"] =
                "The algorithm that turns noise into the image, step by step.\n\n" +
                "How to set it: \"default\" lets the engine pick the model's native sampler, which is always a safe choice.\n" +
                "• euler: simple and reliable, the standard for FLUX, Z-Image, Qwen-Image and Chroma.\n" +
                "• euler_a and other *_a samplers: add fresh noise each step, so the image keeps changing with the step " +
                "count. Popular for anime SDXL models.\n" +
                "• dpm++2m: sharp and quick to converge, the usual photoreal SDXL choice (pair it with karras).\n" +
                "• lcm / tcd: only for LCM- or TCD-distilled models." +
                R($"{rec.Sampler}."),

            ["Scheduler"] =
                "How the noise levels are spread across the steps.\n\n" +
                "How to set it: \"default\" uses the model's own schedule.\n" +
                "• karras: more steps at low noise for extra fine detail. Best with dpm++ samplers on SDXL.\n" +
                "• simple / discrete / sgm_uniform: even spacing, the standard for flow models.\n" +
                "• exponential, ays, beta: alternatives worth trying on SDXL if images look mushy." +
                R($"{rec.Scheduler}."),

            ["ClipSkip"] =
                "Uses an earlier layer of the CLIP text encoder to read the prompt. Only SD 1.5 and SDXL models use it; " +
                "FLUX, Z-Image, Qwen-Image and Chroma ignore it.\n\n" +
                "How to set it: -1 means the model's default. Anime models trained on Danbooru tags (Illustrious, NoobAI, " +
                "Pony) often look better at 2." +
                R(sdxl ? $"{rec.ClipSkip} (SDXL model, so it applies)." : "-1, this model doesn't use CLIP."),

            ["NegativePrompt"] =
                "Things you don't want in the image, such as \"blurry, watermark, extra fingers\". " +
                "It's only used when CFG is above 1." +
                R(distilled ? "ignored, because it runs at CFG 1. Describe what you want in the prompt instead."
                            : "used. Keep it short; long lists of negatives can dull the image."),

            ["OffloadToCpu"] =
                "Keeps the model weights in system RAM and streams each part to the GPU when it's needed. This lets models " +
                "bigger than your VRAM run (13–22 GB models on a 12 GB card).\n\n" +
                "How to set it: turn it on if you get out-of-memory errors or the model is larger than your VRAM. " +
                "Turn it off when everything fits, so loading is faster. Changing it reloads the model." +
                R($"{OnOff(rec.OffloadToCpu)}."),

            ["TextEncoderOnCpu"] =
                "Runs the text encoder (CLIP, T5 or the Qwen language model that reads your prompt) on the CPU.\n\n" +
                "How to set it: leave it off. It saves 2–9 GB of VRAM, but every new prompt takes seconds to tens of " +
                "seconds longer to encode. Use it only if offloading isn't enough." +
                R($"{OnOff(rec.TextEncoderOnCpu)}."),

            ["VaeOnCpu"] =
                "Decodes the finished image on the CPU instead of the GPU.\n\n" +
                "How to set it: leave it off. It saves 1–3 GB of VRAM at the end of each image, but decoding takes " +
                "10–60 s instead of under a second. Try VAE tiling first." +
                R($"{OnOff(rec.VaeOnCpu)}."),

            ["VaeTiling"] =
                "Decodes the image in tiles instead of all at once, which lowers the VRAM peak at the very end.\n\n" +
                "How to set it: turn it on for large images (above about 1.5 megapixels) or if it fails right at the " +
                "end with an out-of-memory error. The speed cost is small; very rarely you may see faint tile seams." +
                R($"{OnOff(rec.VaeTiling)}."),

            ["FlashAttention"] =
                "A faster, more memory-efficient way to compute attention inside the model.\n\n" +
                "How to set it: \"auto\" lets the engine decide and is right almost always. \"on\" forces it, which saves " +
                "VRAM at large sizes. Use \"off\" only if you get black, noisy or broken images." +
                R($"{rec.FlashAttention}."),

            ["WeightType"] =
                "Converts the weights to a smaller format while the model loads. Empty keeps the file's own precision.\n\n" +
                "How to set it:\n" +
                "• q8_0: half the size of fp16/bf16 with almost no visible quality loss.\n" +
                "• q6_K / q5_K: a little smaller, still very good.\n" +
                "• q4_K: about a quarter of fp16, with some loss of fine detail.\n" +
                "• q3_K / q2_K: only if nothing else fits.\n" +
                "It doesn't shrink GGUF files that are already quantized, and loading is slower because the conversion " +
                "runs every time." +
                R(string.IsNullOrEmpty(rec.WeightType) ? "empty (keep the file as it is)." : $"{rec.WeightType}."),

            ["MaxVram"] =
                "Caps how much GPU memory the engine may use, in GiB.\n\n" +
                "How to set it: empty is automatic. -1 keeps about 1 GiB free for Windows, your browser and other apps. " +
                "A number such as 10 means never use more than 10 GiB; anything beyond that streams from RAM, which is " +
                "slower. Set it if the desktop stutters or other GPU apps crash while you generate." +
                R(string.IsNullOrEmpty(rec.MaxVram) ? "empty (automatic)." : $"{rec.MaxVram}."),

            ["ModelArgs"] =
                "Advanced: extra key=value options passed to the engine for this model, separated by commas " +
                "(for example chroma_use_dit_mask=false for Chroma).\n\n" +
                "How to set it: leave it as the catalog set it, unless a model card tells you otherwise." +
                R(string.IsNullOrEmpty(rec.ModelArgs) ? "empty." : $"{rec.ModelArgs}."),

            ["Seed"] =
                "The random starting noise. The same seed, prompt and settings always give the same image.\n\n" +
                "How to set it: -1 picks a new random seed each time. When you find an image you like, click ♻ to keep " +
                "its seed, then change one word or one setting at a time to see its effect. 🎲 goes back to random.",

            ["BatchCount"] =
                "How many images to make in one go (1–16). Each one uses the next seed. They're made one after another, " +
                "so the time grows with the count.",

            ["SizePreset"] =
                "Common aspect ratios, scaled to this model's recommended size so the total pixel count stays about the " +
                "same. Picking one fills in Width and Height on the right.",

            ["LivePreview"] =
                "Shows the image forming while it samples. It costs a little speed. It's switched off automatically " +
                "while the NSFW classifier is on, because previews aren't checked.",

            ["NsfwFilter"] =
                "Checks each finished image with a small classifier on the CPU. Flagged images are blurred in the app and " +
                "saved to a \"flagged\" subfolder. The first time, it downloads a 90 MB model.",

            ["Incognito"] =
                "Leaves no trace of what you generate.\n\n" +
                "While it's on:\n" +
                "• engine.log isn't written (it would contain your prompts), and the existing one is deleted.\n" +
                "• The prompt isn't remembered for the next start.\n" +
                "• Images are saved without the .json file, and without the prompt and settings inside the PNG.\n" +
                "• Each run's images are deleted when you run the next prompt or close the app. Save copies elsewhere " +
                "(the Open button) if you want to keep one.\n" +
                "• Delete on an image also clears the prompt box (and the start image, if it was that image).\n\n" +
                "Images and the prompt stay visible on screen until you replace or delete them. Models still download from " +
                "Hugging Face as usual.",

            ["EmbedMetadata"] =
                "Writes the prompt, negative prompt, model and settings inside each PNG, so they travel with the image.\n\n" +
                "Where you'll see them:\n" +
                "• Windows: right-click › Properties › Details shows the prompt as Title and Comments, the model under Tags and " +
                "ImageMango as Program name. Windows Search finds images by words in the prompt or by model.\n" +
                "• macOS and Linux: photo apps and exiftool read the same fields.\n" +
                "• Stable Diffusion tools: A1111/Forge PNG Info, Civitai and prompt readers show them, and Reuse in ImageMango " +
                "puts them back.\n\n" +
                "Nothing about you or this PC is written: no names, folders or file paths, no time zone (the time is in UTC), " +
                "no start-image file name, and LoRAs you added from this PC are listed only as \"own LoRA\". " +
                "Turn this off if you share images and don't want the prompt to go with them. Never written in incognito.",

            ["StartImage"] =
                "Image-to-image: instead of starting from random noise, the model starts from your picture and redraws it " +
                "to match the prompt.\n\n" +
                "Use it to restyle a photo (\"as a watercolour painting\"), turn a rough sketch into a finished image, or make " +
                "variations of a result you like (click 'Use as start' on it).\n\n" +
                "How to use it: choose a picture, drop one on the window, or paste one with Ctrl+V. Width and Height change to the " +
                "picture's shape; it's cropped to fit, never stretched. Write the prompt for the result you want, not a " +
                "description of the original, then set Change.",

            ["Strength"] =
                "How much of the start image is changed (the denoising strength).\n\n" +
                "• 0.2–0.35: small touch-ups. Colours, light and fine detail change; the picture stays.\n" +
                "• 0.4–0.6: restyles. Keeps the layout and subjects, changes the style and surfaces.\n" +
                "• 0.65–0.85: reimagines. Keeps rough shapes and colours only.\n" +
                "• 0.9–1: almost a new image.\n\n" +
                "It also skips the start of the schedule, so lower values run fewer steps and finish sooner." +
                (distilled ? $" {name} only runs {rec.Steps} steps, so changes come in big jumps; 0.5–0.7 works best." : ""),

            ["Loras"] =
                "LoRAs are small add-on files (usually 10–500 MB) that teach the model one thing: a style (anime, watercolour, " +
                "film photo), a character, a face, a kind of object, or a quality boost. Several can be on at once.\n\n" +
                "A LoRA works only with the model type it was trained for: an SDXL LoRA works with every SDXL model " +
                "(RealVisXL, Illustrious, NoobAI, WAI), but not with Z-Image, FLUX or Qwen-Image." +
                (model?.Family is { } fam ? $" {name} uses {LoraFamilies.NameOf(fam)} LoRAs." : "") + "\n\n" +
                "How to get them: Add LoRA › Find LoRAs opens Hugging Face or Civitai filtered to this model type. Copy a LoRA " +
                "page's address and use Download from a link; ImageMango finds the file, its model type and its trigger words. " +
                "You can also drop .safetensors files on the window.\n\n" +
                "Many LoRAs only switch on when their trigger words are in the prompt; ImageMango adds them when it knows them. " +
                "Changing LoRAs doesn't reload the model.",

            ["LoraWeight"] =
                "How strongly this LoRA is applied. 1 is full strength. Most look best between 0.6 and 1; lower it if the image " +
                "looks overcooked or the LoRA takes over, and raise it (up to 1.5) if it barely shows. With several LoRAs, lower " +
                "each one a little.",

            ["NsfwThreshold"] =
                "The score (0 to 1) above which an image is flagged.\n\n" +
                "How to set it: lower is stricter and also flags some safe images; higher flags only clear cases. " +
                "0.5–0.7 is a balanced range.",
        };
    }

    private const string SizeText =
        "The image size in pixels. It's rounded to a multiple of 16 when you generate.\n\n" +
        "How to set it: each model is trained around a pixel budget (usually about 1 megapixel, such as 1024×1024). " +
        "Far above it you get duplicated subjects or stretched bodies; far below it loses detail. For other shapes keep " +
        "the area about the same (for example 832×1216) by using the size presets next to Seed. Larger images take " +
        "longer and need more VRAM; turn on VAE tiling if it fails at the end.";
}
