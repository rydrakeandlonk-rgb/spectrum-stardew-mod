# Roadmap

The product is a private Spectrum integration. Control Center decides which active Spectrum members can use it; there is no public registration or self-service activation.

## Phase 1 — Gameplay foundation

- [x] Create the SMAPI project and host-only Green Slime test command.
- [x] Discover all vanilla monster subclasses supported by the built-in monster command, validate canonical names and use inverse-health weighted random selection.
- [x] Add direct, explicit vanilla constructor dispatch for named monster variants.
- [x] Add a local random monster test command.
- [x] Add first-pass location-group progression checks using vanilla save flags and completed Special Order history.
- [x] Add an in-game diagnostic command for unlocked pools, full catalogue mappings and constructor/setup support.
- [x] Add explicit spawn definitions for the reference roster's non-excluded named variants and location pools.
- [x] Implement first-pass dangerous monster setup with an additional-difficulty stat step and optional dangerous textures.
- [ ] Validate dangerous scaling and visuals in-game; compare the fixed global stat step with vanilla mine-specific scaling.
- [x] Add an authenticated WebSocket client, reconnect loop and typed command validation.
- [x] Queue remote commands for processing on the SMAPI update thread and acknowledge after handling.
- [ ] Compile against the target Stardew Valley installation.
- [ ] Verify every explicit monster variant, its post-construction behaviour and its spawn compatibility in-game.
- [ ] Verify multiplayer replication with a host and farmhand.
- [x] Add compact one-line SMAPI redemption and spawn-result logs with command IDs.
- [x] Announce confirmed successful spawns through Stardew's existing in-game chat with the Twitch redeemer, monster and location.

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
- [x] Report live connection state to Control Center in source; production verification remains pending.
- [ ] Complete rate-limit and operational monitoring policy.
- [ ] Verify end-to-end delivery and acknowledgements.

## Phase 5 — Packaging and release

- [x] Add a reproducible build/package script that creates a ready-to-install SMAPI ZIP.
- [x] Compile and package a ZIP through GitHub Actions using pinned reference assemblies.
- [ ] Install and verify the ZIP on a clean SMAPI installation.
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
