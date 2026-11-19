# Development and release validation

## Product boundary

This repository contains the SMAPI game client and its release packaging. Spectrum Control Center, Event Hub and the shared Game Mods gateway live in the `spectrum-cloudflare` repository. The integration is restricted to active Spectrum members explicitly enabled by a Control Center administrator.

## Supported environment

- Stardew Valley 1.6.15 (the version used for current in-game validation)
- SMAPI 4.5.0 or later (tested with SMAPI 4.5.2)
- Generic Mod Config Menu 1.16.0 or later

The mod uses the standard SMAPI `Mods/SpectrumStardewMod` folder. GMCM is a required dependency and is declared in `manifest.json`.

## Cloud build and package

All compilation and packaging must run through GitHub Actions; do not ask contributors to build locally.

The `.github/workflows/build-smapi-mod.yml` workflow:
1. Checks out the mod source and pinned Stardew reference assemblies.
2. Validates monster catalogue IDs, progression-pool assignments and exclusions.
3. Syntax-checks the bundled StreamElements JavaScript and parses its Fields JSON.
4. Restores the .NET project.
5. Compiles the SMAPI project in Release mode.
6. Creates a ready-to-install ZIP.
7. Verifies the ZIP contains `SpectrumStardewMod.dll` and `manifest.json`, and excludes a local `config.json`.
8. Bundles the installable mod ZIP with the StreamElements widget source and setup instructions.
9. Uploads the mod-only and complete-bundle ZIPs as a 14-day GitHub Actions artifact.

The pinned reference-assembly snapshot is Stardew Valley 1.6.15 with SMAPI 4.4.0. This is a repeatable compile check against the pinned assembly set; the mod manifest requires SMAPI 4.5.0 or later, and the current runtime test environment is SMAPI 4.5.2. CI success does not replace testing the produced ZIP in the actual game.

## Install and pair

1. Install SMAPI and Generic Mod Config Menu.
2. Download the ZIP from the successful **Build Stardew SMAPI Mod** workflow run.
3. Extract the ZIP contents into `Stardew Valley/Mods/SpectrumStardewMod`.
4. Launch Stardew Valley and confirm SMAPI loads **Spectrum Stardew Monster Spawner 1.0.0** without an error.
5. Open **Options → Generic Mod Config Menu → Spectrum Stardew Monster Spawner**.
6. An administrator must first enable the member in Control Center.
7. Under **Connection**, click **Link Twitch ↗**. Pairing starts immediately and the browser opens; no GMCM Save click is required.
8. Approve the request while signed into the Twitch account that owns the enabled Spectrum channel.
9. Return to Stardew and confirm `spectrum_connection_status` reports the correct broadcaster connection.
10. In **Monster controls**, click **Enable Monster Spawning**. Every fresh connection starts paused; the in-game toggle opens the gateway gate. Administrator Force disabled and access revocation still take priority.

Difficulty, chat responses, debug messages and challenge-series settings still require GMCM's **Save** button. **Clear Enemies** is a single-click host-only action and immediately clears hostile monsters from loaded locations.

If the browser does not open automatically, use the in-game status message and open Spectrum Control Center manually. Never copy, share or commit the local SMAPI config or host token.

## In-game smoke tests

Use a disposable save for destructive tests. Run the following from the SMAPI console where applicable:

- `spectrum_connection_status` — verify the broadcaster-scoped gateway session.
- `spectrum_spawn_test` — verify the local Green Slime test path.
- `spectrum_spawn_random_test [easy|normal|hard]` — exercise progression-aware random selection.
- `spectrum_monster_catalog_test` — execute every supported constructor/setup path without adding monsters to the world.
- `spectrum_monster_pool_status` — inspect the progression-unlocked candidate pools.
- `spectrum_spawn_horde_test` — force the ten-monster horde event in a safe open area; this does not consume a redemption or change the live 1-in-100 chance.
- `spectrum_progression_test <profile>` — simulate a progression profile in memory for HUD and pool testing; finish with `spectrum_progression_test reset`.

Final release smoke-test sequence:
1. Install the ZIP into a clean SMAPI Mods folder with GMCM installed.
2. Confirm SMAPI loads the mod and reports version 1.0.0.
3. Pair a streamer through the in-game connection action and confirm the browser approval flow.
4. Confirm the mod stores its revocable game-session credential locally and reconnects to its own broadcaster session.
5. Redeem the configured Twitch reward with the game connected and spawning enabled; confirm one eligible monster appears near the redeeming farmer and the command is acknowledged only after success.
6. Repeat from a farmhand account; confirm the farmhand forwards the typed command and the host creates the shared-world monster near that farmer.
7. Use Control Center Test Spawn for a selected connected host and confirm the in-game action log and monster spawn.
8. Use Control Center Kill All and confirm the host clears loaded hostile monsters and the action reports completion.
9. Verify the global safety gate remains stopped on a fresh connection and that streamer preference/admin override cannot bypass the gate.
10. Verify reconnect/re-pair and access revocation do not replay completed redemptions or leave an old host credential usable.

The user has already confirmed live Twitch redemption spawning, multiplayer host/farmhand spawning, and the Control Center Test Spawn action with the current mod. The remaining items above are release-gate checks, not claims that all test cases have passed.

## Security and operational invariants

- Never ship Twitch client secrets, OAuth tokens, signing keys, service credentials, or a local `config.json` in the mod ZIP.
- The mod never stores Twitch access or refresh tokens.
- The host token is a revocable broadcaster-scoped game-session credential.
- Only explicitly enabled Spectrum members can pair.
- Network callbacks enqueue typed requests; game state changes only on the SMAPI game thread.
- Never accept arbitrary C# types, commands or executable content from the service.
- Keep the gateway spawn gate closed on fresh connections and disconnects; only the authenticated current host can open it from GMCM, and administrator Force disabled remains an independent hard stop.
- Keep generated build outputs out of source control.

## Current release status

The manifest is now **2.1.0** for the development build. The original 1.0.0 gameplay integration has prior live testing; the new challenge counter and combined overlay still require the 2.0.0 smoke tests above. Do not distribute this development build as a final release until those checks pass.


## 2.0.0 challenge and overlay smoke tests

- Confirm the existing config migrates to schema version 2 without clearing broadcaster ID or host token.
- In GMCM, set a challenge name and a known death count; save and confirm the local config updates.
- Trigger one normal in-game player death and verify exactly one increment appears in the local config and overlay.
- Verify the counter does not increment repeatedly during the same death/respawn sequence.
- Return to title, load a different save, and confirm the challenge total is unchanged.
- Restart Stardew and confirm the challenge name and total remain.
- Manually change the total in GMCM while the overlay is connected and confirm the display updates.
- Disconnect/reconnect the game client and reload the overlay; confirm the state snapshot restores the latest total.
- Trigger a Twitch spawn redemption and confirm the wheel starts at request delivery and reveals only the game-confirmed monster result.
- Test the counter and spawn animation together in StreamElements and OBS.
- Confirm the widget makes no polling requests and uses no StreamElements persistent storage.

The death counter is per paired game installation and tracks that installation's local player. If the streamer pairs a different installation, that installation has its own local challenge state unless the streamer manually enters the same total.
