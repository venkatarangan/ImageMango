# Rebuild ImageMango from scratch with a coding agent

This file is a complete prompt for an AI coding agent: Claude Code, OpenAI Codex, Google Antigravity or GitHub
Copilot (agent mode). It builds, from an empty folder, a native desktop app with the same features as ImageMango,
configured for the machine it runs on: Windows (NVIDIA, AMD or Intel GPU), macOS on Apple Silicon, or Linux.

**How to use it**

1. Make an empty folder and open your coding agent in it. Use its most capable model and let it run commands.
2. Either paste everything below the line into the agent, or send this one line:
   `Fetch https://raw.githubusercontent.com/venkatarangan/ImageMango/main/docs/REBUILD-PROMPT.md and follow the prompt in it.`
3. Answer its first questions, then let it work phase by phase. Expect a few hours of agent time, about 2 GB of
   tools and engine downloads, and 5–25 GB per model you try.

**Adults only.** The catalog includes uncensored and not-for-all-audiences models; the prompt makes them opt-in.

**How it was validated (2026-10-16).**
- Every release asset, checksum, raw URL, search API and llama-server flag below was checked, and all 37 model and
  classifier download URLs answered with the listed sizes.
- An independent agent with no other context followed phases 1 and 2 from an empty folder on Linux. Its engine
  bindings matched the C header on the first attempt (15 structs, 164 fields), and it generated a correct 512×512
  image with FLUX.2 klein 4B.
- A second agent reviewed the prompt as if building on an RTX 4070 PC, an M3 Pro MacBook and a Radeon RX 7800 XT PC.
  Its findings are folded in: macOS version limits, the compiler step, Apple unified-memory tiering, the AMD backend
  choice, and exact API and parsing details.
- The features themselves (prompt helper, LoRA browser, PNG metadata, incognito and the rest) come from the working
  Windows app this prompt describes, and were tested there on an NVIDIA GPU.
- Not tested: a full end-to-end build by an agent on macOS or on AMD hardware.

---

# Prompt: build "ImageMango", a local image generation app for this machine

You are building a native desktop app, from an empty folder, that generates images locally with open-weight
diffusion models. Work autonomously in phases, verify each phase with the checks given, and keep a short
`NOTES.md` log of decisions, test results and problems as you go. Ask the user only where this prompt says to ask.

## 0. First: inspect the machine and ask four questions

Detect, without asking:
- OS and version, CPU architecture and core count, total RAM, free disk space.
- GPU vendor, model, VRAM and driver.
  - NVIDIA (Windows/Linux): `nvidia-smi --query-gpu=name,memory.total,driver_version --format=csv,noheader`. The
    engine's CUDA 12.8 runtime needs driver 570 or newer; if it's older, tell the user to update.
  - Windows AMD/Intel: registry `HKLM\SYSTEM\CurrentControlSet\Control\Class\{4d36e968-e325-11ce-bfc1-08002be10318}\000*`,
    value `HardwareInformation.qwMemorySize`; take the adapter with the **largest** value (integrated GPUs are
    listed too). Don't use WMI `AdapterRAM` (it caps at 4 GB).
  - macOS: `sw_vers`, `sysctl -n hw.memsize`, `system_profiler SPDisplaysDataType`. Apple Silicon has unified memory:
    use 65% of RAM as the "VRAM" figure.
- Installed tools: Git, .NET SDK version, a C compiler (`cl`, `clang`, `gcc` or `zig cc`; needed once for the
  layout check in phase 1), and on macOS older than 26 also `cmake` and the Xcode command-line tools (see section 1).

Then show what you found and ask:
1. Where should the app keep models (default: the per-user app-data folder; models are 5–25 GB each)?
2. Should the catalog include **uncensored / not-for-all-audiences** models (adults only)? Default: no. If yes, they
   appear in their own group, which the user can hide.
3. UI framework: **Avalonia 11.3.22** (one codebase for Windows, macOS and Linux) unless the user is on Windows and
   prefers **WPF**.
4. App name (default "ImageMango").

Install missing tools only after the user agrees:
- .NET 10 SDK: `winget install Microsoft.DotNet.SDK.10`, `brew install --cask dotnet-sdk`, or Microsoft's
  `dotnet-install.sh`. With a private SDK install, set `DOTNET_ROOT` so the built app host can start.
- C compiler: Windows `winget install zig.zig` (use `zig cc`) or the Visual Studio Build Tools; macOS
  `xcode-select --install`; Linux `gcc`.

## 1. Architecture (fixed decisions)

- **Language:** C# on .NET 10. No Python, no web UI, no Electron, no telemetry.
- **UI:** Avalonia 11.3.22 (11.3.2 pulls in a package with a security advisory; don't go below 11.3.22), or WPF on
  Windows if chosen. MVVM with a small hand-written `Observable` base and `RelayCommand`. Follow the OS light/dark theme.
- **Image engine:** [stable-diffusion.cpp](https://github.com/leejet/stable-diffusion.cpp) release
  **`master-964-1d0a58a`** (commit `1d0a58a`), called through its C API with P/Invoke (`DllImport("stable-diffusion")`
  resolves `stable-diffusion.dll`, `libstable-diffusion.dylib` or `libstable-diffusion.so`). Prebuilt assets at
  `https://github.com/leejet/stable-diffusion.cpp/releases/download/master-964-1d0a58a/<asset>`:
  - **Windows NVIDIA:** `sd-master-1d0a58a-bin-win-cuda12-x64.zip` plus the CUDA runtime `cudart-sd-bin-win-cu12-x64.zip`.
  - **Windows AMD or Intel:** `sd-master-1d0a58a-bin-win-vulkan-x64.zip` by default. `-win-rocm-10.1.0-x64.zip` is an
    opt-in for AMD (about 1 GB and needs the HIP runtime). Use `-win-cpu-x64.zip` only when there is no GPU.
  - **macOS Apple Silicon:** `sd-master-1d0a58a-bin-Darwin-macOS-26.6.2-arm64.zip`. It contains a Metal build of
    `libstable-diffusion.dylib` with a minimum OS of **macOS 26**. On older macOS, tell the user and build from
    source instead: `git clone --recursive https://github.com/leejet/stable-diffusion.cpp`, `git checkout 1d0a58a`,
    `git submodule update --init --recursive`, then
    `cmake -B build -DSD_METAL=ON -DSD_BUILD_SHARED_LIBS=ON -DCMAKE_OSX_DEPLOYMENT_TARGET=14.0 && cmake --build build --config Release`.
    Remove quarantine from downloaded libraries with `xattr -dr com.apple.quarantine engine`.
  - **Linux:** `sd-master-1d0a58a-bin-Linux-Ubuntu-24.04-x86_64-vulkan.zip` with a GPU, `-x86_64.zip` (CPU) without,
    or `-x86_64-rocm-10.1.0.zip` as an AMD opt-in.

  Write a setup script (`setup-engine.ps1` on Windows, `setup-engine.sh` elsewhere) that puts the files in `engine/`.
  Flatten zips. The project file copies `engine/*` next to the app binary.
- **Prompt helper engine:** [llama.cpp](https://github.com/ggml-org/llama.cpp) `llama-server` release **`b11683`**,
  from `https://github.com/ggml-org/llama.cpp/releases/download/b11683/<asset>`, downloaded by the app on first use.
  Check each asset's SHA-256 against the release API's `digest` field. The known Windows values:
  - **Windows NVIDIA:** `llama-b11683-bin-win-cuda-12.4-x64.zip`
    (SHA-256 `e416c97fcab725b8a5405fd50483abd3448336a762393a0cde4997d7651e2d0e`).
  - **Windows AMD or Intel:** `llama-b11683-bin-win-vulkan-x64.zip`
    (`9dc41183433fc1dd99dfec86b1a512ecda8cb8bcfc85db535dc746e9c8d893e0`).
  - **Windows, no GPU:** `llama-b11683-bin-win-cpu-x64.zip` (`69921e204b856c0e01b71361273eaa4158863e15dcc603022a8dbfef611e640e`).
  - **macOS:** `llama-b11683-bin-macos-arm64.tar.gz` (minimum macOS 13.3).
  - **Linux:** `llama-b11683-bin-ubuntu-vulkan-x64.tar.gz` or `llama-b11683-bin-ubuntu-x64.tar.gz`.

  Unpack `llama-server` with **all** its libraries; it needs `mtmd`, `llama-common` and `ggml*` too. The macOS and
  Linux tarballs have a top-level `llama-b11683/` folder and **symlinks** (for example `libmtmd.0.dylib`). Extract
  them with `System.Formats.Tar`, keeping the symlinks and the executable bit, and don't flatten them.
- **NSFW classifier:** ONNX Runtime (`Microsoft.ML.OnnxRuntime` NuGet) with `onnx-community/nsfw_image_detection-ONNX`,
  file `onnx/model_quantized.onnx`: a ViT at 224×224. Scale RGB to 0–1, normalise with mean 0.5 and std 0.5, apply
  softmax; labels are `0 normal`, `1 nsfw`. Downloaded the first time the classifier is switched on.
- **Model catalog:** a bundled `catalog/models.json` (section 4), which the user can override with a copy in the config folder.

## 2. Lessons already learned (follow these; each one cost a debugging round)

1. **Pin the engine and prove the struct layout.**
   - Download the header from `https://raw.githubusercontent.com/leejet/stable-diffusion.cpp/1d0a58a/include/stable-diffusion.h`.
   - Mirror every struct reachable from `sd_ctx_params_t` and `sd_img_gen_params_t`, plus `sd_image_t`, as blittable
     C# structs with the C field names. That's 15 structs at this commit.
   - Type mapping:
     - C `bool` becomes a 1-byte wrapper struct (`struct CBool { byte v; }`), never C# `bool`.
     - `const char*` becomes `nint`, holding UTF-8 strings allocated with `Marshal.StringToCoTaskMemUTF8`.
     - Enums become `int`.
     - For functions that take or return `bool`, use `[MarshalAs(UnmanagedType.U1)]`.
   - Generate a C program from the header that prints `sizeof` and `offsetof` for every field. Write a C# check that
     compares them with `Marshal.SizeOf` / `Marshal.OffsetOf` in both directions. Everything must match before
     anything else is built.
   - If no C compiler can be installed, also do this sentinel check: after `sd_ctx_params_init` and
     `sd_img_gen_params_init`, read back width 512, height 512, strength 0.75, seed -1, batch_count 1, clip_skip -1.
   - How CLI flags fill the structs: `examples/common/common.cpp`. CLI defaults: `examples/common/common.h`.
     Default sampler and scheduler resolution: `examples/cli/main.cpp`. All three files are at the same commit.
2. **The settings that aren't plain struct fields.**

   | Setting | How it reaches the engine |
   |---|---|
   | Offload weights to RAM | `params_backend = "*=cpu"`. An explicit `params_backend` disables `auto_fit` |
   | Text encoder on CPU / VAE on CPU | `backend = "te=cpu"` / `"vae=cpu"`, comma-joined |
   | Flash attention | `flash_attn_mode` = "auto", "on" or "off" |
   | Max VRAM | a GiB *string* |
   | Quantize on load | `wtype = str_to_sd_type("q8_0")` |
   | Extra model args | `model_args` string |
   | Sampler / scheduler "default" | the enum's COUNT value, resolved with `sd_get_default_sample_method(ctx)` / `sd_get_default_scheduler(ctx, method)` |
3. **Entry points.**
   - Used: `sd_ctx_params_init`, `new_sd_ctx`, `free_sd_ctx`, `sd_img_gen_params_init`,
     `generate_image(ctx, &params, &images, &count)`, `free_sd_images`, `sd_cancel_generation(ctx, SD_CANCEL_ALL)`
     (for Esc), `sd_set_log_callback`, `sd_set_progress_callback`, `sd_set_preview_callback`,
     `sd_get_default_sample_method`, `sd_get_default_scheduler`, `str_to_sample_method`, `str_to_scheduler`,
     `str_to_sd_type`, `sd_commit`, `sd_list_devices` (shows the GPU backend), `sd_get_system_info`.
   - `sd_version()` returns "unknown" in release builds, so show the release tag and `sd_commit()` instead.
   - Never free strings returned by the engine from managed code.
   - Keep the native callback delegates in static fields so the GC doesn't collect them.
   - `sd_lora_t` is `{ bool is_high_noise; float multiplier; const char* path; }`.
4. ggml loads its backend libraries (`ggml-cuda`, `ggml-cpu-*`) from the executable's folder **and the current
   directory**. Set the working directory to the app folder at startup, but resolve any user-given relative paths
   before you change it.
5. Windows: add an app manifest with `activeCodePage` = UTF-8 (the engine opens files with narrow-char APIs, so
   non-ASCII paths break otherwise), long-path awareness and PerMonitorV2 DPI.
6. The progress callback also reports tensor loading and VAE tiles. Only callbacks whose total equals the run's
   effective step count (lesson 7) are sampling steps; show anything else as "Working x%".
7. Image-to-image runs `floor(steps × strength) + 1` steps. Show that number in the UI.
8. **LoRAs.**
   - LoRAs are passed per run in an `sd_lora_t` array, so switching them doesn't reload the model.
   - The engine logs `(applied / total) LoRA tensors` per file. The line may end with ` --- lora.hpp:NNNN`, and 0 is
     printed in sizing passes, so keep the highest count of a run. It also logs `Only (x / y)` and
     `incompatible LoRA tensors have been skipped`.
   - A LoRA that applies almost nothing was made for another model family: show a ⚠ explaining that.
   - Keep "unused lora tensor" warnings out of the UI log.
9. Keep the model loaded between runs. Reload only when a load-affecting option changes: compute a key from the load
   options, and don't serialise the key property itself.
10. **Output folder.**
    - Never default output into a cloud-synced folder: Windows "Pictures" is often redirected into OneDrive or guarded
      by Controlled Folder Access, and writes fail.
    - Windows: default to `%TEMP%\<App>` and say on first run that Storage Sense may clean it.
    - macOS/Linux: default to `~/Pictures/<App>` (`$TMPDIR` is purged after a few days).
    - Always changeable, with a write test; if a save fails, fall back to the temp folder.
11. **PNG metadata.** Windows Explorer shows it only from **XMP** (an iTXt chunk with keyword `XML:com.adobe.xmp`),
    not from plain PNG text chunks:

    | XMP field | Shows in Explorer as |
    |---|---|
    | `dc:title` | Title |
    | `exif:UserComment` | Comments |
    | `dc:subject` | Tags |
    | `xmp:CreatorTool` | Program name |
    | `xmp:CreateDate` | Date taken |

    In a console tool with no UI library, write a minimal PNG encoder: `ZLibStream` plus a hand-written CRC-32 table.
    `System.IO.Hashing` is a NuGet package, not part of the base library.
12. **Running llama-server.**
    - Windows: a missing DLL raises a system error dialog on the user's desktop. Call
      `SetErrorMode(SEM_FAILCRITICALERRORS | SEM_NOOPENFILEERRORBOX)` before starting it (children inherit it).
    - Windows: put it in a Job Object with `JOB_OBJECT_LIMIT_KILL_ON_JOB_CLOSE` so it dies with the app.
    - macOS/Linux: record its PID and at startup kill a leftover only if that PID's executable is your llama-server.
    - Start it with **its own folder as the working directory**: ggml scans the current directory, and the app
      folder holds the image engine's different ggml build.
    - Remove `LLAMA_ARG_*` variables from its environment.
13. The Windows CUDA llama.cpp build can reuse the CUDA 12 runtime DLLs the image engine already ships
    (`cudart64_12.dll`, `cublas64_12.dll`, `cublasLt64_12.dll`). Add the app folder to the child's PATH instead of
    downloading llama.cpp's separate 391 MB `cudart` package.
14. Small language models repeat themselves. Constrain helper replies with a JSON schema with `maxLength`s (all
    properties required, `additionalProperties: false`), and use Qwen3's non-thinking sampling: temperature 0.7,
    top_p 0.8, top_k 20, min_p 0, plus `presence_penalty` 1.0.
15. **Civitai search.**
    - The `nsfw=false` parameter is not reliable: filter on the `nsfwLevel` bit flags yourself (1 PG, 2 PG-13, 4 R,
      8 X, 16 XXX). A model's level combines all its gallery images, so R alone is common for ordinary style LoRAs.
    - Repeat `baseModels=` once per name; a comma-separated list returns nothing.
    - Many downloads return 401 without an API key.
16. Several Hugging Face LoRA repos name their file `pytorch_lora_weights.safetensors`. Save it under the repo's name.
17. A modal dialog opened from an off-screen self-test window blocks forever. Self-test code paths must never show dialogs.

## 3. Features (build all of them; each has an acceptance check)

**Layout.**
- Three columns: model list (left), prompt and image (centre), settings (right).
- At the bottom: a "Recent images" strip, a collapsible engine log, and a status bar.
- Either side panel can be hidden. Each has a hide button and leaves a thin strip to bring it back, and a "focus"
  button on the image hides or shows both. Remember the state and the widths.
- Shortcuts: Ctrl+Enter generates, Esc cancels. Focus is F11 on Windows/Linux and Cmd+Ctrl+F on macOS, where Ctrl
  becomes Cmd and F11 is taken. Ctrl+Shift+M toggles the models panel and Ctrl+Shift+S the settings panel.
- Use larger-than-default fonts.
- Every setting has an ⓘ tooltip: what it does, how to set it, and the recommended value for the selected model.

F1 **Catalog and machine fit.**
- Models are grouped as Filtered / Unfiltered / Not for all audiences, with show/hide toggles.
- Each model shows name, publisher, release, licence, size for this machine's tier, a fit estimate (fits in VRAM /
  needs RAM offload / too large) and download state.
- On Apple Silicon there is no separate VRAM: "fits" means the sum of the tier's files is within the VRAM figure,
  and offload doesn't help.
- *Check:* on this machine the tier and fit labels are plausible.

F2 **Recommended settings per machine.**
- A model's settings are the catalog `defaults`, overlaid with the settings of the tier with the largest
  `minVramGb` at or below this machine's VRAM, overlaid with the user's saved per-model overrides.
- The tier search allows 0.5 GB of tolerance on discrete GPUs and none on Apple Silicon.
- Buttons: "Save as my defaults", "Reset to recommended", and "Detect this PC" (re-detects the hardware and offers to
  drop saved overrides).
- `machine.json` holds the detected hardware and an optional `vramOverrideGb`.
- *Check:* changing `vramOverrideGb` changes the files and settings.

F3 **Downloads.**
- One click per model, resumable (`.part` file plus HTTP Range), with progress, speed and Pause.
- Files shared by several models (VAEs, text encoders) are stored once at `models/<owner>/<repo>/<path>`.
  Deleting a model's files keeps those another downloaded model still needs.
- Check disk space before downloading.
- Optional Hugging Face token, sent only to huggingface.co.
- *Check:* interrupt and resume a download.

F4 **Generate.**
- Inputs: prompt; negative prompt (used only when CFG > 1; say so); seed (random, or reuse the last); batch count;
  size presets by aspect ratio, scaled to the model's pixel budget; width and height (sent to the engine rounded to
  multiples of 16); steps, CFG, guidance, flow shift, sampler, scheduler, CLIP skip.
- Memory options (lesson 2): offload weights to RAM, text encoder on CPU, VAE on CPU, VAE tiling, flash attention,
  quantize on load, max VRAM, extra model args.
- While running: optional live preview, a progress bar with s/it, and a timer from the button press that splits
  model-load time from sampling time.
- An Unload button frees the GPU memory.
- *Check:* generate one image with the smallest model.

F5 **Saving.**
- PNG named `yyyyMMdd-HHmmss-<model>-<seed>.png`, plus a `.json` sidecar with everything needed to reproduce it:
  model, files, prompt, negative, seed, size, all settings, LoRAs, start-image strength, timings, engine version,
  and the time in UTC with a trailing `Z`.
- Write the same inside the PNG, as a setting that is on by default and never applies in incognito:
  - XMP (lesson 11), plus the IPTC field `Iptc4xmpExt:DigitalSourceType` set to
    `http://cv.iptc.org/newscodes/digitalsourcetype/trainedAlgorithmicMedia`.
  - PNG text chunks Title, Description, Comment, Software, Source and Creation Time: `tEXt` when the text is Latin-1,
    UTF-8 `iTXt` otherwise.
  - An AUTOMATIC1111-style `parameters` chunk: the prompt, then `Negative prompt: …`, then one line
    `Steps: …, Sampler: …, Schedule type: …, CFG scale: …, Seed: …, Size: WxH, Model: …`. Quote values that contain a
    comma or colon.
- *Check:* `exiftool -a -G1 <file>.png` shows all of it on every OS, and on Windows Explorer › Properties › Details
  shows the title and comments.

F6 **Privacy rules (mandatory).**
- No telemetry. If ONNX Runtime is used, call `DisableTelemetryEvents()` first.
- Network access only for downloads and searches the user starts.
- Nothing that identifies the user or the machine goes into output files or their metadata: no user, machine or
  folder names, no file paths, no time zone (UTC times), no start-image file name. LoRAs without a public source URL
  are written as "own LoRA".
- Tokens and keys are sent only to their own site.
- *Check:* search an output PNG and its `.json` for the user name, machine name, home path and time-zone offset.

F7 **Gallery and reuse.**
- A Recent images strip (newest first, thumbnails); click one to show it.
- On the shown image: Open; Use as start image; **Reuse**; Delete.
- Reuse puts back model, prompt, negative, seed, size, steps, CFG, sampler, scheduler and LoRAs from the `.json`, or
  else from the PNG's `parameters` (which also works for PNGs from other A1111-style apps).
- Delete moves files to the Recycle Bin or Trash: Windows
  `Microsoft.VisualBasic.FileIO.FileSystem.DeleteFile(path, UIOption.OnlyErrorDialogs, RecycleOption.SendToRecycleBin)`;
  macOS `NSFileManager trashItemAtURL` (through Objective-C interop, or `osascript`); Linux `gio trash`.
- "Clear folder…" deletes only files matching the app's own naming pattern, after asking.

F8 **Incognito.**
- No engine log on disk (delete the existing one), the prompt isn't remembered, no `.json`, no metadata inside images.
- Each run's images are deleted when the next run starts or the app closes. Track pending paths in settings so a
  crash doesn't leave them behind.
- Deleting an incognito image also clears the prompt box, and the start image if it was that image.

F9 **Image-to-image.**
- A "Start from an image" card. Ways in: choose a file; drag and drop; paste with Ctrl/Cmd+V (a bitmap or a copied
  file; clipboard formats differ per OS); or "Use as start" on a result.
- The picture is turned upright from its EXIF orientation, its long side is capped at 2048 px, and it's kept in memory.
- Width and height switch to the picture's aspect ratio at about the same pixel count, in multiples of 64.
- The picture is centre-cropped to cover, never stretched; transparency goes on white.
- A Change (strength 0.05–1) slider says in words what it does and how many steps will run.

F10 **LoRAs.**
- A "LoRA add-ons" card lists the LoRAs switched on for the current model. Each row has a weight slider (0–1.5), a
  "+ trigger" button and the ⚠ from lesson 8.
- The library lives in `models/loras`, with A1111/Forge-style `name.json` sidecars (activation text, preferred
  weight, sd version, description). Read `name.civitai.info` files too.
- The Add LoRA menu: fitting LoRAs first, others in a submenu, browse links, "Download from a link…" (Hugging Face
  repo or file, Civitai model or version page, direct `.safetensors` link) and "Add a file".
- Work out each LoRA's family in this order; when unknown, allow it and rely on the ⚠:
  1. From its source: the Hugging Face card's `base_model` and `base_model:adapter:` tags, or Civitai's `baseModel`.
  2. From the safetensors header (8-byte little-endian length, then JSON): `__metadata__` keys
     `ss_base_model_version` (sdxl…, sd_v1…, flux1…, contains zimage, chroma, klein 4b/9b) and
     `modelspec.architecture` (stable-diffusion-xl…, flux-1…).
  3. From tensor names and widths (the width is the input dimension of the first LoRA "down"/"A" matrix):
     - `lora_te2_` / `text_encoder_2`, or UNet blocks with 2048-wide cross-attention → sdxl (768 → SD 1.5).
     - `layers.N` with `adaLN_modulation` / `attention.to_` / `feed_forward.w` → z-image.
     - `double_blocks`/`single_blocks` or `single_transformer_blocks`: width 3072 with at most 6 double blocks or
       FLUX.2 names (`to_qkv_mlp_proj`) → flux2-klein-4b; 3072 otherwise → flux1; 4096 → flux2-klein-9b.
     - `transformer_blocks` with `img_mlp` / `txt_mlp`: width 4096 → qwen-image-2.1 (3072 is first-generation
       Qwen-Image, which doesn't fit).

  Families and their names on each site:

| Family | Civitai baseModel names | Hugging Face base repos (adapters) |
|---|---|---|
| sdxl | SDXL 1.0, Illustrious, NoobAI, Pony, SDXL 0.9, SDXL Lightning, SDXL Hyper | stabilityai/stable-diffusion-xl-base-1.0, OnomaAIResearch/Illustrious-xl-early-release-v0, OnomaAIResearch/Illustrious-XL-v2.0, Laxhar/noobai-XL-1.1, SG161222/RealVisXL_V5.0 |
| z-image | ZImageTurbo, ZImageBase | Tongyi-MAI/Z-Image-Turbo, Tongyi-MAI/Z-Image |
| flux2-klein-4b | Flux.2 Klein 4B, Flux.2 Klein 4B-base | black-forest-labs/FLUX.2-klein-4B, black-forest-labs/FLUX.2-klein-base-4B |
| flux2-klein-9b | Flux.2 Klein 9B, Flux.2 Klein 9B-base | black-forest-labs/FLUX.2-klein-9B, black-forest-labs/FLUX.2-klein-base-9B |
| qwen-image-2.1 | Qwen 2.1 | Qwen/Qwen-Image-2.1 |
| chroma | Chroma | lodestones/Chroma1-HD, lodestones/Chroma, lodestones/Chroma1-Base |
| flux1 (recognise only) | Flux.1 D, Flux.1 S, Flux.1 Krea, Flux.1 Kontext | black-forest-labs/FLUX.1-dev, black-forest-labs/FLUX.1-schnell |

F11 **Find LoRAs** (a window). It searches both sites for the selected model's family.

- **Civitai:**
  `https://civitai.com/api/v1/models?types=LORA&limit=40&sort=Most%20Downloaded|Highest%20Rated|Newest&baseModels=<name>&baseModels=<name>…&query=<words>`.
  Add `&nsfw=false` in safe mode and `&cursor=<metadata.nextCursor>` for "More results".
  - Use `modelVersions[0]` and its primary `.safetensors` file (`sizeKB`, `downloadUrl`).
  - Likes come from `stats.thumbsUpCount` and downloads from `stats.downloadCount`; triggers from `trainedWords`.
  - The preview is the `type == "image"` entry with the lowest `nsfwLevel`, with `/original=true/` in its URL
    replaced by `/width=320/`.
- **Hugging Face:**
  `https://huggingface.co/api/models?filter=base_model:adapter:<repo>&sort=downloads|likes|lastModified&direction=-1&limit=40&search=<words>&expand[]=downloads&expand[]=likes&expand[]=cardData&expand[]=tags&expand[]=lastModified&expand[]=author`.
  Run it for every base repo, then merge and de-duplicate.
  - The preview is `cardData.widget[].output.url`, which may be absolute or relative to the repo.
  - Search results have no file sizes: when downloading, read `/api/models/<repo>?blobs=true` and pick the main
    `.safetensors`. If it's ambiguous, ask, but never in self-test.
- **Each row shows:** preview, name, author, base model, size, downloads, likes, trigger words, a Page link, and
  "Downloaded" state.
- **Safe mode** (the "Not for all audiences" group hidden): drop HF repos tagged `not-for-all-audiences` and Civitai
  models with X/XXX levels, and show only PG/PG-13 previews. Otherwise blur mature previews. Also blur any preview
  the NSFW classifier flags when it's on.
- **Downloading:** tick several; they download one after another, with errors on their row while the queue
  continues, and a Stop button. Each is switched on if it fits.
- **Civitai API key:** an optional field here and in Settings, sent as a Bearer token only to civitai.com and only
  with downloads.
- *Check:* search "watercolor" for an SDXL model and download two small LoRAs (from Hugging Face, or from Civitai
  with a key).

F12 **Prompt helper** (a window next to "Sample prompts").
- **Modes:** improve my prompt, or write one from an idea; plus a checkbox "Ask me questions when something
  important is unclear".
- **Helper models:**
  - **Qwen3 4B Instruct 2507** (default): `unsloth/Qwen3-4B-Instruct-2507-GGUF` / `Qwen3-4B-Instruct-2507-Q8_0.gguf`, 4.28 GB.
  - **Qwen3 8B**: `unsloth/Qwen3-8B-GGUF` / `Qwen3-8B-Q4_K_M.gguf`, 5.03 GB.

  Both are the same files some image models use as text encoder, so they're shared.
- **Starting the server:** on demand, with
  `-m <gguf> --host 127.0.0.1 --port <free port> -c 8192 -np 1 --reasoning off --no-webui --offline --no-slots`.
  Its default `--fit on` puts as many layers on the GPU as free memory allows. Wait for `GET /health` to return 200.
- **Each request:** `POST /v1/chat/completions` with
  `response_format: {type: "json_schema", json_schema: {name: "answer", schema: …}}` and the sampling from lesson 14.
- **Reply schema:**
  - `questions`: array of strings, maxItems 3, or 0 when questions aren't allowed.
  - `prompt`: maxLength 520 for tag-style models, 1100 otherwise.
  - `negative_prompt`: maxLength 0 when the model ignores it (CFG ≤ 1).
  - `notes`: maxLength 400.
- **The system prompt contains:** the model's name, description, `promptGuide` and up to 4 sample prompts; whether a
  negative prompt is used; and these rules:
  - Keep everything the user asked for.
  - Length like the examples.
  - English output from any input language. Text meant to appear in the image stays in its own language, in quotes.
  - Never invent text to appear in the image.
  - Never put the model's own style in the negative prompt.
  - Make clear that people in romantic or suggestive scenes are adults.
  - Only the description in the prompt, with no labels.
  - Notes are one short sentence.
- **Questions:** allowed only in the first reply. Then send the helper's JSON back as an assistant message, followed
  by a user message `My answers:` with one `- <question> <answer>` line each ("no preference, you choose" for empty
  answers) and "Now write the prompt." "Skip, you choose" sends a message asking it to choose and write.
- **The result:** shown editable, with the negative prompt and the notes. Buttons: Use this prompt, Try again (a new
  seed) and Improve this further.
- **Lifetime:** stop the server after 5 idle minutes, before every image generation (to free GPU memory) and when
  the app exits.
- Nothing is logged or saved.
- *Check:* "a dragon" with questions on gives 1–3 questions, then a model-appropriate prompt within seconds on a GPU.

F13 **NSFW classifier** (off by default). When it's on:
- Score each finished image on the CPU.
- At or above the threshold (default 0.7), blur the image in the UI with a Reveal button, and save it in a `flagged`
  subfolder.
- Pause live preview while it's on, because previews aren't checked.

F14 **Sample prompts and self-test.**
- A Sample prompts menu per model, from the catalog.
- **Self-test mode for agents:** `--screenshot out.png [--prompt "text"]` renders the window to a PNG (after one
  generation if a prompt is given), writes unhandled errors to `out.png.errors.txt` instead of showing dialogs, and exits.
- An `<APPNAME>_HOME` environment variable keeps config, models and output in one portable folder for testing.

**Data locations:**
- Config (`settings.json`, `machine.json`, optional `models.json`), `engine.log` and `tools/` live in the per-user
  app-data folder: `%LOCALAPPDATA%\<App>`, `~/Library/Application Support/<App>`, or `~/.local/share/<App>`.
- Models go where the user chose in section 0.
- Output: see lesson 10.

## 4. The model catalog (validated 2026-10-16)

**Schema per model:** `id, name, publisher, released, category (filtered|unfiltered|nfaa), license, pageUrl,
description, tips, promptGuide, promptStyle (tags|prose), samplePrompts[], family, files{key: {role, repo, path,
sizeGb}}, defaults{…}, tiers[{minVramGb, files[], settings{}}]`.
- File roles: `model` (all-in-one checkpoint), `diffusion`, `llm`, `t5xxl`, `vae`.
- A tier lists one file key per role it uses. A role with a single file (for example `vae`) is always included.
- Download URL: `https://huggingface.co/<repo>/resolve/main/<path>`.
- Before shipping, HEAD-check every URL and its size, and look on Hugging Face for newer versions of each family.
  Replace a model only with a newer one of the same family.

Write a `promptGuide` (2–4 sentences on how to prompt that model) and 3–5 sample prompts for each model:

| Model | How to prompt it | promptStyle |
|---|---|---|
| Z-Image, FLUX.2 klein | Plain English sentences, 30–90 words, no quality tags. CFG 1, so no negative prompt | prose |
| Qwen-Image 2.1 | Long art-director prose, with any text to appear in the image in quotes | prose |
| Chroma | Natural language that names the medium, plus a negative prompt | prose |
| RealVisXL | "RAW photo" phrases with camera terms | prose |
| Illustrious, NoobAI, WAI | Lowercase Danbooru tags with their quality-tag prefixes, plus a short negative prompt | tags |

How to read the table below: weights are GGUF quantisations unless stated, and sizes are in GB. In the Tiers column,
each entry is the minimum VRAM in GB, then the file keys that tier uses, then any extra settings in brackets:
"offload" = offload weights to RAM, "tile" = VAE tiling, "q8_0"/"q4_K" = quantise on load, "768²" = a smaller
default size.

| Model (id, family, category, licence) | Files (key: repo / path, GB) | Defaults | Tiers |
|---|---|---|---|
| Z-Image Turbo (`z-image-turbo`, z-image, unfiltered, Apache-2.0) | dit-q8 / q6 / q4 / q3: `leejet/Z-Image-Turbo-GGUF` / `z_image_turbo-Q8_0.gguf` 6.58, `-Q6_K` 5.26, `-Q4_K` 3.86, `-Q3_K` 3.14; llm-q8: `unsloth/Qwen3-4B-Instruct-2507-GGUF` / `Qwen3-4B-Instruct-2507-Q8_0.gguf` 4.28; llm-q4: same repo `Qwen3-4B-Instruct-2507-Q4_K_M.gguf` 2.5; vae: `Comfy-Org/z_image_turbo` / `split_files/vae/ae.safetensors` 0.34 | 1024², 8 steps, CFG 1 | 0: dit-q3+llm-q4 [768², offload, tile]; 8: dit-q4+llm-q4 [offload]; 12: dit-q8+llm-q8 [offload]; 16: dit-q8+llm-q8 |
| FLUX.2 [klein] 4B (`flux2-klein-4b`, flux2-klein-4b, filtered, Apache-2.0) | dit-q8: `leejet/FLUX.2-klein-4B-GGUF` / `flux-2-klein-4b-Q8_0.gguf` 4.3; dit-q4: `flux-2-klein-4b-Q4_0.gguf` 2.46; llm-q8: `unsloth/Qwen3-4B-GGUF` / `Qwen3-4B-Q8_0.gguf` 4.28; llm-q4: `Qwen3-4B-Q4_K_M.gguf` 2.5; vae: `Comfy-Org/flux2-dev` / `split_files/vae/flux2-vae.safetensors` 0.34 | 1024², 4 steps, CFG 1, euler | 0: dit-q4+llm-q4 [768², offload, tile]; 8: dit-q8+llm-q4 [offload]; 12: dit-q8+llm-q8 |
| FLUX.2 [klein] 9B (`flux2-klein-9b`, flux2-klein-9b, filtered, FLUX Non-Commercial) | dit-q8: `leejet/FLUX.2-klein-9B-GGUF` / `flux-2-klein-9b-Q8_0.gguf` 9.98; dit-q4: `flux-2-klein-9b-Q4_0.gguf` 5.62; llm-q8: `unsloth/Qwen3-8B-GGUF` / `Qwen3-8B-Q8_0.gguf` 8.71; llm-q4: `Qwen3-8B-Q4_K_M.gguf` 5.03; vae: as klein 4B | 1024², 4 steps, CFG 1, euler, offload | 0: dit-q4+llm-q4 [768², tile]; 8: dit-q4+llm-q4; 12: dit-q8+llm-q4; 16: dit-q8+llm-q8; 24: same [no offload] |
| Qwen-Image 2.1 (`qwen-image-2.1`, qwen-image-2.1, filtered, Qwen Research, non-commercial) | dit-q8 / q6 / q4 / q3: `leejet/Qwen-Image-2.1-GGUF` / `qwen_image_2.1-Q8_0.gguf` 7.69, `-Q6_K` 6.0, `-Q4_K` 4.2, `-Q3_K` 3.27; llm-q8: `Qwen/Qwen3-VL-8B-Instruct-GGUF` / `Qwen3VL-8B-Instruct-Q8_0.gguf` 8.71; llm-q4: `Qwen3VL-8B-Instruct-Q4_K_M.gguf` 5.03; vae: `Comfy-Org/Qwen-Image-2.1` / `vae/qwen_image_2.1_vae_bf16.safetensors` 0.68 | 1024², 30 steps, CFG 4, euler, offload | 0: dit-q3+llm-q4 [768², tile]; 8: dit-q4+llm-q4; 12: dit-q8+llm-q4; 16: dit-q8+llm-q8; 24: same [1536², no offload] |
| Chroma1-HD (`chroma1-hd`, chroma, unfiltered, Apache-2.0) | dit-q8 / q6 / q4 / q3: `silveroxides/Chroma1-HD-GGUF` / `Chroma1-HD-Q8_0.gguf` 9.74, `-Q6_K` 7.65, `-Q4_K_S` 5.43, `-Q3_K_S` 4.29; t5-q8: `city96/t5-v1_1-xxl-encoder-gguf` / `t5-v1_1-xxl-encoder-Q8_0.gguf` 5.06; t5-q4: `t5-v1_1-xxl-encoder-Q4_K_M.gguf` 2.9; vae: `Comfy-Org/z_image_turbo` / `split_files/vae/ae.safetensors` 0.34 | 1024², 26 steps, CFG 4, euler, model args `chroma_use_dit_mask=false`, offload | 0: dit-q3+t5-q4 [768², tile]; 8: dit-q4+t5-q4; 12: dit-q6+t5-q8; 16: dit-q8+t5-q8; 24: same [no offload] |
| RealVisXL V5.0 (`realvisxl-v5`, sdxl, unfiltered, OpenRAIL++) | model: `SG161222/RealVisXL_V5.0` / `RealVisXL_V5.0_fp16.safetensors` 6.94; vae: `madebyollin/sdxl-vae-fp16-fix` / `sdxl_vae.safetensors` 0.33 | 1024², 30 steps, CFG 5, dpm++2m, karras | 0: [q8_0, offload, tile]; 8: plain |
| Illustrious XL v2.0 (`illustrious-xl-v2`, sdxl, unfiltered, OpenRAIL-M) | model: `OnomaAIResearch/Illustrious-XL-v2.0` / `Illustrious-XL-v2.0.safetensors` 6.94; vae: as RealVisXL | 1024², 28 steps, CFG 6, euler_a | as RealVisXL |
| NoobAI-XL 1.1 (`noobai-xl-1.1`, sdxl, nfaa, NoobAI non-commercial) | model: `Laxhar/noobai-XL-1.1` / `NoobAI-XL-v1.1.safetensors` 7.11; vae: as RealVisXL | 1024², 28 steps, CFG 5.5, euler_a | as RealVisXL |
| WAI-illustrious-SDXL v17 (`wai-illustrious-v17`, sdxl, nfaa, FAIPL-1.0-SD) | model: `IbarakiDouji/WAI-NSFW-illustrious-SDXL` / `waiIllustriousSDXL_v170.safetensors` 6.94; vae: as RealVisXL | 1024×1344, 28 steps, CFG 6, euler_a | 0: [832×1216, q8_0, offload, tile]; 8: plain |
| REDCraft REDZ v1.5 (`redcraft-redz-zit`, z-image, nfaa, Apache-2.0) | dit-bf16: `GuangyuanSD/REDCraft-DarkBeast-Z-Image-TURBO` / `RedCraft-ZImageTurbo/REDZ-v1.5-BF16-diffusion-model.safetensors` 12.31; llm and vae as Z-Image Turbo | 1024², 8 steps, CFG 1, offload | 0: llm-q4 [q4_K, 768², tile]; 8: llm-q4 [q4_K]; 12: llm-q8 [q8_0]; 24: llm-q8 [no offload] |
| Noct Q V4 (`noct-q-v4`, qwen-image-2.1, nfaa, Qwen Research, non-commercial) | dit-int8: `Noctaluna/Noct-Q-Uncensored-Qwen-Image-2.1` / `NoctQ_V4_int8_convrot.safetensors` 7.26; llm and vae as Qwen-Image 2.1 | 1024×1536, 25 steps, CFG 3, euler, offload | 0: llm-q4 [768×1152, tile]; 8: llm-q4; 16: llm-q8; 24: llm-q8 [no offload] |

If the user didn't opt in to adult models in section 0, leave out the `nfaa` models.

## 5. Phases (verify each before moving on; record results in NOTES.md)

1. **Engine bindings.**
   - Create the project skeleton: solution, app project, and a console check tool.
   - Download the engine for this machine and the pinned header, write the bindings, then build the C layout program
     and the C# layout check (lesson 1).
   - *Done when:* every size and offset matches, and the tool prints `sd_commit()`, `sd_list_devices()` and
     `sd_get_system_info()` from the downloaded library, showing the GPU backend (or CPU when there is no GPU).
2. **First image from the console tool.**
   - Download the smallest catalog setup, FLUX.2 klein 4B (dit-q4 + llm-q4 + vae, about 5.3 GB), and generate
     512×512 in 4 steps.
   - *Done when:* a correct image is written as PNG (lesson 11) and the timings are logged. For reference, on 16 CPU
     cores without a GPU this took about 3 minutes and 6.3 GB of RAM; a GPU should take seconds.
3. **Catalog, machine profile, settings layering, downloader** (F1–F3), with unit-level checks.
4. **UI and generation** (F4, layout, tooltips), then **saving, metadata, gallery, Reuse, incognito** (F5–F8). Use
   the self-test mode (F14) to render screenshots, and look at them after every UI change.
5. **Image-to-image and LoRAs** (F9–F10), then **Find LoRAs** (F11).
6. **Prompt helper** (F12), then the **NSFW classifier** (F13).
7. **Finish.**
   - Write a short README: what it is, an adults-only note if nfaa models are included, and how to build and run it
     on this OS. Also write a user guide and a `.gitignore` (bin, obj, engine, downloads).
   - Do a final end-to-end run: download a model, generate, check the metadata and the privacy search (F6), use the
     helper, then download a LoRA and generate with it.

Report at the end: what works, what wasn't tested and why, and the exact commands to build and run the app.
