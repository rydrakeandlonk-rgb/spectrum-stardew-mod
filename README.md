# Spectrum Stardew Mod

A public, install-and-connect Stardew Valley mod that turns Twitch Channel Point redemptions into in-game monster encounters.

The goal is that any streamer can download the mod, install it through SMAPI, connect their own Twitch account, configure their rewards, and play. They should not need to create a Cloudflare account, run a server, manage API credentials, or build the project themselves.

## Intended player experience

1. Download a versioned ZIP from GitHub Releases (and, when published, Nexus Mods).
2. Install SMAPI, then extract the mod folder into the Stardew Valley Mods folder.
3. Launch the game and open the mod's setup page/link.
4. Sign in to Twitch using the official Twitch authorization page.
5. Connect the game using a short-lived pairing code, then configure which Channel Point rewards trigger which monsters.
6. Start the farm. The host mod connects automatically and processes redemptions.

No Twitch password or long-lived Twitch token is entered into the game or committed to this repository.

## Product architecture

This is a small monorepo: the SMAPI mod, hosted-service source, account/setup web app, packaging scripts, and documentation all live here.

`Twitch OAuth + EventSub → hosted Spectrum service → host SMAPI mod → Stardew Valley`

- **SMAPI mod:** installed by each streamer; connects the host game to the service and applies validated spawn requests.
- **Hosted service:** handles Twitch OAuth, EventSub subscriptions, account/reward configuration, pairing, event delivery, and reconnect handling. It is operated centrally; end users do not deploy it.
- **Setup web app:** lets streamers sign in with Twitch, select their channel, connect a game installation, and map rewards to supported monsters.
- **GitHub Releases:** publishes a ready-to-install ZIP for each version.
- **Nexus Mods:** optional additional distribution channel using the same tested release package.

## Multiplayer

The farm host is authoritative. Only the host's mod connection receives and applies spawn requests. Stardew's multiplayer world synchronisation should make the spawned monster visible to farmhands; this must be verified in real multiplayer tests. Farmhands do not need Twitch accounts or separate mod connections for the feature to work.

## Repository layout

- `src/SpectrumStardewMod/` — SMAPI mod source.
- `server/` — hosted service source and deployment configuration.
- `web/` — streamer setup and Twitch account-linking app.
- `packaging/` — scripts and release package layout.
- `docs/` — architecture, security, development, installation, and release guides.

The existing root-level SMAPI project is the initial prototype and will be migrated into this layout as implementation proceeds.

## Current status

**Prototype foundation only.** The repository currently contains a basic SMAPI project and a local Green Slime test command. Twitch OAuth, EventSub, the hosted service, setup UI, automatic game pairing, reward configuration, release ZIP packaging, and Nexus publication are not implemented yet. The test spawn and multiplayer replication also still need compilation and in-game verification.

See [Architecture](docs/ARCHITECTURE.md), [Roadmap](docs/ROADMAP.md), and [Development](docs/DEVELOPMENT.md).

## Requirements

- Stardew Valley version supported by the published release.
- SMAPI version supported by the published release.
- Internet connection for Twitch redemption integration.
- A Twitch account with the appropriate broadcaster permissions to authorize Channel Point redemption access.

The base game mod should still load when the online service is unavailable; Twitch-triggered spawns will pause until the connection returns.

## Security principles

- Twitch OAuth is completed in the browser using the hosted service; never collect Twitch passwords.
- Never ship a Twitch client secret or service-wide credential inside the mod ZIP.
- Store only the minimum account and reward configuration required to operate the service.
- Use short-lived pairing codes, authenticated game sessions, event IDs, expiry, deduplication, and rate limits.
- Never accept arbitrary code or unrestricted game object types from the network.

## License

To be decided before public distribution.
