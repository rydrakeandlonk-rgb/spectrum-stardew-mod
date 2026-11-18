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
3. Run spectrum_spawn_test to request a Green Slime through Stardew's built-in debug command parser.
4. Run spectrum_spawn_random_test to exercise the weighted random monster allowlist.
5. Check the SMAPI log for the selected monster and command result.
6. Repeat in a multiplayer farm with a farmhand connected; confirm exactly one monster appears for both players.

The mod invokes Game1.parseDebugInput with a generated monster command. Stardew's own DebugCommands handler performs monster construction. The mod does not reflect over monster constructors. The monster type is selected from a fixed allowlist; approximate health estimates are used only to weight selection.

Back up saves before early gameplay testing.

## Build a distributable ZIP

Run from the repository root in PowerShell:

    .\packaging\Build-Release.ps1

The script restores and builds the project in Release mode, stages the compiled output and manifest, and creates dist/SpectrumStardewMod-VERSION.zip. It does not publish a GitHub Release or Nexus page automatically. Inspect and test the ZIP on a clean SMAPI installation before uploading it to a release.

The ZIP must contain the mod's compiled DLL, manifest.json, and required dependencies/assets at its root. It must not contain source-only files, Twitch client secrets, service credentials, or private gateway configuration.

## Gateway configuration\n\nAfter an administrator enables the member in Control Center, use the Mods page's Pair / re-pair action. It displays the broadcaster's numeric Twitch user ID and a one-time host token. On the host computer, launch the mod once so SMAPI creates its config file, then set:\n\n- `BroadcasterUserId`: the numeric ID shown by Control Center.\n- `HostToken`: the one-time token shown by Control Center.\n- `GatewayUrl`: leave the default Spectrum Game Mods HTTPS URL unless the service endpoint changes.\n\nRestart Stardew/SMAPI after editing the config. Use `spectrum_connection_status` in the SMAPI console to check the connection. Never commit the local config or share the host token. Re-pairing rotates the token and disconnects the previous host session.\n\n## Secrets and configuration

- Never commit Twitch client secrets, OAuth tokens, signing keys, or production service credentials.
- The game mod must never store Twitch access or refresh tokens.
- The gateway issues revocable game-session credentials after Control Center verifies member access.
- Network callbacks enqueue typed requests; game state changes only on the SMAPI game thread.

## Current limitations

- Built-in monster command dispatch and weighted selection are implemented in source but have not been compiled or verified against the target game installation.
- Gateway and SMAPI WebSocket client are implemented in source but have not been compiled or deployed.
- Control Center pairing/revocation controls and Event Hub routing are committed in the Spectrum Cloudflare repository; production deployment, migration 0028 and dashboard KV sync still need separate verification.
- Reward mapping UI and live connection reporting are not implemented yet.
- No tested ZIP, GitHub Release or Nexus release exists yet.
