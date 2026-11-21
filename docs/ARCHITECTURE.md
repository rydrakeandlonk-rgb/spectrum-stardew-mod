# Architecture

## Product boundary

This is a private Spectrum integration, not a public self-service service. Only active Spectrum members explicitly enabled by a Control Center administrator may pair a game. The existing Spectrum Cloudflare repository owns the management plane and live service; this repository owns the SMAPI game client.

## High-level flow

Control Center admin enables each member → each player pairs their own Twitch account → one gateway session per broadcaster

Twitch Channel Point redemption → Event Hub → that broadcaster's gateway session → that player's SMAPI mod → (SMAPI relay if farmhand) → host SMAPI mod → Stardew Valley

## Component responsibilities

### Spectrum Control Center

- Administrator-only Mods page.
- Reads the active Spectrum member roster.
- Enables/disables Stardew Monster Spawner per member.
- Shows activation, pairing and live connection state; the connected-host status has been exercised in the live Spectrum dashboard.
- Owns the canonical Twitch identity and authorisation.
- Provides one shared Twitch Channel Point reward title and per-member Start/Stop Spawns controls.

### Spectrum Event Hub

- Uses the existing central EventSub ingress and signing secret.
- Must subscribe to channel.channel_points_custom_reward_redemption.add for enabled Spectrum broadcasters with the required central Twitch scope.
- Validates EventSub signatures and broadcaster identity.
- Routes only redemptions for active, enabled Stardew members.
- Deduplicates by Twitch EventSub message ID and redemption ID.
- Does not perform Stardew gameplay or choose monster classes.

The central Control Center OAuth scope list already includes channel:read:redemptions; each affected broadcaster must have granted it in their stored Twitch authorisation. The live Twitch redemption route has been exercised end-to-end with the current mod; new broadcaster accounts still need the required Twitch redemption scope.

### Stardew gateway

A dedicated service in the Spectrum Cloudflare repository.

- Creates one-time pairing codes for each player's SMAPI installation.
- Completes pairing only after Control Center confirms the active Spectrum member and Stardew mod activation.
- Issues a revocable, broadcaster-scoped game-session credential.
- Maintains one persistent outbound connection per broadcaster's existing Durable Object session.
- Accepts authenticated, typed redemption requests from Event Hub.
- Stores live connection, global safety-gate state, versioned streamer settings, administrator override and pending-command/action state in the per-broadcaster Durable Object; new connections and disconnects default the safety gate to stopped. The dashboard can pause or resume delivery without deleting queued redemptions.
- Computes effective spawning from broadcaster authorisation, connection state, that broadcaster's global safety gate, streamer preference and administrator override. Force enabled/disabled only overrides the streamer preference; neither can bypass the safety gate or revoked access.
- Sends typed remote actions to the host and records acknowledgements, including loaded-monster clearing and a one-shot random test spawn. The test spawn uses the mod's local progression-eligible catalogue and never changes the normal safety gate.
- Enforces request expiry, deduplication, reconnect handling and rate limits.
- Never accepts a client-supplied monster type; the game client chooses from its local allowlist.

### SMAPI mod

- Runs on every participating player's Stardew installation.
- Each player uses the one-time credential generated for their own Twitch identity, stored in their local SMAPI config.
- Each installation maintains only its own authenticated broadcaster-scoped gateway connection.
- Provides a clickable HUD button and dedicated Spectrum menu for connection status, streamer spawn preference, difficulty, chat responses, debug messages and local clear confirmation.
- Synchronises streamer settings with the per-host Durable Object using revision checks; administrator updates are reflected back into the game menu.
- Processes typed commands from its own gateway session. A host queues its own commands locally; a farmhand forwards its commands to the host using SMAPI multiplayer messages.
- The host validates the sending peer, resolves the sender's online Farmer, and processes the command on the SMAPI game thread. The host is the only client allowed to mutate the shared world.
- Uses an explicit allowlist of vanilla monster variants, constructor arguments and post-construction setup; progression filters the host save's candidate pool.
- Constructs the selected vanilla Monster through its known constructor, applies required variant setup and dangerous-mode treatment, then adds it to the redeeming farmer's current location on a valid tile.
- Confirms the location's character count increases before reporting success. For farmhand-originated commands, the host sends a completion response to that farmhand; the farmhand acknowledges the gateway only after the host confirms success.
- Each player's settings, safety gate and gateway connection remain independent. The host does not subscribe to other streamers' channels.
- The paired installer's local player death count and challenge name live in its local SMAPI config, independent of Stardew save files. GMCM can manually override both values. The mod pushes transient death events and challenge-state snapshots through the existing broadcaster-scoped WebSocket; no challenge history is persisted in Cloudflare storage.

## Random monster selection

The redemption type is configured through typed behaviour keys (weighted-random, chat-selected, fixed-count and random-count variants). The SMAPI client owns final candidate selection and validates specific monster names against its local progression-aware allowlist, so the gateway never needs to know or trust a game class name.

The catalogue is an explicit list of variant definitions rather than reflection-discovered class names. Each definition includes a stable local ID and display name, a vanilla class and exact constructor argument where required, post-construction setup for special forms, a balancing-only weighting-health value, and whether it is a dangerous variant. `MonsterLocationPools` is the single source of truth for assigning those IDs to progression groups.

Unlocked location pools are combined into a single global candidate set. A definition appearing in multiple unlocked pools is included once. Catalogue validation checks that every definition has a pool, required roster entries remain in their approved pools, and excluded base monsters cannot re-enter through a dangerous variant. Farm entries are always available without a Combat Level gate. Progression is read from the active save only when selecting or validating a candidate.

Dangerous candidates are separate entries. They receive one additional-difficulty stat application and load a matching `_dangerous` texture if that asset exists. This is a consistent global-spawn approximation: vanilla mine generation can apply a different additional-difficulty amount depending on the mine floor and state, which isn't available when spawning in arbitrary locations.

The definitions use known vanilla constructor patterns for named variants: Bat subtype integer codes; Ghost, Serpent, RockCrab and Shooter name arguments; Fly, Grub and Skeleton boolean options; and explicit setup methods for Stick Bug and Tiger Slime. Copper/Iron Slimes and Iridium Golem require post-construction setup based on vanilla data and constructor behaviour. These paths compile-check against the pinned Stardew 1.6.15 reference assemblies but still require in-game validation.

Random selection now targets a threat band based on permanent save progression:
- Before Mines floor 120: target 75.
- After Mines floor 120, before Skull Cavern floor 100: target 155.
- After Skull Cavern floor 100: target 260.
- Combat Level contributes a secondary adjustment of up to 25% within each stage.
- The active Mines or Skull Cavern area's Shrine of Challenge difficulty adds 12% per level (0–2) to the target; it does not affect selection outside that matching area.
- Easy shifts the target lower; Hard shifts it higher. The weighting curve controls how tightly selection clusters around the target.
- Dangerous variants use a higher catalogue threat estimate.

The score is derived from vanilla `Data/Monsters` health, attack, defence, speed, experience, miss chance and glider fields. Dangerous variants receive a 1.5x threat estimate. The old catalogue health estimate is a fallback if a data entry cannot be resolved. This affects selection only; it does not alter monster stats. The target bands and score formula still need distribution checks in-game.
## Control Center data

Migration 0028_stardew_mod_access.sql creates:

- stardew_mod_access: per-member enable state, current connection status, game/SMAPI version and last-seen timestamps.
- stardew_mod_reward_mappings: per-member Twitch reward IDs and display titles.

Access is disabled by default. Only the administrator API can change it. The gateway must re-check the member's active status and enabled flag at pairing and before delivery. Disabling access must invalidate active sessions.

## Multiplayer authority

Each Spectrum streamer who wants their own redemptions to trigger spawns connects only their own broadcaster-specific gateway session. Other farmhands only need the mod installed to receive the shared-world result. Event Hub already subscribes to eligible Spectrum broadcasters and routes each interpreted redemption to that broadcaster's session; the game mod does not fan in or subscribe to other channels.

When a farmhand receives a command, it forwards a typed payload to the host through SMAPI's multiplayer messaging API. The host checks the sender's SMAPI peer identity and that the sender is an online farmer, then creates the monster near that farmer in their current location. The host sends a completion result back to the originating farmhand. The farmhand acknowledges the original gateway command only after success is confirmed. Host and farmhand both use command IDs to prevent duplicate spawns across retries and reconnects. Control Center clear-loaded-monsters actions received by a farmhand use the same host relay and are acknowledged only after the host completes the action.

Only the host creates monster entities, changes shared-world state, and executes the clear-loaded-monsters action. Farmhands can manage their own Spectrum connection and streamer settings, but cannot directly mutate the shared world. The host may accept forwarded commands even when the host's own Spectrum account is not paired; the source farmhand's own gateway safety gate and settings govern that command. Stardew's built-in multiplayer synchronisation is expected to replicate the host-created vanilla monster. Host/farmhand monster spawning and the remote Test Spawn action have been verified with two game clients. Keep the remote clear action on the final release smoke-test list.

## Security and reliability

- Derive member identity from the validated Control Center session/pairing proof, never from a body field supplied by the mod.
- Do not expose a public registration or activation endpoint.
- Keep all service credentials and Twitch tokens server-side.
- Verify EventSub signatures against the raw body.
- Deduplicate EventSub delivery and redemption IDs.
- Reject disabled, removed, suspended or non-Spectrum broadcasters.
- Expire old events rather than replaying a backlog when a game reconnects.
- Enforce per-member/reward/session cooldowns and spawn caps.
- Mutate Stardew state only on the game thread.
- Log operational status, not OAuth tokens or session credentials.

## Implementation and verification status

Implemented:
- SMAPI client, authenticated broadcaster-scoped WebSocket sessions, typed commands/actions, reconnect handling and game-thread execution.
- Progression-aware vanilla monster catalogue, safe spawn selection, danger variants, milestone notices and rare horde event.
- Host-authoritative multiplayer forwarding, command deduplication and completion acknowledgement.
- Control Center member activation, pairing/revocation, live connection state, shared reward configuration, spawn safety gate, streamer preferences and administrator overrides.
- Event Hub redemption routing and Game Mods gateway delivery.
- Control Center remote Test Spawn and loaded-monster clear actions.

Verified in the live Spectrum environment:
- Twitch Channel Point redemption reaches the correct broadcaster-scoped game session and spawns through the current mod.
- Host/farmhand command forwarding creates the shared-world monster near the redeeming farmer.
- Control Center Test Spawn reaches the selected connected host and is recognised by the current mod.
- Connection state is visible in the Control Center Stardew page.

Remaining release checks:
- Clean-install validation of the version 1.0.0 ZIP.
- In-game validation of the streamlined immediate-start pairing selector.
- Final remote Kill All smoke test, access-revocation/re-pair check and reconnect/safety-gate regression.
- Broader in-world appearance checks for the complete monster catalogue and dangerous variants.

The CI reference assemblies are pinned to Stardew Valley 1.6.15 and SMAPI 4.4.0; the mod manifest requires SMAPI 4.5.0 or later. Current runtime validation has been performed with Stardew Valley 1.6.15 and SMAPI 4.5.2.



## Shared Stardew settings and actions

The per-broadcaster Durable Object is the source of truth for:
- Streamer preference: spawn enabled/disabled.
- Spawn difficulty: easy, normal or hard.
- Chat success responses: enabled/disabled.
- Debug messages: enabled/disabled.
- Administrator override: follow, force_on or force_off.
- Global Control Center safety gate: closed on every fresh connection and disconnect.

Effective spawning requires a valid, connected host, an open global safety gate and a positive effective preference. The administrator override changes only the preference result. It cannot bypass the global gate or host authorisation. Settings revisions prevent a stale game client from overwriting a newer Control Center update. Game-side setting changes are stored in the Durable Object; they do not create D1/KV writes.

The typed stardew.clear_loaded_monsters action is sent to the broadcaster's paired game client and acknowledged with the number of removed monsters; only a client that is the Stardew multiplayer host executes it. The in-game menu can perform the same host-only clear locally, with confirmation. Each player's in-game Connect button starts a short-lived device-code flow, opens Control Center for approval by that member's own Twitch identity, then receives and stores their revocable game-session credential automatically. The browser never receives the credential.


## Challenge-series state and stream events

The local SMAPI config is the source of truth for the challenge name and death count. Config schema version 2 adds ChallengeName and ChallengeDeathCount. These values are deliberately separate from Stardew save data, so one streamer's series continues across multiple saves and streaming sessions. A manual GMCM override supports corrections and challenge continuation.

The paired client observes its local farmer's health on the normal SMAPI update loop and increments once when health reaches zero, guarded by a death latch until the farmer is revived. It writes the updated local config and publishes stardew.player_died. On connection, and when the gateway asks for overlay state after an overlay reconnect, the mod publishes stardew.challenge_state with the current challenge name and total. The gateway validates and fans out these messages to connected read-only overlay sockets without persisting them.

The StreamElements widget displays the challenge counter persistently and spawn events temporarily. It reconnects with backoff and relies on the mod's state snapshot for recovery. No StreamElements store, polling, D1, KV or Durable Object event history is used.


### Shared charity horde actions

- Twitch native Charity Tool donation notifications are aggregated by the Event Hub's shared AUD charity manager. A donation crossing multiple $5 boundaries creates one typed event with the threshold count and a single normal/MEGA classification.
- The gateway applies the configured normal or MEGA base, adds the configured bonus for each additional crossed threshold, and clamps the final request to 50 monsters.
- Donor display name and charity name are transient action metadata used for the optional in-game announcement. They are not written into a donor-history table.
- The event carries an immutable broadcaster/session snapshot from the donation moment. The per-host gateway rechecks the exact session ID and effective spawning permission before sending the action.
- Farmhands relay the typed action and announcement metadata to the multiplayer host; the host remains the only client that changes the shared world and broadcasts the announcement to connected modded farmhands.


## Host-owned concurrent game sessions (2.3.0 requirement)

A Stardew game session is an independent, host-owned unit of state. Spectrum may have multiple game sessions running at the same time, including separate solo games and separate multiplayer farms. Simultaneous streaming or mod connectivity does not imply that players share a Stardew session.

- The multiplayer host's active save is the sole authority for that session's progression and spawn-pool eligibility.
- Every connected farmhand is associated with the host's session and inherits its progression context. A farmhand's own save/progression must never influence the host farm's eligibility.
- A solo player is the host of their own independent session.
- Session-specific state must be keyed by a unique game-session identity, not only by Twitch broadcaster ID and never by a process-wide/global active-save variable.
- Session-specific state includes progression snapshots, pool eligibility, manual pool overrides, queued/pending spawn work, horde and charity actions, diagnostics, and configuration revisions.
- Session start, host/save transition, join/leave, reconnect, and session end must initialise, refresh, detach, or clear the relevant context without leaking stale state into another session.
- Control Center must present sessions grouped by host and show the associated clients.

### Redemption identity, host authority, and spawn target

Keep three identities distinct for every redemption/action:

1. **Redemption owner:** the Twitch broadcaster whose chat or channel-point redemption triggered the action.
2. **Session host:** the Stardew multiplayer host whose save and session rules govern eligibility.
3. **Spawn target:** the in-game farmer who receives the monster near their current location.

A farmhand's broadcaster-scoped connection continues to receive and own that farmhand's redemptions. The farmhand forwards the typed request to the host; the host validates the peer, applies the host session's progression/pool rules, and creates the monster near the redeeming farmhand. The result is acknowledged back through the originating farmhand connection. Do not replace the redemption owner or spawn target with the host identity.

Apply the same separation to normal redemptions, ordinary hordes, mega hordes, and charity-triggered actions. Preserve existing delivery, deduplication, queueing, and acknowledgement behaviour.

### Session-scoped spawn-pool diagnostics and overrides

Control Center's Stardew page will provide per-session Spawn Pool Diagnostics & Management. For each pool, report:
- Expected eligibility according to the host save and the pool's progression requirements.
- Actual effective state reported by the running mod.
- Override mode: Automatic, Force enabled, or Force disabled.
- The progression facts/requirements used to reach the expected result.
- A clear reason for any mismatch between expected and actual state.
- Configuration revision/synchronisation status and recent spawn decisions.

Override precedence:
- Force disabled always excludes the pool.
- Force enabled includes the pool regardless of progression eligibility.
- Automatic follows the host save's progression checks.

Overrides are session-scoped. Changing one host's pool configuration must not affect another host's session. The mod is authoritative for reporting its evaluated/effective state; Control Center must not infer progression from streamer identity or guess the active pools. Diagnostics should be event-driven and avoid unnecessary high-frequency polling or storage writes.

## Local per-farmer starter weapon (2.3.0 requirement)

The starter weapon is independent of the remote session-control architecture:
- On first load into a save, grant the local farmer one vanilla Wooden Blade if their save-specific GivenStartWeapon flag is absent/false.
- Persist the flag with that farmer's save data, so each farmer receives the grant once per farmer per save. Do not store this flag in Control Center, Event Hub, the gateway, or shared remote configuration.
- Ensure the granted Wooden Blade is excluded from Stardew's normal weapon-loss-on-death handling. The protection must be reliable through the game's actual death/inventory-loss path, not merely rely on re-granting the item after death.
- Provide a custom in-game description/tooltip: “This blade won't leave your side.”
- Keep the feature local to SMAPI and independent of progression, network availability, and session pool configuration.
- Validate first-load, reload, death, save switching, and multiplayer host/farmhand cases; avoid duplicate grants.

