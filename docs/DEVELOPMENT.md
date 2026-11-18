# Development

## Product development model

This repository contains the SMAPI game client and its release packaging. Spectrum Control Center, Event Hub and the shared Game Mods gateway live in the existing `spectrum-cloudflare` repository. The integration is private to active Spectrum members explicitly enabled by a Control Center administrator.

## Requirements

- Stardew Valley 1.6.14 or later
- SMAPI 4.5.0 or later

No local SDK, editor, terminal, dependency installation, or build is required. The repository's GitHub Actions workflow performs compilation and packaging in the cloud.

## Build the mod

The `.github/workflows/build-smapi-mod.yml` workflow builds the mod when relevant source files are pushed to `main`, on pull requests, or when manually dispatched.

The workflow:
1. Checks out the mod source.
2. Checks out the pinned community reference assemblies used for automated builds.
3. Restores the project dependencies.
4. Compiles the SMAPI project in Release mode.
5. Creates and verifies a release ZIP.
6. Uploads the ZIP as a GitHub Actions artifact for 14 days.

The current reference-assembly snapshot is based on Stardew Valley 1.6.15 and SMAPI 4.4.0. This provides a repeatable compile check, but does not replace testing against the installed Stardew Valley and SMAPI versions required by the mod.

## In-game validation (required before release)

1. Load a disposable test save as the host.
2. Open the SMAPI console.
3. Run `spectrum_spawn_test` to request a Green Slime through Stardew's built-in debug command parser.
4. Run `spectrum_spawn_random_test` to exercise the weighted random monster allowlist.
5. Run `spectrum_connection_status` to inspect the gateway connection.
6. Check the compact SMAPI log entries for the redeemer, reward, redemption time, command ID and confirmed spawn result.
7. Confirm a successful redemption appears in Stardew's existing chat with the Twitch username, monster name and location. Failed spawns must not produce a success chat message.
8. Repeat in a multiplayer farm with a farmhand connected; confirm exactly one monster appears for both players.

The network client validates typed messages and queues them for the SMAPI update thread. It does not execute server-supplied command text or monster class names. The local catalogue discovers eligible vanilla monster classes from the game's own assembly and selects the type and tile coordinate. In-game validation should exercise each discovered candidate, including terrain-dependent monsters.

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

## Build artifact and installation

Download the ZIP from the successful **Build Stardew SMAPI Mod** workflow run's Artifacts section. The artifact is a build output, not a public release. Do not publish it to a public mod directory until the private beta and multiplayer tests are complete.

Before any distribution, verify the ZIP on a clean SMAPI installation. It must contain the compiled DLL, `manifest.json`, and required dependencies/assets at its root. It must not contain source-only files, Twitch client secrets, service credentials, or private gateway configuration.

## Secrets and configuration

- Never commit Twitch client secrets, OAuth tokens, signing keys, or production service credentials.
- The game mod must never store Twitch access or refresh tokens.
- The host token is a revocable game-session credential stored only in the local SMAPI config.
- Network callbacks enqueue typed requests; game state changes only on the SMAPI game thread.

## Current limitations

- The SMAPI project has compiled successfully and a ZIP artifact has been produced through GitHub Actions using the pinned community reference assemblies. The artifact has not yet been installed and verified against the target game/SMAPI installation.
- The shared gateway, Control Center pairing/revocation controls and guarded Event Hub routing are committed but production deployment and end-to-end delivery remain unverified.
- Migration 0028 has not been applied to production D1 and dashboard KV assets have not been synchronised.
- Live connection reporting is implemented in the gateway and Control Center API source, but its deployed behaviour remains unverified.
- No in-game-tested ZIP, GitHub Release or public mod release exists yet.
