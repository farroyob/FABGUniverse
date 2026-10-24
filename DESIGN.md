# DESIGN.md — FAB Games design directions (Phase 3, awaiting approval)

Context: `PRODUCT.md`, the logo (blue `#2972F5` on ink `#080C18`, white wordmark) and the two key-art renders (blue-lit marble boards, steel checkers, obsidian and pearl chess pieces edged in copper, drifting dust and sparks). A visual specimen of both directions is in `docs/design-directions.html`.

Both directions share the same structure (boot → profile → dashboard → lobby → game → result), the same input model and the same accessibility rules. They differ in material, geometry and motion character.

## Shared rules

- Animate only `transform` and `opacity`. Input feedback ≤ 200 ms, transitions ≤ 400 ms, boot ≤ 4 s and skippable.
- `prefers-reduced-motion`: crossfades and fades only; no parallax, tilt, particles or sweeps.
- Contrast: body text ≥ 4.5:1. Brand blue `#2972F5` is 4.48:1 on ink, so it is for large type, graphics and glows. Small blue text uses `#5B94FF` (6.6:1); button labels sit on `#1F5BD1` (white on it is 6.0:1).
- Focus ring follows the last input (mouse, touch, keyboard, gamepad) and is always visible for keyboard and gamepad.
- Fonts are self-hosted in `wwwroot/fonts` (WOFF2, subset). No request to a font CDN, in keeping with "no trackers".

---

## A. Night Vault — cinematic marble, light as the material

*The key art, turned into an interface. Deep blue-black, soft rim light, glass panels, slow atmospheric motion.*

**Palette**

| Role | Value | Notes |
|---|---|---|
| Page / panel / raised | `#05080F` / `#0D1426` / `#151F3A` | Brand ink `#080C18` sits between page and panel |
| Line | `rgba(157,190,255,.14)` | 1 px hairlines, plus a 1 px top highlight on glass |
| Brand blue | `#2972F5` | Glows, rings, large type, key-art tint |
| Blue 600 / 400 / 300 | `#1F5BD1` / `#5B94FF` / `#9DBEFF` | Button fill / small text and links / halo text |
| Text 1 / 2 / 3 | `#F4F7FF` / `#A9B4CC` / `#7C88A6` | 18.2 / 9.4 / 5.5 :1 on ink |
| Ember (copper) | `#E8A15C` | Your turn, last move; lifted from the pieces' bands (9.0:1) |
| Danger | `#FF6B7A` | Check, last 30 s on a clock (7.1:1) |

**Type.** Archivo (variable, width 125, weight 800) for display: its extended letterforms echo the FAB wordmark. Hanken Grotesk for interface text. Geist Mono for clocks and room codes. Display is large and tight, labels small and tracked.

**Shape.** Tiles 20 px radius, panels 14, chips pill. Glass panels use backdrop blur on at most three static panels per screen.

**Boot (3.8 s).** 0.0 black → 0.4 a faint blue bloom rises from below centre, dust fades up → 1.2 the shield strokes slide in along their cut angle and assemble, one light sweep crosses → 2.2 the wordmark rises into place as the chime sounds (two soft stacked fifths) → 3.2 "Toca para empezar" breathes → any input fades to profile select in 200 ms.

**Profile select.** 112 px avatar bubbles in a centred row. The focused one scales to 1.12 with a blue ring and under-glow; its name fades in below. A "+" bubble creates a profile.

**Dashboard tiles.** Tall 4:5 key-art cards. Focus: scale 1.08, glow behind (a pre-rendered radial gradient fading in via opacity), siblings dim to 55 % and shrink to .96, spring-like (stiffness ≈ 300, damping ≈ 28). The full-screen background crossfades (600 ms) to that game's art with a 40 s slow drift; ≤ 40 dust particles on one canvas, paused when the tab is hidden. Pointer devices add a ≤ 3° magnetic tilt; keyboard, gamepad and reduced-motion get none. The description row under the tiles enters with an 8 px rise and 160 ms stagger.

**Boards and pieces.**
- *Marble board*: light squares cool marble (`#C9D3E6` with faint veining), dark squares `#151B2E`, a beveled frame with a steel hairline. Light/dark squares are 11.4:1 apart.
- *Chess*: obsidian vs pearl pieces with a copper band at the base (straight from the art), flat shapes with soft gradients and one contour line.
- *Checkers*: steel-rimmed discs with a knurled edge; dark = blue-black with blue rim light, light = pearl; kings carry a stacked ring.
- Last move: underlit ember. Legal moves: soft blue dots, capture targets ringed. Check: a slow red pulse on the king's square only.

**Motion language.** Springs for focus and selection; ease-out-quint 240 ms for crossfades. Pieces glide 180–260 ms scaled to distance with a 2 px lift arc; captures scale down and fade in 120 ms; promotion picker blooms from the pawn. Sound: soft, low, glassy.

**Risks.** Blur and glow cost on weak TVs and phones. Mitigation: blur only on static panels, glows pre-rendered and faded by opacity, `will-change` only during transitions, a "lite effects" toggle in settings.

---

## B. Signal Deck — broadcast HUD, cut geometry

*Tournament-broadcast clarity. Flat ink, hairline frames, and the logo's steep cut angle as the only ornament.*

**Palette**

| Role | Value | Notes |
|---|---|---|
| Ink / deck / steel / rule | `#070A12` / `#0E1320` / `#1A2236` / `#2A3550` | Flat fills, no blur |
| Signal blue | `#2972F5` (text `#5B94FF`) | Single cool accent |
| Text ice / mute | `#DCE8FF` / `#8390AE` | 16.1 / 6.2 :1 |
| Live copper | `#E58A4A` | The one warm colour: your turn, last move, low time (7.6:1) |

**Type.** Barlow Condensed 700/800 in caps for headings and the scoreboard, Barlow for text, IBM Plex Mono for clocks, codes and move numbers.

**Shape.** No radii. Corners are chamfered at the logo's cut angle (~12°) with `clip-path`; panels are hairline frames with small corner ticks.

**Boot (3.4 s).** 0.0 black → 0.3 a hairline tick grid draws out from the centre → 0.9 the shield strokes draw on like blueprint lines, then fill → 1.8 the wordmark wipes in left to right along the 12° cut → 2.4 a short click and rising two-note → 3.0 "PRESS ANY BUTTON" with a blinking tick.

**Profile select.** A roster strip of chamfered cards; the focused card extends upward and shows its name in condensed caps.

**Dashboard tiles.** Wide 16:9 cards. Focus: frame brightens, corner brackets snap in around the card, scale only 1.04. Backgrounds change with a 200 ms diagonal wipe instead of a fade. Grain overlay, almost no particles.

**Boards and pieces.**
- *Duotone board* `#1B2440` / `#34456E` with mono coordinates.
- *Chess*: constructed geometric Staunton silhouettes, white vs blue-black with outline, no copper.
- *Checkers*: flat discs with a concentric ring; kings carry a chevron stack.
- Last move copper outline; legal moves are small diamonds.

**Motion language.** Quick and mechanical: 120–180 ms, `cubic-bezier(.2,.8,.2,1)`, snaps rather than springs, overshoot under 3 %.

**Risks.** Flat graphic boards waste the supplied marble and metal renders and clash with them on the dashboard; the result is crisper but less "console-cinematic".

---

## Recommendation

**Go with A (Night Vault), borrowing three things from B.**

- The brief asks for cinematic, fluid, premium, and the supplied art is photographic marble and light. A shows it at full strength; B would sit beside it awkwardly.
- From B: the logo's 12° cut as a recurring shape (focus brackets, clock plates, the result banner), mono scoreboard-style clocks, and B's fast snap for in-game feedback so play never feels floaty.

## Decisions I need from you

1. **A, B, or A with B's borrowings?** (recommended: A + borrowings)
2. **Fonts:** self-host WOFF2 for the chosen families (recommended; no third-party requests), or load from Google Fonts.
3. **Chess pieces:** draw an original set (recommended; consistent with the copper-band look), or use an open-licensed set and credit it.
4. **Missing art:** `home-bg` was not provided; I would build the idle dashboard background from the two covers plus a CSS/SVG composition.
