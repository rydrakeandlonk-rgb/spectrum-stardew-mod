# Architecture

## Current phase

The first implementation is a local SMAPI gameplay foundation. It exposes a host-only console command to test creation of a vanilla Green Slime near the player.

## Planned event flow

`Twitch EventSub → Spectrum Event Hub → Spectrum gateway → host SMAPI mod → Stardew Valley`

## Responsibilities

- **Spectrum Event Hub:** receives and routes Twitch EventSub events.
- **Spectrum gateway:** authenticates the connected game session and delivers queued, validated spawn requests.
- **SMAPI mod:** owns the game-side connection, request validation, deduplication, and safe scheduling.
- **MonsterSpawner:** performs world changes on the game thread.
- **Spectrum Control Center:** eventually manages reward-to-monster mappings and mod configuration.

## Multiplayer authority

The host is authoritative. Only the host processes external spawn requests and creates monsters. Vanilla monster entities are added to the current location's character collection so Stardew's multiplayer networking can replicate the world entity. Farmhands must never create a second copy of the same spawn.

This must be verified in a real multiplayer test before the integration is considered complete. If a monster type or game context does not synchronise correctly, add a small mod-message protocol rather than allowing each client to independently spawn it.

## Network safety

- Never mutate game state from a network/background thread.
- Queue incoming requests and process them from a SMAPI game-loop event.
- Validate event IDs, monster IDs, request age, rate limits, save state, location, and spawn capacity.
- Deduplicate redemption IDs so reconnects/retries cannot create duplicate monsters.
- Do not accept arbitrary C# types or executable content from the gateway.
- Keep gateway credentials out of source control and logs.
