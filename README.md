# Spectrum Stardew Monster Spawner

A private Spectrum integration that turns Twitch Channel Point redemptions into random vanilla monster encounters in Stardew Valley.

Only Spectrum members explicitly enabled by an administrator in Spectrum Control Center can connect. Downloading or building the mod does not grant access to the live service.

## Intended experience

1. An enabled Spectrum streamer installs the Spectrum mod through SMAPI.
2. They launch Stardew Valley and pair the host game with Spectrum Control Center.
3. Control Center verifies the signed-in Twitch identity and administrator-controlled activation record.
4. The streamer connects the Channel Point reward used for Random Monster Spawner.
5. The host game connects to the Spectrum gateway and receives redemption requests.
6. The host selects a monster from the local allowlist and invokes Stardew Valley's built-in monster debug handler.

No Twitch password, Twitch client secret, or long-lived Twitch token is stored in the mod.

## Spectrum architecture

The existing Spectrum Cloudflare project owns identity, permissions, dashboard management, Twitch authorisation, EventSub and the live gateway.

Twitch Channel Points → Spectrum Event Hub → Stardew gateway → host SMAPI mod → Stardew Valley

- Control Center: administrator-only Mods page, Spectrum member activation, connection status and reward configuration.
- Event Hub: receives and verifies Twitch Channel Point redemption notifications and routes approved redemptions.
- Stardew gateway: owns short-lived game pairing, authenticated host connections, deduplication and request delivery.
- SMAPI mod: runs in the host game, chooses a random monster from its allowlist, and invokes the game's own debug-command parser on the game thread.

## Monster spawning

The mod does not implement monster constructors or duplicate Stardew's spawn logic. It calls the game's Game1.parseDebugInput method with a locally generated command in the form:

    monster GreenSlime 24 18

This invokes the same built-in debug-command handler used by SMAPI's debug console route. The command type is selected from a hard-coded allowlist, and tile coordinates are selected locally near the host player. No command text or monster type supplied by the gateway is executed.

The initial allowlist follows the Stardew Valley Wiki's documented monster command types: Bat, DinoMonster, DustSpirit, Fly, Ghost, GreenSlime, Grub, LavaCrab, Mummy, RockCrab, RockGolem, Serpent, ShadowBrute, ShadowShaman, Skeleton and SquidKid. Duggy is excluded because its spawn depends on terrain. Higher-health monsters are intended to be less common, using approximate health values as weighting inputs; these are initial balancing estimates, not verified game data.

SMAPI installs its bundled Console Commands component as part of the normal SMAPI installation. Players should not need to download a separate command mod. The Spectrum mod invokes the game's parser directly, so it does not need to automate typing into the SMAPI console or depend on an external console-command API.

## Multiplayer

The farm host is authoritative. Only the host's mod connection processes external redemption requests and creates monsters. Farmhands do not need separate Twitch connections. Monster replication must be verified in a real host/farmhand test.

## Repository contents

- Gameplay/MonsterSpawner.cs — host-only command dispatch and safe tile selection.
- Gameplay/MonsterCatalog.cs — allowlisted monster names and weighted random selection.
- ModEntry.cs — SMAPI entry point, local test commands and game-thread command processing.
- Networking/GameModsClient.cs — authenticated WebSocket connection, typed command validation, reconnect and acknowledgements.
- ModConfig.cs — local broadcaster ID and revocable host token.
- manifest.json and SpectrumStardewMod.csproj — SMAPI project metadata.
- docs/ — architecture, development and roadmap.

## Current implementation status

The local gameplay path now delegates monster creation to Stardew's built-in debug-command parser instead of using reflection to construct monster classes. This code has not yet been compiled or tested in-game.

The Control Center repository contains an admin-only Mods page, per-member activation API, and additive D1 migration. The shared Game Mods gateway, Event Hub's guarded redemption route, Control Center pairing/revocation APIs and Mods dashboard controls, and the SMAPI WebSocket client are now implemented in source. They have not yet been compiled, deployed, or tested end-to-end. Reward mapping UI, live connection reporting, release ZIP verification, and distribution remain outstanding. The migration has not been applied and the dashboard has not been deployed.

## Pair the host

An administrator enables the member in Control Center, then uses **Pair / re-pair** on the Mods page. The dashboard displays the broadcaster's numeric Twitch user ID and a one-time host token.

1. Launch the mod once so SMAPI creates its local config file.
2. Open the generated `config.json` in the mod folder.
3. Set `BroadcasterUserId` to the numeric ID shown by Control Center.
4. Set `HostToken` to the one-time token shown by Control Center.
5. Leave `GatewayUrl` at its default value and restart Stardew Valley.

The host token is stored locally as a revocable game-session credential. Never commit or share the local config. Re-pairing rotates the token and closes the previous host connection. Use `spectrum_connection_status` in the SMAPI console to check the connection.

## Local test commands

After building and installing through SMAPI:

    spectrum_spawn_test
    spectrum_spawn_random_test

The first command requests a Green Slime. The second selects from the weighted random pool. Both are host-only.

## Security

- Only approved, active Spectrum members with an explicit enabled record can pair.
- A valid Twitch login alone does not grant access.
- Never ship service credentials or Twitch tokens in the mod ZIP.
- Use revocable game-session credentials, one-time host tokens, redemption IDs, expiry, deduplication and spawn caps.
- Network callbacks must enqueue typed requests; game state is changed only on the SMAPI game thread.
- Never accept arbitrary C# types, commands, or executable content from the service.

## License

Private Spectrum project. Redistribution and service access are not authorised.
