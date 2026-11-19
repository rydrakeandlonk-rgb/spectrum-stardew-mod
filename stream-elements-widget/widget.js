(() => {
  "use strict";

  const GATEWAY = "wss://spectrum-game-mods.rydrakeandlonk.workers.dev/overlay/stardew/";
  const root = document.getElementById("spectrum-stardew-widget");
  const setupHint = document.getElementById("spectrum-stardew-setup");
  const wheel = document.getElementById("summon-wheel");
  const wheelSymbol = document.getElementById("wheel-symbol");
  const title = document.getElementById("event-title");
  const viewer = document.getElementById("event-viewer");
  const monsters = document.getElementById("event-monsters");
  const locationLabel = document.getElementById("event-location");
  const stateLabel = document.getElementById("event-state");

  let fields = {};
  let socket = null;
  let reconnectTimer = null;
  let hideTimer = null;
  let requestTimer = null;
  let hideAnimationTimer = null;
  let reconnectDelay = 1000;
  let activeEventId = "";
  const seenRequestIds = new Set();
  const completedEventIds = new Set();
  let isUnloading = false;

  function clearDisplayTimers() {
    if (hideTimer) clearTimeout(hideTimer);
    if (requestTimer) clearTimeout(requestTimer);
    if (hideAnimationTimer) clearTimeout(hideAnimationTimer);
    hideTimer = null;
    requestTimer = null;
    hideAnimationTimer = null;
  }

  function show() {
    if (hideAnimationTimer) clearTimeout(hideAnimationTimer);
    hideAnimationTimer = null;
    root.classList.remove("is-hidden");
    requestAnimationFrame(() => root.classList.add("is-visible"));
  }

  function hide() {
    root.classList.remove("is-visible");
    if (hideAnimationTimer) clearTimeout(hideAnimationTimer);
    hideAnimationTimer = setTimeout(() => {
      root.classList.add("is-hidden");
      hideAnimationTimer = null;
    }, 240);
  }

  function formatMonsterNames(names) {
    const clean = (Array.isArray(names) ? names : [])
      .filter(name => typeof name === "string" && name.trim())
      .slice(0, 10);
    if (clean.length <= 3) return clean.join("  ·  ");
    return clean.slice(0, 3).join("  ·  ") + "  +" + (clean.length - 3) + " more";
  }

  function showSpawnRequested(event) {
    const id = String(event.event_id || "");
    if (!id || seenRequestIds.has(id) || completedEventIds.has(id)) return;
    seenRequestIds.add(id);
    if (seenRequestIds.size > 200) seenRequestIds.delete(seenRequestIds.values().next().value);

    activeEventId = id;
    clearDisplayTimers();
    wheel.classList.remove("is-landed");
    wheel.classList.add("is-spinning");
    wheelSymbol.textContent = "?";
    title.textContent = "CHAT SUMMONED";
    viewer.textContent = (event.payload?.redeemer || "A viewer") + " is summoning a monster";
    monsters.textContent = "Something is coming...";
    locationLabel.textContent = "";
    stateLabel.textContent = "IN PROGRESS";
    show();

    requestTimer = setTimeout(() => {
      if (activeEventId !== id) return;
      title.textContent = "AWAITING GAME CONFIRMATION";
      viewer.textContent = "The game is still processing this spawn";
      monsters.textContent = "";
      stateLabel.textContent = "PENDING";
      hideTimer = setTimeout(hide, 7000);
    }, 25000);
  }

  function showSpawned(event) {
    const id = String(event.event_id || "");
    if (id && completedEventIds.has(id)) return;

    activeEventId = id;
    if (id) {
      completedEventIds.add(id);
      if (completedEventIds.size > 200) completedEventIds.delete(completedEventIds.values().next().value);
    }
    clearDisplayTimers();
    wheel.classList.remove("is-spinning");
    wheel.classList.add("is-landed");
    wheelSymbol.textContent = "!";
    title.textContent = "SPAWN CONFIRMED";
    viewer.textContent = (event.payload?.redeemer || "A viewer") + " spawned:";
    monsters.textContent = formatMonsterNames(event.payload?.monster_names) || "Monster spawned";
    const location = String(event.payload?.location || "");
    const showLocation = fields.showLocation !== false && fields.showLocation !== "false";
    locationLabel.textContent = !showLocation || !location ? "" : "LOCATION  ·  " + location;
    stateLabel.textContent = event.payload?.is_horde ? "HORDE" : "CONFIRMED";
    show();

    const duration = Math.max(3, Math.min(15, Number(fields.displaySeconds) || 7));
    hideTimer = setTimeout(() => {
      hide();
      activeEventId = "";
    }, duration * 1000);
  }

  function handleMessage(raw) {
    let message;
    try { message = JSON.parse(raw); } catch { return; }

    if (message.type === "overlay.ready") {
      stateLabel.textContent = "CONNECTED";
      reconnectDelay = 1000;
      return;
    }

    if (message.type !== "stream.event" || message.game !== "stardew-valley") return;

    if (message.event_name === "stardew.monster_spawn_requested") {
      showSpawnRequested(message);
    } else if (message.event_name === "stardew.monster_spawned") {
      showSpawned(message);
    }
  }

  function scheduleReconnect() {
    if (isUnloading || reconnectTimer) return;
    reconnectTimer = setTimeout(() => {
      reconnectTimer = null;
      connect();
    }, reconnectDelay);
    reconnectDelay = Math.min(30000, Math.round(reconnectDelay * 1.8));
  }

  function connect() {
    if (isUnloading) return;
    const broadcasterId = String(fields.broadcasterId || "").trim();
    if (!/^\d{1,30}$/.test(broadcasterId)) {
      setupHint.classList.remove("is-hidden");
      root.classList.add("is-hidden");
      return;
    }

    setupHint.classList.add("is-hidden");
    try {
      socket = new WebSocket(GATEWAY + encodeURIComponent(broadcasterId));
    } catch {
      scheduleReconnect();
      return;
    }

    socket.addEventListener("open", () => {
      reconnectDelay = 1000;
      stateLabel.textContent = "CONNECTED";
    });
    socket.addEventListener("message", event => handleMessage(event.data));
    socket.addEventListener("close", () => {
      socket = null;
      scheduleReconnect();
    });
    socket.addEventListener("error", () => {
      try { socket?.close(); } catch {}
    });
  }

  window.addEventListener("onWidgetLoad", event => {
    fields = event.detail?.fieldData || {};
    connect();
  });

  window.addEventListener("onWidgetUnload", () => {
    isUnloading = true;
    clearDisplayTimers();
    if (reconnectTimer) clearTimeout(reconnectTimer);
    try { socket?.close(); } catch {}
    socket = null;
  });
})();
