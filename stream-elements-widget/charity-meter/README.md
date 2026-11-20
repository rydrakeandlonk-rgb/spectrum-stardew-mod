# Spectrum Shared Charity Meter — StreamElements Widget

This is a standalone StreamElements Custom Widget for the shared Spectrum AUD charity meter. It is independent of the Stardew widget and can be used on any Spectrum channel.

## Install

1. In StreamElements, open the stream overlay and add a **Custom Widget**.
2. Copy `widget.html`, `widget.css`, `widget.js`, and `fields.json` from this folder into their matching editor tabs.
3. Save the widget and add the overlay to OBS as a Browser Source.
4. Set the accent colour and title in Fields if desired.

The widget connects to the shared read-only WebSocket feed:

`wss://spectrum-event-hub.rydrakeandlonk.workers.dev/api/charity/overlay`

No broadcaster ID or campaign name is required. All active Spectrum broadcaster campaigns contribute to the same meter. The widget updates from server-pushed WebSocket messages; it does not poll, call `SE_API.store`, or write to D1, KV, or Durable Object storage.

Only the official Twitch Charity Tool donation event is used by the backend. The widget never reads Bits, Power-ups, subscriptions, StreamElements tips, or direct streamer payments.
