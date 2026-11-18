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
3. Runs static roster and pool validation before compiling.
4. Restores the project dependencies.
5. Compiles the SMAPI project in Release mode.
6. Creates and verifies a release ZIP.
7. Uploads the ZIP as a GitHub Actions artifact for 14 days.

The current reference-assembly snapshot is based on Stardew Valley 1.6.15 and SMAPI 4.4.0. This provides a repeatable compile check, but does not replace testing against the installed Stardew Valley and SMAPI versions required by the mod.

## In-game validation (required before release)

1. Load a disposable test save as the host.
2. Open the SMAPI console.
3. Run `spectrum_spawn_test` to request a Green Slime through its explicit vanilla constructor definition.
4. Run `spectrum_spawn_random_test` to exercise the weighted random monster allowlist.
5. Run `spectrum_monster_catalog_test` to execute every supported monster constructor and post-construction setup without adding the monsters to the world. This is a constructor check, not a substitute for in-world appearance tests.
6. Run `spectrum_progression_test early`, then `spectrum_monster_pool_status`, to confirm only Mines and Farm are unlocked. Repeat with `mines`, `skull100`, `danger-mines`, `quarry`, `skull-cavern`, `danger-skull`, `bug-lair`, `volcano`, and `all`; confirm each profile exposes only its intended pools and stage. Use `combat-0`, `combat-5`, `combat-10`, and `shrine-0`, `shrine-1`, `shrine-2` to exercise the secondary weighting inputs without changing the farmer's actual Combat Level or mine difficulty. Finish with `spectrum_progression_test reset` to restore the real save.
7. Use `spectrum_spawn_monster_force_test <monster name or ID>` to spawn a progression-locked entry for a local constructor/appearance check. This bypasses progression only; excluded entries and unsupported constructors remain blocked. The gateway path never uses this override.
8. Run `spectrum_spawn_random_test easy`, `spectrum_spawn_random_test normal`, and `spectrum_spawn_random_test hard` under early, Mines-complete and Skull-Cavern-100 profiles to exercise the threat weighting modes.
9. Run `spectrum_monster_pool_status` again after `spectrum_progression_test reset` to confirm the real save's progression is restored.
10. Run `spectrum_connection_status` to inspect the gateway connection.
11. Check the compact SMAPI log entries for the redeemer, reward, redemption time, command ID and confirmed spawn result. Failed attempts stay queued for retry; only successful spawns are acknowledged. The latest 500 successful redemption IDs are also saved in the mod's local global data to prevent normal reconnect/restart replay.
12. Confirm a successful redemption appears in Stardew's existing chat with the Twitch username, monster name and location. Failed spawns must not produce a success chat message.
13. Repeat in a multiplayer farm with a farmhand connected; confirm exactly one monster appears for both players.

The network client validates typed messages and queues them for the SMAPI update thread. It does not execute server-supplied command text or monster class names. The local catalogue filters explicit vanilla variant definitions through the mod installation's progression snapshot. See docs/MONSTER_POOLS.md for unlock rules, constructor mappings and dangerous-variant setup. Every entry still needs in-game validation.

Back up saves before early gameplay testing.

## Pair the host

After an administrator enables the member in Control Center, use the Mods page's **Pair / re-pair** action. It displays the broadcaster's numeric Twitch user ID and a one-time host token.

1. Launch the mod once so SMAPI creates its local `config.json`.
2. Open the generated config file in the mod folder.
3. Set `BroadcasterUserId` to the numeric ID shown by Control Center.
4. In the Stardew Valley dashboard, set the one shared **Spawn Random Monster** reward title used on every channel.
5. Set `HostToken` to the one-time token shown by Control Center.
6. Leave `GatewayUrl` at its default Spectrum Game Mods URL.
7. Restart Stardew/SMAPI.
8. Once `spectrum_connection_status` confirms the host is connected, click **Start Spawns** in Control Center. Every fresh connection defaults to stopped.

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

- Version 0.7.3 was installed and tested on Stardew Valley 1.6.15 / SMAPI 4.5.2. Startup, gateway connection, catalogue validation, basic/random spawning, and the Twitch Channel Point flow passed. Version 0.7.4 adds the developer test harness and threat-data alias corrections; its compiled artifact still needs the consolidated in-game test.
- The shared gateway, Control Center pairing/revocation controls and guarded Event Hub routing are committed but production deployment and end-to-end delivery remain unverified.
- Migration 0028 has not been applied to production D1 and dashboard KV assets have not been synchronised.
- Live connection reporting is implemented in the gateway and Control Center API source, but its deployed behaviour remains unverified.
- No final in-game-tested 0.7.4 ZIP, GitHub Release or public mod release exists yet. Multiplayer replication and redemption retry/reconnect edge cases remain unverified.
