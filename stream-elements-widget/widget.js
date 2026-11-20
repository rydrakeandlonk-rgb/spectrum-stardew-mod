(() => {
  "use strict";

  const GATEWAY = "wss://spectrum-game-mods.rydrakeandlonk.workers.dev/overlay/stardew/";
  const root = document.getElementById("spectrum-stardew-widget");
  const setupHint = document.getElementById("spectrum-stardew-setup");
  const counter = document.getElementById("death-counter");
  const challengeNameElement = document.getElementById("challenge-name");
  const deathCountElement = document.getElementById("death-count");
  const connectionElement = document.getElementById("counter-connection");
  const eventPanel = document.getElementById("event-panel");
  const wheel = document.getElementById("summon-wheel");
  const wheelSegments = document.querySelector("#summon-wheel .wheel-segments");
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
  const seenDeathEventIds = new Set();
  let isUnloading = false;

  function clearDisplayTimers() {
    if (hideTimer) clearTimeout(hideTimer);
    if (requestTimer) clearTimeout(requestTimer);
    if (hideAnimationTimer) clearTimeout(hideAnimationTimer);
    hideTimer = null;
    requestTimer = null;
    hideAnimationTimer = null;
  }

  function showEvent() {
    if (hideAnimationTimer) clearTimeout(hideAnimationTimer);
    hideAnimationTimer = null;
    eventPanel.classList.remove("is-hidden");
    requestAnimationFrame(() => eventPanel.classList.add("is-visible"));
  }

  function hideEvent() {
    eventPanel.classList.remove("is-visible");
    if (hideAnimationTimer) clearTimeout(hideAnimationTimer);
    hideAnimationTimer = setTimeout(() => {
      eventPanel.classList.add("is-hidden");
      hideAnimationTimer = null;
    }, 240);
  }

  function setDeathCount(value, animate) {
    if (!Number.isInteger(value) || value < 0 || value > 1000000) return;
    deathCountElement.textContent = value.toLocaleString();
    if (animate) {
      counter.classList.remove("death-bump");
      void counter.offsetWidth;
      counter.classList.add("death-bump");
    }
  }

  function applyChallengeState(payload) {
    if (!payload || typeof payload !== "object") return;
    if (typeof payload.challenge_name === "string" && payload.challenge_name.trim()) {
      challengeNameElement.textContent = payload.challenge_name.slice(0, 80);
    }
    setDeathCount(payload.death_count, false);
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
    showEvent();

    requestTimer = setTimeout(() => {
      if (activeEventId !== id) return;
      title.textContent = "AWAITING GAME CONFIRMATION";
      viewer.textContent = "The game is still processing this spawn";
      monsters.textContent = "";
      stateLabel.textContent = "PENDING";
      hideTimer = setTimeout(hideEvent, 7000);
    }, 25000);
  }

  function showCommunityRoulette(event) {
    const id = String(event.event_id || "");
    if (!id || completedEventIds.has(id) || seenRequestIds.has(id)) return;
    seenRequestIds.add(id);
    if (seenRequestIds.size > 200) seenRequestIds.delete(seenRequestIds.values().next().value);

    const weights = event.payload?.weights || {};
    const nothing = Math.max(0, Number(weights.nothing) || 0);
    const single = Math.max(0, Number(weights.single) || 0);
    const horde = Math.max(0, Number(weights.horde) || 0);
    const total = nothing + single + horde;
    if (!Number.isFinite(total) || total <= 0) return;

    const nothingEnd = 360 * nothing / total;
    const singleEnd = nothingEnd + 360 * single / total;
    wheelSegments.style.background = `conic-gradient(
      #64748b 0deg ${nothingEnd}deg,
      #78a86b ${nothingEnd}deg ${singleEnd}deg,
      #a65ac7 ${singleEnd}deg 360deg
    )`;
    wheelSegments.style.transform = "rotate(0deg)";

    activeEventId = id;
    clearDisplayTimers();
    wheel.classList.remove("is-landed");
    wheel.classList.add("is-spinning");
    wheelSymbol.textContent = "?";
    title.textContent = "COMMUNITY ROULETTE";
    viewer.textContent = "Chat has spun the shared wheel";
    monsters.textContent = "The wheel is deciding...";
    locationLabel.textContent = "";
    stateLabel.textContent = "SPINNING";
    showEvent();

    const outcome = String(event.payload?.outcome || "");
    const start = outcome === "nothing" ? 0
      : outcome === "single" ? nothingEnd
      : outcome === "horde" ? singleEnd : NaN;
    const size = outcome === "nothing" ? nothingEnd
      : outcome === "single" ? singleEnd - nothingEnd
      : outcome === "horde" ? 360 - singleEnd : 0;
    if (!Number.isFinite(start) || size <= 0) {
      monsters.textContent = "The wheel result was unavailable";
      stateLabel.textContent = "ERROR";
      return;
    }

    const rotation = 360 * 5 + (360 - (start + size / 2));
    window.setTimeout(() => {
      if (activeEventId !== id) return;
      wheel.classList.remove("is-spinning");
      wheel.classList.add("is-landed");
      wheelSegments.style.transform = `rotate(${rotation}deg)`;
      wheelSymbol.textContent = outcome === "nothing" ? "·" : outcome === "single" ? "!" : "!!";
      title.textContent = "COMMUNITY ROULETTE";
      viewer.textContent = Number(event.payload?.eligible_players) > 0
        ? "For " + Number(event.payload.eligible_players) + " enabled player(s)"
        : "Shared Spectrum event";
      monsters.textContent = outcome === "nothing"
        ? "Nothing this time"
        : outcome === "single"
          ? "One enemy for every enabled player!"
          : "A HORDE for every enabled player!";
      stateLabel.textContent = outcome === "nothing" ? "NO SPAWN"
        : outcome === "single" ? "SINGLE ENEMY" : "HORDE";
      const duration = Math.max(3, Math.min(15, Number(fields.displaySeconds) || 7));
      hideTimer = setTimeout(() => {
        hideEvent();
        activeEventId = "";
      }, duration * 1000);
    }, 2300);
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
    showEvent();

    const duration = Math.max(3, Math.min(15, Number(fields.displaySeconds) || 7));
    hideTimer = setTimeout(() => {
      hideEvent();
      activeEventId = "";
    }, duration * 1000);
  }

  function handleMessage(raw) {
    let message;
    try { message = JSON.parse(raw); } catch { return; }

    if (message.type === "overlay.ready") {
      connectionElement.textContent = "LIVE CONNECTION";
      reconnectDelay = 1000;
      return;
    }

    if (message.type !== "stream.event" || message.game !== "stardew-valley") return;

    if (message.event_name === "stardew.community_roulette") {
      showCommunityRoulette(message);
    } else if (message.event_name === "stardew.monster_spawn_requested") {
      showSpawnRequested(message);
    } else if (message.event_name === "stardew.monster_spawned") {
      showSpawned(message);
    } else if (message.event_name === "stardew.challenge_state") {
      applyChallengeState(message.payload);
    } else if (message.event_name === "stardew.player_died") {
      const id = String(message.event_id || "");
      if (id && seenDeathEventIds.has(id)) return;
      if (id) {
        seenDeathEventIds.add(id);
        if (seenDeathEventIds.size > 200) seenDeathEventIds.delete(seenDeathEventIds.values().next().value);
      }
      setDeathCount(message.payload?.death_count, true);
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
    root.classList.remove("is-hidden");
    try {
      socket = new WebSocket(GATEWAY + encodeURIComponent(broadcasterId));
    } catch {
      connectionElement.textContent = "RECONNECTING";
      scheduleReconnect();
      return;
    }

    socket.addEventListener("open", () => {
      reconnectDelay = 1000;
      connectionElement.textContent = "CONNECTED";
    });
    socket.addEventListener("message", event => handleMessage(event.data));
    socket.addEventListener("close", () => {
      socket = null;
      connectionElement.textContent = "RECONNECTING";
      scheduleReconnect();
    });
    socket.addEventListener("error", () => {
      try { socket?.close(); } catch {}
    });
  }

  window.addEventListener("onWidgetLoad", event => {
    fields = event.detail?.fieldData || {};
    const showCounter = fields.showDeathCounter !== false && fields.showDeathCounter !== "false";
    if (!showCounter) counter.classList.add("is-hidden");
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
