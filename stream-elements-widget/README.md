# Spectrum Stardew StreamElements Widget

This folder contains the source for the Spectrum Stardew monster-spawn overlay.

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

- A `stardew.monster_spawn_requested` event starts the spinning-wheel animation when the gateway sends a redemption to the connected game.
- A `stardew.monster_spawned` event stops the wheel and reveals the actual monster or monsters confirmed by the game.
- The widget reconnects automatically after a temporary WebSocket interruption.
- The widget uses browser memory only. It does not call `SE_API.store`, use D1/KV, or poll the gateway.
- The feed is read-only. The paired game host is the only publisher.

The broadcaster ID is a numeric Twitch user ID, not the Twitch username. Each streamer uses their own ID so their overlay receives only their own Stardew events.

## Notes

The overlay animation is a reveal effect; it does not choose the monster. The Stardew game remains authoritative and the widget displays only the result reported by the mod after the full spawn succeeds.
