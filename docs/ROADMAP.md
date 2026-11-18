# Roadmap

The product is a private Spectrum integration. Control Center decides which active Spectrum members can use it; there is no public registration or self-service activation.

## Phase 1 — Gameplay foundation

- [x] Create the SMAPI project and host-only Green Slime test command.
- [x] Add an allowlisted monster type catalogue with inverse-health weighted random selection.
- [x] Dispatch monster creation through Stardew's built-in debug command parser instead of custom constructors.
- [x] Add a local random monster test command.
- [x] Add an authenticated WebSocket client, reconnect loop and typed command validation.
- [x] Queue remote commands for processing on the SMAPI update thread and acknowledge after handling.
- [ ] Compile against the target Stardew Valley installation.
- [ ] Verify the command route and every candidate type in-game.
- [ ] Verify multiplayer replication with a host and farmhand.

## Phase 2 — Control Center Mods management

- [x] Add administrator-only Mods page.
- [x] Add active Spectrum member roster and per-member enable/disable API.
- [x] Add additive D1 schema for access state, connection metadata and reward mappings.
- [x] Add admin-only pairing and revocation APIs and dashboard controls.
- [x] Revoke host credentials when access is disabled.
- [ ] Apply migration 0028 to production D1 through the confirmation-gated workflow.
- [ ] Deploy Control Center and sync dashboard KV assets.
- [x] Add administrator reward-mapping controls.
- [ ] Add live connection detail view.

## Phase 3 — Event Hub Twitch redemption integration

- [x] Add guarded central EventSub subscription sync for `channel.channel_points_custom_reward_redemption.add`.
- [x] Restrict subscription sync to active members with Stardew access and the required Twitch scope.
- [x] Validate EventSub signatures and check active member, enabled access and configured reward mapping before forwarding.
- [x] Deduplicate redemption IDs in the gateway.
- [x] Route approved Random Monster Spawner redemptions to Game Mods.
- [ ] Enable the readiness gate only after the full path is deployed and verified.

## Phase 4 — Shared Game Mods gateway

- [x] Add the shared Game Mods Worker and per-broadcaster host-session Durable Object.
- [x] Issue revocable host tokens after checking Control Center member activation.
- [x] Maintain persistent outbound host WebSocket connections.
- [x] Deliver typed redemption requests with deduplication and reconnect queueing.
- [x] Expire stale pending commands and clear pending commands on revocation/re-pairing.
- [ ] Report live connection state to Control Center.
- [ ] Complete rate-limit and operational monitoring policy.
- [ ] Verify end-to-end delivery and acknowledgements.

## Phase 5 — Packaging and release

- [x] Add a reproducible build/package script that creates a ready-to-install SMAPI ZIP.
- [ ] Compile and verify the ZIP on a clean SMAPI installation.
- [ ] Publish a versioned ZIP asset in GitHub Releases.
- [ ] Decide whether the source repository remains public or private; the live service remains restricted either way.
- [ ] Prepare distribution only after the private beta is stable.

## Phase 6 — Spectrum beta

- [ ] Test all enabled Spectrum streamers and reject disabled/non-member accounts.
- [ ] Test reward mapping, duplicates, offline game, reconnect, save transitions and service outage handling.
- [ ] Test multiplayer with real host and farmhand clients.
- [ ] Verify access revocation and token/session handling.
- [ ] Confirm monitoring and troubleshooting information is visible in Control Center.

## Release principle

A successful GitHub commit is not proof of deployment or in-game behaviour. Keep implementation, deployment, smoke-test and end-to-end verification status separate.
