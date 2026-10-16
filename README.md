# ImageMango

A fast, native Windows 11 app that makes images on your own PC with current open-weight models. No browser, no
Python, no cloud.

> **Adults only (18+).** ImageMango can download and run **uncensored and not-for-all-audiences models**, and its
> LoRA browser can show adult content when you switch that on. This repository contains **no models and no images**:
> only source code. Models are downloaded from Hugging Face by the app when you ask, under their own licences (several
> are non-commercial). You are responsible for how you use them and for following the law where you live.

## Why I built it

I wanted to try the newest image models on my own PC: quickly, privately, with models both with and without guard
rails. The usual tools are browser front ends on a Python stack, slow to set up and easy to break. I wanted one native
app that picks good models and good settings for the hardware it runs on.

## What it does

- Lists 11 recent, well-rated models (Z-Image, FLUX.2 klein, Qwen-Image 2.1, Chroma, SDXL anime and photo models),
  grouped as Filtered, Unfiltered and Not for all audiences, and downloads them with one click.
- Generates from a prompt or from a start image (image-to-image), with LoRA add-ons and a live preview.
- **Prompt helper:** a small local language model (Qwen3 4B, or 8B) improves your prompt or writes one from an
  idea, in the style the chosen model understands, and asks questions when the idea is vague. Any language in.
- **Find LoRAs:** searches Civitai and Hugging Face for LoRAs that fit the model; tick, download, done.
- Saves the prompt, model and settings inside each PNG (visible in Explorer › Properties › Details); **Reuse** puts
  them back later.
- Incognito mode, an optional NSFW classifier (off by default), and hideable side panels for a big preview.

## Fits your PC

On first start it detects your GPU, VRAM and RAM. Every model in the catalog carries recommended settings per VRAM
tier (under 8, 8, 12, 16 and 24 GB), and ImageMango picks the tier for your GPU, with RAM offload when a model is bigger
than your VRAM. **Detect this PC** re-tunes it after a hardware change; `machine.json` lets you override it.

## How it's built

C# / WPF on .NET 10 for the UI. [stable-diffusion.cpp](https://github.com/leejet/stable-diffusion.cpp) (C++, CUDA or
Vulkan) generates the images, called directly through its C API. [llama.cpp](https://github.com/ggml-org/llama.cpp)
runs the prompt helper on localhost, and ONNX Runtime runs the NSFW classifier. A JSON catalog
(`src/ImageMango/catalog/models.json`) holds the models, their settings tiers and their prompt guides.

## Get it and run it

You need Windows 11 x64, an NVIDIA GPU with a recent driver (AMD/Intel work through Vulkan; CPU works but is slow),
Git, about 2 GB for the engine and 7–25 GB per model. In PowerShell:

```powershell
winget install Git.Git                                        # once
winget install Microsoft.DotNet.SDK.10                        # once; then reopen PowerShell
git clone https://github.com/venkatarangan/ImageMango.git
cd ImageMango
powershell -ExecutionPolicy Bypass -File .\setup-engine.ps1   # once: engine + CUDA runtime (~900 MB)
#   AMD/Intel GPU: .\setup-engine.ps1 -Backend vulkan    no GPU: -Backend cpu
dotnet publish src\ImageMango -c Release -o publish
.\publish\ImageMango.exe
```

Pick a model, click **Download**, type a prompt, press **Ctrl+Enter**. The full guide is in [USER-GUIDE.md](USER-GUIDE.md).

### Or let your coding agent set it up

Open Claude Code, OpenAI Codex, Google Antigravity or GitHub Copilot (agent mode) in an empty folder and paste:

> Clone https://github.com/venkatarangan/ImageMango, read README.md and USER-GUIDE.md, and set it up on this PC:
> check my OS, GPU, driver, VRAM and RAM; install what's missing (Git, the .NET 10 SDK); run setup-engine.ps1 with
> the right backend for my GPU (cuda, vulkan or cpu); build with `dotnet publish src\ImageMango -c Release -o publish`
> and start publish\ImageMango.exe. Tell me which VRAM tier the app picked and which catalog models fit my PC best.
> Ask before installing anything or changing system settings.

## Build your own from scratch (Windows, macOS or Linux)

[docs/REBUILD-PROMPT.md](docs/REBUILD-PROMPT.md) is a complete, tested prompt for a coding agent. It rebuilds an app
with the same features from an empty folder, configured for your machine, including Apple Silicon Macs (Metal).

## How it's different

- **Native and fast:** one desktop app over a C++ GPU engine. The model stays loaded between runs, and there's no Python
  environment to break.
- **Opinionated:** a short list of current models with settings already worked out for your GPU, instead of a blank
  canvas of knobs.
- **Private by design:** no telemetry. Nothing leaves the PC except downloads you ask for. Nothing about you or your PC
  is written into images, and incognito leaves no trace.
- **Helps you write prompts** with an AI that also runs locally, and finds LoRAs without a browser.

## Licence

MIT, see [LICENSE](LICENSE). The engine, the CUDA runtime, llama.cpp and the models are not part of this repository:
`setup-engine.ps1` and the app download them from their publishers, under their own licences.
