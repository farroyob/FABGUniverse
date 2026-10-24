// Checkers renderer. A turn is a whole capture sequence, so the player builds a path square by square
// (or drags a piece to the final landing square when only one sequence ends there).

import { BoardCore, squareIndex, squareName } from "./board-core.js";
import { BOARD_DEFS, checkersPiece } from "./board-art.js";
import { t } from "../core/i18n.js";

export class CheckersBoard {
  constructor({ container, onMove, announce }) {
    this.onMove = onMove;
    this.announce = announce;
    this.moves = []; // [{ from, path: [idx] }]
    this.selected = null;
    this.prefix = [];
    this.game = null;

    this.core = new BoardCore({
      container,
      pieceMarkup: checkersPiece,
      defs: BOARD_DEFS,
      label: t("a11y.board.checkers"),
      help: t("a11y.board.help"),
      canGrab: (idx) => this.moves.some((m) => m.from === idx),
      onGrab: (idx) => { this.reclick = this.selected === idx && this.prefix.length === 0; this.select(idx); },
      onActivate: (idx, via) => this.activate(idx, via),
      onDrop: (from, to) => this.drop(from, to),
      onCancel: () => this.clear(true),
      onCursor: (idx) => this.describe(idx),
    });
  }

  update(game, { orientation, interactive, animate }) {
    this.game = game;
    this.core.resetDrag();
    this.core.setOrientation(orientation);
    this.core.setInteractive(interactive);

    this.moves = game.legalMoves.map((m) => ({ from: squareIndex(m.from), path: m.path.map(squareIndex) }));

    const last = game.lastMove;
    const move = last
      ? { from: squareIndex(last.from), to: squareIndex(last.path.at(-1)), path: last.path.map(squareIndex) }
      : null;
    this.core.setBoard(game.board, { animate: animate && move !== null, move: animate ? move : null });

    if (this.selected != null && !this.moves.some((m) => m.from === this.selected)) this.clear();
    this.paint();
  }

  #candidates() {
    return this.moves.filter(
      (m) => m.from === this.selected && this.prefix.every((sq, i) => m.path[i] === sq) && m.path.length > this.prefix.length);
  }

  paint() {
    const game = this.game;
    const targets = this.selected == null ? [] : [...new Set(this.#candidates().map((m) => m.path[this.prefix.length]))];

    // Preview the pieces the chosen path would take.
    const doomed = [];
    if (this.selected != null && this.prefix.length) {
      let at = this.selected;
      for (const sq of this.prefix) {
        const mid = ((at >> 3) + (sq >> 3)) / 2;
        const midFile = ((at & 7) + (sq & 7)) / 2;
        if (Number.isInteger(mid) && Number.isInteger(midFile) && Math.abs((at >> 3) - (sq >> 3)) === 2) {
          doomed.push((mid << 3) | midFile);
        }
        at = sq;
      }
    }

    const last = game.lastMove
      ? [squareIndex(game.lastMove.from), squareIndex(game.lastMove.path.at(-1))]
      : [];
    this.core.setHighlights({ last, selected: this.selected, targets, path: this.prefix, doomed });
  }

  select(idx) {
    if (!this.moves.some((m) => m.from === idx)) return;
    this.selected = idx;
    this.prefix = [];
    this.paint();
    const n = new Set(this.#candidates().map((m) => m.path[0])).size;
    this.announce(t("announce.selected", { piece: this.pieceName(this.game.board[idx]), square: squareName(idx), n }));
  }

  clear(announce = false) {
    if (this.selected == null) return;
    this.selected = null;
    this.prefix = [];
    this.paint();
    if (announce) this.announce(t("announce.cancel"));
  }

  activate(idx, via) {
    if (!this.core.interactive) {
      this.clear();
      return;
    }
    if (via === "grab") {
      if (this.reclick) this.clear(true);
      this.reclick = false;
      return;
    }
    if (this.selected != null) {
      const options = this.#candidates();
      if (options.some((m) => m.path[this.prefix.length] === idx)) {
        this.extend(idx);
        return;
      }
      if (idx === this.selected && this.prefix.length === 0) {
        this.clear(true);
        return;
      }
    }
    if (this.moves.some((m) => m.from === idx)) this.select(idx);
    else this.clear();
  }

  extend(idx) {
    this.prefix = [...this.prefix, idx];
    const complete = this.moves.find(
      (m) => m.from === this.selected && m.path.length === this.prefix.length && m.path.every((sq, i) => sq === this.prefix[i]));
    if (complete) {
      const from = this.selected;
      const path = this.prefix;
      this.selected = null;
      this.prefix = [];
      this.paint();
      this.onMove({ from: squareName(from), path: path.map(squareName) });
    } else {
      this.paint();
      this.announce(t("announce.square", { square: squareName(idx), what: t("game.mustcapture") }));
    }
  }

  drop(from, to) {
    const matches = this.moves.filter((m) => m.from === from && m.path.at(-1) === to);
    if (matches.length !== 1) {
      // Several sequences end here (or none do): fall back to square-by-square selection.
      this.select(from);
      return false;
    }
    this.selected = null;
    this.prefix = [];
    this.paint();
    this.onMove({ from: squareName(from), path: matches[0].path.map(squareName) });
    return true;
  }

  // ── Accessibility ─────────────────────────────────────────

  pieceName(code) {
    if (!code) return t("announce.empty");
    return t("announce.piece", {
      piece: t(code[1] === "k" ? "piece.checkers.king" : "piece.checkers.man"),
      color: t(`announce.colorword.checkers.${code[0]}`),
    });
  }

  describe(idx) {
    this.announce(t("announce.square", { square: squareName(idx), what: this.pieceName(this.game?.board[idx]) }));
  }

  describeMove(game, moverName) {
    const last = game.lastMove;
    if (!last) return "";
    const to = last.path.at(-1);
    const piece = t(game.board[squareIndex(to)]?.[1] === "k" ? "piece.checkers.king" : "piece.checkers.man");
    return t(last.captured.length ? "announce.capture" : "announce.move", { name: moverName, piece, to });
  }

  isCapture(game) {
    return Boolean(game.lastMove?.captured.length);
  }

  focus() {
    this.core.focus();
  }

  destroy() {
    this.core.destroy();
  }
}
