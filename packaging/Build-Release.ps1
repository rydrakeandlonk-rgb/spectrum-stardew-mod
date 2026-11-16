$ErrorActionPreference = "Stop"

$repoRoot = Split-Path -Parent $PSScriptRoot
$project = Join-Path $repoRoot "SpectrumStardewMod.csproj"
$manifestPath = Join-Path $repoRoot "manifest.json"
$distPath = Join-Path $repoRoot "dist"

if (!(Test-Path $project)) { throw "SMAPI project file was not found: $project" }
if (!(Test-Path $manifestPath)) { throw "SMAPI manifest was not found: $manifestPath" }

$manifest = Get-Content -Raw $manifestPath | ConvertFrom-Json
$version = [string]$manifest.Version
if ([string]::IsNullOrWhiteSpace($version)) { throw "manifest.json must contain a Version." }

Write-Host "Building Spectrum Stardew Monster Spawner $version..."
dotnet restore $project
dotnet build $project --configuration Release --no-restore

$dll = Get-ChildItem -Path (Join-Path $repoRoot "bin/Release") -Filter "SpectrumStardewMod.dll" -File -Recurse |
    Select-Object -First 1
if (!$dll) {
    throw "Build completed but SpectrumStardewMod.dll was not found under bin/Release. Check the ModBuildConfig output path."
}

$stage = Join-Path ([System.IO.Path]::GetTempPath()) ("SpectrumStardewMod-" + [guid]::NewGuid().ToString("N"))
New-Item -ItemType Directory -Path $stage -Force | Out-Null

try {
    Copy-Item -Path (Join-Path $dll.Directory.FullName "*") -Destination $stage -Recurse -Force
    Copy-Item -Path $manifestPath -Destination (Join-Path $stage "manifest.json") -Force

    Get-ChildItem -Path $stage -Include "*.pdb", "*.xml" -File -Recurse |
        Remove-Item -Force -ErrorAction SilentlyContinue

    if (!(Test-Path (Join-Path $stage "SpectrumStardewMod.dll"))) {
        throw "The staged release package is missing SpectrumStardewMod.dll."
    }
    if (!(Test-Path (Join-Path $stage "manifest.json"))) {
        throw "The staged release package is missing manifest.json."
    }

    New-Item -ItemType Directory -Path $distPath -Force | Out-Null
    $zipPath = Join-Path $distPath ("SpectrumStardewMod-" + $version + ".zip")
    if (Test-Path $zipPath) { Remove-Item $zipPath -Force }
    Compress-Archive -Path (Join-Path $stage "*") -DestinationPath $zipPath -CompressionLevel Optimal
    Write-Host "Created $zipPath"
    Write-Host "Extract the ZIP contents into Stardew Valley/Mods/SpectrumStardewMod."
}
finally {
    if (Test-Path $stage) { Remove-Item $stage -Recurse -Force }
}
