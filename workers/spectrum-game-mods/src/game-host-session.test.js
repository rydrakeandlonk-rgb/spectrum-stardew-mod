import test from "node:test";
import assert from "node:assert/strict";
import { GameHostSession } from "./game-host-session.js";

class MemoryStorage {
  values = new Map();

  async get(key) { return this.values.get(key); }
  async put(key, value) { this.values.set(key, value); }
  async delete(key) { return this.values.delete(key); }

  async list({ prefix = "" } = {}) {
    return new Map([...this.values.entries()].filter(([key]) => key.startsWith(prefix)));
  }

  async transaction(callback) {
    return callback(this);
  }
}

function makeSession(env = {}) {
  const storage = new MemoryStorage();
  const sockets = [];
  const ctx = { storage, getWebSockets: () => sockets };
  const session = new GameHostSession(ctx, env);
  return { session, storage, sockets };
}

async function hash(value) {
  const digest = await crypto.subtle.digest("SHA-256", new TextEncoder().encode(value));
  return [...new Uint8Array(digest)].map(byte => byte.toString(16).padStart(2, "0")).join("");
}

function request(path, method = "GET", body) {
  return new Request("https://session.test" + path, {
    method,
    headers: body === undefined ? {} : { "Content-Type": "application/json" },
    body: body === undefined ? undefined : JSON.stringify(body)
  });
}

test("new host sessions report spawning stopped with an empty queue", async () => {
  const { session } = makeSession();
  const response = await session.fetch(request("/status"));
  assert.equal(response.status, 200);
  assert.deepEqual(await response.json(), {
    connected: false,
    spawns_enabled: false,
    effective_spawns_enabled: false,
    safety_gate_enabled: false,
    settings: { spawn_enabled: true, difficulty: "normal", chat_responses: true, debug_messages: false },
    settings_revision: 1,
    spawn_override: "follow",
    pending_count: 0,
    last_action: null,
    session_id: "",
    pool_overrides: {
      Mines: "automatic",
      DangerousMines: "automatic",
      QuarryMine: "automatic",
      SkullCavern: "automatic",
      DangerousSkullCavern: "automatic",
      BugLair: "automatic",
      Farm: "automatic",
      VolcanoDungeon: "automatic"
    },
    pool_overrides_revision: 1,
    pool_diagnostics: null,
    pool_diagnostics_updated_at: 0,
    session_role: "host",
    session_host_broadcaster_id: "",
    session_host_session_id: ""
  });
});


test("pool overrides are validated, versioned and pushed to the active host session", async () => {
  const { session, storage, sockets } = makeSession();
  await storage.put("active_session_id", "session-1");
  await storage.put("active_connection_id", "connection-1");
  const sent = [];
  sockets.push({
    deserializeAttachment: () => ({ broadcasterId: "12345", sessionId: "session-1", connectionId: "connection-1", role: "host" }),
    send: value => sent.push(JSON.parse(value))
  });

  let response = await session.fetch(request("/pool-overrides", "POST", {
    overrides: { SkullCavern: "force_disabled", Farm: "automatic" }
  }));
  assert.equal(response.status, 200);
  const result = await response.json();
  assert.equal(result.success, true);
  assert.equal(result.session_id, "session-1");
  assert.equal(result.revision, 2);
  assert.equal(result.overrides.SkullCavern, "force_disabled");
  assert.equal(result.overrides.Farm, "automatic");
  assert.equal(sent.length, 1);
  assert.equal(sent[0].type, "spawn.pools.state");
  assert.equal(sent[0].session_id, "session-1");
  assert.equal(sent[0].overrides.SkullCavern, "force_disabled");

  response = await session.fetch(request("/pool-overrides", "POST", {
    overrides: { NotARealPool: "force_enabled" }
  }));
  assert.equal(response.status, 400);
  assert.equal((await storage.get("pool_overrides")).SkullCavern, "force_disabled");
});

test("pool diagnostics are accepted only from the active host session", async () => {
  const { session, storage } = makeSession();
  await storage.put("active_session_id", "session-current");
  await storage.put("active_connection_id", "connection-current");
  const host = {
    deserializeAttachment: () => ({ broadcasterId: "12345", sessionId: "session-current", connectionId: "connection-current", role: "host" }),
    send() {}
  };
  const diagnostics = {
    progression: { skull_key_obtained: false, stage: "Early" },
    pools: [{ Pool: "SkullCavern", ExpectedEnabled: false, EffectiveEnabled: false, Override: "automatic", Reason: "Skull Key not obtained.", DefinitionCount: 8 }],
    recent_decisions: [{ Monster: "serpent", Pool: "SkullCavern", Outcome: "rejected", Reason: "Pool locked by progression.", RecordedAt: "2026-11-21T00:00:00Z" }]
  };

  await session.webSocketMessage(host, JSON.stringify({
    type: "pool.diagnostics",
    session_id: "session-current",
    diagnostics
  }));
  assert.deepEqual(await storage.get("pool_diagnostics"), diagnostics);

  await session.webSocketMessage(host, JSON.stringify({
    type: "pool.diagnostics",
    session_id: "session-old",
    diagnostics: { progression: {}, pools: [] }
  }));
  assert.deepEqual(await storage.get("pool_diagnostics"), diagnostics);
});

test("farmhand session context is recorded separately from the farmhand connection session", async () => {
  const { session, storage } = makeSession();
  await storage.put("active_session_id", "farmhand-connection");
  await storage.put("active_connection_id", "farmhand-socket");
  const farmhandSocket = {
    deserializeAttachment: () => ({ broadcasterId: "22222", sessionId: "farmhand-connection", connectionId: "farmhand-socket", role: "host" }),
    send() {}
  };

  await session.webSocketMessage(farmhandSocket, JSON.stringify({
    type: "session.context",
    host_broadcaster_id: "11111",
    host_session_id: "abcdef0123456789abcdef0123456789"
  }));
  assert.equal(await storage.get("session_role"), "client");
  assert.equal(await storage.get("session_host_broadcaster_id"), "11111");
  assert.equal(await storage.get("session_host_session_id"), "abcdef0123456789abcdef0123456789");
});

test("spawning cannot be enabled until a host is connected", async () => {
  const { session, storage } = makeSession();
  const response = await session.fetch(request("/spawns", "POST", { enabled: true }));
  assert.equal(response.status, 409);
  assert.equal(await storage.get("spawns_enabled"), undefined);
});

test("redemptions remain queued while stopped and flush oldest-first after Start Spawns", async () => {
  const { session, storage, sockets } = makeSession();
  await storage.put("host_token_hash", "a".repeat(64));
  await storage.put("broadcaster_user_id", "12345");
  await storage.put("revoked", false);

  const now = Date.now();
  for (const [id, receivedAt] of [["older", now - 2000], ["newer", now - 1000]]) {
    await storage.put("redemption:" + id, { received_at: receivedAt });
    await storage.put("pending:" + id, {
      type: "game.command",
      session_id: "session-1",
      command_id: id,
      received_at: receivedAt,
      game: "stardew-valley",
      name: "stardew.spawn_monster",
      payload: {}
    });
  }

  const sent = [];
  sockets.push({ send: value => sent.push(JSON.parse(value)) });
  await storage.put("active_session_id", "session-1");
  await storage.put("spawns_enabled", false);

  const start = await session.fetch(request("/spawns", "POST", { enabled: true }));
  assert.equal(start.status, 200);
  assert.deepEqual(sent.map(message => message.type), ["settings.state", "spawns.state", "game.command", "game.command"]);
  assert.equal(sent[1].enabled, true);
  assert.deepEqual(sent.slice(2).map(message => message.command_id), ["older", "newer"]);
  assert.equal((await (await session.fetch(request("/status"))).json()).pending_count, 2);
});

test("flushing a redemption announces the in-progress spawn to connected overlays", async () => {
  const { session, storage, sockets } = makeSession();
  await storage.put("host_token_hash", "a".repeat(64));
  await storage.put("broadcaster_user_id", "12345");
  await storage.put("revoked", false);
  await storage.put("active_session_id", "session-1");
  await storage.put("spawns_enabled", false);
  await storage.put("settings", { spawn_enabled: true, difficulty: "normal", chat_responses: true, debug_messages: false });
  await storage.put("spawn_override", "follow");
  await storage.put("redemption:redeem-1", { received_at: Date.now() });
  await storage.put("pending:redeem-1", {
    type: "game.command",
    session_id: "session-1",
    command_id: "redeem-1",
    received_at: Date.now(),
    game: "stardew-valley",
    name: "stardew.spawn_monster",
    payload: { user_name: "viewer", reward_title: "Spawn a Monster" }
  });
  const hostMessages = [];
  const overlayMessages = [];
  sockets.push({
    deserializeAttachment: () => ({ broadcasterId: "12345", sessionId: "session-1", role: "host" }),
    send: value => hostMessages.push(JSON.parse(value))
  }, {
    deserializeAttachment: () => ({ broadcasterId: "12345", role: "overlay" }),
    send: value => overlayMessages.push(JSON.parse(value))
  });

  await session.fetch(request("/spawns", "POST", { enabled: true }));

  assert.equal(hostMessages.some(message => message.type === "game.command"), true);
  assert.equal(overlayMessages.length, 1);
  assert.equal(overlayMessages[0].type, "stream.event");
  assert.equal(overlayMessages[0].event_name, "stardew.monster_spawn_requested");
  assert.equal(overlayMessages[0].event_id, "redeem-1");
  assert.equal(overlayMessages[0].payload.redeemer, "viewer");
  assert.equal(await storage.get("stream.event:redeem-1"), undefined);
});

test("Stop Spawns broadcasts the paused state without deleting pending redemptions", async () => {
  const { session, storage, sockets } = makeSession();
  await storage.put("active_session_id", "session-1");
  await storage.put("spawns_enabled", true);
  await storage.put("pending:kept", { received_at: Date.now() });
  const sent = [];
  sockets.push({ send: value => sent.push(JSON.parse(value)) });

  const response = await session.fetch(request("/spawns", "POST", { enabled: false }));
  assert.equal(response.status, 200);
  assert.deepEqual(sent.map(message => message.type), ["settings.state", "spawns.state"]);
  assert.equal(sent[1].enabled, false);
  assert.equal(await storage.get("spawns_enabled"), false);
  assert.equal((await storage.list({ prefix: "pending:" })).size, 1);
});

test("disconnect resets the spawn state to stopped", async () => {
  const { session, storage } = makeSession();
  await storage.put("active_session_id", "session-1");
  await storage.put("active_connection_id", "connection-1");
  await storage.put("spawns_enabled", true);
  const socket = {
    deserializeAttachment: () => ({ broadcasterId: "12345", sessionId: "session-1", connectionId: "connection-1" }),
    close() {}
  };

  await session.webSocketClose(socket);
  assert.equal(await storage.get("active_session_id"), undefined);
  assert.equal(await storage.get("spawns_enabled"), false);
});


test("streamer settings are versioned and the administrator override controls effective preference", async () => {
  const { session, storage, sockets } = makeSession();
  await storage.put("active_session_id", "session-1");
  await storage.put("spawns_enabled", true);
  await storage.put("settings", { spawn_enabled: false, difficulty: "normal", chat_responses: true, debug_messages: false });
  await storage.put("settings_revision", 4);
  const sent = [];
  sockets.push({ send: value => sent.push(JSON.parse(value)) });

  let response = await session.fetch(request("/status"));
  let status = await response.json();
  assert.equal(status.safety_gate_enabled, true);
  assert.equal(status.effective_spawns_enabled, false);
  assert.equal(status.settings_revision, 4);

  response = await session.fetch(request("/override", "POST", { mode: "force_on" }));
  status = await response.json();
  assert.equal(status.spawn_override, "force_on");
  assert.equal(status.effective_spawns_enabled, true);

  response = await session.fetch(request("/override", "POST", { mode: "force_off" }));
  status = await response.json();
  assert.equal(status.effective_spawns_enabled, false);

  response = await session.fetch(request("/override", "POST", { mode: "follow" }));
  status = await response.json();
  assert.equal(status.effective_spawns_enabled, false);

  response = await session.fetch(request("/settings", "POST", {
    settings: { spawn_enabled: true, difficulty: "hard", chat_responses: false, debug_messages: true },
    spawn_override: "force_on"
  }));
  status = await response.json();
  assert.equal(status.settings_revision, 5);
  assert.deepEqual(status.settings, {
    spawn_enabled: true, difficulty: "hard", chat_responses: false, debug_messages: true
  });
  assert.equal(status.spawn_override, "force_on");
  assert.equal(status.spawns_enabled, true);
  assert.ok(sent.some(message => message.type === "settings.state"));
});

test("the global safety gate cannot be bypassed by Force enabled", async () => {
  const { session, storage } = makeSession();
  await storage.put("active_session_id", "session-1");
  await storage.put("spawns_enabled", false);
  await storage.put("spawn_override", "force_on");
  await storage.put("settings", { ...{ spawn_enabled: false, difficulty: "normal", chat_responses: true, debug_messages: false } });

  const response = await session.fetch(request("/status"));
  const status = await response.json();
  assert.equal(status.spawn_override, "force_on");
  assert.equal(status.safety_gate_enabled, false);
  assert.equal(status.effective_spawns_enabled, false);
});

test("the authenticated host can pause and resume spawning from the game without losing queued redemptions", async () => {
  const { session, storage, sockets } = makeSession();
  await storage.put("active_session_id", "session-1");
  await storage.put("spawns_enabled", false);
  await storage.put("settings", { spawn_enabled: true, difficulty: "normal", chat_responses: true, debug_messages: false });
  await storage.put("spawn_override", "follow");
  await storage.put("pending:queued-1", {
    type: "game.command",
    session_id: "session-1",
    command_id: "queued-1",
    received_at: Date.now(),
    game: "stardew-valley",
    name: "stardew.spawn_monster",
    payload: { user_name: "viewer", reward_title: "Spawn a Monster" }
  });
  const sent = [];
  const host = {
    deserializeAttachment: () => ({ broadcasterId: "12345", sessionId: "session-1", role: "host" }),
    send: value => sent.push(JSON.parse(value))
  };
  sockets.push(host);

  await session.webSocketMessage(host, JSON.stringify({ type: "spawns.set", enabled: true }));
  assert.equal(await storage.get("spawns_enabled"), true);
  assert.deepEqual(sent.map(message => message.type), ["settings.state", "spawns.state", "game.command"]);
  assert.equal(sent[1].enabled, true);
  assert.equal(sent[2].command_id, "queued-1");

  sent.length = 0;
  await session.webSocketMessage(host, JSON.stringify({ type: "spawns.set", enabled: false }));
  assert.equal(await storage.get("spawns_enabled"), false);
  assert.deepEqual(sent.map(message => message.type), ["settings.state", "spawns.state"]);
  assert.equal((await storage.list({ prefix: "pending:" })).size, 1);
});

test("the in-game toggle cannot bypass an administrator Force disabled override", async () => {
  const { session, storage, sockets } = makeSession();
  await storage.put("active_session_id", "session-1");
  await storage.put("spawns_enabled", false);
  await storage.put("spawn_override", "force_off");
  await storage.put("settings", { spawn_enabled: true, difficulty: "normal", chat_responses: true, debug_messages: false });
  const sent = [];
  const host = {
    deserializeAttachment: () => ({ broadcasterId: "12345", sessionId: "session-1", role: "host" }),
    send: value => sent.push(JSON.parse(value))
  };
  sockets.push(host);

  await session.webSocketMessage(host, JSON.stringify({ type: "spawns.set", enabled: true }));
  assert.equal(await storage.get("spawns_enabled"), true);
  assert.equal(sent.some(message => message.type === "game.command"), false);
  const state = sent.find(message => message.type === "settings.state");
  assert.equal(state.safety_gate_enabled, true);
  assert.equal(state.effective_spawn_enabled, false);
});

test("stale or non-host sessions cannot change the in-game spawn gate", async () => {
  const { session, storage, sockets } = makeSession();
  await storage.put("active_session_id", "session-current");
  await storage.put("spawns_enabled", false);
  const stale = {
    deserializeAttachment: () => ({ broadcasterId: "12345", sessionId: "session-old", role: "host" }),
    send: () => {}
  };
  const overlay = {
    deserializeAttachment: () => ({ broadcasterId: "12345", role: "overlay" }),
    send: () => {}
  };
  sockets.push(stale, overlay);

  await session.webSocketMessage(stale, JSON.stringify({ type: "spawns.set", enabled: true }));
  await session.webSocketMessage(overlay, JSON.stringify({ type: "spawns.set", enabled: true }));
  assert.equal(await storage.get("spawns_enabled"), false);
});

test("host settings updates require the current revision", async () => {
  const { session, storage, sockets } = makeSession();
  await storage.put("active_session_id", "session-1");
  await storage.put("settings", { spawn_enabled: true, difficulty: "normal", chat_responses: true, debug_messages: false });
  await storage.put("settings_revision", 7);
  const sent = [];
  sockets.push({ send: value => sent.push(JSON.parse(value)) });

  await session.webSocketMessage(sockets[0], JSON.stringify({
    type: "settings.update",
    base_revision: 6,
    settings: { difficulty: "hard" }
  }));
  assert.equal((await storage.get("settings")).difficulty, "normal");
  assert.ok(sent.some(message => message.type === "settings.state" && message.revision === 7));

  sent.length = 0;
  await session.webSocketMessage(sockets[0], JSON.stringify({
    type: "settings.update",
    base_revision: 7,
    settings: { difficulty: "hard" }
  }));
  assert.equal((await storage.get("settings")).difficulty, "hard");
  assert.equal(await storage.get("settings_revision"), 8);
  assert.ok(sent.some(message => message.type === "settings.state" && message.revision === 8));
});

test("random test spawn is sent to a connected host without opening the normal spawn safety gate", async () => {
  const { session, storage, sockets } = makeSession();
  await storage.put("active_session_id", "session-1");
  await storage.put("spawns_enabled", false);
  const sent = [];
  sockets.push({ send: value => sent.push(JSON.parse(value)) });

  const response = await session.fetch(request("/test-spawn", "POST", {}));
  assert.equal(response.status, 202);
  const result = await response.json();
  assert.equal(result.status, "sent");
  assert.equal(sent[0].type, "game.action");
  assert.equal(sent[0].name, "stardew.spawn_random_test");
  assert.equal(sent[0].difficulty, "normal");
  assert.equal(sent[0].action_id, result.action_id);
  assert.equal(await storage.get("spawns_enabled"), false);

  await session.webSocketMessage(sockets[0], JSON.stringify({
    type: "action.ack",
    action_id: result.action_id,
    success: true,
    detail: "Random progression-eligible enemy spawned"
  }));
  const lastAction = await storage.get("last_action");
  assert.equal(lastAction.status, "completed");
  assert.equal(lastAction.name, "stardew.spawn_random_test");
  assert.equal(lastAction.detail, "Random progression-eligible enemy spawned");
});

test("charity horde test actions use shared saved counts and require spawning to be enabled", async () => {
  const sharedSettingsBinding = {
    idFromName: value => value,
    get: id => ({ async fetch() {
      assert.equal(id, "stardew-community:global");
      return Response.json({ settings: { normal_horde_count: 13, mega_horde_count: 27 } });
    } })
  };
  const { session, storage, sockets } = makeSession({ GAME_HOST_SESSION: sharedSettingsBinding });
  await storage.put("broadcaster_user_id", "12345");
  await storage.put("active_session_id", "session-1");
  await storage.put("spawns_enabled", true);
  await storage.put("settings", { spawn_enabled: true, difficulty: "hard", chat_responses: true, debug_messages: false });
  const sent = [];
  sockets.push({
    deserializeAttachment: () => ({ broadcasterId: "12345", sessionId: "session-1", role: "host" }),
    send: value => sent.push(JSON.parse(value))
  });

  let response = await session.fetch(request("/test-horde", "POST", {}));
  assert.equal(response.status, 202);
  let result = await response.json();
  assert.equal(result.monster_count, 13);
  assert.equal(sent[0].name, "stardew.community_horde");
  assert.equal(sent[0].monster_count, 13);
  assert.equal(sent[0].difficulty, "hard");

  response = await session.fetch(request("/test-mega-horde", "POST", {}));
  assert.equal(response.status, 202);
  result = await response.json();
  assert.equal(result.monster_count, 27);
  assert.equal(sent[1].name, "stardew.community_mega_horde");
  assert.equal(sent[1].monster_count, 27);

  await storage.put("spawns_enabled", false);
  response = await session.fetch(request("/test-horde", "POST", {}));
  assert.equal(response.status, 409);
  assert.equal(sent.length, 2);
});

test("clear-monsters sends a typed host action and records its acknowledgement", async () => {
  const { session, storage, sockets } = makeSession();
  await storage.put("active_session_id", "session-1");
  const sent = [];
  sockets.push({ send: value => sent.push(JSON.parse(value)) });

  const response = await session.fetch(request("/clear-monsters", "POST", {}));
  assert.equal(response.status, 202);
  const result = await response.json();
  assert.equal(result.status, "sent");
  assert.equal(sent[0].type, "game.action");
  assert.equal(sent[0].name, "stardew.clear_loaded_monsters");
  assert.equal(sent[0].action_id, result.action_id);

  await session.webSocketMessage(sockets[0], JSON.stringify({
    type: "action.ack",
    action_id: result.action_id,
    success: true,
    removed_count: 6
  }));
  const lastAction = await storage.get("last_action");
  assert.equal(lastAction.status, "completed");
  assert.equal(lastAction.removed_count, 6);
});


test("authenticated host game events are broadcast to read-only overlay sockets without storage writes", async () => {
  const { session, storage, sockets } = makeSession();
  await storage.put("active_session_id", "session-1");
  const overlayMessages = [];
  const host = {
    deserializeAttachment: () => ({ broadcasterId: "12345", sessionId: "session-1", role: "host" }),
    send: () => {}
  };
  const overlay = {
    deserializeAttachment: () => ({ broadcasterId: "12345", role: "overlay" }),
    send: value => overlayMessages.push(JSON.parse(value))
  };
  sockets.push(host, overlay);

  await session.webSocketMessage(host, JSON.stringify({
    type: "game.event",
    event_id: "redemption-123",
    game: "stardew-valley",
    name: "stardew.monster_spawned",
    occurred_at: "2026-11-19T10:00:00Z",
    payload: {
      redeemer: "viewer",
      monster_names: ["Pepper Rex"],
      spawned_count: 1,
      location: "Skull Cavern",
      is_horde: false
    }
  }));

  assert.equal(overlayMessages.length, 1);
  assert.equal(overlayMessages[0].type, "stream.event");
  assert.equal(overlayMessages[0].event_name, "stardew.monster_spawned");
  assert.equal(overlayMessages[0].broadcaster_user_id, "12345");
  assert.equal(overlayMessages[0].payload.monster_names[0], "Pepper Rex");
  assert.equal(await storage.get("stream.event:redemption-123"), undefined);
});

test("game events from stale host sessions and malformed payloads are ignored", async () => {
  const { session, storage, sockets } = makeSession();
  await storage.put("active_session_id", "session-current");
  const sent = [];
  const host = {
    deserializeAttachment: () => ({ broadcasterId: "12345", sessionId: "session-old", role: "host" }),
    send: value => sent.push(JSON.parse(value))
  };
  const overlay = {
    deserializeAttachment: () => ({ broadcasterId: "12345", role: "overlay" }),
    send: value => sent.push(JSON.parse(value))
  };
  sockets.push(host, overlay);

  await session.webSocketMessage(host, JSON.stringify({
    type: "game.event", event_id: "old", game: "stardew-valley",
    name: "stardew.monster_spawned",
    payload: { redeemer: "viewer", monster_names: ["Slime"], spawned_count: 1, location: "Farm" }
  }));
  await session.webSocketMessage({
    deserializeAttachment: () => ({ broadcasterId: "12345", sessionId: "session-current", role: "host" }),
    send: value => sent.push(JSON.parse(value))
  }, JSON.stringify({
    type: "game.event", event_id: "bad", game: "stardew-valley",
    name: "stardew.monster_spawned",
    payload: { redeemer: "viewer", monster_names: [], spawned_count: 1, location: "Farm" }
  }));

  assert.equal(sent.some(message => message.type === "stream.event"), false);
  assert.equal(await storage.get("stream.event:old"), undefined);
});

test("device pairing accepts only the matching short-lived device secret", async () => {
  const { session, storage } = makeSession();
  const deviceCode = "a".repeat(32);
  const userCode = "ABCDEFG2";
  const response = await session.fetch(request("/pairing/start", "POST", {
    user_code: userCode,
    device_code_hash: await hash(deviceCode),
    expires_at: Date.now() + 600000
  }));
  assert.equal(response.status, 200);
  assert.equal((await response.json()).success, true);

  const wrong = await session.fetch(request("/pairing/poll", "POST", {
    device_code: "b".repeat(32)
  }));
  assert.equal(wrong.status, 403);

  const pending = await session.fetch(request("/pairing/poll", "POST", {
    device_code: deviceCode
  }));
  assert.deepEqual(await pending.json(), { status: "pending" });
  assert.equal(await storage.get("pairing_status"), "pending");
});

test("device pairing provisions the broadcaster host and returns its credential only to the paired device", async () => {
  const provisioned = [];
  const env = {
    GAME_HOST_SESSION: {
      idFromName: name => name,
      get: id => ({
        fetch: async (_url, options) => {
          provisioned.push({ id, body: JSON.parse(options.body) });
          return new Response(JSON.stringify({ ok: true }), { status: 200 });
        }
      })
    }
  };
  const { session } = makeSession(env);
  const deviceCode = "c".repeat(32);
  const userCode = "JKLMNPQ3";
  await session.fetch(request("/pairing/start", "POST", {
    user_code: userCode,
    device_code_hash: await hash(deviceCode),
    expires_at: Date.now() + 600000
  }));

  const authorize = await session.fetch(request("/pairing/authorize", "POST", {
    broadcaster_user_id: "494871179",
    broadcaster_login: "spectrumstreamer"
  }));
  assert.equal(authorize.status, 200);
  assert.deepEqual(await authorize.json(), { success: true, status: "authorized" });
  assert.equal(provisioned.length, 1);
  assert.equal(provisioned[0].id, "stardew:494871179");
  assert.equal(provisioned[0].body.broadcaster_user_id, "494871179");
  assert.match(provisioned[0].body.token_hash, /^[a-f0-9]{64}$/);

  const poll = await session.fetch(request("/pairing/poll", "POST", { device_code: deviceCode }));
  const result = await poll.json();
  assert.equal(result.status, "authorized");
  assert.equal(result.broadcaster_user_id, "494871179");
  assert.equal(result.broadcaster_login, "spectrumstreamer");
  assert.match(result.host_token, /^[a-f0-9]{64}$/);

  const retry = await session.fetch(request("/pairing/poll", "POST", { device_code: deviceCode }));
  assert.equal((await retry.json()).host_token, result.host_token);
});

test("pairing start requests are rate limited per source IP", async () => {
  const { session } = makeSession();
  const first = await session.fetch(request("/pairing/rate-limit", "POST", { ip: "192.0.2.10" }));
  assert.equal(first.status, 200);
  const second = await session.fetch(request("/pairing/rate-limit", "POST", { ip: "192.0.2.10" }));
  assert.equal(second.status, 429);
});

test("device pairing cannot be authorised after expiry", async () => {
  const { session, storage } = makeSession();
  const deviceCode = "d".repeat(32);
  await session.fetch(request("/pairing/start", "POST", {
    user_code: "RSTUVWXY",
    device_code_hash: await hash(deviceCode),
    expires_at: Date.now() + 600000
  }));
  await storage.put("pairing_expires_at", Date.now() - 1);
  await storage.put("pairing_host_token", "temporary-token");
  const response = await session.fetch(request("/pairing/authorize", "POST", {
    broadcaster_user_id: "494871179",
    broadcaster_login: "spectrumstreamer"
  }));
  assert.equal(response.status, 410);
  const poll = await session.fetch(request("/pairing/poll", "POST", { device_code: deviceCode }));
  assert.deepEqual(await poll.json(), { status: "expired" });
  assert.equal(await storage.get("pairing_host_token"), undefined);
});


test("community coordinator registers and removes only the matching live session", async () => {
  const { session, storage } = makeSession();
  await session.fetch(request("/community/register", "POST", {
    broadcaster_id: "12345", session_id: "session-a"
  }));
  await session.fetch(request("/community/register", "POST", {
    broadcaster_id: "67890", session_id: "session-b"
  }));
  await session.fetch(request("/community/unregister", "POST", {
    broadcaster_id: "12345", session_id: "stale-session"
  }));
  assert.deepEqual(await storage.get("community:participants"), {
    "12345": "session-a",
    "67890": "session-b"
  });
  await session.fetch(request("/community/unregister", "POST", {
    broadcaster_id: "12345", session_id: "session-a"
  }));
  assert.deepEqual(await storage.get("community:participants"), {
    "67890": "session-b"
  });
});


test("community session snapshot includes only currently connected hosts with spawning enabled", async () => {
  const binding = {
    idFromName: value => value,
    get: id => ({ async fetch() {
      return id === "stardew:12345"
        ? Response.json({ session_id: "enabled-session", eligible: true })
        : Response.json({ session_id: "paused-session", eligible: false });
    } })
  };
  const { session, storage } = makeSession({ GAME_HOST_SESSION: binding });
  await storage.put("community:participants", {
    "12345": "enabled-session",
    "67890": "paused-session"
  });
  const response = await session.fetch(request("/community/sessions"));
  assert.deepEqual(await response.json(), { sessions: { "12345": "enabled-session" } });
});

test("charity milestone fans out to every connected participant and deduplicates retries", async () => {
  const calls = [];
  const binding = {
    idFromName: value => value,
    get: id => ({
      async fetch(input, init) {
        const req = input instanceof Request ? input : new Request(input, init);
        const body = await req.json();
        if (new URL(req.url).pathname === "/community/charity-dispatch") {
          calls.push({ id, body });
          return Response.json({ dispatched: true, spawned: id === "stardew:12345" });
        }
        return Response.json({ error: "not found" }, { status: 404 });
      }
    })
  };
  const { session, storage } = makeSession({ GAME_HOST_SESSION: binding });
  await storage.put("community:participants", { "12345": "session-a", "67890": "session-b" });

  let response = await session.fetch(request("/community/charity-event", "POST", {
    event_id: "twitch-donation-1:1",
    event_type: "charity.milestone.normal",
    currency: "AUD",
    threshold_count: 1,
    recipient_sessions: { "12345": "session-a", "67890": "session-b" }
  }));
  let result = await response.json();
  assert.equal(result.outcome, "dispatched");
  assert.equal(result.connected_players, 2);
  assert.equal(result.eligible_players, 1);
  assert.equal(calls.length, 2);
  assert.deepEqual(calls.map(call => call.id).sort(), ["stardew:12345", "stardew:67890"]);
  assert.deepEqual(calls.map(call => call.body.session_id).sort(), ["session-a", "session-b"]);

  response = await session.fetch(request("/community/charity-event", "POST", {
    event_id: "twitch-donation-1:1",
    event_type: "charity.milestone.normal",
    currency: "AUD",
    threshold_count: 1,
    recipient_sessions: { "12345": "session-a", "67890": "session-b" }
  }));
  result = await response.json();
  assert.equal(result.outcome, "duplicate");
  assert.equal(calls.length, 2);
});

test("charity milestone never discovers sessions at delivery time when no snapshot was captured", async () => {
  const calls = [];
  const binding = {
    idFromName: value => value,
    get: id => ({ async fetch(input, init) {
      const req = input instanceof Request ? input : new Request(input, init);
      calls.push({ id, path: new URL(req.url).pathname });
      return Response.json({ dispatched: true, spawned: false });
    } })
  };
  const { session, storage } = makeSession({ GAME_HOST_SESSION: binding });
  await storage.put("community:participants", { "12345": "new-session" });
  const response = await session.fetch(request("/community/charity-event", "POST", {
    event_id: "old-donation:1", event_type: "charity.milestone.normal", currency: "AUD", threshold_count: 1
  }));
  const result = await response.json();
  assert.equal(result.outcome, "no_connected_players");
  assert.equal(calls.length, 0);
});

test("charity dispatch respects each broadcaster's spawning opt-in", async () => {
  const { session, storage, sockets } = makeSession();
  await storage.put("broadcaster_user_id", "12345");
  await storage.put("active_session_id", "session-1");
  await storage.put("spawns_enabled", false);
  await storage.put("settings", { spawn_enabled: true, difficulty: "normal", chat_responses: true, debug_messages: false });
  await storage.put("spawn_override", "follow");
  const hostMessages = [];
  sockets.push({
    deserializeAttachment: () => ({ broadcasterId: "12345", sessionId: "session-1", role: "host" }),
    send: value => hostMessages.push(JSON.parse(value))
  });

  const payload = { event_id: "twitch-donation-2:1", session_id: "session-1", event_type: "charity.milestone.normal" };
  let response = await session.fetch(request("/community/charity-dispatch", "POST", payload));
  let result = await response.json();
  assert.equal(result.dispatched, true);
  assert.equal(result.spawned, false);
  assert.equal(hostMessages.length, 0);

  await storage.put("spawns_enabled", true);
  response = await session.fetch(request("/community/charity-dispatch", "POST", payload));
  result = await response.json();
  assert.equal(result.duplicate, true);
  assert.equal(result.spawned, false);
  assert.equal(hostMessages.length, 0);
});

test("eligible charity MEGA milestone sends a typed action to the connected game host once", async () => {
  const { session, storage, sockets } = makeSession();
  await storage.put("broadcaster_user_id", "12345");
  await storage.put("active_session_id", "session-1");
  await storage.put("spawns_enabled", true);
  await storage.put("settings", { spawn_enabled: true, difficulty: "hard", chat_responses: true, debug_messages: false });
  await storage.put("spawn_override", "follow");
  const hostMessages = [];
  sockets.push({
    deserializeAttachment: () => ({ broadcasterId: "12345", sessionId: "session-1", role: "host" }),
    send: value => hostMessages.push(JSON.parse(value))
  });

  const payload = { event_id: "twitch-donation-3:5", session_id: "session-1", event_type: "charity.milestone.mega" };
  let response = await session.fetch(request("/community/charity-dispatch", "POST", payload));
  let result = await response.json();
  assert.equal(result.dispatched, true);
  assert.equal(result.spawned, true);
  assert.equal(hostMessages.length, 1);
  assert.equal(hostMessages[0].name, "stardew.community_mega_horde");
  assert.equal(hostMessages[0].outcome, "mega");
  assert.equal(hostMessages[0].difficulty, "hard");

  response = await session.fetch(request("/community/charity-dispatch", "POST", payload));
  result = await response.json();
  assert.equal(result.duplicate, true);
  assert.equal(hostMessages.length, 1);
});


test("shared charity horde settings default safely and accept bounded admin values", async () => {
  const { session, storage } = makeSession();
  let response = await session.fetch(request("/community/charity-settings"));
  assert.deepEqual(await response.json(), {
    settings: { normal_horde_count: 10, mega_horde_count: 20, bonus_monsters_per_additional_threshold: 2, max_monsters_per_event: 50, announce_charity_hordes: true }
  });

  response = await session.fetch(request("/community/charity-settings", "PUT", {
    normal_horde_count: 7,
    mega_horde_count: 32,
    bonus_monsters_per_additional_threshold: 3,
    announce_charity_hordes: false
  }));
  assert.equal(response.status, 200);
  assert.deepEqual((await response.json()).settings, {
    normal_horde_count: 7, mega_horde_count: 32, bonus_monsters_per_additional_threshold: 3, max_monsters_per_event: 50, announce_charity_hordes: false
  });
  assert.deepEqual(await storage.get("community:charity:settings"), {
    normal_horde_count: 7, mega_horde_count: 32, bonus_monsters_per_additional_threshold: 3, max_monsters_per_event: 50, announce_charity_hordes: false
  });
});

test("shared charity horde settings reject invalid or unsafe counts", async () => {
  const { session, storage } = makeSession();
  for (const body of [
    { normal_horde_count: 0, mega_horde_count: 20, bonus_monsters_per_additional_threshold: 2, announce_charity_hordes: true },
    { normal_horde_count: 10, mega_horde_count: 51, bonus_monsters_per_additional_threshold: 2, announce_charity_hordes: true },
    { normal_horde_count: 2.5, mega_horde_count: 20, bonus_monsters_per_additional_threshold: 2, announce_charity_hordes: true }
  ]) {
    const response = await session.fetch(request("/community/charity-settings", "PUT", body));
    assert.equal(response.status, 400);
  }
  assert.equal(await storage.get("community:charity:settings"), undefined);
});


test("one charity donation creates one horde sized from additional thresholds", async () => {
  const calls=[];
  const binding={idFromName:value=>value,get:id=>({async fetch(input,init){
    const req=input instanceof Request?input:new Request(input,init);
    calls.push({id,body:await req.json()});
    return Response.json({dispatched:true,spawned:true});
  }})};
  const {session,storage}=makeSession({GAME_HOST_SESSION:binding});
  await storage.put("community:participants",{"12345":"session-a"});
  await session.fetch(request("/community/charity-settings","PUT",{
    normal_horde_count:10,mega_horde_count:20,bonus_monsters_per_additional_threshold:2,announce_charity_hordes:true
  }));
  const response=await session.fetch(request("/community/charity-event","POST",{
    event_id:"donation-15",event_type:"charity.milestone.normal",currency:"AUD",threshold_count:3,
    donor_name:"Viewer",charity_name:"Charity Name",recipient_sessions:{"12345":"session-a"}
  }));
  assert.equal(response.status,200);
  assert.equal(calls.length,1);
  assert.equal(calls[0].body.monster_count,14);
  assert.equal(calls[0].body.donor_name,"Viewer");
  assert.equal(calls[0].body.charity_name,"Charity Name");
});
test("a large multi-threshold MEGA donation is capped at 50 monsters", async () => {
  const calls=[];
  const binding={idFromName:value=>value,get:id=>({async fetch(input,init){
    const req=input instanceof Request?input:new Request(input,init);
    calls.push(await req.json());
    return Response.json({dispatched:true,spawned:true});
  }})};
  const {session,storage}=makeSession({GAME_HOST_SESSION:binding});
  await storage.put("community:participants",{"12345":"session-a"});
  const response=await session.fetch(request("/community/charity-event","POST",{
    event_id:"donation-100",event_type:"charity.milestone.mega",currency:"AUD",threshold_count:20,
    recipient_sessions:{"12345":"session-a"}
  }));
  assert.equal(response.status,200);
  assert.equal(calls[0].monster_count,50);
});
