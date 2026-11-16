# Architecture

## Product goal

Any streamer should be able to install the mod through SMAPI, sign in to Twitch in a browser, connect their own Stardew Valley host session, map Channel Point rewards to monsters, and use the feature without operating their own backend.

The repository is intended to contain the complete source for the product: game mod, hosted service, setup web app, packaging, and documentation. The hosted service is operated centrally by the project maintainer.

## High-level flow

`Streamer browser → Twitch OAuth → Spectrum account/configuration service`

`Twitch EventSub → hosted EventSub receiver → per-streamer event routing → host SMAPI connection → Stardew Valley`

The browser handles sign-in and configuration. The game mod receives a one-time pairing code or authenticated session credential, not the streamer's Twitch password or OAuth refresh token.

## Components

### SMAPI mod

- Runs in the streamer's Stardew Valley installation.
- Exposes a setup URL and short-lived pairing flow.
- Maintains an authenticated outbound connection to the hosted service.
- Receives typed, validated spawn requests.
- Queues requests and applies them only on SMAPI's game thread.
- Only the multiplayer host processes requests and changes the world.
- Reconnects safely after network interruptions and reports basic connection state.

### Hosted service

- Provides the public HTTPS setup/API endpoints.
- Performs Twitch OAuth authorization-code flow and securely stores the minimum required token material server-side.
- Creates and maintains Twitch EventSub subscriptions for each connected broadcaster.
- Validates EventSub webhook signatures and message types.
- Associates Twitch broadcaster IDs, configured rewards, and paired game sessions.
- Delivers spawn requests to the correct active host connection.
- Handles offline sessions, retry policy, event expiry, deduplication, rate limits, and revocation/disconnection.
- Keeps client secrets and signing keys in server-side secret storage only.

### Setup web app

- Uses Twitch's official authorization page; the app never asks for a Twitch password.
- Shows the signed-in Twitch channel and authorization status.
- Lets the streamer pair a running game mod.
- Lists eligible Channel Point rewards and maps selected rewards to an allowlisted monster type and safe spawn settings.
- Provides pause, disconnect, and connection-status controls.

### Distribution

- GitHub is the source of truth for all project code and documentation.
- Each release publishes a versioned, ready-to-install ZIP containing the SMAPI mod and required mod files, not the server source or server secrets.
- Nexus Mods can host the same release ZIP as an additional download location.
- The hosted service and setup app are deployed once by the project maintainer; individual users do not deploy infrastructure.

## Twitch authorization and EventSub

The Twitch integration is per streamer. Each broadcaster explicitly authorizes the application with the scopes required to read their Channel Point redemptions. The service must verify the broadcaster identity and create the appropriate EventSub subscription for that broadcaster.

The exact OAuth scopes, EventSub subscription version, webhook verification, token refresh, and Twitch app configuration must be checked against the current Twitch developer documentation during implementation. Tokens must be encrypted at rest, access must be revocable, and logs must not contain tokens.

## Game pairing and connection

1. The user launches Stardew Valley with the mod installed.
2. The mod displays a setup URL and requests a short-lived pairing code from the service.
3. The user signs in to Twitch in the browser and enters/approves the pairing code.
4. The service binds the authorized broadcaster account to that game installation/session.
5. The mod exchanges the pairing proof for a revocable game-session credential and opens an outbound authenticated connection.
6. The streamer configures reward-to-monster mappings in the web app.
7. EventSub redemptions are validated, mapped, and delivered to that streamer's active host connection.

Pairing codes must expire quickly, be single-use, and be rate-limited. Game-session credentials must be revocable and must not grant access to Twitch APIs.

## Multiplayer authority

The host is authoritative. Only the host's mod connection processes external spawn requests and creates monsters. Farmhands do not need Twitch authorization or a separate service connection. Vanilla monster entities are added to the current location's character collection so Stardew's multiplayer networking can replicate the world entity.

This must be verified in a real multiplayer test. If a monster type or game context does not synchronise correctly, add a small mod-message protocol rather than allowing each client to independently spawn it.

## Reliability and abuse controls

- Validate Twitch webhook signatures and message types.
- Validate broadcaster, reward ID, game-session binding, monster allowlist, request age, and game state.
- Deduplicate using the Twitch redemption/event ID.
- Rate-limit per broadcaster, reward, and game session.
- Enforce configurable per-minute and per-session spawn caps.
- Expire events when the game is offline or the event is too old; do not unexpectedly replay a backlog after a long absence.
- Never mutate game state from a network/background thread.
- Queue incoming requests and process them from a SMAPI game-loop event.
- Do not accept arbitrary C# types, commands, or executable content from the service.
- Provide a clear pause/disconnect control.

## Privacy and data minimisation

Store only what is required to authenticate the broadcaster, maintain EventSub, remember reward mappings, and route events to the paired game. Publish a clear privacy policy before public launch, including retention, deletion, disconnect, and token revocation behavior. Avoid collecting viewer information unless a feature explicitly requires it.

## Current implementation status

The repository currently contains only the initial SMAPI prototype. The hosted service, OAuth, EventSub, setup web app, pairing protocol, reward configuration, and release pipeline described here are design targets, not implemented features.
