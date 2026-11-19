# Version 1.0.0 release checklist

This checklist separates verified live behaviour from the remaining release gate. A CI build alone is not an in-game test.

## Already verified in the live Spectrum environment

- [x] Twitch Channel Point redemption reaches the correct broadcaster-specific game session.
- [x] The current mod receives the typed redemption and spawns a progression-eligible monster.
- [x] Multiplayer host/farmhand forwarding works; the host creates the monster near the redeeming farmer.
- [x] Control Center Test Spawn reaches the selected connected host and spawns a random progression-eligible monster.
- [x] The older mod build's unsupported-command response was resolved by updating to the current mod build.

## Required before publishing 1.0.0

- [ ] Build the 1.0.0 candidate through GitHub Actions and inspect the successful run.
- [ ] Confirm the ZIP contains `manifest.json` and `SpectrumStardewMod.dll`, and no local config or credentials.
- [ ] Test the immediate-start Connect Twitch / open Control Center selector in GMCM.
- [ ] Install the ZIP into a clean SMAPI 4.5.2 installation with GMCM 1.16.0.
- [ ] Confirm SMAPI reports version 1.0.0 and loads without errors.
- [ ] Pair a streamer and confirm the browser approval flow completes and the mod reconnects.
- [ ] Confirm streamer settings still synchronise when GMCM Save is pressed.
- [ ] Run Control Center Kill All and verify the host clears loaded hostile monsters and reports completion.
- [ ] Confirm a fresh connection starts with the global spawn safety gate closed.
- [ ] Confirm streamer preference and administrator override cannot bypass the global safety gate.
- [ ] Verify reconnect/re-pair does not duplicate completed redemptions and that revocation invalidates the old session.
- [ ] Complete a visual spot-check of representative normal and dangerous monster variants.
- [ ] Confirm the changelog, README, manifest and ZIP all identify version 1.0.0.

## Distribution decision

The integration remains restricted to Spectrum members explicitly enabled in Control Center. Do not publish the ZIP to a public mod directory or announce an unrestricted release unless the project owner explicitly changes that distribution decision. A versioned internal release can be prepared independently of public distribution.
