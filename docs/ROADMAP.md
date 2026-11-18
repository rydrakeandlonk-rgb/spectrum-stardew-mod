# Roadmap

The product is a private Spectrum integration. Control Center decides which active Spectrum members can use it; there is no public registration or self-service activation.

## Phase 1 — Gameplay foundation

- [x] Create the SMAPI project and host-only Green Slime test command.
- [x] Add an allowlisted monster type catalogue with inverse-health weighted random selection.
- [x] Dispatch monster creation through Stardew's built-in debug command parser instead of custom constructors.
- [x] Add a local random monster test command.
- [ ] Compile against the target Stardew Valley installation.
- [ ] Verify the command route and every candidate type in-game.
- [ ] Tune health weighting using actual gameplay results.
- [ ] Verify multiplayer replication with a host and farmhand.
- [ ] Add spawn caps, cooldowns and safe game-state checks.

## Phase 2 — Control Center Mods management

- [x] Add administrator-only Mods page.
- [x] Add active Spectrum member roster and per-member enable/disable API.
- [x] Add additive D1 schema for access state, connection metadata and reward mappings.
- [ ] Apply migration 0028 to production D1 through the confirmation-gated workflow.
- [ ] Deploy Control Center and sync dashboard KV assets.
- [ ] Add streamer reward-mapping editor and connection detail view.
- [ ] Connect access disable to active game-session revocation.

## Phase 3 — Event Hub Twitch redemption integration

- [ ] Add central EventSub subscription sync for channel.channel_points_custom_reward_redemption.add.
- [ ] Restrict subscriptions to active Spectrum members with Stardew access enabled and channel:read:redemptions granted.
- [ ] Validate broadcaster, configured reward ID, EventSub signature and redemption status.
- [ ] Deduplicate EventSub message and redemption IDs.
- [ ] Route only approved Random Monster Spawner redemptions to the gateway.
- [ ] Ensure disabling a member removes or ignores their EventSub subscription.

## Phase 4 — Stardew gateway and pairing

- [ ] Add a dedicated Stardew gateway Worker and host-session Durable Object.
- [ ] Implement short-lived, single-use pairing codes using the authenticated Control Center session.
- [ ] Issue revocable game-session credentials after checking membership and mod activation.
- [ ] Maintain persistent outbound host connections and report connection state to Control Center.
- [ ] Deliver typed redemption requests with expiry, deduplication, reconnect handling and rate limits.
- [ ] Queue network messages and apply them only on the SMAPI game thread.
- [ ] Test host authority and farmhand replication.

## Phase 5 — Packaging and release

- [ ] Add a reproducible build/package script that creates a ready-to-install SMAPI ZIP.
- [ ] Verify the ZIP on a clean SMAPI installation.
- [ ] Publish a versioned ZIP asset in GitHub Releases.
- [ ] Decide whether the source repository remains public or private; the live service remains restricted either way.
- [ ] Prepare Nexus Mods distribution only after the private beta is stable.

## Phase 6 — Spectrum beta

- [ ] Test all enabled Spectrum streamers and reject disabled/non-member accounts.
- [ ] Test reward mapping, duplicates, offline game, reconnect, save transitions and service outage handling.
- [ ] Test multiplayer with real host and farmhand clients.
- [ ] Verify access revocation and token/session handling.
- [ ] Confirm monitoring and troubleshooting information is visible in Control Center.

## Release principle

A successful GitHub commit is not proof of deployment or in-game behaviour. Keep implementation, deployment, smoke-test and end-to-end verification status separate.
