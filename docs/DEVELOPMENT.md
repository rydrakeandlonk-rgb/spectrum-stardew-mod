# Development

## Product development model

This repository contains the SMAPI game client and its release packaging. Spectrum Control Center, Event Hub and the Stardew gateway live in the existing spectrum-cloudflare repository. The integration is private to Spectrum members explicitly enabled by a Control Center administrator.

## Current SMAPI prototype requirements

- Stardew Valley 1.6.14 or later
- SMAPI 4.5.0 or later (development baseline: 4.5.2)
- .NET 6 SDK
- Visual Studio 2022 or another .NET-capable editor

The root-level project uses Pathoschild's Stardew ModBuildConfig package to reference local Stardew Valley and SMAPI assemblies and prepare the mod output.

## Build the prototype

From the repository root:

    dotnet restore
    dotnet build

ModBuildConfig uses the Stardew Valley installation detected on the development machine. If detection fails, configure the game path according to the ModBuildConfig documentation.

## Local gameplay tests

1. Load a disposable test save as the host.
2. Open the SMAPI console.
3. Run spectrum_spawn_test to spawn a Green Slime.
4. Run spectrum_spawn_random_test to exercise the weighted random candidate pool.
5. Check the SMAPI log for the selected monster and spawn result.
6. Repeat in a multiplayer farm with a farmhand connected; confirm exactly one monster appears for both players.

The random pool uses inverse-health weighting. Candidate constructors are discovered from the installed Stardew assembly; unavailable candidates are skipped. Confirm the actual candidate pool and balance in-game before release.

Back up saves before early gameplay testing.

## Build a distributable ZIP

Run from the repository root in PowerShell:

    .\packaging\Build-Release.ps1

The script restores and builds the project in Release mode, stages the compiled output and manifest, and creates dist/SpectrumStardewMod-VERSION.zip. It does not publish a GitHub Release or Nexus page automatically. Inspect and test the ZIP on a clean SMAPI installation before uploading it to a release.

The ZIP must contain the mod's compiled DLL, manifest.json, and required dependencies/assets at its root. It must not contain source-only files, Twitch client secrets, service credentials, or private gateway configuration.

## Secrets and configuration

- Never commit Twitch client secrets, OAuth tokens, signing keys, or production service credentials.
- The game mod must never store Twitch access or refresh tokens.
- The gateway issues revocable game-session credentials after Control Center verifies member access.
- Network callbacks enqueue typed requests; game state changes only on the SMAPI game thread.

## Current limitations

- Weighted random monster selection is implemented in source but has not been compiled or verified against the target game installation.
- Twitch EventSub redemption subscription/routing, gateway, pairing, and remote connection are not implemented yet.
- Control Center Mods page and D1 migration are committed in the Spectrum Cloudflare repository but have not been deployed/applied.
- Reward mapping UI and live connection reporting are not implemented yet.
- No tested ZIP, GitHub Release or Nexus release exists yet.
