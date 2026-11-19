# Changelog

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
