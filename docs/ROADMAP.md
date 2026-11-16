# Roadmap

The product target is a public install-and-connect mod. Streamers should not need to compile source, create cloud infrastructure, or manage Twitch developer credentials.

## Phase 1 — Prove the SMAPI gameplay foundation

- [x] Create the repository and initial SMAPI project.
- [x] Add a host-only test spawn command.
- [ ] Compile against the target Stardew Valley installation.
- [ ] Verify the spawn position and monster behavior in-game.
- [ ] Verify multiplayer replication with a host and farmhand.
- [ ] Add configurable spawn limits and safe game-state checks.
- [ ] Move the SMAPI project into the planned monorepo `src/` layout.

## Phase 2 — Define the public connection contract

- [ ] Define typed spawn request and connection status models.
- [ ] Design one-time browser pairing and revocable game-session credentials.
- [ ] Define authenticated persistent delivery from the hosted service to the host mod.
- [ ] Add an in-game queue processed on SMAPI's game thread.
- [ ] Add request ID deduplication, expiry, reconnect handling, and rate limits.
- [ ] Add an allowlist of supported vanilla monster IDs.

## Phase 3 — Build the hosted multi-user Twitch service

- [ ] Create the hosted service source under `server/` in this repository.
- [ ] Register/configure the Twitch developer application.
- [ ] Implement Twitch OAuth for each broadcaster with the minimum required scopes.
- [ ] Securely store and refresh tokens server-side; implement revoke/disconnect.
- [ ] Implement signed Twitch EventSub webhook verification and per-broadcaster subscriptions.
- [ ] Route redemptions to the correct paired game session.
- [ ] Handle offline sessions, expiry, deduplication, and abuse limits.

## Phase 4 — Build the streamer setup app

- [ ] Create the setup web app under `web/`.
- [ ] Add Twitch sign-in and connected-channel status.
- [ ] Add game pairing flow using a short-lived one-time code.
- [ ] List eligible Channel Point rewards.
- [ ] Let streamers map rewards to supported monster types and spawn settings.
- [ ] Add pause, disconnect, and connection health controls.
- [ ] Add privacy policy and data deletion/disconnection guidance.

## Phase 5 — Release packaging and distribution

- [ ] Add a reproducible packaging script under `packaging/`.
- [ ] Produce a ready-to-install ZIP with the correct folder structure and manifest.
- [ ] Verify clean install, update, and uninstall instructions.
- [ ] Publish versioned ZIP assets on GitHub Releases.
- [ ] Prepare a Nexus Mods page and publish the same tested ZIP there.
- [ ] Document supported Stardew/SMAPI versions and troubleshooting.
- [ ] Add release notes and checksums.

## Phase 6 — Public beta and launch

- [ ] Test single-player and multiplayer thoroughly.
- [ ] Test multiple unrelated Twitch broadcasters using the hosted service.
- [ ] Test token expiry/revocation, disconnected games, reconnects, duplicate deliveries, and save transitions.
- [ ] Test Twitch EventSub retries and service outages.
- [ ] Publish privacy policy, terms/usage notes, and support instructions.
- [ ] Run a limited public beta before general release.

## Release principle

A GitHub source repository is not itself a ready-to-install release. Each public version must include a tested ZIP asset. Nexus Mods is an additional distribution channel, not a separate codebase.
