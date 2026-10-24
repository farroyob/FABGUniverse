// Room page: connects to the hub, enters (create / join / rejoin) and routes server events to the
// lobby, the game screen and the result banner.

import { initApp } from "../core/app.js";
import { t, culture, errorText } from "../core/i18n.js";
import { play } from "../core/audio.js";
import { setBackdrop } from "../core/atmos.js";
import { bus, focusFirst } from "../core/input.js";
import { getCurrent, playerId } from "../core/profile.js";
import { HubClient } from "../core/hub-client.js";
import { error, toast } from "../core/ui.js";
import { Chat } from "./chat.js";
import { Lobby } from "./lobby.js";
import { GameScreen } from "./game.js";
import { GameOverScreen } from "./gameover.js";
import { gameKey } from "./keys.js";

initApp();

const $ = (id) => document.getElementById(id);
const main = $("main");
const params = new URLSearchParams(location.search);
const isNew = main.dataset.new === "true";
let roomCode = main.dataset.roomCode;

const sections = { loading: $("room-loading"), error: $("room-error"), lobby: $("lobby"), game: $("game") };
let current = "loading";

function show(name) {
  if (current === name) return;
  for (const [key, el] of Object.entries(sections)) el.hidden = key !== name;
  current = name;
  focusFirst(sections[name]);
}

function fail(code) {
  $("room-error-text").textContent = errorText(code);
  show("error");
}

// ── Preconditions ───────────────────────────────────────────

const profile = getCurrent();
if (main.dataset.invalid === "true") {
  fail("ROOM_NOT_FOUND");
  throw new Error("invalid room code");
}
if (!profile) {
  const next = isNew ? "" : `?next=${encodeURIComponent(`/${culture}/play/${roomCode}`)}`;
  location.replace(`/${culture}${next}`);
  throw new Error("no profile");
}

// ── Wiring ──────────────────────────────────────────────────

const hub = new HubClient();
const chat = new Chat({ hub, roomCode, onUnread: () => game.noteUnread() });
let state = { view: null };

async function leave() {
  if (roomCode && roomCode !== "new") await hub.leaveRoom(roomCode).catch(() => {});
  location.href = `/${culture}`;
}

const lobby = new Lobby({ hub, leave });
const game = new GameScreen({ hub, chat, leave });
const over = new GameOverScreen({ hub, leave, profile });

chat.mount($("lobby").querySelector("[data-chat]"));
chat.mount($("chat-drawer").querySelector("[data-chat]"), { isVisible: () => game.chatVisible() });

const nameOf = (publicId) => state.view?.room.players.find((p) => p.id === publicId)?.displayName ?? "";

function apply(view) {
  const previous = state.view;
  if (view.session && previous?.session && gameKey(view) === gameKey(previous)
      && view.session.stateVersion < previous.session.stateVersion) {
    return; // an older snapshot overtaken on the wire
  }
  state.view = view;
  roomCode = view.room.code;
  chat.roomCode = roomCode;
  chat.setMe(view.youId);

  setBackdrop(view.room.status === "waiting" ? view.room.gameTypeId : null);
  if (view.room.status === "waiting") {
    show("lobby");
    lobby.update(view, previous);
    over.update(view, previous);
  } else {
    show("game");
    game.update(view, previous).then(() => over.update(view, previous));
  }
}

// ── Server events ───────────────────────────────────────────

const forRoom = (handler) => (e) => {
  const detail = e.detail;
  const code = detail.room?.code ?? detail.roomCode;
  if (code === roomCode) handler(detail);
};

for (const name of ["StateUpdated", "ReadyStateChanged"]) {
  hub.addEventListener(name, forRoom(apply));
}
hub.addEventListener("GameStarted", forRoom((view) => {
  play("select");
  apply(view);
}));

hub.addEventListener("PlayerReconnecting", forRoom(({ playerId: id, secondsLeft }) => {
  if (state.view && id !== state.view.youId) game.opponentOffline(nameOf(id), secondsLeft);
}));
hub.addEventListener("PlayerReconnected", forRoom(({ playerId: id }) => {
  if (state.view && id !== state.view.youId) game.opponentBack(nameOf(id));
}));
hub.addEventListener("PlayerLeftRoom", forRoom(() => play("back")));
hub.addEventListener("DrawDeclined", forRoom(({ playerId: id }) => {
  if (state.view && id !== state.view.youId) toast(t("game.draw.declined", { name: nameOf(id) }));
}));
hub.addEventListener("RematchRequested", forRoom(() => play("notify")));
hub.addEventListener("ChatMessageReceived", forRoom(({ message }) => chat.add(message)));

// ── Connection health ───────────────────────────────────────

const overlay = $("conn-overlay");
hub.addEventListener("connection-lost", () => {
  $("conn-text").textContent = t("game.conn.lost");
  overlay.hidden = false;
});
hub.addEventListener("connection-restored", async () => {
  const result = await hub.rejoinRoom(roomCode, playerId());
  overlay.hidden = true;
  if (result.ok) apply(result.data);
  else fail(result.error);
});
hub.addEventListener("connection-closed", () => {
  $("conn-home").hidden = false;
  overlay.hidden = false;
});

bus.addEventListener("back", () => {
  if (current === "lobby" || current === "error") leave();
});

// ── Enter the room ──────────────────────────────────────────

async function enter() {
  try {
    await hub.start();
  } catch {
    fail("UNKNOWN");
    return;
  }

  const identity = { displayName: profile.name, avatarId: profile.avatar, playerId: playerId() };
  let result;

  if (isNew) {
    const [minutes, increment] = (params.get("tc") ?? "0-0").split("-").map(Number);
    result = await hub.createRoom({
      gameTypeId: params.get("game") ?? "chess",
      ...identity,
      timeControl: { initialMinutes: minutes || 0, incrementSeconds: increment || 0 },
    });
    if (result.ok) history.replaceState(null, "", `/${culture}/play/${result.data.room.code}`);
  } else {
    result = await hub.rejoinRoom(roomCode, identity.playerId);
    if (!result.ok && result.error === "NOT_IN_ROOM") {
      result = await hub.joinRoom({ roomCode, ...identity });
    }
  }

  if (!result.ok) {
    fail(result.error);
    error(result.error);
    return;
  }
  apply(result.data);
}

await enter();
