// The result banner: Victory / Defeat / Draw with the reason and quick stats, rematch and trophies.

import { t } from "../core/i18n.js";
import { play } from "../core/audio.js";
import { session as sessionStore } from "../core/storage.js";
import { recordResult } from "../core/profile.js";
import { error, toast } from "../core/ui.js";
import { gameKey } from "./keys.js";

const $ = (id) => document.getElementById(id);

export class GameOverScreen {
  constructor({ hub, leave, profile }) {
    this.hub = hub;
    this.profile = profile;
    this.dialog = $("gameover");
    this.key = null;
    this.timer = 0;
    this.view = null;

    $("btn-rematch").addEventListener("click", async () => {
      play("select");
      const result = await this.hub.requestRematch(this.view.room.code);
      if (!result.ok) error(result.error);
    });
    $("btn-view").addEventListener("click", () => { play("back"); this.dialog.close(); });
    $("btn-over-home").addEventListener("click", () => leave());
  }

  update(view, previous) {
    this.view = view;
    const { room, session } = view;
    if (!session?.result) {
      clearTimeout(this.timer);
      if (this.dialog.open) this.dialog.close();
      this.key = null;
      return;
    }

    const key = gameKey(view);
    if (this.key !== key) {
      this.key = key;
      const live = Boolean(previous?.session && !previous.session.result);
      clearTimeout(this.timer);
      // Let the final move land before the banner covers the board.
      this.timer = setTimeout(() => this.open(view, live), live ? 900 : 120);
    } else if (this.dialog.open) {
      this.paintRematch(view);
    }
  }

  open(view, live) {
    const { room, session, youId } = view;
    const result = session.result;
    const outcome = result.outcome === "draw" ? "draw" : result.winnerId === youId ? "win" : "loss";

    const banner = $("over-banner");
    banner.className = `gameover__banner is-${outcome}`;
    $("over-eyebrow").textContent = t(`home.${room.gameTypeId}.title`);
    $("over-title").textContent = t(outcome === "win" ? "over.victory" : outcome === "loss" ? "over.defeat" : "over.draw");
    $("over-reason").textContent = t(`reason.${result.reason}`);
    $("over-moves").textContent = String(session.moveCount);
    const seconds = Math.max(0, Math.round(((session.endedAtMs ?? session.startedAtMs) - session.startedAtMs) / 1000));
    $("over-duration").textContent = `${String(Math.floor(seconds / 60)).padStart(2, "0")}:${String(seconds % 60).padStart(2, "0")}`;
    this.paintRematch(view);

    if (!this.dialog.open) this.dialog.showModal();
    $("btn-rematch").focus();
    play(outcome === "win" ? "victory" : outcome === "loss" ? "defeat" : "draw");

    // Local-only milestones, counted once per finished game even if the page is refreshed.
    const recorded = sessionStore.get("fab.recorded", []);
    const id = gameKey(view);
    if (live && this.profile && !recorded.includes(id)) {
      sessionStore.set("fab.recorded", [...recorded.slice(-20), id]);
      setTimeout(() => {
        for (const trophy of recordResult(this.profile.id, outcome)) {
          toast(t(`trophy.${trophy}`), { type: "trophy", icon: "trophy", sub: t("trophy.unlocked"), ms: 5200 });
          play("notify");
        }
      }, 1200);
    }
  }

  paintRematch(view) {
    const { room, youId } = view;
    const opponent = room.players.find((p) => p.id !== youId);
    const mine = room.rematchVotes.includes(youId);
    const theirs = opponent && room.rematchVotes.includes(opponent.id);
    const note = $("over-rematch-note");
    note.hidden = !mine && !theirs;
    note.textContent = mine ? t("over.rematch.waiting") : theirs ? t("over.rematch.requested", { name: opponent.displayName }) : "";
    $("btn-rematch").disabled = mine || !opponent;
    $("btn-rematch-inline").disabled = mine || !opponent;
  }
}
