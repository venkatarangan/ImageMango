# ImageMango — user guide

The full guide. For a short overview see [README.md](README.md).

A fast, native Windows app for generating images locally with open-weight models.
It uses WPF (.NET 10) for the UI and [stable-diffusion.cpp](https://github.com/leejet/stable-diffusion.cpp)
(C++/ggml, CUDA) for inference. There's no Python and no browser, and nothing leaves your PC apart from the model downloads.

- A built-in catalog of 11 recent, well-rated models (the newest of each family), grouped as **Filtered**, **Unfiltered** and
  **Not for all audiences** (the Hugging Face `not-for-all-audiences` tag). Each group can be shown or hidden.
- Each model gets **recommended settings for your PC**, picked by GPU VRAM tier (under 8 / 8 / 12 / 16 / 24 GB).
  **Detect this PC** re-tunes everything on another machine. You can also edit `machine.json`.
- One-click resumable downloads. Shared parts (VAEs, text encoders) are downloaded only once.
- The model stays loaded between runs. You get a live preview while sampling, Cancel (Esc), batches and seeds.
- An optional **NSFW image classifier** (default **off**). Flagged images are blurred and saved to a `flagged` subfolder.
- Each image is saved as PNG with a `.json` file next to it holding the prompt, seed and every setting.
  Images go to `%TEMP%\ImageMango` by default. Use **Change…** under *Storage* to pick another folder. Windows may
  clean the temp folder, so move images you want to keep, or pick a permanent folder.
- **Prompt and model inside the image** (Storage › *Save the prompt and model inside images*, on by default): each PNG
  carries its prompt, negative prompt, model and settings. Windows shows them under Properties › Details (Title,
  Comments, Tags = model, Program name) and Windows Search finds images by prompt words or model; macOS/Linux photo
  tools and exiftool read the same XMP, and A1111/Forge PNG Info, Civitai and SD prompt readers read the `parameters`
  text. Nothing identifies you or the PC: no names, folders or paths, no time zone (times are UTC; the `.json` too), no
  start-image file name, and LoRAs added from your PC show as "own LoRA". Never written in incognito.
- **Reuse** (on the image, or right-click a Recent image) puts back an image's model, prompt, negative prompt, seed,
  size, steps, CFG, sampler and LoRAs, from its `.json` or, without one, from the text inside the PNG (also works for
  PNGs from A1111-style apps).
- **Prompt helper** (next to Sample prompts): improves your prompt or writes one from an idea, in the form the selected
  model understands best (prose, Danbooru tags, photo terms), and suggests a negative prompt when the model uses one.
  With "Ask me questions" on, it asks 1–3 short questions when your idea is vague. Write in any language; the prompt
  comes back in English. It runs a small language model **on this PC** with llama.cpp: **Qwen3 4B Instruct** (default,
  4.3 GB) or **Qwen3 8B** (5.0 GB, better with tricky prompts, about half as fast). Both are the same files some image
  models use as text encoder, so they're shared. First use downloads llama.cpp (267 MB, reusing the CUDA runtime that
  ImageMango already has) and the model if missing. It answers in a few seconds on a mid-range GPU, uses the GPU memory that's
  free, stops before each image run and after 5 idle minutes, and saves or logs nothing.
- **Find and download LoRAs** (Add LoRA › Find and download … LoRAs): searches Civitai and Hugging Face for LoRAs
  made for the selected model's type, with previews, sizes, popularity and trigger words. Tick the ones you want and
  click Download: they're downloaded one after another and switched on. Adult content is left out unless "Not for all
  audiences" is ticked in the model list (then its previews are blurred; the NSFW classifier, when on, also blurs
  flagged previews). Many Civitai LoRAs download only with a free **Civitai API key** (civitai.com › Account settings ›
  API Keys); paste it in Find LoRAs or under Storage. It's sent only to civitai.com, only when downloading.
- **Hide the side panels** for a bigger image: the ‹ / › buttons at the top of the model list and the settings, the
  ⤢ button on the image (hides or shows both), **F11** (both), **Ctrl+Shift+M** (models), **Ctrl+Shift+S** (settings).
  A hidden panel leaves a thin strip to bring it back; the choice and the panel widths are remembered.
- Every setting has an **ⓘ** tooltip that explains what it does, how to set it, and the recommended value for the selected model.
- **Sample prompts** (above the prompt box) lists prompts that show off what the selected model does best.
- **Incognito** (above the prompt box): writes no `engine.log`, doesn't remember the prompt, and saves images without the
  `.json` file. Each run's images are permanently deleted when you run the next prompt or close the app (or at the next
  start, if the app didn't close cleanly). Copy an image somewhere else with **Open** if you want to keep it.
- **Delete** (on the image) removes the shown image from the preview and Recent images and deletes the file and its `.json`.
  Files go to the Recycle Bin, except in incognito, where they're deleted permanently and the prompt box is cleared too
  (and the start image, if it was that image).
- **Clear folder…** (Storage) permanently deletes every image and `.json` the app saved in the output folder, after asking.
  It only touches files with the app's own naming pattern, so other files in that folder are safe.
- **Run timer**: a live timer runs from the moment you press Generate. The status bar then shows the total, split into
  model load and sampling/decoding, and the total is saved in the image's `.json` as `totalSeconds`.
- **Start from an image** (image-to-image, under the prompt): choose a picture, drop one on the window, paste one with
  Ctrl+V, or click **Use as start** on a result (or right-click a Recent image). **Change** (0.05–1) sets how much is
  redrawn: about 0.3 touches up, 0.5 restyles and keeps the layout, 0.8 keeps only rough shapes. Width and Height switch
  to the picture's shape, and the picture is centre-cropped, never stretched. Lower Change also runs fewer steps.
- **LoRA add-ons** (under the prompt): small files that add a style, character or look. **Add LoRA** lists your LoRAs
  made for the selected model's type, links to Hugging Face and Civitai listings filtered to that type, downloads a
  LoRA from a pasted Hugging Face / Civitai / `.safetensors` link, or adds a file from your PC (or drop `.safetensors`
  files on the window). Each LoRA has a weight slider. Trigger words are added to the prompt when known (from the
  Hugging Face card, Civitai, or the file's notes). ImageMango works out which model type a LoRA is for from its source
  or the file itself, keeps LoRAs for other model types in a separate submenu, and after a run marks a LoRA with ⚠ if
  it hardly applied. LoRAs are remembered per model, saved in the image's `.json`, and don't reload the model.

**What stays on your PC:** the app sends no telemetry, and the optional NSFW classifier switches off ONNX Runtime's
Windows telemetry before it starts. Network traffic happens only when you ask for it: model downloads from Hugging Face
(Download), and LoRA links you paste or browse (Hugging Face or Civitai, only the site in the link), the Find LoRAs
window's searches and previews (civitai.com, huggingface.co, only while it's open), and the one-time prompt helper
download (github.com for llama.cpp, huggingface.co for the model). The prompt helper itself runs offline on this PC.
The Hugging Face token is only ever sent to huggingface.co, and the Civitai API key only to civitai.com. Locally it keeps the images and their `.json` files, `settings.json`
(folders, your last prompt, your LoRA choices, and your Hugging Face token and Civitai API key if you set them), `machine.json`, and
`engine.log` for the current session. `engine.log` includes your prompts. Use Incognito to keep none of the
prompt-related files.

## Build and run on Windows 11

You need Windows 11 x64, an NVIDIA GPU (other GPUs: see *Vulkan* below), about 2 GB of disk for the engine,
and 7–25 GB per model.

Open **PowerShell** and run:

```powershell
# 1. .NET 10 SDK (one time)
winget install Microsoft.DotNet.SDK.10

# 2. Get the source (any folder on a local drive; building from a network share or WSL path is slow)
git clone https://github.com/venkatarangan/ImageMango.git
cd ImageMango

# 3. Download the pinned engine DLLs (CUDA build + CUDA runtime, ~900 MB, one time)
powershell -ExecutionPolicy Bypass -File .\setup-engine.ps1

# 4. Build and run
dotnet run -c Release --project src\ImageMango
```

To get a standalone folder you can launch directly or pin to Start:

```powershell
dotnet publish src\ImageMango -c Release -o publish
.\publish\ImageMango.exe
```

You can also open `ImageMango.sln` in Visual Studio 2022/2026, set **ImageMango** as the startup project and press F5
(it builds `ImageMango.exe`).

**Vulkan (AMD / Intel GPUs):** run `.\setup-engine.ps1 -Backend vulkan`, then rebuild.
`-Backend cpu` also works, but it's slow.

## First run

1. Pick a model on the left. **Z-Image Turbo** is the best all-rounder for a 12 GB card, and **FLUX.2 klein 4B** is the fastest.
2. Click **Download**. Downloads resume if they're interrupted (click Download again).
3. Type a prompt and press **Generate** (or Ctrl+Enter). The first run loads the model, which takes 10–60 s.
   After that, only the sampling steps count.

For example, on a mid-range 12 GB NVIDIA GPU with Z-Image Turbo, model already loaded:
512×512 at 8 steps took about 6 s, and 1024×1024 at 8 steps took about 23 s (2.65 s/step).
*Offload weights to RAM* made no measurable difference to speed.

For bug reports, `ImageMango.exe --screenshot ui.png [--prompt "text"]` renders the window to a PNG
(after one generation if `--prompt` is given) and exits. With it, `--init-image pic.png --strength 0.55` starts from a
picture and `--lora file.safetensors[:weight]` (repeatable) switches LoRAs on. Errors go to `ui.png.errors.txt` instead
of a dialog. Set `IMAGEMANGO_HOME` to keep all app data in one folder.

## LoRAs: which ones work

A LoRA only works with the model type it was trained for. ImageMango knows these types (`family` in the catalog):

| Family | Catalog models | Where to find LoRAs |
|---|---|---|
| SDXL | RealVisXL, Illustrious XL, NoobAI-XL, WAI-illustrious | Civitai "SDXL 1.0", "Illustrious", "NoobAI", "Pony"; the largest choice |
| Z-Image | Z-Image Turbo, REDCraft REDZ | Civitai "ZImageTurbo"; Hugging Face adapters of Tongyi-MAI/Z-Image-Turbo (~900) |
| FLUX.2 [klein] 4B / 9B | the two klein models (4B and 9B LoRAs are not interchangeable) | Civitai "Flux.2 Klein 4B/9B"; Hugging Face adapters of the klein repos |
| Qwen-Image 2.1 | Qwen-Image 2.1, Noct Q V4 | Civitai "Qwen 2.1"; Hugging Face adapters of Qwen/Qwen-Image-2.1. First-generation Qwen-Image LoRAs don't fit |
| Chroma | Chroma1-HD | Civitai "Chroma"; FLUX.1 LoRAs may partly work |

LoRAs live in `models\loras` (inside the models folder). A `name.json` next to each file holds its trigger words
(`activation text`), preferred weight and source, in the same format AUTOMATIC1111 and Forge use, so you can copy LoRAs
between those apps and ImageMango; `name.civitai.info` files from Civitai Helper are read too. Edit the `activation text`
there to change a LoRA's trigger words. `InteropCheck lora` re-checks the type detection against sample files on Hugging Face.

## The models (catalog 2026.10.16)

Only the newest model from each family is listed: FLUX.1 [schnell] and [dev] were dropped because FLUX.2 [klein]
supersedes them, and Qwen-Image 2512 was replaced by Qwen-Image 2.1. Files you already downloaded for removed models
stay in the models folder; delete them by hand if you want the space back.

| Model | Group | Licence | Why |
|---|---|---|---|
| Z-Image Turbo | Unfiltered | Apache-2.0 | 8 steps, photoreal, great prompt following |
| FLUX.2 [klein] 4B | Filtered | Apache-2.0 | 4 steps, fastest |
| FLUX.2 [klein] 9B | Filtered | Non-commercial | Better detail and text than 4B |
| Qwen-Image 2.1 | Filtered | Qwen Research (non-commercial) | Best text and layout rendering; 7B, much lighter than 2512 |
| Chroma1-HD | Unfiltered | Apache-2.0 | Uncensored FLUX-schnell derivative, real CFG |
| RealVisXL V5.0 | Unfiltered | OpenRAIL++ | Photoreal SDXL, huge LoRA ecosystem |
| Illustrious XL v2.0 | Unfiltered | OpenRAIL-M | Anime / illustration SDXL base |
| NoobAI-XL 1.1 | Not for all audiences | NoobAI (non-commercial) | Anime SDXL, Danbooru + e621 |
| WAI-illustrious-SDXL v17 | Not for all audiences | FAIPL-1.0-SD (Illustrious-derived) | Most downloaded anime SDXL fine-tune |
| REDCraft REDZ v1.5 | Not for all audiences | Apache-2.0 | Uncensored Z-Image Turbo fine-tune |
| Noct Q V4 | Not for all audiences | Qwen Research (non-commercial) | Uncensored photoreal Qwen-Image 2.1 fine-tune (INT8 file) |

The **Filtered** label means the publisher states its training data was filtered or the model was safety-tuned.
**Unfiltered** means there's no stated filtering. **Not for all audiences** follows the Hugging Face repo tag.
These are best-effort labels: check the `category` field in the catalog.

## Configuration files

Everything is plain JSON in `%LOCALAPPDATA%\ImageMango\config` (use the **Config folder** button):

| File | What it holds |
|---|---|
| `machine.json` | Detected GPU / VRAM / RAM. Set `"vramOverrideGb": 16` to use another tier's recommendations. |
| `settings.json` | Folders, HF token, NSFW toggle and threshold, your saved per-model settings (`modelOverrides`), the LoRAs on per model (`modelLoras`) and the last Change value (`initStrength`). |
| `models.json` | *(optional)* Your own copy of the catalog. Click **Edit model catalog** to create it. It overrides the bundled one. |

After editing any of these files, click **Reload config**.

**Moving to another PC:** copy the app and click **Detect this PC**. Every model switches to the recommended
settings for that GPU, and you're asked whether to drop any per-model settings you saved.
You can also copy `machine.json` to set the tier by hand.

**Catalog format:** each model lists its `family` (for LoRA matching), its `files` (Hugging Face repo and path), its `defaults`, and its `tiers`.
The tier with the largest `minVramGb` at or below your VRAM is used, and its `settings` are layered over `defaults`.
To add a model in a future version, add an entry here; the engine supports many more architectures
(SD3.5, HiDream, Wan, Ovis, LongCat and more).

## Settings cheat-sheet

| Setting | Effect |
|---|---|
| Offload weights to RAM | Keeps weights in system RAM and streams them to the GPU. Lets 13–22 GB models run on 12 GB, with little speed loss when the diffusion model itself fits. |
| Text encoder / VAE on CPU | Saves VRAM, but costs a few seconds per image. |
| VAE tiling | Lowers peak VRAM at decode, which helps with large images. |
| Quantize on load | Converts weights when loading (for example `q8_0` halves the memory of an fp16 SDXL file). |
| Max VRAM | `-1` keeps about 1 GiB free for the desktop and other apps. |

## Upgrading the engine

The C# bindings (`src/ImageMango/Native/SdNative.cs`) mirror the C header of release
`master-964-1d0a58a`. That release's C structs change often, so the version is pinned. To upgrade:

1. Change `$Release` and `$Commit` in `setup-engine.ps1` and `SdVersion` in `SdNative.cs`.
2. Update the structs to match the new `include/stable-diffusion.h`.
3. Compile `tools/InteropCheck/native/layout.c` against the new header (`gcc -I<sd.cpp>/include layout.c`, then
   `./a.out > c_layout.txt`) and run
   `dotnet run --project tools\InteropCheck -- layout c_layout.txt`. Every size and offset must match.
4. Smoke test: `dotnet run --project tools\InteropCheck -- gen <z_image.gguf> <qwen3-4b.gguf> <ae.safetensors> out.ppm`.
5. `dotnet run --project tools\InteropCheck -- catalog src\ImageMango\catalog\models.json 12 --urls`
   checks that every catalog download URL still exists with the expected size.

## Troubleshooting

- **"Engine missing" on start:** run `setup-engine.ps1`, then rebuild. The DLLs must sit next to `ImageMango.exe`.
- **A LoRA has no effect (⚠ next to it):** it was made for another model type. Hover the ⚠: it says how much of it
  applied. Use LoRAs from **Add LoRA › Find LoRAs** for the selected model.
- **A LoRA download says "access denied":** many Civitai files need a login. Add your free Civitai API key (Find
  LoRAs, or Storage), or download the file in your browser and use **Add a LoRA file from this PC** (or drop it on the window).
- **Out of memory / generation failed:** tick *Offload weights to RAM*, lower the size, or set *Max VRAM* to `-1`.
- **Everything is slow:** check the engine log (bottom right). It should show `CUDA0`. If it only shows CPU,
  update the NVIDIA driver or try `-Backend vulkan`.
- **Full engine log:** `%LOCALAPPDATA%\ImageMango\engine.log`.
- **"Could not find file …\Pictures\…" or another save error:** the output folder can't be written. This often
  happens when OneDrive or Controlled Folder Access protects Pictures. Pick another folder with **Change…** under *Storage*.
  If a save fails, the image is saved to `%TEMP%\ImageMango` instead, so it isn't lost.
