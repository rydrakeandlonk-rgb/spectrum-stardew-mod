# Changelog

## 2.1.0 — development build

### Added
- One-click GMCM controls for pausing and resuming monster redemptions through the authenticated Game Mods WebSocket.
- One-click **Link Twitch ↗** browser action and immediate host-only **Clear Enemies** action.
- Live connection and effective spawning status in the in-game menu.
- Gateway validation for host-session spawn toggles, preserving queued redemptions while paused and respecting administrator Force disabled.

### Improved
- Removed the connection and clear-enemies action dropdowns and the two-step clear confirmation.
- Kept ordinary difficulty, chat-response, debug and challenge settings on GMCM Save.
- Day-to-day pause/resume no longer requires opening Control Center.

### Validation still required
- Build the mod against GMCM 1.16.0 and test the three in-game buttons with mouse input.
- Verify the browser opens from Link Twitch ↗ and the existing pairing approval flow completes.
- Verify pause/resume round-trips through the gateway, queued redemptions remain pending while paused, and administrator Force disabled still blocks delivery.
- Verify Clear Enemies works once for the host and remains unavailable to farmhands.
- Run the existing 2.0.0 gameplay and overlay regression checks.

## 2.0.0 — development build

### Added
- Persistent challenge-series name and death total in the local SMAPI config, independent of save files and streaming sessions.
- Automatic local-player death detection with one increment per death.
- GMCM controls to rename a challenge and manually override the series death total.
- Transient stardew.player_died and stardew.challenge_state events over the existing authenticated WebSocket.
- Challenge-state snapshots on game connection and overlay reconnect.
- StreamElements challenge death counter that remains visible alongside temporary spawn animations.
- Complete mod-plus-overlay source bundle in the GitHub Actions artifact.

### Storage and performance
- No D1 or KV schema changes.
- No new Worker or polling loop.
- No challenge/event history stored in Durable Object storage.
- Only a local config write when the death total or challenge settings change; overlay updates are pushed over the existing WebSocket.

### Validation still required
- Verify one death increments exactly once in single-player and multiplayer.
- Verify challenge name/count survive game restart and a different save.
- Verify GMCM manual override updates the overlay immediately.
- Verify overlay reconnect receives the latest snapshot.
- Verify monster-spawn animation and death counter together in StreamElements and OBS.
- Complete the existing 1.0.0 gameplay regression checks before distributing 2.0.0.


## 1.0.0 — release candidate

This is the first feature-complete release candidate for the private Spectrum Stardew integration.

### Added
- Twitch Channel Point redemptions spawn progression-eligible vanilla monsters in Stardew Valley.
- Per-player Twitch pairing and broadcaster-scoped gateway sessions.
- Multiplayer farmhand-to-host command forwarding with host-authoritative world changes.
- Progression-aware monster pools and threat-weighted random selection.
- Dangerous monster variants, progression milestone HUD notices and a rare ten-monster horde event.
- Control Center streamer settings, spawn safety controls, administrator overrides, remote Test Spawn and loaded-monster clearing.

### Improved
- Connection actions in Generic Mod Config Menu now start immediately when selected; pressing GMCM Save is no longer required to begin Twitch pairing or open Control Center.
- Streamer settings continue to use GMCM Save, keeping settings persistence separate from connection actions.
- Release documentation and validation checklist now reflect the current implementation and live tests.

### Release validation still required
- Test the immediate-start pairing selector in game.
- Install the 1.0.0 ZIP into a clean SMAPI installation.
- Complete the remote Kill All, access-revocation/re-pair, reconnect and safety-gate smoke tests.
- Complete broader in-world visual checks for the monster catalogue and dangerous variants.

Earlier implementation milestones: 0.8.3–0.8.6. See Git history for detailed changes.
