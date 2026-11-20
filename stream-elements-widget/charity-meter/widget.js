(() => {
  "use strict";

  const FEED_URL = "wss://spectrum-event-hub.rydrakeandlonk.workers.dev/api/charity/overlay";
  const card = document.querySelector(".charity-card");
  const title = document.getElementById("charity-title");
  const amount = document.getElementById("charity-amount");
  const percent = document.getElementById("charity-percent");
  const fill = document.getElementById("charity-fill");
  const track = document.querySelector(".charity-track");
  const connection = document.getElementById("charity-connection");
  const alert = document.getElementById("charity-alert");

  let fields = {};
  let socket = null;
  let reconnectTimer = null;
  let alertTimer = null;
  let reconnectDelay = 1000;
  let unloaded = false;

  function render(state) {
    if (!state || state.currency !== "AUD") return;
    const cents = Math.max(0, Math.min(10000, Number(state.meter_cents) || 0));
    const value = cents / 100;
    const ratio = cents / 10000;
    amount.textContent = "$" + value.toFixed(2);
    percent.textContent = Math.round(ratio * 100) + "%";
    fill.style.width = (ratio * 100) + "%";
    track.setAttribute("aria-valuenow", String(Math.round(ratio * 100)));
  }

  function showMilestone(message) {
    if (fields.showMilestoneAlert === false || fields.showMilestoneAlert === "false") return;
    const mega = message.event_type === "charity.milestone.mega";
    alert.textContent = mega ? "MEGA HORDE!" : "HORDE!";
    card.classList.remove("is-flashing", "is-mega");
    void card.offsetWidth;
    card.classList.add("is-flashing");
    if (mega) card.classList.add("is-mega");
    if (alertTimer) clearTimeout(alertTimer);
    alertTimer = setTimeout(() => {
      alert.textContent = "";
      card.classList.remove("is-flashing", "is-mega");
      alertTimer = null;
    }, 4200);
  }

  function handleMessage(raw) {
    let message;
    try { message = JSON.parse(raw); } catch { return; }
    if (message.type === "charity.state" || message.type === "charity.progress") {
      render(message.state);
      connection.textContent = "LIVE";
      return;
    }
    if (message.type === "charity.milestone") showMilestone(message);
  }

  function scheduleReconnect() {
    if (unloaded || reconnectTimer) return;
    reconnectTimer = setTimeout(() => {
      reconnectTimer = null;
      connect();
    }, reconnectDelay);
    reconnectDelay = Math.min(30000, Math.round(reconnectDelay * 1.8));
  }

  function connect() {
    if (unloaded) return;
    try { socket = new WebSocket(FEED_URL); }
    catch {
      connection.textContent = "RECONNECTING";
      scheduleReconnect();
      return;
    }
    socket.addEventListener("open", () => {
      reconnectDelay = 1000;
      connection.textContent = "CONNECTED";
    });
    socket.addEventListener("message", event => handleMessage(event.data));
    socket.addEventListener("close", () => {
      socket = null;
      connection.textContent = "RECONNECTING";
      scheduleReconnect();
    });
    socket.addEventListener("error", () => {
      try { socket?.close(); } catch {}
    });
  }

  window.addEventListener("onWidgetLoad", event => {
    fields = event.detail?.fieldData || {};
    if (typeof fields.titleText === "string" && fields.titleText.trim()) {
      title.textContent = fields.titleText.slice(0, 48);
    }
    connect();
  });

  window.addEventListener("onWidgetUnload", () => {
    unloaded = true;
    if (reconnectTimer) clearTimeout(reconnectTimer);
    if (alertTimer) clearTimeout(alertTimer);
    try { socket?.close(); } catch {}
    socket = null;
  });
})();
