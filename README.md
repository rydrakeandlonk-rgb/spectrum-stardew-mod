# Spectrum Stardew Mod

A SMAPI mod that connects Twitch Channel Point redemptions to monster encounters in Stardew Valley through Spectrum infrastructure.

## Project status

**Phase 1 — SMAPI foundation**

The first milestone is a locally installable mod with a safe, host-authoritative monster spawn test. Twitch and Cloudflare integration will follow after in-game spawning and multiplayer synchronisation are verified.

## Design decisions

- Spawn monsters near the active player, on a valid tile in the current location.
- In multiplayer, only the host processes spawn requests and creates monsters; Stardew's multiplayer layer handles world synchronisation.
- Farmhands do not need their own Twitch or Cloudflare connection.
- External events will be queued and applied on the game thread, never by a background network callback.
- Reward-to-monster mappings will eventually be configurable through Spectrum Control Center.

## Planned architecture

`Twitch EventSub → Spectrum Event Hub → Spectrum gateway → host SMAPI mod → Stardew Valley`

## Development

See [`docs/ARCHITECTURE.md`](docs/ARCHITECTURE.md), [`docs/DEVELOPMENT.md`](docs/DEVELOPMENT.md), and [`docs/ROADMAP.md`](docs/ROADMAP.md).

## Requirements

The mod targets the Stardew Valley / SMAPI version specified in the project file and release documentation. Install SMAPI before installing this mod.

## Safety and compatibility

Spawn requests must be validated, deduplicated, rate-limited, and rejected when the game is not in a safe state. The mod must not spawn into menus, events, cutscenes, or unsupported locations.

## License

To be decided before public distribution.
