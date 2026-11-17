# Architecture

## Product boundary

This is a private Spectrum integration, not a public self-service service. Only active Spectrum members explicitly enabled by a Control Center administrator may pair a game. The existing Spectrum Cloudflare repository owns the management plane and live service; this repository owns the SMAPI game client.

## High-level flow

Control Center admin enables member → member pairs Stardew host → gateway session

Twitch Channel Point redemption → Event Hub → Stardew gateway → host SMAPI mod → Stardew Valley

## Component responsibilities

### Spectrum Control Center

- Administrator-only Mods page.
- Reads the active Spectrum member roster.
- Enables/disables Stardew Monster Spawner per member.
- Shows activation and, after gateway integration, connection/last-seen state.
- Owns the canonical Twitch identity and authorisation.
- Will own streamer-facing reward mapping configuration.

### Spectrum Event Hub

- Uses the existing central EventSub ingress and signing secret.
- Must subscribe to channel.channel_points_custom_reward_redemption.add for enabled Spectrum broadcasters with the required central Twitch scope.
- Validates EventSub signatures and broadcaster identity.
- Routes only redemptions for active, enabled Stardew members.
- Deduplicates by Twitch EventSub message ID and redemption ID.
- Does not perform Stardew gameplay or choose monster classes.

The central Control Center OAuth scope list already includes channel:read:redemptions; each affected broadcaster must have granted it in their stored Twitch authorisation. EventSub subscription sync and notification routing still need implementation.

### Stardew gateway

A dedicated service in the Spectrum Cloudflare repository.

- Creates one-time pairing codes for the SMAPI host.
- Completes pairing only after Control Center confirms the active Spectrum member and Stardew mod activation.
- Issues a revocable, game-session-only credential.
- Maintains the host's persistent outbound connection.
- Accepts authenticated, typed redemption requests from Event Hub.
- Tracks connection status and last-seen timestamps for Control Center.
- Enforces request expiry, deduplication, reconnect handling and rate limits.
- Never accepts a client-supplied monster type; the game client chooses from its local allowlist.

### SMAPI mod

- Runs on the host's Stardew installation.
- Pairs using a short-lived code displayed in the SMAPI console/in-game UI.
- Maintains an authenticated outbound gateway connection.
- Queues typed spawn requests and processes them on the SMAPI game thread.
- Checks that the world is ready and the local player is the multiplayer host.
- Spawns exactly one monster per accepted redemption.
- Farmhands do not connect to Twitch or independently process redemption requests.

## Random monster selection

The redemption type is fixed: Random Monster Spawner.

The SMAPI client owns the final random selection so the gateway never needs to know or trust a game class name. The initial candidate list follows the Stardew Valley Wiki's documented monster command allowlist: Bat, DinoMonster, DustSpirit, Fly, Ghost, GreenSlime, Grub, LavaCrab, Mummy, RockCrab, RockGolem, Serpent, ShadowBrute, ShadowShaman, Skeleton and SquidKid. Duggy is excluded because its spawn depends on terrain. That wiki syntax belongs to the separate Console Commands debug tooling; this mod does not depend on that mod. It attempts to instantiate the listed vanilla game types directly. Each candidate has an expected-health fallback; where possible, the constructed monster's initial health is read from the game object. The selection weight is:

    weight = 1 / health ^ 1.35

A weighted draw from the available candidates makes high-HP monsters less common without making them impossible. Candidate constructors are discovered from the installed Stardew game assembly. A candidate that cannot be constructed is skipped. This reflection-based construction is a prototype and must be verified against the target Stardew version; the wiki's command list establishes valid command names, not necessarily compatible .NET constructor signatures. This is an initial balancing model, not a final promise about exact game spawn rates.

## Control Center data

Migration 0028_stardew_mod_access.sql creates:

- stardew_mod_access: per-member enable state, current connection status, game/SMAPI version and last-seen timestamps.
- stardew_mod_reward_mappings: per-member Twitch reward IDs and display titles.

Access is disabled by default. Only the administrator API can change it. The gateway must re-check the member's active status and enabled flag at pairing and before delivery. Disabling access must invalidate active sessions.

## Multiplayer authority

Only the host creates monster entities. Stardew's multiplayer world synchronisation is expected to replicate the location character to farmhands, but this must be tested with two actual game clients. If a monster does not synchronise correctly, implement an explicit host-to-farmhand mod-message protocol; do not allow each client to independently spawn.

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

## Implementation status

Implemented in source:
- SMAPI prototype and local Green Slime test command.
- Reflection-based allowlisted monster catalogue and inverse-health weighted selection.
- Local random-spawn test command.
- Control Center admin Mods page and per-member activation API.
- Additive D1 schema migration for member access and reward mappings.

Not yet implemented or verified:
- Apply migration 0028 to production D1.
- Deploy Control Center Worker and sync dashboard KV assets.
- Event Hub Channel Point EventSub subscription and redemption route.
- Stardew gateway, pairing, persistent host connection and revocation.
- Reward mapping editor and end-to-end configuration.
- Build/compile against the user's Stardew installation.
- In-game spawn, multiplayer replication, and clean ZIP install tests.
