// Shared SVG board: squares, animated pieces, drag and click input, keyboard cursor and highlights.
// Games plug in piece artwork and rules of interaction; the server remains the only judge of legality.

const NS = "http://www.w3.org/2000/svg";
const CELL = 100;
const DRAG_THRESHOLD = 7;

const make = (name, attrs = {}, parent) => {
  const node = document.createElementNS(NS, name);
  for (const [key, value] of Object.entries(attrs)) node.setAttribute(key, value);
  parent?.append(node);
  return node;
};

export const FILES = "abcdefgh";
export const squareName = (idx) => `${FILES[idx & 7]}${(idx >> 3) + 1}`;
export const squareIndex = (name) => {
  const file = FILES.indexOf(name?.[0]?.toLowerCase());
  const rank = Number(name?.[1]) - 1;
  return file < 0 || !(rank >= 0 && rank < 8) ? -1 : (rank << 3) | file;
};

const prefersReducedMotion = () => window.matchMedia("(prefers-reduced-motion: reduce)").matches;

export class BoardCore {
  /**
   * @param {object} options
   * @param {HTMLElement} options.container Element that will hold the SVG (becomes the keyboard target).
   * @param {(code: string) => string} options.pieceMarkup SVG markup for a piece, drawn in a 100x100 box.
   * @param {string} options.defs Extra <defs> content (gradients).
   * @param {(idx: number) => boolean} [options.isPlayable] Squares that may hold pieces.
   * @param {(idx: number) => boolean} options.canGrab
   * @param {(idx: number) => void} options.onGrab
   * @param {(idx: number, via: string) => void} options.onActivate
   * @param {(from: number, to: number) => boolean} options.onDrop Returns true when a move was submitted.
   * @param {() => void} options.onCancel
   * @param {(idx: number) => void} options.onCursor
   * @param {string} options.label Accessible name of the board.
   * @param {string} options.help Accessible description (keyboard help).
   */
  constructor(options) {
    this.o = options;
    this.orientation = "white";
    this.interactive = false;
    this.pieces = new Map(); // idx -> { code, el }
    this.cursor = null;
    this.drag = null;
    this.destroyed = false;

    const root = options.container;
    root.replaceChildren();
    root.classList.add("board");
    root.tabIndex = 0;
    root.setAttribute("role", "application");
    root.setAttribute("aria-label", options.label);
    root.dataset.ownKeys = "";
    const helpId = `board-help-${Math.random().toString(36).slice(2, 8)}`;
    const help = document.createElement("span");
    help.id = helpId;
    help.className = "sr-only";
    help.textContent = options.help;
    root.setAttribute("aria-describedby", helpId);

    this.svg = make("svg", { viewBox: `0 0 ${CELL * 8} ${CELL * 8}`, class: "board__svg", "aria-hidden": "true" });
    const defs = make("defs", {}, this.svg);
    defs.innerHTML = options.defs ?? "";
    this.layers = {};
    for (const name of ["squares", "labels", "highlights", "pieces", "hints", "cursor"]) {
      this.layers[name] = make("g", { class: `layer layer--${name}` }, this.svg);
    }
    root.append(this.svg, help);

    this.#buildSquares();
    this.#bind();
  }

  // ── Geometry ──────────────────────────────────────────────

  /** Top-left corner of a square in board units, given the orientation. */
  pos(idx) {
    const file = idx & 7;
    const rank = idx >> 3;
    return this.orientation === "white"
      ? { x: file * CELL, y: (7 - rank) * CELL }
      : { x: (7 - file) * CELL, y: rank * CELL };
  }

  #indexAt(clientX, clientY) {
    const r = this.svg.getBoundingClientRect();
    const col = Math.floor(((clientX - r.left) / r.width) * 8);
    const row = Math.floor(((clientY - r.top) / r.height) * 8);
    if (col < 0 || col > 7 || row < 0 || row > 7) return -1;
    return this.orientation === "white" ? ((7 - row) << 3) | col : (row << 3) | (7 - col);
  }

  #toBoardUnits(clientX, clientY) {
    const r = this.svg.getBoundingClientRect();
    return { x: ((clientX - r.left) / r.width) * CELL * 8, y: ((clientY - r.top) / r.height) * CELL * 8 };
  }

  // ── Squares and labels ────────────────────────────────────

  #buildSquares() {
    this.layers.squares.replaceChildren();
    this.layers.labels.replaceChildren();
    this.squareEls = [];
    for (let idx = 0; idx < 64; idx++) {
      const light = (((idx & 7) + (idx >> 3)) & 1) === 1;
      const rect = make("rect", { class: `sq ${light ? "sq--light" : "sq--dark"}`, width: CELL, height: CELL }, this.layers.squares);
      this.squareEls[idx] = rect;
    }
    this.labelEls = [];
    for (let i = 0; i < 8; i++) {
      this.labelEls.push(make("text", { class: "coord" }, this.layers.labels));
      this.labelEls.push(make("text", { class: "coord" }, this.layers.labels));
    }
    this.#layoutSquares();
  }

  #layoutSquares() {
    for (let idx = 0; idx < 64; idx++) {
      const { x, y } = this.pos(idx);
      this.squareEls[idx].setAttribute("x", x);
      this.squareEls[idx].setAttribute("y", y);
    }
    // File letters along the bottom row, rank numbers down the left column.
    const white = this.orientation === "white";
    const bottomRank = white ? 0 : 7;
    const leftFile = white ? 0 : 7;
    for (let i = 0; i < 8; i++) {
      const fileIdx = white ? i : 7 - i; // file shown in column i
      const rankIdx = white ? 7 - i : i; // rank shown in row i
      const bottom = this.labelEls[i * 2];
      bottom.textContent = FILES[fileIdx];
      bottom.setAttribute("x", i * CELL + 7);
      bottom.setAttribute("y", 7 * CELL + CELL - 8);
      bottom.setAttribute("class", `coord ${((fileIdx + bottomRank) & 1) === 1 ? "coord--on-light" : "coord--on-dark"}`);
      const left = this.labelEls[i * 2 + 1];
      left.textContent = String(rankIdx + 1);
      left.setAttribute("x", 8);
      left.setAttribute("y", i * CELL + 22);
      left.setAttribute("class", `coord ${((leftFile + rankIdx) & 1) === 1 ? "coord--on-light" : "coord--on-dark"}`);
    }
  }

  setOrientation(orientation, { animate = false } = {}) {
    if (orientation === this.orientation) return;
    this.orientation = orientation;
    this.#layoutSquares();
    for (const [idx, piece] of this.pieces) this.#place(piece.el, idx, { animate });
    this.setHighlights(this.lastHighlights ?? {});
    this.#drawCursor();
  }

  setInteractive(value) {
    this.interactive = Boolean(value);
    this.o.container.classList.toggle("is-interactive", this.interactive);
  }

  // ── Pieces ────────────────────────────────────────────────

  #createPiece(idx, code, { appear = false } = {}) {
    const el = make("g", { class: "piece", "data-code": code });
    const art = make("g", { class: "piece__art" }, el);
    art.innerHTML = this.o.pieceMarkup(code);
    this.layers.pieces.append(el);
    this.#place(el, idx, { animate: false });
    if (appear && !prefersReducedMotion()) {
      el.classList.add("is-appearing");
      el.addEventListener("animationend", () => el.classList.remove("is-appearing"), { once: true });
    }
    return el;
  }

  #place(el, idx, { animate = true, duration } = {}) {
    const { x, y } = this.pos(idx);
    if (!animate || prefersReducedMotion()) {
      el.style.transition = "none";
      el.style.transform = `translate(${x}px, ${y}px)`;
      void el.getBoundingClientRect();
      el.style.transition = "";
    } else {
      if (duration) el.style.transitionDuration = `${duration}ms`;
      el.style.transform = `translate(${x}px, ${y}px)`;
    }
  }

  #distance(a, b) {
    return Math.abs((a & 7) - (b & 7)) + Math.abs((a >> 3) - (b >> 3));
  }

  /**
   * Makes the pieces on screen match `codes` (64 entries, "" for empty). Pieces that moved are animated
   * from where they were, so captures, castling and en passant all fall out of the same diff.
   * @param {string[]} codes
   * @param {{animate?: boolean, move?: {from: number, to: number, path?: number[]}|null}} [opts]
   */
  setBoard(codes, { animate = true, move = null } = {}) {
    const current = new Map(this.pieces);
    const next = new Map();
    codes.forEach((code, idx) => { if (code) next.set(idx, code); });

    // The announced move first: it keeps the mover's identity through captures, promotion and crowning,
    // even when the captured piece looks exactly like the capturer.
    const moves = [];
    if (move && current.has(move.from) && next.has(move.to) && !next.has(move.from)) {
      const mover = current.get(move.from);
      current.delete(move.from);
      const victim = current.get(move.to);
      if (victim) {
        current.delete(move.to);
        this.#remove(victim.el, animate);
      }
      moves.push({ piece: mover, from: move.from, to: move.to, newCode: next.get(move.to), path: move.path });
      next.delete(move.to);
    }

    const keep = new Set();
    for (const [idx, code] of next) {
      if (current.get(idx)?.code === code) keep.add(idx);
    }
    const gone = [...current].filter(([idx]) => !keep.has(idx));
    const fresh = [...next].filter(([idx]) => !keep.has(idx));

    // Everything else (the castling rook, ...): the nearest piece with the same code.
    for (const [toIdx, code] of [...fresh]) {
      let best = -1;
      let bestDistance = Infinity;
      gone.forEach(([idx, piece], i) => {
        if (piece.code !== code) return;
        const d = this.#distance(idx, toIdx);
        if (d < bestDistance) { best = i; bestDistance = d; }
      });
      if (best >= 0) {
        const [[fromIdx, piece]] = gone.splice(best, 1);
        fresh.splice(fresh.findIndex(([idx]) => idx === toIdx), 1);
        moves.push({ piece, from: fromIdx, to: toIdx, newCode: code });
      }
    }

    for (const [, piece] of gone) this.#remove(piece.el, animate);

    const rebuilt = new Map();
    for (const idx of keep) rebuilt.set(idx, current.get(idx));
    for (const m of moves) {
      rebuilt.set(m.to, { code: m.newCode, el: m.piece.el });
      this.#travel(m, animate);
    }
    for (const [idx, code] of fresh) {
      rebuilt.set(idx, { code, el: this.#createPiece(idx, code, { appear: animate }) });
    }
    this.pieces = rebuilt;

    // A kept piece may still sit where a drag left it; put it back on its square.
    for (const idx of keep) {
      const piece = this.pieces.get(idx);
      if (!piece.el.classList.contains("is-dragging")) this.#place(piece.el, idx, { animate: false });
    }
  }

  #travel({ piece, from, to, newCode, path }, animate) {
    const el = piece.el;
    el.classList.remove("is-lifted", "is-dragging");
    this.layers.pieces.append(el); // draw the mover above the rest
    const swap = () => {
      if (piece.code !== newCode) {
        el.querySelector(".piece__art").innerHTML = this.o.pieceMarkup(newCode);
        el.dataset.code = newCode;
        el.classList.add("is-promoted");
        el.addEventListener("animationend", () => el.classList.remove("is-promoted"), { once: true });
      }
    };

    if (!animate || prefersReducedMotion()) {
      this.#place(el, to, { animate: false });
      swap();
      return;
    }

    const steps = path && path.length ? [from, ...path] : [from, to];
    if (steps.length > 2 && el.animate) {
      // Multi-jump: hop through every landing square.
      const frames = steps.map((idx) => {
        const { x, y } = this.pos(idx);
        return { transform: `translate(${x}px, ${y}px)` };
      });
      const hop = 200;
      const animation = el.animate(frames, { duration: hop * (steps.length - 1), easing: "ease-in-out" });
      this.#place(el, to, { animate: false });
      animation.finished.then(swap, swap);
      return;
    }

    const duration = Math.min(300, 150 + this.#distance(from, to) * 22);
    this.#place(el, from, { animate: false });
    requestAnimationFrame(() => {
      this.#place(el, to, { animate: true, duration });
      el.classList.add("is-moving");
      setTimeout(() => { el.classList.remove("is-moving"); swap(); }, duration);
    });
  }

  #remove(el, animate) {
    if (!animate || prefersReducedMotion()) {
      el.remove();
      return;
    }
    el.classList.add("is-captured");
    setTimeout(() => el.remove(), 260);
  }

  /** Square index the piece `el` currently belongs to, or -1. */
  pieceAt(idx) {
    return this.pieces.get(idx) ?? null;
  }

  // ── Highlights ────────────────────────────────────────────

  /**
   * @param {{last?: number[], check?: number|null, selected?: number|null, targets?: number[],
   *          captures?: number[], path?: number[], doomed?: number[]}} h
   */
  setHighlights(h) {
    this.lastHighlights = h;
    const hl = this.layers.highlights;
    const hints = this.layers.hints;
    hl.replaceChildren();
    hints.replaceChildren();

    const box = (idx, cls) => {
      const { x, y } = this.pos(idx);
      make("rect", { class: `hl ${cls}`, x, y, width: CELL, height: CELL }, hl);
    };
    for (const idx of h.last ?? []) box(idx, "hl--last");
    if (h.check != null && h.check >= 0) box(h.check, "hl--check");
    if (h.selected != null && h.selected >= 0) box(h.selected, "hl--selected");
    for (const idx of h.doomed ?? []) box(idx, "hl--doomed");

    for (const idx of h.path ?? []) {
      const { x, y } = this.pos(idx);
      make("circle", { class: "hint hint--path", cx: x + 50, cy: y + 50, r: 12 }, hints);
    }
    for (const idx of h.targets ?? []) {
      const { x, y } = this.pos(idx);
      make("circle", { class: "hint hint--move", cx: x + 50, cy: y + 50, r: 14 }, hints);
    }
    for (const idx of h.captures ?? []) {
      const { x, y } = this.pos(idx);
      make("circle", { class: "hint hint--capture", cx: x + 50, cy: y + 50, r: 42 }, hints);
    }
  }

  // ── Keyboard cursor ───────────────────────────────────────

  setCursor(idx) {
    this.cursor = idx;
    this.#drawCursor();
    if (idx != null) this.o.onCursor?.(idx);
  }

  #drawCursor() {
    this.layers.cursor.replaceChildren();
    if (this.cursor == null || this.cursor < 0) return;
    const { x, y } = this.pos(this.cursor);
    make("rect", { class: "cursor", x: x + 4, y: y + 4, width: CELL - 8, height: CELL - 8, rx: 10 }, this.layers.cursor);
  }

  #moveCursor(dir) {
    const current = this.cursor ?? (this.orientation === "white" ? 12 : 51);
    const { x, y } = this.pos(current);
    let col = x / CELL;
    let row = y / CELL;
    if (dir === "left") col = Math.max(0, col - 1);
    if (dir === "right") col = Math.min(7, col + 1);
    if (dir === "up") row = Math.max(0, row - 1);
    if (dir === "down") row = Math.min(7, row + 1);
    const idx = this.orientation === "white" ? ((7 - row) << 3) | col : (row << 3) | (7 - col);
    this.setCursor(idx);
  }

  focus() {
    this.o.container.focus({ preventScroll: true });
  }

  // ── Input ─────────────────────────────────────────────────

  #bind() {
    const root = this.o.container;

    root.addEventListener("keydown", (e) => {
      if (e.altKey || e.ctrlKey || e.metaKey) return;
      const dirs = { ArrowLeft: "left", ArrowRight: "right", ArrowUp: "up", ArrowDown: "down" };
      if (e.key in dirs) {
        e.preventDefault();
        this.#moveCursor(dirs[e.key]);
      } else if (e.key === "Enter" || e.key === " ") {
        e.preventDefault();
        if (this.cursor == null) this.#moveCursor("none");
        else this.o.onActivate(this.cursor, "keyboard");
      } else if (e.key === "Escape") {
        e.preventDefault();
        this.o.onCancel();
      }
    });
    root.addEventListener("focus", () => {
      document.documentElement.dataset.boardFocus = "1";
      if (this.cursor == null) this.#moveCursor("none");
      else this.#drawCursor();
    });
    root.addEventListener("blur", () => {
      delete document.documentElement.dataset.boardFocus;
      this.layers.cursor.replaceChildren();
    });
    root.addEventListener("gamepad-dir", (e) => this.#moveCursor(e.detail));
    root.addEventListener("gamepad-select", () => { if (this.cursor != null) this.o.onActivate(this.cursor, "gamepad"); });
    root.addEventListener("gamepad-cancel", () => this.o.onCancel());

    this.svg.addEventListener("pointerdown", (e) => this.#pointerDown(e));
    this.svg.addEventListener("pointermove", (e) => this.#pointerMove(e));
    this.svg.addEventListener("pointerup", (e) => this.#pointerUp(e));
    this.svg.addEventListener("pointercancel", () => this.#endDrag(false, null));
    this.svg.addEventListener("contextmenu", (e) => e.preventDefault());
  }

  #pointerDown(e) {
    if (e.button !== 0 && e.pointerType === "mouse") return;
    const idx = this.#indexAt(e.clientX, e.clientY);
    if (idx < 0) return;
    this.o.container.focus({ preventScroll: true });
    this.drag = { idx, startX: e.clientX, startY: e.clientY, active: false, id: e.pointerId, piece: null, grabbed: false };

    const piece = this.pieces.get(idx);
    if (this.interactive && piece && this.o.canGrab(idx)) {
      this.drag.piece = piece;
      this.drag.grabbed = true;
      this.o.onGrab(idx);
      piece.el.classList.add("is-lifted");
    }
    try {
      this.svg.setPointerCapture?.(e.pointerId);
    } catch {
      // Not an active pointer (synthetic events); dragging still works without capture.
    }
  }

  #pointerMove(e) {
    const d = this.drag;
    if (!d || e.pointerId !== d.id || !d.piece) return;
    if (!d.active) {
      if (Math.hypot(e.clientX - d.startX, e.clientY - d.startY) < DRAG_THRESHOLD) return;
      d.active = true;
      d.piece.el.classList.add("is-dragging");
      this.layers.pieces.append(d.piece.el);
      d.piece.el.style.transition = "none";
    }
    const p = this.#toBoardUnits(e.clientX, e.clientY);
    d.piece.el.style.transform = `translate(${p.x - CELL / 2}px, ${p.y - CELL / 2}px)`;
  }

  #pointerUp(e) {
    const d = this.drag;
    if (!d || e.pointerId !== d.id) return;
    const target = this.#indexAt(e.clientX, e.clientY);
    this.#endDrag(true, target);
  }

  #endDrag(commit, target) {
    const d = this.drag;
    this.drag = null;
    if (!d) return;
    const el = d.piece?.el;
    if (el) {
      el.classList.remove("is-dragging", "is-lifted");
      el.style.transition = "";
    }

    if (!commit) {
      if (el) this.#place(el, d.idx, { animate: true });
      return;
    }

    if (d.active && el) {
      const submitted = target >= 0 && target !== d.idx && this.o.onDrop(d.idx, target);
      // Rest on the target while the server confirms, otherwise glide home.
      this.#place(el, submitted ? target : d.idx, { animate: true });
      this.setCursor(target >= 0 ? target : d.idx);
      return;
    }

    this.o.onActivate(d.idx, d.grabbed ? "grab" : "pointer");
    this.setCursor(d.idx);
  }

  /** Cancels a drag in flight (state changed underneath it). */
  resetDrag() {
    if (this.drag) this.#endDrag(false, null);
  }

  destroy() {
    this.destroyed = true;
    this.o.container.replaceChildren();
  }
}
