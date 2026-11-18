# Development

## Product development model

This repository contains the SMAPI game client and its release packaging. Spectrum Control Center, Event Hub and the shared Game Mods gateway live in the existing `spectrum-cloudflare` repository. The integration is private to active Spectrum members explicitly enabled by a Control Center administrator.

## Requirements

- Stardew Valley 1.6.14 or later
- SMAPI 4.5.0 or later (development baseline: 4.5.2)
- .NET 6 SDK
- Visual Studio 2022 or another .NET-capable editor

The root-level project uses Pathoschild's Stardew ModBuildConfig package to reference local Stardew Valley and SMAPI assemblies.

## Build the mod

From the repository root:

```sh
dotnet restore
dotnet build
```

ModBuildConfig uses the Stardew Valley installation detected on the development machine. If detection fails, configure the game path according to the ModBuildConfig documentation.

## Local gameplay tests

1. Load a disposable test save as the host.
2. Open the SMAPI console.
3. Run `spectrum_spawn_test` to request a Green Slime through Stardew's built-in debug command parser.
4. Run `spectrum_spawn_random_test` to exercise the weighted random monster allowlist.
5. Run `spectrum_connection_status` to inspect the gateway connection.
6. Check the SMAPI log for the selected monster and command result.
7. Repeat in a multiplayer farm with a farmhand connected; confirm exactly one monster appears for both players.

The network client validates typed messages and queues them for the SMAPI update thread. It does not execute server-supplied command text or monster class names. The local monster catalogue selects the type and tile coordinate.

Back up saves before early gameplay testing.

## Pair the host

After an administrator enables the member in Control Center, use the Mods page's **Pair / re-pair** action. It displays the broadcaster's numeric Twitch user ID and a one-time host token.

1. Launch the mod once so SMAPI creates its local `config.json`.
2. Open the generated config file in the mod folder.
3. Set `BroadcasterUserId` to the numeric ID shown by Control Center.
4. In the Mods page, use Set reward and enter the Twitch reward ID and exact title for the member.
5. Set `HostToken` to the one-time token shown by Control Center.
6. Leave `GatewayUrl` at its default Spectrum Game Mods URL.
7. Restart Stardew/SMAPI.

Use `spectrum_connection_status` in the SMAPI console to check the connection. Never commit the local config or share the host token. Re-pairing rotates the token and disconnects the previous host session.

## Build a distributable ZIP

Run from the repository root in PowerShell:

```powershell
.\packaging\Build-Release.ps1
```

The script restores and builds the project in Release mode, stages the compiled output and manifest, and creates a versioned ZIP under `dist/`. It does not publish a GitHub Release or Nexus page automatically. Inspect and test the ZIP on a clean SMAPI installation before uploading it.

The ZIP must contain the compiled DLL, `manifest.json`, and required dependencies/assets at its root. It must not contain source-only files, Twitch client secrets, service credentials, or private gateway configuration.

## Secrets and configuration

- Never commit Twitch client secrets, OAuth tokens, signing keys, or production service credentials.
- The game mod must never store Twitch access or refresh tokens.
- The host token is a revocable game-session credential stored only in the local SMAPI config.
- Network callbacks enqueue typed requests; game state changes only on the SMAPI game thread.

## Current limitations

- The SMAPI WebSocket client and built-in monster command dispatch are implemented in source but have not been compiled or verified against the target game installation.
- The shared gateway, Control Center pairing/revocation controls and guarded Event Hub routing are committed but have not been deployed or tested end-to-end.
- Migration 0028 has not been applied to production D1 and dashboard KV assets have not been synchronised.
- Live connection reporting is not implemented yet.
- No tested ZIP, GitHub Release or Nexus release exists yet.
