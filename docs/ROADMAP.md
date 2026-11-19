# Roadmap and release status

The Spectrum Stardew Monster Spawner is a private Spectrum integration. Control Center decides which active Spectrum members can use it; there is no public self-service activation.

## Gameplay and SMAPI client

- [x] Explicit vanilla monster variant catalogue and constructor/setup dispatch.
- [x] Progression-aware global candidate pools and threat-weighted random selection.
- [x] Dangerous variants and matching dangerous textures where available.
- [x] Host-authoritative monster creation on safe nearby tiles.
- [x] Typed WebSocket commands, game-thread queueing, reconnect handling and acknowledgement after successful spawn.
- [x] Local deduplication of recent successful redemption IDs.
- [x] Progression milestone HUD notices and rare ten-monster horde event.
- [x] Per-player broadcaster-scoped gateway connections; no client subscribes to every Spectrum channel.
- [x] Typed farmhand-to-host command forwarding and host completion response.
- [x] Persistent challenge-series name and death total in local SMAPI config.
- [x] Automatic local-player death detection and duplicate-edge protection.
- [x] GMCM challenge-name and manual death-count override controls.
- [x] Transient challenge/death events and state snapshots over the existing WebSocket.
- [ ] In-game validation of death detection, series persistence, and manual override.
- [x] Bundled StreamElements widget for persistent challenge deaths and temporary spawn reveal.
- [ ] Finish broad visual/behavioural spot checks across every catalogue variant and dangerous form; the constructor diagnostic is not a substitute for in-world appearance checks.
- [ ] Test both overlay features together in StreamElements/OBS.

## Control Center and gateway

- [x] Administrator-only Stardew Mods page and per-member enable/disable access.
- [x] Pairing and revocation APIs with broadcaster-scoped game-session credentials.
- [x] Live connection state and per-host spawn safety gate.
- [x] Shared reward title and per-streamer settings synchronisation.
- [x] Separate administrator override that cannot bypass the global safety gate.
- [x] Host-only remote clear-loaded-monsters action.
- [x] One-shot random Test Spawn action for a selected connected host.
- [x] Event Hub Twitch redemption routing and gateway delivery.
- [x] End-to-end Twitch redemption spawn verified in the live Spectrum environment.
- [x] Multiplayer host/farmhand spawning verified in the Spectrum test farm.
- [x] Control Center Test Spawn verified with the current mod build.
- [ ] Final Kill All end-to-end smoke test and access-revocation/re-pair regression check.

## Version 2.0.0 development and test gate

- [x] Local challenge-series config fields and migration to config version 2.
- [x] Death event and challenge-state snapshot publishing over the existing gateway.
- [x] StreamElements widget death counter, spawn animation, and bundled source.
- [ ] Cloud CI compile and packaging validation.
- [ ] Install the 2.0.0 development artifact and test automatic death counting.
- [ ] Test manual challenge name/count override and persistence across game restart/save change.
- [ ] Test the StreamElements overlay with the actual broadcaster ID and OBS browser source.
- [ ] Verify gateway reconnect and overlay reconnect state recovery.

## Historical Version 1.0.0 release gate

- [x] Reproducible GitHub Actions build and ZIP packaging.
- [x] Static catalogue/pool validation in CI.
- [x] Package-content checks for the mod DLL and manifest; local config exclusion.
- [x] Live Twitch redemption, multiplayer spawn and dashboard Test Spawn have been exercised.
- [ ] Build the 1.0.0 candidate after the in-game pairing action is streamlined.
- [ ] Test the updated immediate-start pairing action in game.
- [ ] Install the 1.0.0 ZIP into a clean SMAPI installation with GMCM.
- [ ] Run Control Center Kill All and verify completion.
- [ ] Complete the final safety-gate, reconnect, re-pair and revocation smoke tests.
- [ ] Publish or distribute the versioned 1.0.0 package after the checks pass.

## Release principle

A successful commit or CI build is not proof of deployment or in-game behaviour. Keep source implementation, live deployment, build validation and in-game verification as separate claims.
