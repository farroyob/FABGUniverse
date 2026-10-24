// Original artwork: marble squares, obsidian and pearl chess pieces with a copper band, steel-rimmed discs.
// Pieces are drawn in a 100 x 100 box; fills come from gradients in the shared <defs>.

export const BOARD_DEFS = `
  <linearGradient id="fab-sq-light" x1="0" y1="0" x2="1" y2="1">
    <stop offset="0" stop-color="#c4cee5"/><stop offset="1" stop-color="#9eadd0"/>
  </linearGradient>
  <linearGradient id="fab-sq-dark" x1="0" y1="0" x2="1" y2="1">
    <stop offset="0" stop-color="#1d2644"/><stop offset="1" stop-color="#0f162d"/>
  </linearGradient>
  <radialGradient id="fab-obsidian" cx=".35" cy=".25" r="1">
    <stop offset="0" stop-color="#34416b"/><stop offset=".55" stop-color="#0d1328"/><stop offset="1" stop-color="#05080f"/>
  </radialGradient>
  <radialGradient id="fab-pearl" cx=".35" cy=".25" r="1">
    <stop offset="0" stop-color="#ffffff"/><stop offset=".6" stop-color="#dde4f3"/><stop offset="1" stop-color="#a8b5d1"/>
  </radialGradient>
  <radialGradient id="fab-disc-dark" cx=".35" cy=".25" r="1">
    <stop offset="0" stop-color="#43538a"/><stop offset=".6" stop-color="#121b38"/><stop offset="1" stop-color="#070b18"/>
  </radialGradient>
  <radialGradient id="fab-disc-light" cx=".35" cy=".25" r="1">
    <stop offset="0" stop-color="#ffffff"/><stop offset=".65" stop-color="#d8e0f1"/><stop offset="1" stop-color="#9eacc9"/>
  </radialGradient>
  <linearGradient id="fab-copper" x1="0" y1="0" x2="0" y2="1">
    <stop offset="0" stop-color="#f3c28c"/><stop offset="1" stop-color="#b9763a"/>
  </linearGradient>
`;

// ── Chess ───────────────────────────────────────────────────

const PLINTH = '<path class="body" d="M24 92 H76 Q81 92 81 88 V87 Q81 83 76 83 H24 Q19 83 19 87 V88 Q19 92 24 92Z"/>';
const BAND = '<rect class="band" x="22" y="79" width="56" height="3.4" rx="1.7"/>';

const SHAPES = {
  P: `<circle class="body" cx="50" cy="29" r="10.5"/>
      <rect class="body" x="35" y="39" width="30" height="5.5" rx="2.75"/>
      <path class="body" d="M41 45 Q40 63 31 79 H69 Q60 63 59 45Z"/>`,
  R: `<path class="body" d="M29 14 H40 V22 H45 V14 H55 V22 H60 V14 H71 V31 H29Z"/>
      <path class="body" d="M34 31 H66 L63 41 H37Z"/>
      <path class="body" d="M38 41 H62 L68 79 H32Z"/>`,
  B: `<circle class="body" cx="50" cy="11" r="4.2"/>
      <path class="body" d="M50 15 C64 25 67 37 61.5 47 C59.5 50 57.5 52 56.5 54 H43.5 C42.5 52 40.5 50 38.5 47 C33 37 36 25 50 15Z"/>
      <path class="detail" d="M54 25 L46 38" fill="none" stroke-width="3.2" stroke-linecap="round"/>
      <rect class="body" x="34" y="54" width="32" height="5.5" rx="2.75"/>
      <path class="body" d="M42 60 Q41 69 31 79 H69 Q59 69 58 60Z"/>`,
  Q: `<path class="body" d="M30 55 L23 25 L33 41 L37 18 L44 39 L50 15 L56 39 L63 18 L67 41 L77 25 L70 55Z"/>
      <circle class="body" cx="23" cy="23" r="3.6"/><circle class="body" cx="37" cy="16" r="3.6"/>
      <circle class="body" cx="50" cy="13" r="3.6"/><circle class="body" cx="63" cy="16" r="3.6"/>
      <circle class="body" cx="77" cy="23" r="3.6"/>
      <rect class="body" x="31" y="55" width="38" height="5.5" rx="2.75"/>
      <path class="body" d="M37 61 Q35 70 30 79 H70 Q65 70 63 61Z"/>`,
  K: `<path class="body" d="M46.5 5 H53.5 V11 H59.5 V17.5 H53.5 V24 H46.5 V17.5 H40.5 V11 H46.5Z"/>
      <path class="body" d="M33 54 C29 39 37 28 50 28 C63 28 71 39 67 54Z"/>
      <rect class="body" x="31" y="54" width="38" height="5.5" rx="2.75"/>
      <path class="body" d="M37 60 Q35 70 30 79 H70 Q65 70 63 60Z"/>`,
  N: `<path class="body" d="M72 79 H29 C29 67 32 59 40 53 C44.5 49.5 46 46.5 45 42.5 C41 44.5 37 48 33 49 L23.5 44 C24 37.5 30 30 38 24.5 L40 13 L47.5 19.5 C49 17.5 51 16 53.5 14.5 L55 22 C67 26 75 38 75 56 C75 65 74 72 72 79Z"/>
      <circle class="detail-fill" cx="46" cy="30.5" r="2.5"/>
      <path class="detail" d="M58 28 C66 34 69 44 68 56" fill="none" stroke-width="2.6" stroke-linecap="round" opacity=".55"/>
      <path class="detail" d="M27.5 44.5 L31 45.5" fill="none" stroke-width="2.4" stroke-linecap="round"/>`,
};

/** @param {string} code e.g. "wP", "bN" */
export function chessPiece(code) {
  const side = code[0] === "w" ? "pearl" : "obsidian";
  const shape = SHAPES[code[1]] ?? SHAPES.P;
  return `<g class="cp cp--${side}">
    <ellipse class="shadow" cx="50" cy="91" rx="31" ry="5"/>
    ${PLINTH}${shape}${BAND}
  </g>`;
}

// ── Checkers ────────────────────────────────────────────────

/** @param {string} code "wm" | "wk" | "bm" | "bk" (w = light, b = dark; m = man, k = king) */
export function checkersPiece(code) {
  const side = code[0] === "w" ? "light" : "dark";
  const king = code[1] === "k";
  return `<g class="dp dp--${side}">
    <ellipse class="shadow" cx="50" cy="63" rx="38" ry="9"/>
    <ellipse class="edge" cx="50" cy="57" rx="38" ry="31"/>
    <ellipse class="face" cx="50" cy="50" rx="38" ry="31"/>
    <ellipse class="knurl" cx="50" cy="50" rx="34.5" ry="27.5" fill="none" stroke-dasharray="2.4 2.6"/>
    <ellipse class="inner" cx="50" cy="50" rx="25" ry="19"/>
    ${king ? '<path class="crown" d="M31 56 L35 40 L43 49 L50 37 L57 49 L65 40 L69 56 Z"/><rect class="crown" x="31" y="57" width="38" height="4" rx="2"/>' : ""}
  </g>`;
}
