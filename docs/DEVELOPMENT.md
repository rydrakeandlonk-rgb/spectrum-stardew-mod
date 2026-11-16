# Development

## Product development model

This repository is the source of truth for the complete product:

- SMAPI game mod.
- Hosted Twitch/EventSub service.
- Streamer setup web app.
- Release packaging scripts.
- Documentation.

The hosted service is centrally operated by the project maintainer. End users should only need the published mod ZIP and a browser to connect Twitch.

## Current SMAPI prototype requirements

- Stardew Valley 1.6.14 or later
- SMAPI 4.5.0 or later (development baseline: 4.5.2)
- .NET 6 SDK
- Visual Studio 2022 or another .NET-capable editor

The current root-level project uses Pathoschild's Stardew ModBuildConfig package to reference local Stardew Valley and SMAPI assemblies and prepare the mod output. The planned monorepo migration will move this project under `src/SpectrumStardewMod/`.

## Build the current prototype

From the repository root:

```powershell
dotnet restore
dotnet build
```

ModBuildConfig uses the Stardew Valley installation detected on the development machine. If detection fails, configure the game path according to the ModBuildConfig documentation.

## Install the current prototype for local testing

Copy the generated mod folder from the build output into:

`%AppData%\StardewValley\Mods\SpectrumStardewMod`

Launch Stardew Valley through SMAPI.

## First gameplay test

1. Load a disposable test save as the host.
2. Open the SMAPI console.
3. Run `spectrum_spawn_test`.
4. Confirm one Green Slime appears on a clear tile near the player.
5. Check the SMAPI log for the spawn confirmation.
6. Repeat in a multiplayer farm with a farmhand connected; confirm exactly one slime appears for both players.

Do not test on a save you cannot afford to restore. Back up saves before early gameplay testing.

## Public release package

The intended user-facing download is a versioned ZIP with the mod folder at the ZIP root, containing the compiled DLL, `manifest.json`, and any required bundled dependencies/assets. It must not contain source-only files, Twitch client secrets, server credentials, or the hosted service's private configuration.

A release packaging script will create the same ZIP for GitHub Releases and Nexus Mods. The package must be tested by extracting it into a clean SMAPI Mods folder before publication.

## Secrets and local configuration

- Never commit Twitch client secrets, OAuth tokens, signing keys, or production service credentials.
- Use local ignored configuration for development.
- Production secrets belong in the hosted service's secret manager/environment, never in the mod or public ZIP.
- Test OAuth and EventSub with dedicated test accounts before inviting public users.

## Current limitations

- Only a Green Slime test spawn is implemented.
- No Twitch, Cloudflare, WebSocket, OAuth, EventSub, setup web app, or reward configuration exists yet.
- No public downloadable ZIP or Nexus release exists yet.
- Multiplayer replication is designed to rely on Stardew's networked location character collection, but still needs an actual host/farmhand test.
