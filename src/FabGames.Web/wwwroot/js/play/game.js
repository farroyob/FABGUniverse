// The game screen: board, player cards with clocks, move list, controls and the draw / resign flow.

import { t } from "../core/i18n.js";
import { play } from "../core/audio.js";
import { announce, error, formatClock, openDialog, toast } from "../core/ui.js";
import { avatarUrl } from "../core/profile.js";
import { renderers } from "../games/registry.js";
import { chessPiece } from "../games/board-art.js";
import { gameKey } from "./keys.js";

const $ = (id) => document.getElementById(id);
const LOW_MS = 30_000;

export class GameScreen {
  constructor({ hub, chat, leave }) {
    this.hub = hub;
    this.chat = chat;
    this.leaveRoom = leave;
    this.view = null;
    this.board = null;
    this.boardKey = null;
    this.token = 0;
    this.queue = Promise.resolve();
    this.clock = null;
    this.notice = null;
    this.leaveAfterResign = false;
    this.unread = 0;

    $("btn-draw").addEventListener("click", () => this.call(this.hub.offerDraw(this.code), "select"));
    $("draw-accept").addEventListener("click", () => this.call(this.hub.respondDraw(this.code, true), "select"));
    $("draw-decline").addEventListener("click", () => this.call(this.hub.respondDraw(this.code, false), "back"));
    $("btn-resign").addEventListener("click", () => this.askResign(false));
    $("game-leave").addEventListener("click", () => {
      play("back");
      if (this.view?.room.status === "inprogress") this.askResign(true);
      else this.leaveRoom();
    });
    $("resign-yes").addEventListener("click", async () => {
      $("dlg-resign").close();
      if (this.leaveAfterResign) this.leaveRoom();
      else this.call(this.hub.resign(this.code), "back");
    });
    $("btn-chat").addEventListener("click", () => this.toggleChat());
    $("btn-rematch-inline").addEventListener("click", () => this.call(this.hub.requestRematch(this.code), "select"));
    $("btn-home-inline").addEventListener("click", () => this.leaveRoom());

    setInterval(() => this.tick(), 100);
  }

  get code() {
    return this.view.room.code;
  }

  async call(promise, sound) {
    if (sound) play(sound);
    const result = await promise;
    if (!result.ok) error(result.error);
    return result;
  }

  askResign(leaveAfter) {
    this.leaveAfterResign = leaveAfter;
    openDialog($("dlg-resign"));
  }

  toggleChat() {
    const drawer = $("chat-drawer");
    const open = drawer.hidden;
    drawer.hidden = !open;
    $("btn-chat").setAttribute("aria-expanded", String(open));
    if (open) {
      this.unread = 0;
      $("chat-badge").hidden = true;
      drawer.querySelector("[data-chat-input]").focus();
    }
    play("tick");
  }

  chatVisible() {
    return !$("chat-drawer").hidden;
  }

  noteUnread() {
    this.unread += 1;
    const badge = $("chat-badge");
    badge.textContent = String(this.unread);
    badge.hidden = false;
  }

  // ── Board lifecycle ───────────────────────────────────────

  async ensureBoard(room, key) {
    if (this.boardKey === key && this.board) return false;
    this.board?.destroy();
    this.board = null;
    const BoardClass = await renderers[room.gameTypeId]();
    const container = $("board-mount");
    container.dataset.nav = "";
    this.board = new BoardClass({
      container,
      overlay: $("board-overlay"),
      onMove: (intent) => this.sendMove(intent),
      announce: (text) => announce(text),
    });
    container.addEventListener("gamepad-cancel", () => {
      if (!this.board?.selected && !this.board?.prefix?.length) $("btn-resign").focus();
    });
    this.boardKey = key;
    return true;
  }

  async sendMove(intent) {
    const version = this.view.session.stateVersion;
    const result = await this.hub.makeMove(this.code, intent, version);
    if (!result.ok) error(result.error);
  }

  // ── Rendering ─────────────────────────────────────────────

  /** Snapshots are applied one at a time; one that is already superseded by a newer snapshot is skipped. */
  update(view, previous) {
    const token = ++this.token;
    this.queue = this.queue
      .then(() => (token === this.token ? this.render(view, previous) : undefined))
      .catch((err) => console.error(err));
    return this.queue;
  }

  async render(view, previous) {
    const { room, session, youId } = view;
    const key = `${gameKey(view)}:${room.gameTypeId}`;
    const rebuilt = await this.ensureBoard(room, key);

    this.view = view;
    const me = room.players.find((p) => p.id === youId);
    const opponent = room.players.find((p) => p.id !== youId);
    const sides = session.colorOrder; // [white, black]
    const mySide = sides[0] === youId ? "white" : "black";
    const game = session.game;
    const sameGame = gameKey(previous) === gameKey(view);
    const moved = Boolean(sameGame && previous.session && session.stateVersion > previous.session.stateVersion);
    const animate = moved && !rebuilt;
    const finished = Boolean(session.result);
    const interactive = room.status === "inprogress" && !finished && session.currentTurnPlayerId === youId;

    this.chat.setMe(youId);
    $("game-code").textContent = room.code;
    $("game-h1").textContent = `${t(`home.${room.gameTypeId}.title`)} · ${room.code}`;
    this.board.update(game, { orientation: mySide, interactive, animate, myColor: mySide });

    this.renderCard($("card-top"), opponent, mySide === "white" ? "black" : "white", false, game, room);
    this.renderCard($("card-bottom"), me, mySide, true, game, room);

    this.clock = session.clock
      ? { remaining: session.clock.remainingMs, running: session.clock.runningPlayerId, at: performance.now() }
      : null;
    this.tick();

    this.renderTurn(view, finished);
    this.renderStatus(view, finished);
    this.renderMoves(game);

    $("controls").hidden = finished;
    $("result-controls").hidden = !finished;
    $("btn-draw").disabled = session.drawOfferedBy === youId;

    const offered = session.drawOfferedBy && session.drawOfferedBy !== youId && !finished;
    $("draw-offer").hidden = !offered;
    if (offered) {
      $("draw-offer-text").textContent = t("game.draw.received", { name: opponent?.displayName ?? "" });
      if (!previous?.session?.drawOfferedBy) play("notify");
    }

    if (moved) this.reactToMove(view, previous);
    if (rebuilt && !previous?.session) this.board.focus();
    else if (rebuilt) play("select");
  }

  renderCard(el, player, side, isMe, game, room) {
    el.replaceChildren();
    el.classList.toggle("is-me", isMe);
    el.dataset.player = player?.id ?? "";
    el.dataset.side = side;
    el.classList.toggle("is-turn", !this.view.session.result && this.view.session.currentTurnPlayerId === player?.id);
    if (!player) return;

    const avatar = document.createElement("img");
    avatar.className = "avatar avatar--md";
    avatar.src = avatarUrl(player.avatarId);
    avatar.alt = "";
    avatar.width = 56;
    avatar.height = 56;

    const info = document.createElement("div");
    info.className = "player-card__info";
    const name = document.createElement("div");
    name.className = "player-card__name";
    const label = document.createElement("span");
    label.textContent = player.displayName;
    const dot = document.createElement("span");
    dot.className = `dot${player.connected ? "" : " is-off"}`;
    dot.title = player.connected ? "" : t("lobby.offline");
    name.append(label, dot);
    if (isMe) {
      const tag = document.createElement("span");
      tag.className = "tag";
      tag.textContent = t("game.you");
      name.append(tag);
    }
    const sub = document.createElement("div");
    sub.className = "player-card__sub";
    sub.textContent = t(`color.${room.gameTypeId}.${side}`);
    info.append(name, sub, this.captured(game, side, room.gameTypeId));

    el.append(avatar, info);
    if (this.view.session.clock) {
      const clock = document.createElement("div");
      clock.className = "clock mono";
      clock.dataset.id = player.id;
      el.append(clock);
    }
  }

  captured(game, side, gameType) {
    const wrap = document.createElement("div");
    wrap.className = "captured";
    wrap.setAttribute("aria-label", t("game.captured"));
    if (gameType === "chess") {
      const list = side === "white" ? game.captured.byWhite : game.captured.byBlack;
      for (const code of list) {
        const span = document.createElement("span");
        span.className = "captured__piece";
        span.innerHTML = `<svg viewBox="0 0 100 100" aria-hidden="true">${chessPiece(code)}</svg>`;
        wrap.append(span);
      }
    } else {
      const count = side === "white" ? game.capturedByWhite : game.capturedByBlack;
      if (count > 0) {
        wrap.textContent = `×${count}`;
        wrap.classList.add("captured--count", "mono");
      }
    }
    return wrap;
  }

  renderTurn(view, finished) {
    const { session, room, youId } = view;
    const turn = $("turn");
    turn.hidden = finished;
    if (finished) return;
    const mine = session.currentTurnPlayerId === youId;
    turn.classList.toggle("is-mine", mine);
    const current = room.players.find((p) => p.id === session.currentTurnPlayerId);
    $("turn-text").textContent = mine ? t("game.turn.yours") : t("game.turn.theirs", { name: current?.displayName ?? "" });
  }

  renderStatus(view, finished) {
    const { session, youId } = view;
    const game = session.game;
    const lines = [];
    let danger = false;
    if (!finished) {
      if (game.inCheck) { lines.push(t("game.check")); danger = true; }
      if (game.mustCapture && session.currentTurnPlayerId === youId) lines.push(t("game.mustcapture"));
      if (session.drawOfferedBy === youId) lines.push(t("game.draw.offered"));
      if (this.notice) { lines.push(this.notice); danger = true; }
    }
    const status = $("status");
    status.hidden = lines.length === 0;
    status.textContent = lines.join(" · ");
    status.classList.toggle("is-danger", danger);
  }

  renderMoves(game) {
    const moves = game.moves;
    $("moves-empty").hidden = moves.length > 0;
    const rows = [];
    for (let i = 0; i < moves.length; i += 2) {
      const li = document.createElement("li");
      li.className = "moves__row";
      const n = document.createElement("span");
      n.className = "moves__n mono";
      n.textContent = `${i / 2 + 1}.`;
      li.append(n);
      for (const m of moves.slice(i, i + 2)) {
        const span = document.createElement("span");
        span.className = `moves__m mono${m === moves.at(-1) ? " is-last" : ""}`;
        span.textContent = m.san ?? m.notation;
        li.append(span);
      }
      rows.push(li);
    }
    const list = $("moves");
    list.replaceChildren(...rows);
    list.scrollTop = list.scrollHeight;
  }

  reactToMove(view, previous) {
    const { session, room, youId } = view;
    const game = session.game;
    const moverSide = game.moves.at(-1)?.color;
    const mover = room.players.find((p) => p.id === session.colorOrder[moverSide === "white" ? 0 : 1]);
    announce(this.board.describeMove(game, mover?.displayName ?? ""));

    play(this.board.isCapture(game) ? "capture" : "move");
    if (game.inCheck) setTimeout(() => play("check"), 140);
    if (!session.result && session.currentTurnPlayerId === youId && mover?.id !== youId) {
      setTimeout(() => announce(t("game.turn.yours")), 900);
    }
  }

  // ── Clocks and opponent presence ──────────────────────────

  tick() {
    if (!this.clock) return;
    const elapsed = performance.now() - this.clock.at;
    for (const el of document.querySelectorAll(".clock")) {
      const id = el.dataset.id;
      const running = this.clock.running === id;
      const ms = Math.max(0, (this.clock.remaining[id] ?? 0) - (running ? elapsed : 0));
      el.textContent = formatClock(ms);
      el.classList.toggle("is-running", running);
      el.classList.toggle("is-low", ms < LOW_MS);
    }
  }

  /** Called while the opponent's connection is down (secondsLeft null clears the notice). */
  opponentOffline(name, secondsLeft) {
    this.notice = secondsLeft == null ? null : t("game.opp.reconnecting", { name, s: secondsLeft });
    if (this.view) this.renderStatus(this.view, Boolean(this.view.session?.result));
  }

  opponentBack(name) {
    this.notice = null;
    if (this.view) this.renderStatus(this.view, Boolean(this.view.session?.result));
    toast(t("game.opp.back", { name }), { icon: "check" });
    play("notify");
  }
}
