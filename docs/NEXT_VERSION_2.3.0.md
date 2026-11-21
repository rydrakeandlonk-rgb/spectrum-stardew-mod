# Spectrum Stardew Monster Spawner — Version 2.3.0 implementation specification

**Status:** Planned next development version  
**Current baseline:** 2.2.0 development build  
**Scope:** Session ownership/isolation, session-aware spawn-pool diagnostics and overrides, and a local starter Wooden Blade.

This document is the implementation contract for the next version. It records the agreed behaviour; it does not claim that the features have been implemented or tested.

## 1. Core architecture: independent host-owned sessions

A game session is a distinct Stardew game world owned by its multiplayer host. Multiple sessions can run concurrently. Two streamers being live or using the mod at the same time does not mean they share a game session.

Examples:
- AllNightLonk hosts a multiplayer farm and RydrakeLive joins as a farmhand. Both belong to Lonk's session; Lonk's save progression governs the session.
- Cutlertron hosts a separate solo game at the same time. Cutler's progression and configuration govern only Cutler's session.

Requirements:
- Host save is the sole source of progression and pool eligibility for that host's session.
- Farmhands inherit the host session's progression context. Their personal save/progression must not affect that shared farm.
- A solo player is the host of their own session.
- Every session has a unique identity. Do not key session state solely by broadcaster ID, streamer identity, or one global active-save variable.
- Session-specific state includes progression snapshots, pool eligibility, overrides, queues/pending work, horde and charity actions, diagnostics, and configuration revisions.
- Session start, save/host transition, join/leave, reconnect, and session end must correctly initialise, refresh, detach, or clear context. Never reuse stale state from another save/session.
- Control Center groups connected players by host/session.

## 2. Keep redemption owner, session host, and spawn target separate

Every redemption/action carries three distinct identities:
1. **Redemption owner:** broadcaster whose Twitch chat or redemption triggered it.
2. **Session host:** multiplayer host whose save and session rules determine eligibility.
3. **Spawn target:** in-game farmer near whom the monster is created.

Example: Rydrake is a farmhand in Lonk's session. Rydrake's own chat redemption must still be delivered through Rydrake's broadcaster-scoped connection and spawn near Rydrake's in-game farmer. The host validates and executes the request using Lonk's progression and pool rules, then returns the result to Rydrake's originating connection for acknowledgement.

Preserve this behaviour for:
- Normal monster redemptions.
- Ordinary horde redemptions.
- Mega hordes.
- Charity-triggered hordes.

Do not change existing redemption attribution, target selection, queues, deduplication, retry, or acknowledgement semantics except where necessary to associate the request with the correct session.

## 3. Control Center: Spawn Pool Diagnostics & Management

Add a session-aware panel to the existing Stardew Valley Control Center page. Show sessions grouped by host, with clients listed under their host.

For every pool, expose:
- **Expected state:** whether the pool should be eligible according to the host save's progression.
- **Actual state:** whether the mod currently considers the pool effectively enabled.
- **Override:** Automatic, Force enabled, or Force disabled.
- **Progression evidence:** the host-save facts and requirements used in the decision.
- **Reason/mismatch:** explain why a pool is unavailable or why expected and actual states differ.
- **Sync diagnostics:** session identity, host, progression refresh, configuration revision, last successful sync, and host/mod agreement.
- **Recent decisions:** concise event-driven record of accepted/rejected spawn selection with pool and reason.

Override precedence:
1. **Force disabled** — always exclude the pool.
2. **Force enabled** — include the pool regardless of progression.
3. **Automatic** — follow host-save progression requirements.

Overrides are session-scoped. An override made for Lonk's session must not change Cutler's session. The mod evaluates progression and reports its effective state; Control Center displays those reports and sends override changes rather than guessing which pools should be active.

Keep diagnostics lightweight and event-driven. Do not introduce high-frequency polling or unnecessary D1/KV/DO writes. Apply acknowledged changes without requiring a game/mod restart.

## 4. Local gameplay feature: starter Wooden Blade

This is a separate, local-only SMAPI feature. It does not require Control Center, Event Hub, gateway configuration, progression checks, or remote calls.

Behaviour:
- When a farmer first loads into a save, check a save-persisted GivenStartWeapon flag.
- If absent/false, grant that farmer one vanilla **Wooden Blade** and set the flag to true.
- Store the flag with the individual farmer's save data, so the grant is once per farmer per save. Do not use a global mod config flag or remote storage.
- The Wooden Blade must not be lost through Stardew's normal weapon-loss-on-death behaviour. Implement protection in the actual death/inventory-loss path; do not rely solely on granting a replacement after death.
- Give the item a custom in-game description/tooltip: **“This blade won't leave your side.”**
- Avoid duplicate grants on reload, reconnect, or repeated save-load events.

This feature applies to each local farmer's own character, whether they are hosting or joining a multiplayer session. It does not change the session's progression or pool eligibility.

## 5. Non-regression requirements

The following existing behaviour must continue working:
- Each streamer maintains their own broadcaster-scoped Twitch connection.
- A farmhand's own chat redemptions still target that farmhand's in-game character.
- The host remains the only authority that mutates the shared world.
- The host's progression governs monster eligibility for all players in that host's session.
- Different host sessions remain independent even when active simultaneously.
- Existing normal spawn, horde, mega-horde, charity-horde, pause/resume, queue, deduplication, and acknowledgement behaviour remains intact.
- Existing charity threshold and horde sizing rules are unchanged.
- The starter weapon works offline and does not add remote-service dependencies.

## 6. Acceptance and regression checklist

### Session and progression
- [ ] Run a multiplayer session hosted by Lonk with Rydrake as a farmhand.
- [ ] Run Cutler's independent solo session at the same time.
- [ ] Confirm Rydrake and Lonk use Lonk's progression; Cutler uses Cutler's progression.
- [ ] Confirm save/session switching and reconnects cannot leak stale progression or configuration.
- [ ] Confirm clients cannot influence host progression eligibility.

### Spawn pools and Control Center
- [ ] Show expected and actual state for every pool.
- [ ] Show progression facts and a human-readable eligibility reason.
- [ ] Test Automatic, Force enabled, and Force disabled.
- [ ] Confirm overrides are isolated per host session.
- [ ] Confirm configuration changes apply live and are acknowledged.
- [ ] Confirm recent spawn decisions identify pool, result, and reason.
- [ ] Confirm diagnostics do not introduce unnecessary polling or storage writes.

### Redemption routing
- [ ] Rydrake's chat redemption while a farmhand in Lonk's session spawns near Rydrake, using Lonk's pool rules.
- [ ] Lonk's redemption spawns near Lonk, using Lonk's pool rules.
- [ ] Cutler's redemption in his solo session uses Cutler's rules.
- [ ] Repeat for ordinary hordes, mega hordes, and charity actions.
- [ ] Verify originating broadcaster attribution, deduplication, retry, and acknowledgement.

### Wooden Blade
- [ ] First load grants one Wooden Blade and persists GivenStartWeapon.
- [ ] Reloading the same save does not grant a duplicate.
- [ ] Each farmer receives their own one-time grant in their save data.
- [ ] Death does not remove the granted Wooden Blade.
- [ ] Tooltip/description displays “This blade won't leave your side.”
- [ ] Verify host and farmhand cases and save switching.

### Existing regression
- [ ] Re-run the 2.2.0 charity horde rules and announcements.
- [ ] Re-run normal redemption, horde, mega-horde, pause/resume, and multiplayer tests.
- [ ] Re-run gateway reconnect, access/safety-gate, Control Center actions, and overlay checks.
- [ ] Build and package cleanly; do not treat CI success as a substitute for in-game validation.

## 7. Implementation notes

Before changing code, audit the current session identifiers, per-broadcaster gateway routing, SMAPI multiplayer message contracts, progression snapshot ownership, pool construction, and charity action routing. Confirm where the current implementation conflates broadcaster and session identity, if anywhere. For the Wooden Blade, inspect the pinned Stardew/SMAPI APIs and the actual death item-loss path before choosing the persistence/protection mechanism.

No implementation detail should weaken host authority, broadcaster attribution, existing safety gates, or the lightweight service architecture.
