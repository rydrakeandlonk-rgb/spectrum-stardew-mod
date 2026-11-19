# Spectrum Stardew StreamElements Widget

The bundled widget displays two live features from the Spectrum Stardew mod:

- A persistent challenge-series death counter, including the challenge name.
- A temporary spawn animation that starts when a redemption is sent to the game and reveals the actual monster(s) only after the game confirms the spawn.

## Install

1. In StreamElements, create or open an overlay and add a **Custom Widget**.
2. Open its Custom Code Editor.
3. Copy `widget.html` into the HTML tab.
4. Copy `widget.css` into the CSS tab.
5. Copy `widget.js` into the JS tab.
6. Copy `fields.json` into the FIELDS tab.
7. In the widget Fields panel, enter the numeric Twitch broadcaster ID for the streamer paired to this Stardew installation.
8. Save the widget and add the StreamElements overlay URL to OBS as a Browser Source.

The widget connects to:

`wss://spectrum-game-mods.rydrakeandlonk.workers.dev/overlay/stardew/{broadcaster_id}`

## Behaviour

- The challenge name and death total are loaded from the paired mod's local config snapshot when the overlay connects.
- Each newly detected local-player death updates the counter immediately.
- The counter survives game restarts, save changes and separate streaming sessions because the mod stores the series values in its local SMAPI config.
- The mod's GMCM page lets the streamer rename the series or manually override the death total. Saving sends the new snapshot to the overlay.
- Spawn requests start the wheel animation; successful spawn events reveal the confirmed monster names.
- The widget reconnects automatically after temporary WebSocket interruptions.
- The widget uses browser memory only. It does not call `SE_API.store`, poll the gateway, or write D1/KV/Durable Object storage.

## Layout

- The challenge counter stays visible in the bottom-right corner.
- Spawn events appear temporarily near the top-centre.
- The accent colour, result display duration, location label and death-counter visibility are configurable in Fields.

Each streamer uses their own numeric broadcaster ID. The public feed is read-only and scoped to that streamer's paired game session.
