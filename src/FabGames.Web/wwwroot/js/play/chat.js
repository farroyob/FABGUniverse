// Chat: one message store, rendered into every mounted chat box (lobby and in-game drawer).
// Text only: messages are inserted with textContent, never as HTML.

import { error } from "../core/ui.js";
import { play } from "../core/audio.js";

export class Chat {
  constructor({ hub, roomCode, onUnread }) {
    this.hub = hub;
    this.roomCode = roomCode;
    this.onUnread = onUnread;
    this.messages = [];
    this.roots = [];
    this.me = "";
  }

  setMe(id) {
    this.me = id;
  }

  mount(root, { isVisible = () => true } = {}) {
    const box = { root, isVisible };
    this.roots.push(box);
    const form = root.querySelector("[data-chat-form]");
    const input = root.querySelector("[data-chat-input]");
    form.addEventListener("submit", async (e) => {
      e.preventDefault();
      const text = input.value.trim();
      if (!text) return;
      input.value = "";
      const result = await this.hub.sendChat(this.roomCode, text);
      if (!result.ok) {
        input.value = text;
        error(result.error);
      }
    });
    this.#render(box);
  }

  add(message) {
    this.messages.push(message);
    if (this.messages.length > 200) this.messages.shift();
    const mine = message.playerId === this.me;
    if (!mine) play("notify");
    for (const box of this.roots) {
      this.#render(box);
      if (!mine && !box.isVisible()) this.onUnread?.(box);
    }
  }

  #render({ root }) {
    const log = root.querySelector("[data-chat-log]");
    const empty = root.querySelector("[data-chat-empty]");
    empty.hidden = this.messages.length > 0;
    log.replaceChildren(...this.messages.map((m) => {
      const li = document.createElement("li");
      li.className = `chat-msg${m.playerId === this.me ? " is-mine" : ""}`;
      const name = document.createElement("strong");
      name.textContent = m.displayName;
      const text = document.createElement("span");
      text.textContent = m.text;
      li.append(name, text);
      return li;
    }));
    log.scrollTop = log.scrollHeight;
  }
}
