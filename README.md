# Spectrum Stardew Monster Spawner

A private Spectrum integration that turns Twitch Channel Point redemptions into random vanilla monster encounters in Stardew Valley.

Only Spectrum members explicitly enabled by an administrator in Spectrum Control Center can connect. Downloading or building the mod does not grant access to the live service.

## Intended experience

1. Each enabled Spectrum streamer installs the Spectrum mod through SMAPI.
2. Each player pairs their own game installation with their own Twitch account through Control Center.
3. Control Center verifies the signed-in Twitch identity and administrator-controlled activation record.
4. Event Hub interprets that streamer's redemptions using the approved Control Center configuration.
5. Each mod connects only to its own broadcaster-specific gateway session and receives only that streamer's commands.
6. If the redeemer is a farmhand, their mod forwards the typed command to the host through SMAPI. The host selects and constructs the vanilla monster near that farmer.

No Twitch password, Twitch client secret, or long-lived Twitch token is stored in the mod.

## Spectrum architecture

The existing Spectrum Cloudflare project owns identity, permissions, dashboard management, Twitch authorisation, EventSub and the live gateway.

Twitch Channel Points → Spectrum Event Hub → that streamer's gateway session → their SMAPI mod → (SMAPI relay if farmhand) → host SMAPI mod → Stardew Valley

- Control Center: administrator-only Stardew Valley page, per-member access, live game status, shared reward title, per-host safety gate, streamer preferences, administrator overrides, remote clear-monsters actions and one-shot random test spawns.
- Event Hub: receives and verifies Twitch Channel Point redemption notifications and routes approved redemptions.
- Stardew gateway: owns one authenticated session per enabled broadcaster, versioned per-streamer settings, administrator overrides, deduplication, pending requests and acknowledged remote actions.
- SMAPI mod: each player connects only their own broadcaster session. Farmhands forward typed spawn requests to the host through SMAPI; only the host mutates the shared world and confirms completion back to the originating client.

## Monster spawning

The mod uses an explicit, variant-aware allowlist. Each entry identifies the vanilla monster class, constructor arguments, any required vanilla post-construction setup, and a balancing-only weighting health value. A separate location-pool map assigns each entry to its progression groups, so pool membership has one source of truth. That value is deliberately separate from vanilla monster data and the monster's runtime health. The Dangerous Mines roster includes the regular Lava Bat, Dangerous Copper Slime, Dangerous Iron Slime and Skeleton Mage. Both normal and Dangerous Armored Bug are excluded. All slime families in the approved catalogue remain represented. This lets the catalogue distinguish normal and dangerous Rock Crabs, named Ghost and Serpent variants, Bat subtypes, Skeleton Mage, Stick Bug, Tiger Slime, and Volcano forms.

The spawner creates the selected vanilla monster on a valid nearby tile and adds it to the current location, following Stardew's own monster construction patterns. Dangerous entries receive an additional-difficulty stat step and use the matching `_dangerous` texture when the game provides one. The gateway never supplies a class name or constructor argument.

Newly unlocked monster pools use Stardew's native achievement-style HUD notification. The host checks the local progression snapshot once per second while a save is active; the initial snapshot is a baseline, so already-unlocked pools are not announced again when loading a save. No save flags, database records, or background network work are added.

A redemption that would normally spawn exactly one random-pool monster has a temporary 1-in-100 chance to become a horde of ten different eligible variants. Variants are selected without replacement using the same progression- and difficulty-aware threat weighting as normal spawns, so the group remains broadly aligned with the current challenge level while avoiding duplicates. The event is rolled once per redemption and remains the same if safe placement needs to retry. After all ten monsters are successfully placed, in-game chat displays `Oh no... something is happening.`; three seconds later Stardew's large achievement-style HUD notification announces `USERNAME HAS SPAWNED A HORDE!`. Both messages are shown even when ordinary chat responses are disabled. Existing configured multi-monster counts and specific-monster requests are unchanged. Farmhands with the mod installed receive the same two-stage announcement; vanilla-only clients still see the host-spawned monsters through Stardew's world sync.

The eight location groups are progression gates, not physical spawn restrictions. All unlocked groups contribute to one global pool; duplicate definition IDs are counted once. Random selection targets a threat band across three permanent milestones: before Mines floor 120, after Mines floor 120, and after Skull Cavern floor 100. Combat Level is a secondary adjustment within each stage. The threat score is derived from vanilla `Data/Monsters` health, attack, defence, speed, experience, miss chance and glider behaviour. Missing entries fall back to the catalogue estimate. This changes selection only, not monster stats. See [docs/MONSTER_POOLS.md](docs/MONSTER_POOLS.md) for the full pool list, unlock checks, exclusions and variant mapping.
## Multiplayer

Every Spectrum streamer who wants their own redemptions to trigger spawns pairs their own Twitch account. All players in the shared farm install the mod, but a non-streaming farmhand doesn't need to pair an account. A client never subscribes to every Spectrum channel and never needs another streamer's credentials. The gateway and Event Hub continue routing each interpreted redemption only to that broadcaster's own mod connection.

If the connected player is the host, their mod queues the command locally. If they're a farmhand, their mod forwards the typed command to the host using SMAPI's multiplayer messaging API. The host validates that the message came from a connected player running this mod, resolves that sender to their online farmer, and creates the monster near that farmer in their current location. The host sends a completion response to the originating farmhand, which only then acknowledges the redemption to the gateway. Both sides retain command-ID deduplication so retries don't create duplicate monsters. Control Center clear-monsters and random test-spawn actions received by a farmhand are forwarded to the host through the same SMAPI path and acknowledged after the host completes them.

The host does not need to pair with or listen to other streamers. It can accept forwarded commands even if its own Spectrum connection is not paired; it only pairs if it also wants its own channel's redemptions to trigger spawns. Stardew remains responsible for synchronising the host-created vanilla monster to the other players. This multiplayer path still requires a real two-client runtime test.

## Repository contents

- Gameplay/MonsterSpawner.cs — host-only command dispatch and safe tile selection.
- Gameplay/MonsterCatalog.cs — explicit vanilla monster variants, progression-filtered weighting and safe constructor dispatch.
- Gameplay/MonsterProgression.cs and Gameplay/MonsterLocationPools.cs — vanilla save checks and location-group unlock rules.
- docs/MONSTER_POOLS.md — complete progression rules, exclusions, and variant implementation requirements.
- ModEntry.cs — SMAPI entry point, GMCM settings/actions, local test commands and game-thread command/action processing.
- Networking/GameModsClient.cs — authenticated WebSocket connection, versioned settings sync, typed command/action validation, reconnect and acknowledgements.
- Networking/HostSettings.cs and Networking/GameAction.cs — shared settings and typed remote action contracts.
- ModConfig.cs — versioned local configuration, validated gateway URL, broadcaster ID and revocable host token.
- manifest.json and SpectrumStardewMod.csproj — SMAPI project metadata.
- docs/ — architecture, development and roadmap.

## Current implementation status

Version 0.8.3 passed the user's single-player runtime checks for pairing, settings synchronisation and Twitch redemption spawning. Multiplayer remains untested.

Version 0.8.4 adds an administrator-triggered random test-spawn action from Control Center. It is delivered as a typed one-shot game action, uses the mod's local progression-eligible monster catalogue, targets the requesting farmer in multiplayer, and does not open the normal spawn safety gate. The game reports the result to Control Center.

Version 0.8.5 adds native HUD milestone notifications and the rare ten-monster horde event. Version 0.8.6 changes the temporary horde odds to 1-in-100, selects ten distinct progression-eligible variants using the existing threat weighting, and upgrades the final announcement to the large native HUD notification. Both features are host-authoritative and require fresh in-game validation; CI compilation does not prove runtime or multiplayer behaviour.

## Connect each player in-game

An administrator first enables the Spectrum member's Stardew access in Control Center. Each streamer then connects their own Twitch account:

1. Load the multiplayer save as either host or farmhand.
2. Open **Options → Generic Mod Config Menu → Spectrum Stardew Monster Spawner**.
3. Under **Connection actions**, choose **Connect Twitch / open Control Center**, then press GMCM's **Save** button.
4. Sign in using the Twitch account that owns your active Spectrum channel.
5. Confirm the pairing request. Control Center only authorises the signed-in member's own account, and only if an administrator has enabled Stardew access.
6. Return to Stardew. Your mod receives its own revocable game-session credential securely and writes it to your local SMAPI config automatically.
7. Your mod connects only to your broadcaster's gateway session. Spawning remains stopped until that streamer's Control Center safety gate is explicitly opened.

The GMCM page contains the same streamer settings as the former Spectrum panel: spawning, difficulty, chat responses and debug messages. Change settings and press **Save** to send them to the gateway. Connection and host actions are explicit dropdown selections followed by Save. Clearing loaded monsters requires selecting **Arm clear**, saving, then selecting **Confirm clear** and saving again. Choose **No action** and save before repeating an action.

Pairing codes expire after ten minutes. The private device code is separate from the visible approval code and is never shown in the dashboard. The game-session credential is not displayed to the streamer or sent to browser JavaScript. Revoke access in Control Center before moving your connection to another Twitch account.

The local config carries a schema version and is migrated automatically on first launch after an update. Gateway URLs must use HTTPS, and incomplete or malformed saved credentials are cleared so the host can be paired again safely. The status section explains whether spawning is stopped by the global safety gate, the administrator override, the streamer's preference, a revoked connection, or a gateway interruption. Temporary gateway interruptions retry automatically; revoked or rotated credentials stop retrying and require a fresh Twitch pairing. Never commit or share the local config or host token.

## In-game test commands

After installing the GitHub Actions ZIP through SMAPI, enter these commands in the SMAPI in-game console:

    spectrum_spawn_test
    spectrum_spawn_random_test
    spectrum_spawn_horde_test

The first command requests a Green Slime. The second selects from the progression-eligible weighted random pool. The pool-status command reports unlocked progression groups, selectable variant definitions, and any definitions still missing constructor/setup support. All are host-only.

Developer validation commands:
- `spectrum_progression_test <early|mines|skull100|danger-mines|quarry|skull-cavern|danger-skull|bug-lair|volcano|combat-0|combat-5|combat-10|shrine-0|shrine-1|shrine-2|all|reset>` temporarily simulates progression in memory only; it never edits the save. To preview a HUD unlock notice, apply `early`, wait for the next progression check, then apply an individual pool profile such as `skull-cavern` or `volcano`. Use `reset` to restore real save checks.
- `spectrum_spawn_horde_test` forces the ten-monster horde spawn and its two chat messages near the host. Use an open area with enough clear tiles; it does not consume a Twitch redemption or change the 1-in-1,000 live chance.
- `spectrum_spawn_monster_force_test <monster name or ID>` bypasses progression for a local test spawn only; catalogue exclusions remain enforced.
- `spectrum_monster_catalog_test` executes every supported constructor and special setup without adding monsters to the world.
- `spectrum_spawn_random_test [easy|normal|hard]` exercises the three selection difficulty modes.

The integration writes compact redemption and spawn results to the SMAPI console, retaining each command ID for deduplication and tracing. Successful normal spawns are announced through Stardew's existing in-game chat with the Twitch redeemer, monster type and location. Horde spawns use their two-stage rare-event announcement instead, and only after all ten monsters have been added successfully.

The integration currently accepts one Twitch redemption behaviour: **Spawn Random Monster**. Control Center also supports a separate administrator-only random test-spawn action for validating the dashboard-to-game connection. The mod chooses one progression-eligible vanilla variant locally from the explicit catalogue and constructs it using its supported vanilla setup. Spawn requests remain queued until Start Spawns is enabled in Control Center; each new connection defaults to stopped. Failed attempts remain pending and are retried rather than acknowledged.

## Security

- Only approved, active Spectrum members with an explicit enabled record can pair.
- A valid Twitch login alone does not grant access.
- Never ship service credentials or Twitch tokens in the mod ZIP.
- Use revocable game-session credentials, one-time game-session tokens, redemption IDs, expiry, deduplication and spawn caps.
- Network callbacks must enqueue typed requests; game state is changed only on the SMAPI game thread.
- Never accept arbitrary C# types, commands, or executable content from the service.

## License

Private Spectrum project. Redistribution and service access are not authorised.


## In-game menu access

Spectrum's streamer settings and actions are registered directly in the vanilla Options menu through Generic Mod Config Menu (GMCM). Open **Options → Generic Mod Config Menu → Spectrum Stardew Monster Spawner**. There is no separate Spectrum launch panel or floating HUD button. GMCM is a required dependency and is included in the mod manifest.
