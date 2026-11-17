# Spectrum Stardew Monster Spawner

A private Spectrum integration that turns Twitch Channel Point redemptions into random vanilla monster encounters in Stardew Valley.

Only Spectrum members explicitly enabled by an administrator in Spectrum Control Center can connect. Downloading or building the mod does not grant access to the live service.

## Intended experience

1. An enabled Spectrum streamer installs the mod through SMAPI.
2. They launch Stardew Valley and pair the host game with Spectrum Control Center.
3. Control Center verifies the signed-in Twitch identity and administrator-controlled activation record.
4. The streamer connects the Channel Point reward used for Random Monster Spawner.
5. The host game connects to the Spectrum gateway and receives redemption requests.
6. The host chooses a random supported monster using inverse-health weighting and spawns it near the player.

No Twitch password, Twitch client secret, or long-lived Twitch token is stored in the mod.

## Spectrum architecture

The existing Spectrum Cloudflare project owns identity, permissions, dashboard management, Twitch authorisation, EventSub and the live gateway.

Twitch Channel Points → Spectrum Event Hub → Stardew gateway → host SMAPI mod → Stardew Valley

- Control Center: administrator-only Mods page, Spectrum member activation, connection status and reward configuration.
- Event Hub: receives and verifies Twitch Channel Point redemption notifications and routes approved redemptions.
- Stardew gateway: owns short-lived game pairing, authenticated host connections, deduplication and request delivery.
- SMAPI mod: runs in the host game, chooses a random monster from its safe allowlist, and creates it on the game thread.

The live service is centrally operated. Spectrum streamers do not deploy Workers or manage Twitch developer credentials.

## Random monster selection

The redemption is always a random monster; viewers do not choose a monster ID. The mod uses an allowlist of vanilla monsters and inverse-health weighting: higher-health monsters have a lower selection weight. Monster types whose constructors are unavailable on the installed game version are skipped safely.

The initial allowlist follows the Stardew Valley Wiki's documented monster command types: Bat, DinoMonster, DustSpirit, Fly, Ghost, GreenSlime, Grub, LavaCrab, Mummy, RockCrab, RockGolem, Serpent, ShadowBrute, ShadowShaman, Skeleton and SquidKid. Duggy is excluded because its spawn depends on terrain. The wiki documents these names for the Console Commands debug command; this mod does not require that separate mod and instead attempts to construct the allowlisted game types directly. Constructor compatibility still needs in-game verification. Higher-health candidates receive lower selection weights.

## Multiplayer

The farm host is authoritative. Only the host's mod connection processes external redemption requests and creates monsters. Farmhands do not need separate Twitch connections. Monster replication must be verified in a real host/farmhand test before release.

## Repository contents

- Gameplay/MonsterSpawner.cs — host-only world spawning.
- Gameplay/MonsterCatalog.cs — allowlisted random monster pool and weighted selection.
- ModEntry.cs — SMAPI entry point and local test commands.
- manifest.json and SpectrumStardewMod.csproj — SMAPI project metadata.
- docs/ — architecture, development and roadmap.

## Current implementation status

Gameplay prototype + Control Center management foundation. The mod now contains a weighted random monster selection prototype and local SMAPI test command. The Control Center repository contains an admin-only Mods page, per-member activation API, and additive D1 migration.

The live gateway, browser pairing, Twitch redemption EventSub subscription/routing, remote game connection, reward mapping UI, release ZIP packaging, and Nexus distribution are not complete. The migration has not been applied and the dashboard has not been deployed. The C# project has not yet been compiled or tested in-game.

## Local test commands

After building and installing through SMAPI:

    spectrum_spawn_test
    spectrum_spawn_random_test

The first command spawns a Green Slime. The second selects from the weighted random pool. Both are host-only.

## Security

- Only approved, active Spectrum members with an explicit enabled record can pair.
- A valid Twitch login alone does not grant access.
- Never ship service credentials or Twitch tokens in the mod ZIP.
- Use revocable game-session credentials, one-time pairing codes, redemption IDs, expiry, deduplication, rate limits and spawn caps.
- Network callbacks must enqueue typed requests; game state is changed only on the SMAPI game thread.
- Never accept arbitrary C# types, commands, or executable content from the service.

## License

Private Spectrum project. Redistribution and service access are not authorised.
