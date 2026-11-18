# Spectrum Stardew Monster Spawner

A private Spectrum integration that turns Twitch Channel Point redemptions into random vanilla monster encounters in Stardew Valley.

Only Spectrum members explicitly enabled by an administrator in Spectrum Control Center can connect. Downloading or building the mod does not grant access to the live service.

## Intended experience

1. An enabled Spectrum streamer installs the Spectrum mod through SMAPI.
2. They launch Stardew Valley and pair the host game with Spectrum Control Center.
3. Control Center verifies the signed-in Twitch identity and administrator-controlled activation record.
4. An administrator sets the shared **Spawn Random Monster** Channel Point reward title in Control Center.
5. The host game connects to the Spectrum gateway and receives typed random-monster spawn requests.
6. The host selects an explicit monster variant from the local allowlist and constructs it using its vanilla game constructor.

No Twitch password, Twitch client secret, or long-lived Twitch token is stored in the mod.

## Spectrum architecture

The existing Spectrum Cloudflare project owns identity, permissions, dashboard management, Twitch authorisation, EventSub and the live gateway.

Twitch Channel Points → Spectrum Event Hub → Stardew gateway → host SMAPI mod → Stardew Valley

- Control Center: administrator-only Stardew Valley page, per-member Mod access, live game connection, shared reward title, pending queue count and per-host Start/Stop Spawns control.
- Event Hub: receives and verifies Twitch Channel Point redemption notifications and routes approved redemptions.
- Stardew gateway: owns short-lived game pairing, authenticated host connections, deduplication and request delivery.
- SMAPI mod: runs in the host game, chooses and constructs a random vanilla monster variant from its explicit allowlist on the game thread, saves the latest 500 successful redemption IDs in mod-global data, and acknowledges only after that history is persisted.

## Monster spawning

The mod uses an explicit, variant-aware allowlist. Each entry identifies the vanilla monster class, constructor arguments, any required vanilla post-construction setup, and a balancing-only weighting health value. A separate location-pool map assigns each entry to its progression groups, so pool membership has one source of truth. That value is deliberately separate from vanilla monster data and the monster's runtime health. The Dangerous Mines roster includes the regular Lava Bat, Dangerous Copper Slime, Dangerous Iron Slime and Skeleton Mage. Dangerous Armored Bug is included in Dangerous Skull Cavern; only the normal Armored Bug is excluded. All slime families in the approved catalogue remain represented. This lets the catalogue distinguish normal and dangerous Rock Crabs, named Ghost and Serpent variants, Bat subtypes, Skeleton Mage, Stick Bug, Tiger Slime, and Volcano forms.

The spawner creates the selected vanilla monster on a valid nearby tile and adds it to the current location, following Stardew's own monster construction patterns. Dangerous entries receive an additional-difficulty stat step and use the matching `_dangerous` texture when the game provides one. The gateway never supplies a class name or constructor argument.

The eight location groups are progression gates, not physical spawn restrictions. All unlocked groups contribute to one global pool; duplicate definition IDs are counted once. Random selection targets a threat band across three permanent milestones: before Mines floor 120, after Mines floor 120, and after Skull Cavern floor 100. Combat Level is a secondary adjustment within each stage. The threat score is derived from vanilla `Data/Monsters` health, attack, defence, speed, experience, miss chance and glider behaviour. Missing entries fall back to the catalogue estimate. This changes selection only, not monster stats. See [docs/MONSTER_POOLS.md](docs/MONSTER_POOLS.md) for the full pool list, unlock checks, exclusions and variant mapping.
## Multiplayer

The farm host is authoritative. Only the host's mod connection processes external redemption requests and creates monsters. Farmhands do not need separate Twitch connections. Monster replication must be verified in a real host/farmhand test.

## Repository contents

- Gameplay/MonsterSpawner.cs — host-only command dispatch and safe tile selection.
- Gameplay/MonsterCatalog.cs — explicit vanilla monster variants, progression-filtered weighting and safe constructor dispatch.
- Gameplay/MonsterProgression.cs and Gameplay/MonsterLocationPools.cs — vanilla save checks and location-group unlock rules.
- docs/MONSTER_POOLS.md — complete progression rules, exclusions, and variant implementation requirements.
- ModEntry.cs — SMAPI entry point, local test commands and game-thread command processing.
- Networking/GameModsClient.cs — authenticated WebSocket connection, typed command validation, reconnect and acknowledgements.
- ModConfig.cs — local broadcaster ID and revocable host token.
- manifest.json and SpectrumStardewMod.csproj — SMAPI project metadata.
- docs/ — architecture, development and roadmap.

## Current implementation status

The gameplay path now uses explicit vanilla monster variant definitions and their supported constructors instead of depending on the debug-command parser's narrower argument support. GitHub Actions now successfully compiles the SMAPI project and creates a ZIP artifact using the pinned community reference assemblies. This is a compile/package check only; the mod has not yet been tested in-game or in multiplayer.

The Control Center repository contains the admin-only Mods page, per-member activation API, pairing/revocation controls, one shared reward-title setting, and per-host spawn pause/resume controls. The Game Mods gateway keeps pending redemptions in its Durable Object until successful acknowledgement. Production deployment and live end-to-end validation remain separate from the successful source build.

## Pair the host

An administrator enables the member in Control Center, then uses **Pair / re-pair** on the Mods page. The dashboard displays the broadcaster's numeric Twitch user ID and a one-time host token.

1. Launch the mod once so SMAPI creates its local config file.
2. Open the generated `config.json` in the mod folder.
3. Set `BroadcasterUserId` to the numeric ID shown by Control Center.
4. In the Stardew Valley page, set the shared **Spawn Random Monster** Channel Point reward title. Use the same title on each Spectrum channel's Twitch reward. No Twitch reward ID needs to be copied.
5. Set `HostToken` to the one-time token shown by Control Center.
6. Leave `GatewayUrl` at its default value and restart Stardew Valley.
7. Once the host appears as Connected, use **Start Spawns** in the dashboard when you want queued redemptions to begin processing. New game connections always start paused.

The host token is stored locally as a revocable game-session credential. Never commit or share the local config. Re-pairing rotates the token and closes the previous host connection. Use `spectrum_connection_status` in the SMAPI console to check the connection.

## In-game test commands

After installing the GitHub Actions ZIP through SMAPI, enter these commands in the SMAPI in-game console:

    spectrum_spawn_test
    spectrum_spawn_random_test

The first command requests a Green Slime. The second selects from the progression-eligible weighted random pool. The pool-status command reports unlocked progression groups, selectable variant definitions, and any definitions still missing constructor/setup support. All are host-only.

The integration writes compact redemption and spawn results to the SMAPI console, retaining each command ID for deduplication and tracing. Successful spawns are announced through Stardew's existing in-game chat with the Twitch redeemer, monster type and location. The announcement is only shown after the game reports that a character was added.

The integration currently accepts one typed behaviour: **Spawn Random Monster**. The mod chooses one progression-eligible vanilla variant locally from the explicit catalogue and constructs it using its supported vanilla setup. Spawn requests remain queued until Start Spawns is enabled in Control Center; each new connection defaults to stopped. Failed attempts remain pending and are retried rather than acknowledged.

## Security

- Only approved, active Spectrum members with an explicit enabled record can pair.
- A valid Twitch login alone does not grant access.
- Never ship service credentials or Twitch tokens in the mod ZIP.
- Use revocable game-session credentials, one-time host tokens, redemption IDs, expiry, deduplication and spawn caps.
- Network callbacks must enqueue typed requests; game state is changed only on the SMAPI game thread.
- Never accept arbitrary C# types, commands, or executable content from the service.

## License

Private Spectrum project. Redistribution and service access are not authorised.
