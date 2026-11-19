# Spectrum Stardew StreamElements Widget

The bundled widget displays two live features from the Spectrum Stardew mod:

- A temporary Stardew Valley textbox that slides in when the challenge death total changes.
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

- The challenge name and death total are loaded from the paired mod's local config snapshot when the overlay connects. The textbox remains hidden for this initial snapshot.
- Each newly detected local-player death updates the total immediately and shows the textbox for four seconds. Further deaths while it is visible reset the hide timer.
- The counter survives game restarts, save changes and separate streaming sessions because the mod stores the series values in its local SMAPI config.
- The mod's GMCM page lets the streamer rename the series or manually override the death total. Saving sends the new snapshot to the overlay.
- Spawn requests start the wheel animation; successful spawn events reveal the confirmed monster names.
- The widget reconnects automatically after temporary WebSocket interruptions.
- The death textbox uses the Stardew Valley font hosted in `custom-stream-elements-assets` and the supplied Stardew textbox border CSS. The widget uses browser memory only. It does not call `SE_API.store`, poll the gateway, or write D1/KV/Durable Object storage.

## Layout

- The challenge textbox stays hidden until a new death is recorded, then slides in at the bottom-right for four seconds before sliding away.
- Spawn events appear temporarily near the top-centre.
- The accent colour, result display duration, location label and death-counter visibility are configurable in Fields.

Each streamer uses their own numeric broadcaster ID. The public feed is read-only and scoped to that streamer's paired game session.
