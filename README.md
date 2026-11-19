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

- Control Center: administrator-only Stardew Valley page, per-member access, live game status, shared reward title, per-host safety gate, streamer preferences, administrator overrides and remote clear-monsters actions.
- Event Hub: receives and verifies Twitch Channel Point redemption notifications and routes approved redemptions.
- Stardew gateway: owns authenticated host sessions, versioned settings, administrator overrides, deduplication, pending requests and acknowledged remote actions.
- SMAPI mod: provides a clickable in-game Spectrum menu, synchronises streamer settings, processes host-only world actions, chooses and constructs vanilla monsters on the game thread, and acknowledges completed actions.

## Monster spawning

The mod uses an explicit, variant-aware allowlist. Each entry identifies the vanilla monster class, constructor arguments, any required vanilla post-construction setup, and a balancing-only weighting health value. A separate location-pool map assigns each entry to its progression groups, so pool membership has one source of truth. That value is deliberately separate from vanilla monster data and the monster's runtime health. The Dangerous Mines roster includes the regular Lava Bat, Dangerous Copper Slime, Dangerous Iron Slime and Skeleton Mage. Both normal and Dangerous Armored Bug are excluded. All slime families in the approved catalogue remain represented. This lets the catalogue distinguish normal and dangerous Rock Crabs, named Ghost and Serpent variants, Bat subtypes, Skeleton Mage, Stick Bug, Tiger Slime, and Volcano forms.

The spawner creates the selected vanilla monster on a valid nearby tile and adds it to the current location, following Stardew's own monster construction patterns. Dangerous entries receive an additional-difficulty stat step and use the matching `_dangerous` texture when the game provides one. The gateway never supplies a class name or constructor argument.

The eight location groups are progression gates, not physical spawn restrictions. All unlocked groups contribute to one global pool; duplicate definition IDs are counted once. Random selection targets a threat band across three permanent milestones: before Mines floor 120, after Mines floor 120, and after Skull Cavern floor 100. Combat Level is a secondary adjustment within each stage. The threat score is derived from vanilla `Data/Monsters` health, attack, defence, speed, experience, miss chance and glider behaviour. Missing entries fall back to the catalogue estimate. This changes selection only, not monster stats. See [docs/MONSTER_POOLS.md](docs/MONSTER_POOLS.md) for the full pool list, unlock checks, exclusions and variant mapping.
## Multiplayer

The farm host is authoritative. Only the host's mod connection processes external redemption requests and creates monsters. Farmhands do not need separate Twitch connections. Monster replication must be verified in a real host/farmhand test.

## Repository contents

- Gameplay/MonsterSpawner.cs — host-only command dispatch and safe tile selection.
- Gameplay/MonsterCatalog.cs — explicit vanilla monster variants, progression-filtered weighting and safe constructor dispatch.
- Gameplay/MonsterProgression.cs and Gameplay/MonsterLocationPools.cs — vanilla save checks and location-group unlock rules.
- docs/MONSTER_POOLS.md — complete progression rules, exclusions, and variant implementation requirements.
- ModEntry.cs — SMAPI entry point, clickable HUD button, local test commands and game-thread command/action processing.
- UI/SpectrumMenu.cs — in-game streamer settings, connection status, host-only controls and clear confirmation.
- Networking/GameModsClient.cs — authenticated WebSocket connection, versioned settings sync, typed command/action validation, reconnect and acknowledgements.
- Networking/HostSettings.cs and Networking/GameAction.cs — shared settings and typed remote action contracts.
- ModConfig.cs — versioned local configuration, validated gateway URL, broadcaster ID and revocable host token.
- manifest.json and SpectrumStardewMod.csproj — SMAPI project metadata.
- docs/ — architecture, development and roadmap.

## Current implementation status

Version 0.7.5 is the last runtime-validated version. Its monster catalogue, progression profiles, reconnect safety, Twitch redemption handling and retry/acknowledgement behaviour passed the user's single-player checks. Multiplayer remains untested.

Version 0.8.0 is the current development target. It adds a clickable HUD entry point and dedicated Spectrum menu; streamer preference, difficulty, chat-response and debug settings; host-only in-game controls; host-wide clearing of loaded hostile monsters; versioned settings synchronisation; administrator override modes; and Control Center remote actions. The gateway stores these settings in the existing per-host Durable Object rather than adding D1/KV writes. This development version still needs a successful CI build and in-game validation before installation. The in-game Connect button currently opens Control Center; the existing one-time host pairing/config setup is still required.

## Connect the host in-game

An administrator first enables the Spectrum member's Stardew access in Control Center. The streamer can then connect without manually copying a host token:

1. Load a Stardew save as the multiplayer host.
2. Click the **Spectrum** HUD button and choose **Connect Twitch account**.
3. The mod creates a short-lived pairing code and opens the Control Center approval page.
4. Sign in using the Twitch account that owns the active Spectrum channel.
5. Confirm the pairing request. Control Center only authorises the signed-in member's own account, and only if an administrator has enabled Stardew access.
6. Return to Stardew. The mod receives its revocable host credential securely and writes it to its local SMAPI config automatically.
7. The host connects to the gateway. Spawning remains stopped until the Control Center global safety gate is explicitly opened.

Pairing codes expire after ten minutes. The private device code is separate from the visible approval code and is never shown in the dashboard. The host credential is not displayed to the streamer or sent to browser JavaScript. Revoke access in Control Center before moving the connection to another Twitch account.

The local config carries a schema version and is migrated automatically on first launch after an update. Gateway URLs must use HTTPS, and incomplete or malformed saved credentials are cleared so the host can be paired again safely. The in-game menu and `spectrum_connection_status` explain whether spawning is stopped by the global safety gate, the administrator override, the streamer's preference, a revoked connection, or a gateway interruption. Temporary gateway interruptions retry automatically; revoked or rotated credentials stop retrying and require a fresh Twitch pairing. The older administrator **Pair / re-pair** action and manual config setup remain available as a fallback. Never commit or share the local config or host token.
 
## In-game test commands

After installing the GitHub Actions ZIP through SMAPI, enter these commands in the SMAPI in-game console:

    spectrum_spawn_test
    spectrum_spawn_random_test

The first command requests a Green Slime. The second selects from the progression-eligible weighted random pool. The pool-status command reports unlocked progression groups, selectable variant definitions, and any definitions still missing constructor/setup support. All are host-only.

Developer validation commands:
- `spectrum_progression_test <early|mines|skull100|danger-mines|quarry|skull-cavern|danger-skull|bug-lair|volcano|combat-0|combat-5|combat-10|shrine-0|shrine-1|shrine-2|all|reset>` temporarily simulates progression in memory only; it never edits the save. Use `reset` to restore real save checks.
- `spectrum_spawn_monster_force_test <monster name or ID>` bypasses progression for a local test spawn only; catalogue exclusions remain enforced.
- `spectrum_monster_catalog_test` executes every supported constructor and special setup without adding monsters to the world.
- `spectrum_spawn_random_test [easy|normal|hard]` exercises the three selection difficulty modes.

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
