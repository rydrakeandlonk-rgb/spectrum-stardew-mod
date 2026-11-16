# Development

## Requirements

- Stardew Valley 1.6.14 or later
- SMAPI 4.5.0 or later (development baseline: 4.5.2)
- .NET 6 SDK
- Visual Studio 2022 or another .NET-capable editor

The project uses Pathoschild's Stardew ModBuildConfig package to reference the local Stardew Valley and SMAPI assemblies and prepare the mod output.

## Build

From the repository root:

```powershell
dotnet restore
dotnet build
```

ModBuildConfig uses the Stardew Valley installation detected on the development machine. If detection fails, configure the game path according to the ModBuildConfig documentation.

## Install for local testing

Copy the generated mod folder from the build output into:

`%AppData%\StardewValley\Mods\SpectrumStardewMod`

Launch Stardew Valley through SMAPI.

## First test

1. Load a disposable test save as the host.
2. Open the SMAPI console.
3. Run `spectrum_spawn_test`.
4. Confirm one Green Slime appears on a clear tile near the player.
5. Check the SMAPI log for the spawn confirmation.
6. Repeat in a multiplayer farm with a farmhand connected; confirm exactly one slime appears for both players.

Do not test on a save you cannot afford to restore. Back up saves before early gameplay testing.

## Current limitations

- Only a Green Slime test spawn is implemented.
- No Twitch, Cloudflare, WebSocket, or reward configuration exists yet.
- Multiplayer replication is designed to rely on Stardew's networked location character collection, but still needs an actual host/farmhand test.
