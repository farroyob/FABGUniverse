// Chess renderer: turns the server's ChessStateDto into a board and the player's gestures into move intents.

import { BoardCore, squareIndex, squareName } from "./board-core.js";
import { BOARD_DEFS, chessPiece } from "./board-art.js";
import { t } from "../core/i18n.js";

const PROMOTION_ORDER = ["q", "r", "b", "n"];

export class ChessBoard {
  constructor({ container, overlay, onMove, announce, nameOf }) {
    this.onMove = onMove;
    this.announce = announce;
    this.nameOf = nameOf;
    this.overlay = overlay;
    this.legal = new Map(); // from idx -> [{ to, promotion }]
    this.selected = null;
    this.game = null;
    this.myColor = "white";
    this.lastVersion = -1;

    this.core = new BoardCore({
      container,
      pieceMarkup: chessPiece,
      defs: BOARD_DEFS,
      label: t("a11y.board.chess"),
      help: t("a11y.board.help"),
      canGrab: (idx) => this.legal.has(idx),
      onGrab: (idx) => { this.reclick = this.selected === idx; this.select(idx); },
      onActivate: (idx, via) => this.activate(idx, via),
      onDrop: (from, to) => this.drop(from, to),
      onCancel: () => this.clear(true),
      onCursor: (idx) => this.describe(idx),
    });
  }

  /**
   * @param {object} game ChessStateDto
   * @param {{orientation: string, interactive: boolean, animate: boolean, myColor: string}} opts
   */
  update(game, { orientation, interactive, animate, myColor }) {
    this.game = game;
    this.myColor = myColor;
    this.core.resetDrag();
    this.core.setOrientation(orientation);
    this.core.setInteractive(interactive);

    this.legal = new Map();
    for (const move of game.legalMoves) {
      const from = squareIndex(move.from);
      if (!this.legal.has(from)) this.legal.set(from, []);
      this.legal.get(from).push({ to: squareIndex(move.to), promotion: move.promotion });
    }

    const last = game.lastMove;
    const move = last ? { from: squareIndex(last.from), to: squareIndex(last.to) } : null;
    this.core.setBoard(game.board, { animate: animate && move !== null, move: animate ? move : null });

    this.closePromotion();
    if (this.selected != null && !this.legal.has(this.selected)) this.selected = null;
    this.paint();
  }

  paint() {
    const game = this.game;
    const targets = [];
    const captures = [];
    if (this.selected != null) {
      for (const { to } of this.legal.get(this.selected) ?? []) {
        if (game.board[to]) captures.push(to);
        else if (game.board[this.selected]?.[1] === "P" && (to & 7) !== (this.selected & 7)) captures.push(to); // en passant
        else targets.push(to);
      }
    }
    const last = game.lastMove ? [squareIndex(game.lastMove.from), squareIndex(game.lastMove.to)] : [];
    this.core.setHighlights({
      last,
      check: game.checkSquare ? squareIndex(game.checkSquare) : null,
      selected: this.selected,
      targets,
      captures,
    });
  }

  select(idx) {
    if (!this.legal.has(idx)) return;
    this.selected = idx;
    this.paint();
    const piece = this.pieceName(this.game.board[idx]);
    this.announce(t("announce.selected", { piece, square: squareName(idx), n: this.legal.get(idx).length }));
  }

  clear(announce = false) {
    this.closePromotion();
    if (this.selected == null) return;
    this.selected = null;
    this.paint();
    if (announce) this.announce(t("announce.cancel"));
  }

  activate(idx, via) {
    if (!this.core.interactive) {
      this.clear();
      return;
    }
    if (via === "grab") {
      // The press already selected the piece; pressing the selected piece again lets go of it.
      if (this.reclick) this.clear(true);
      this.reclick = false;
      return;
    }
    if (this.selected != null && this.legal.get(this.selected)?.some((m) => m.to === idx)) {
      this.submit(this.selected, idx);
    } else if (this.selected === idx) {
      this.clear(true);
    } else if (this.legal.has(idx)) {
      this.select(idx);
    } else {
      this.clear();
    }
  }

  drop(from, to) {
    if (!this.legal.get(from)?.some((m) => m.to === to)) {
      this.clear();
      return false;
    }
    return this.submit(from, to) !== "picker";
  }

  /** Sends the move, or opens the picker when the pawn reaches the last rank. Returns "sent" | "picker". */
  submit(from, to) {
    const options = this.legal.get(from).filter((m) => m.to === to);
    if (options.length > 1 || options[0].promotion) {
      this.openPromotion(from, to, options.map((m) => m.promotion));
      return "picker";
    }
    this.selected = null;
    this.paint();
    this.onMove({ from: squareName(from), to: squareName(to) });
    return "sent";
  }

  // ── Promotion picker ──────────────────────────────────────

  openPromotion(from, to, available) {
    this.closePromotion();
    const mine = this.myColor === "white" ? "w" : "b";
    const { x } = this.core.pos(to);
    const fromTop = this.core.pos(to).y === 0;
    const picker = document.createElement("div");
    picker.className = `promo ${fromTop ? "promo--down" : "promo--up"}`;
    picker.setAttribute("role", "dialog");
    picker.setAttribute("aria-label", t("game.promote"));
    picker.style.setProperty("--col", String(x / 100));

    const order = PROMOTION_ORDER.filter((p) => available.includes(p));
    for (const letter of order) {
      const button = document.createElement("button");
      button.type = "button";
      button.className = "promo__piece";
      button.dataset.nav = "";
      button.setAttribute("aria-label", t(`piece.chess.${letter.toUpperCase()}`));
      button.innerHTML = `<svg viewBox="0 0 100 100" aria-hidden="true">${chessPiece(mine + letter.toUpperCase())}</svg>`;
      button.addEventListener("click", () => {
        this.closePromotion();
        this.selected = null;
        this.paint();
        this.onMove({ from: squareName(from), to: squareName(to), promotion: letter });
      });
      picker.append(button);
    }
    picker.addEventListener("keydown", (e) => {
      if (e.key === "Escape") {
        e.stopPropagation();
        this.closePromotion();
        this.core.focus();
      }
    });
    this.overlay.append(picker);
    this.overlay.hidden = false;
    picker.firstElementChild?.focus();
  }

  closePromotion() {
    if (!this.overlay) return;
    this.overlay.replaceChildren();
    this.overlay.hidden = true;
  }

  // ── Accessibility ─────────────────────────────────────────

  pieceName(code) {
    if (!code) return t("announce.empty");
    return t("announce.piece", {
      piece: t(`piece.chess.${code[1]}`),
      color: t(`announce.colorword.chess.${code[0]}`),
    });
  }

  describe(idx) {
    const what = this.pieceName(this.game?.board[idx]);
    this.announce(t("announce.square", { square: squareName(idx), what }));
  }

  /** Text for a move that just happened, used by the screen-reader live region. */
  describeMove(game, moverName) {
    const last = game.lastMove;
    if (!last) return "";
    if (last.san.startsWith("O-O-O")) return t("announce.castle.q", { name: moverName });
    if (last.san.startsWith("O-O")) return t("announce.castle.k", { name: moverName });
    const to = squareIndex(last.to);
    const piece = t(`piece.chess.${game.board[to]?.[1] ?? "P"}`);
    const key = last.san.includes("x") ? "announce.capture" : "announce.move";
    const base = t(key, { name: moverName, piece, to: last.to });
    return game.inCheck ? `${base}. ${t("announce.check")}` : base;
  }

  isCapture(game) {
    return Boolean(game.lastMove?.san.includes("x"));
  }

  focus() {
    this.core.focus();
  }

  destroy() {
    this.closePromotion();
    this.core.destroy();
  }
}
