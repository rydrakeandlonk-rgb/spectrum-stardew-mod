# Roadmap

## Phase 1 — SMAPI foundation

- [x] Create the repository and project structure.
- [x] Add a host-only test spawn command.
- [ ] Compile against the target Stardew Valley installation.
- [ ] Verify the spawn position and monster behavior in-game.
- [ ] Verify multiplayer replication with a host and farmhand.
- [ ] Add configurable spawn limits and safe game-state checks.

## Phase 2 — Local event queue

- [ ] Define a typed spawn request model.
- [ ] Add an in-game queue processed on SMAPI's game thread.
- [ ] Add request ID deduplication, expiry, and rate limits.
- [ ] Add a small allowlist of supported vanilla monster IDs.

## Phase 3 — Spectrum gateway

- [ ] Define the authenticated gateway protocol.
- [ ] Establish a persistent connection from the host mod.
- [ ] Reconnect safely and drain queued events.
- [ ] Add health/status reporting without excessive Cloudflare writes.

## Phase 4 — Twitch and Control Center

- [ ] Route Twitch Channel Point redemptions through Spectrum Event Hub.
- [ ] Configure reward-to-monster mappings in Spectrum Control Center.
- [ ] Add viewer attribution and optional monster naming.
- [ ] Add stream-safe cooldowns, caps, and pause controls.

## Phase 5 — Release readiness

- [ ] Test single-player and multiplayer thoroughly.
- [ ] Test disconnects, reconnects, duplicate deliveries, and save transitions.
- [ ] Document installation and configuration.
- [ ] Package a versioned release.
