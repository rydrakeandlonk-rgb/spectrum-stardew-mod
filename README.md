# Spectrum Stardew Monster Spawner

A private Spectrum integration that turns Twitch Channel Point redemptions into random vanilla monster encounters in Stardew Valley.

Only Spectrum members explicitly enabled by an administrator in Spectrum Control Center can connect. Downloading or building the mod does not grant access to the live service.

## Intended experience

1. An enabled Spectrum streamer installs the Spectrum mod through SMAPI.
2. They launch Stardew Valley and pair the host game with Spectrum Control Center.
3. Control Center verifies the signed-in Twitch identity and administrator-controlled activation record.
4. An administrator sets the shared **Spawn Random Monster** Channel Point reward title in Control Center.
5. The host game connects to the Spectrum gateway and receives typed spawn-behaviour requests.
6. The host selects a monster from the local allowlist and invokes Stardew Valley's built-in monster debug handler.

No Twitch password, Twitch client secret, or long-lived Twitch token is stored in the mod.

## Spectrum architecture

The existing Spectrum Cloudflare project owns identity, permissions, dashboard management, Twitch authorisation, EventSub and the live gateway.

Twitch Channel Points → Spectrum Event Hub → Stardew gateway → host SMAPI mod → Stardew Valley

- Control Center: administrator-only Stardew Valley page, per-member Mod access, live game connection, shared reward title, pending queue count and per-host Start/Stop Spawns control. Each behaviour matches an exact Twitch Channel Point reward title (case-insensitive).
- Event Hub: receives and verifies Twitch Channel Point redemption notifications and routes approved redemptions.
- Stardew gateway: owns short-lived game pairing, authenticated host connections, deduplication and request delivery.
- SMAPI mod: runs in the host game, chooses a random monster from its allowlist, and invokes the game's own debug-command parser on the game thread.

## Monster spawning

The mod does not implement monster constructors or duplicate Stardew's spawn logic. It calls the game's Game1.parseDebugInput method with a locally generated command in the form:

    monster GreenSlime 24 18

This invokes the same built-in debug-command handler used by SMAPI's debug console route. The command type is selected from a runtime-discovered catalogue of vanilla monster classes that support the game's standard monster-command constructor. Tile coordinates are selected locally near the host player. Modded assemblies and gateway-supplied class names are never trusted.

The catalogue discovers every concrete monster class in Stardew Valley's own game assembly that exposes the same public `Vector2` constructor used by the built-in `monster Type X Y` debug command. This includes documented types and compatible vanilla additions (including terrain-dependent types such as Duggy when supported by the installed game version), without admitting modded monster classes. Known monsters use approximate health values for weighted random selection; newly discovered types receive a neutral fallback weight until balanced.

SMAPI installs its bundled Console Commands component as part of the normal SMAPI installation. Players should not need to download a separate command mod. The Spectrum mod invokes the game's parser directly, so it does not need to automate typing into the SMAPI console or depend on an external console-command API.

## Multiplayer

The farm host is authoritative. Only the host's mod connection processes external redemption requests and creates monsters. Farmhands do not need separate Twitch connections. Monster replication must be verified in a real host/farmhand test.

## Repository contents

- Gameplay/MonsterSpawner.cs — host-only command dispatch and safe tile selection.
- Gameplay/MonsterCatalog.cs — runtime-discovered vanilla monster names and weighted random selection.
- ModEntry.cs — SMAPI entry point, local test commands and game-thread command processing.
- Networking/GameModsClient.cs — authenticated WebSocket connection, typed command validation, reconnect and acknowledgements.
- ModConfig.cs — local broadcaster ID and revocable host token.
- manifest.json and SpectrumStardewMod.csproj — SMAPI project metadata.
- docs/ — architecture, development and roadmap.

## Current implementation status

The gameplay path delegates monster creation to Stardew's built-in debug-command parser instead of using reflection to construct monster classes. GitHub Actions now successfully compiles the SMAPI project and creates a ZIP artifact using the pinned community reference assemblies. This is a compile/package check only; the mod has not yet been tested in-game or in multiplayer.

The Control Center repository contains the admin-only Mods page, per-member activation API, additive D1 migration, pairing/revocation controls, and reward mapping UI. The shared Game Mods gateway and guarded Event Hub redemption route are implemented. Live connection reporting is now implemented in source. Production migration, deployment, Twitch subscription sync, host pairing, redemption delivery, in-game behaviour and multiplayer replication still require end-to-end verification.

## Pair the host

An administrator enables the member in Control Center, then uses **Pair / re-pair** on the Mods page. The dashboard displays the broadcaster's numeric Twitch user ID and a one-time host token.

1. Launch the mod once so SMAPI creates its local config file.
2. Open the generated `config.json` in the mod folder.
3. Set `BroadcasterUserId` to the numeric ID shown by Control Center.
4. In the Stardew Valley page, select this streamer under Spawn behaviour configuration. Enable the desired behaviour, enter the exact Twitch Channel Point reward title, configure its options, and save it. No Twitch reward ID needs to be copied.
5. Set `HostToken` to the one-time token shown by Control Center.
6. Leave `GatewayUrl` at its default value and restart Stardew Valley.
7. For the chat-selected monster behaviour, enable “Require viewers to enter text” on that Twitch reward. Chat can enter a supported monster name such as `Green Slime`.

The host token is stored locally as a revocable game-session credential. Never commit or share the local config. Re-pairing rotates the token and closes the previous host connection. Use `spectrum_connection_status` in the SMAPI console to check the connection.

## In-game test commands

After installing the GitHub Actions ZIP through SMAPI, enter these commands in the SMAPI in-game console:

    spectrum_spawn_test
    spectrum_spawn_random_test

The first command requests a Green Slime. The second selects from the weighted random pool. Both are host-only.

The 0.4.0 integration writes compact redemption and spawn results to the SMAPI console, retaining each command ID for deduplication and tracing. Successful spawns are announced through Stardew's existing in-game chat with the Twitch redeemer, monster type and location. The announcement is only shown after the game reports that a character was added.

The integration accepts six typed behaviours from the dashboard: difficulty-weighted random monster; chat-selected monster name (the Twitch reward must require user input); random quantity of random monsters; fixed quantity of random monsters; random quantity of a dashboard-selected monster; and fixed quantity of a dashboard-selected monster. Quantities are capped at 10 per redemption, and all monster names are checked against the locally discovered vanilla monster catalogue.

## Security

- Only approved, active Spectrum members with an explicit enabled record can pair.
- A valid Twitch login alone does not grant access.
- Never ship service credentials or Twitch tokens in the mod ZIP.
- Use revocable game-session credentials, one-time host tokens, redemption IDs, expiry, deduplication and spawn caps.
- Network callbacks must enqueue typed requests; game state is changed only on the SMAPI game thread.
- Never accept arbitrary C# types, commands, or executable content from the service.

## License

Private Spectrum project. Redistribution and service access are not authorised.
