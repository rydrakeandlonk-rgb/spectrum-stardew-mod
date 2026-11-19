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
13. Repeat in a multiplayer farm with at least one farmhand connected and paired to their own Twitch account. Confirm each client receives only its own channel's commands.
14. Redeem on the host's channel and confirm the host spawns one monster near the host farmer.
15. Redeem on the farmhand's channel and confirm the farmhand forwards one command, the host spawns one monster near the farmhand farmer, and both players see the same monster.
16. Put the host and farmhand in different locations and confirm the monster appears near the redeeming farmer in that farmer's current location.
17. Confirm the farmhand gateway command is acknowledged only after the host reports a successful spawn. Force a retry/reconnect and confirm no duplicate monster appears.
18. Confirm the farmhand can receive commands when the host has not paired their own Twitch account, and confirm a closed source-streamer safety gate prevents that streamer's commands from spawning.
19. Trigger a Control Center clear-loaded-monsters action for the farmhand's broadcaster and confirm the farmhand forwards it to the host, the host clears loaded locations, and the gateway action is acknowledged only after completion.

The network client validates typed messages and queues them for the SMAPI update thread. It does not execute server-supplied command text or monster class names. The local catalogue filters explicit vanilla variant definitions through the mod installation's progression snapshot. See docs/MONSTER_POOLS.md for unlock rules, constructor mappings and dangerous-variant setup. Every entry still needs in-game validation.

Back up saves before early gameplay testing.

## Pair each player

After an administrator enables each Spectrum member in Control Center, each player pairs their own Twitch account through the GMCM **Connection actions** selector. Each installation receives a separate broadcaster-scoped game-session credential.

1. Load the multiplayer save as host or farmhand.
2. Open **Options → Generic Mod Config Menu → Spectrum Stardew Monster Spawner**.
3. Under **Connection actions**, choose **Connect Twitch / open Control Center**, then press GMCM's **Save** button. Approve the pairing while signed into your own Twitch account.
4. Return to Stardew and confirm `spectrum_connection_status` reports your broadcaster connection.
5. Set your own streamer preference in the in-game panel or Control Center. Every fresh connection defaults to stopped.

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

- Version 0.7.5 is the last runtime-validated build. It passed the user's single-player tests for catalogue construction, progression selection, Twitch redemption handling, reconnect safety and retry/acknowledgement behaviour. Multiplayer remains untested.
- Version 0.8.2 adds independent per-player gateway connections and the typed farmhand-to-host SMAPI command relay. It must pass GitHub Actions compilation/package checks and two-client runtime tests before it is considered ready.
- The in-game Connect button starts a ten-minute device-code flow. The streamer approves their own Twitch identity in Control Center; the mod then receives and stores its revocable host credential automatically. This self-service flow still needs end-to-end testing.
- The new in-game menu, host-wide clear action, settings conflict handling and Control Center remote controls still require in-game testing.
- Multiplayer relay, host/farmhand spawn targeting, retry/acknowledgement behaviour, and Stardew monster replication must be verified with two actual game clients. A successful CI build does not prove runtime or multiplayer behaviour.
- No public mod release is planned. Build ZIPs remain private GitHub Actions artifacts.


## Vanilla Options menu integration

Spectrum registers its streamer settings and actions directly in Generic Mod Config Menu. The page uses native GMCM checkboxes and dropdowns; settings are sent when GMCM's Save button is pressed. Pairing is selected under Connection actions. Clearing loaded monsters is a host-only two-step action (Arm clear, then Confirm clear), each followed by Save. There is no separate launch button or floating HUD control. Verify the controls in-game with GMCM 1.16.0 before treating them as runtime-validated.
