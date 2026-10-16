<#
.SYNOPSIS
  Downloads the pinned stable-diffusion.cpp engine DLLs into .\engine (used by the build).

.PARAMETER Backend
  cuda   (default) NVIDIA GPUs. Also downloads the CUDA 12 runtime DLLs (~900 MB total).
  vulkan AMD / Intel / NVIDIA GPUs without CUDA (~30 MB).
  cpu    No GPU (slow).

.EXAMPLE
  powershell -ExecutionPolicy Bypass -File .\setup-engine.ps1
  powershell -ExecutionPolicy Bypass -File .\setup-engine.ps1 -Backend vulkan
#>
param(
    [ValidateSet('cuda', 'vulkan', 'cpu')]
    [string]$Backend = 'cuda'
)

$ErrorActionPreference = 'Stop'

# Must match ImageMango.Native.SdVersion (the C# bindings are checked against this release).
$Release = 'master-964-1d0a58a'
$Commit = '1d0a58a'
$BaseUrl = "https://github.com/leejet/stable-diffusion.cpp/releases/download/$Release"

$Assets = @{
    cuda   = @("sd-master-$Commit-bin-win-cuda12-x64.zip", 'cudart-sd-bin-win-cu12-x64.zip')
    vulkan = @("sd-master-$Commit-bin-win-vulkan-x64.zip")
    cpu    = @("sd-master-$Commit-bin-win-cpu-x64.zip")
}

$EngineDir = Join-Path $PSScriptRoot 'engine'
$CacheDir = Join-Path $PSScriptRoot '.engine-cache'
New-Item -ItemType Directory -Force -Path $EngineDir, $CacheDir | Out-Null

# Start clean so switching backends never mixes DLLs.
Get-ChildItem -Path $EngineDir -File | Remove-Item -Force

foreach ($asset in $Assets[$Backend]) {
    $zip = Join-Path $CacheDir $asset
    if (-not (Test-Path $zip)) {
        Write-Host "Downloading $asset ..."
        & curl.exe -L --fail --retry 3 -o "$zip.part" "$BaseUrl/$asset"
        if ($LASTEXITCODE -ne 0) { throw "Download failed: $asset" }
        Move-Item -Force "$zip.part" $zip
    }
    else {
        Write-Host "Using cached $asset"
    }

    $tmp = Join-Path $CacheDir 'extract'
    if (Test-Path $tmp) { Remove-Item -Recurse -Force $tmp }
    Expand-Archive -Path $zip -DestinationPath $tmp -Force
    # Flatten: some archives keep files in a sub-folder.
    Get-ChildItem -Path $tmp -Recurse -File | Where-Object { $_.Extension -in '.dll', '.exe', '.txt' } |
        ForEach-Object { Copy-Item $_.FullName -Destination $EngineDir -Force }
    Remove-Item -Recurse -Force $tmp
}

if (-not (Test-Path (Join-Path $EngineDir 'stable-diffusion.dll'))) {
    throw "stable-diffusion.dll was not found in the downloaded archive."
}

Set-Content -Path (Join-Path $EngineDir 'BACKEND.txt') -Value "$Backend $Release"
$count = (Get-ChildItem -Path $EngineDir -Filter *.dll).Count
Write-Host ""
Write-Host "Engine ready: $Backend backend, release $Release, $count DLLs in $EngineDir"
Write-Host "Next: dotnet run -c Release --project src\ImageMango"
