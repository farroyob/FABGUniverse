// Lobby: room code, seats with ready states, share tools and the host's start button.

import { t, culture } from "../core/i18n.js";
import { play } from "../core/audio.js";
import { toast, error } from "../core/ui.js";
import { avatarUrl } from "../core/profile.js";

const $ = (id) => document.getElementById(id);

export class Lobby {
  constructor({ hub, leave }) {
    this.hub = hub;
    this.view = null;
    this.root = $("lobby");

    $("lobby-leave").addEventListener("click", () => { play("back"); leave(); });
    $("copy-code").addEventListener("click", () => this.copy(this.view.room.code, t("lobby.copied")));
    $("copy-link").addEventListener("click", () => this.copy(this.link(), t("lobby.copied")));

    const share = $("share-link");
    if (navigator.share) {
      share.hidden = false;
      share.addEventListener("click", () => {
        navigator.share({ title: t("app.name"), text: t("lobby.share.text"), url: this.link() }).catch(() => {});
      });
    }

    $("btn-ready").addEventListener("click", async () => {
      const mine = this.view.room.players.find((p) => p.id === this.view.youId);
      play("select");
      const result = await this.hub.setReady(this.view.room.code, !mine?.ready);
      if (!result.ok) error(result.error);
    });
    $("btn-start").addEventListener("click", async () => {
      play("select");
      const result = await this.hub.startGame(this.view.room.code);
      if (!result.ok) error(result.error);
    });
  }

  link() {
    return `${location.origin}/${culture}/play/${this.view.room.code}`;
  }

  async copy(text, message) {
    try {
      await navigator.clipboard.writeText(text);
    } catch {
      const field = document.createElement("textarea");
      field.value = text;
      field.setAttribute("readonly", "");
      field.className = "sr-only";
      document.body.append(field);
      field.select();
      document.execCommand("copy");
      field.remove();
    }
    play("tick");
    toast(message, { icon: "check" });
  }

  update(view, previous) {
    this.view = view;
    const { room, youId } = view;

    $("lobby-game").textContent = t(`home.${room.gameTypeId}.title`);
    $("lobby-code").textContent = room.code;
    $("lobby-time").textContent = room.timeControl.initialMinutes
      ? t("lobby.time", { min: room.timeControl.initialMinutes, inc: room.timeControl.incrementSeconds })
      : t("lobby.time.none");

    const full = room.players.length >= room.maxPlayers;
    $("lobby-hint").textContent = full ? "" : t("lobby.hint");
    $("lobby-hint").hidden = full;

    const seats = [];
    for (let i = 0; i < room.maxPlayers; i++) seats.push(room.players[i] ?? null);
    $("seats").replaceChildren(...seats.map((player) => this.seat(player, youId)));

    const mine = room.players.find((p) => p.id === youId);
    const ready = $("btn-ready");
    ready.textContent = mine?.ready ? t("lobby.btn.unready") : t("lobby.btn.ready");
    ready.classList.toggle("btn--primary", !mine?.ready);
    ready.classList.toggle("btn--glass", Boolean(mine?.ready));

    const start = $("btn-start");
    const isHost = room.hostId === youId;
    start.hidden = !isHost;
    start.disabled = room.players.length < room.minPlayers;

    if (previous && room.players.length > previous.room.players.length) play("notify");
  }

  seat(player, youId) {
    const li = document.createElement("li");
    if (!player) {
      li.className = "seat is-empty";
      li.innerHTML = '<span class="seat__ghost" aria-hidden="true"></span><div class="seat__name"></div>';
      li.querySelector(".seat__name").textContent = t("lobby.seat.empty");
      return li;
    }
    li.className = `seat${player.ready ? " is-ready" : ""}${player.id === youId ? " is-me" : ""}${player.connected ? "" : " is-offline"}`;

    const img = document.createElement("img");
    img.className = "avatar avatar--md";
    img.src = avatarUrl(player.avatarId);
    img.alt = "";
    img.width = 56;
    img.height = 56;

    const body = document.createElement("div");
    body.className = "seat__name";
    const name = document.createElement("span");
    name.textContent = player.displayName;
    body.append(name);
    for (const [flag, label] of [[player.host, t("lobby.host")], [player.id === youId, t("lobby.you")]]) {
      if (!flag) continue;
      const tag = document.createElement("span");
      tag.className = "tag";
      tag.textContent = label;
      body.append(tag);
    }

    const state = document.createElement("span");
    state.className = "seat__state";
    state.textContent = !player.connected ? t("lobby.offline") : player.ready ? t("lobby.ready") : t("lobby.notready");

    li.append(img, body, state);
    return li;
  }
}
