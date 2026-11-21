# Starter Gift Letters

The Starter Gift integration is managed from Control Center → Stardew Valley → Starter Gift Letter.

## Save isolation

The mod derives a stable 128-bit session identifier from Stardew's save identity and the host farmer ID. It remains stable when the same save reconnects, and changes when a different save is loaded. The Game Mods Durable Object stores starter gift settings under that session identifier, so settings aren't shared merely because two saves use the same Twitch broadcaster account.

The host reports its role and active save session to Game Mods. Control Center only offers connected host-owned saves, and the gateway rejects starter gift configuration or delivery from a farmhand session. The mod also checks `Context.IsMainPlayer` before changing save data or queuing mail.

## Configuration

Each host save has its own:

- Item identifier, resolved through Stardew's item registry.
- Letter title (up to 80 characters).
- Letter message (up to 500 characters).

The mod runs the configured identifier through `ItemEligibilityValidator`. Only resolved edible object items with positive edibility are accepted. If the configured identifier is invalid or ineligible, the mod validates and uses the fallback Cookie `(O)223`. If the fallback itself cannot be resolved and validated, no letter is queued.

Message and title text are sanitized. Percent signs are removed from user text so it can't inject additional Stardew mail attachment/action tokens. The item attachment token is generated separately from the validated object.

## Delivery

- **Queue permanent starter gift** uses the fixed save-scoped mail ID `Spectrum_StarterGift`.
- The mod checks both its save-data entitlement flag and Stardew's `hasOrWillReceiveMail` before queueing. Repeated requests for the same save are rejected.
- **Send test letter** creates a unique mail ID. It does not set or consume the permanent gift entitlement.
- The exact letter content is snapshotted in that save's mod data before being queued. Later Control Center edits won't change mail already waiting for delivery.
- Letters are injected into Stardew's `Data/mail` asset and queued with `Game1.addMailForTomorrow`, using the native mailbox and item attachment system.

The configuration and delivery state use the existing Game Mods Durable Object and the existing save's mod data. No D1 migration, KV namespace, external item database, or new Worker is required.

## Testing status

The SMAPI mod build and package validation run in GitHub Actions. The Game Mods Worker has automated tests for save-scoped settings, stale-session rejection, and farmhand rejection. A successful build or Worker test does not replace the first in-game integration test with the actual installed mod set.
