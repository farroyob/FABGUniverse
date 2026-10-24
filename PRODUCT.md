# PRODUCT.md — FAB Games

## What it is

FAB Games is an ad-free, real-time multiplayer board game site for playing with friends. Two games at launch (chess, checkers) and a "coming soon" slot. Open source, non-commercial licence, single server, no accounts.

## Who it is for

- **Friends who want a game right now.** One person creates a room, sends a link or a six-letter code, the other opens it and they are playing within seconds. Often on a phone, often in the same chat thread as the invite.
- **People who dislike what the big game sites became.** No ads, no popups, no sign-up wall, no tracking. Quiet confidence is the selling point.
- **Couch and big-screen use.** The home screen works from a sofa with a gamepad or a TV browser, not only with a mouse.

## Register

Product (an app people use repeatedly), with a brand layer on the first screens. Design serves play: the board is the hero once a game starts. Cinematic treatment belongs to boot, profile select and the home dashboard, and recedes as soon as a game begins.

## Brand

- **Name and mark.** FAB Games. The shield mark stacks F, A, B in broken blue strokes cut at a steep angle; the wordmark is white, extended and heavy, with GAMES tracked wide beneath in blue.
- **Colour from the logo** (sampled): brand blue `#2972F5`, ink background `#080C18`, white `#FEFEFE`.
- **Key art.** Marble boards in blue-black light, brushed steel checkers, obsidian and pearl chess pieces edged in copper, drifting dust and sparks. This is the mood: expensive, quiet, a little dangerous.

## Personality

Confident, precise, unhurried. Speaks in short plain sentences in Spanish first, English second. Never jokes at the player's expense, never nags, never celebrates louder than the moment deserves.

## Principles

1. **The game comes first.** Anything that delays the first move needs a reason. The boot sequence is skippable by any key, click or tap, and plays once per session.
2. **Motion is the signature, and it is cheap.** Animate only `transform` and `opacity`. Spring-like focus, crossfades, satisfying piece moves. Reduced motion means fades only, never nothing.
3. **Original, console-inspired, never copied.** The flow (boot → profile → dashboard of tiles) is a familiar pattern; every asset, name, sound, symbol and layout is original.
4. **Server decides, client shows.** The UI never implies a move is legal before the server says so beyond highlighting the legal moves the server sent.
5. **Every input works.** Mouse, touch and swipe, keyboard, gamepad. A visible focus ring follows whichever input was used last.
6. **Calm under pressure.** Clocks, check, forced captures and disconnects are the stressful moments; they get clear, restrained signals, not alarms.

## Anti-references

- Ad-supported game portals, cookie-wall casinos, "daily reward" mechanics.
- Neon-overload "gamer" UI, comic fonts, particle confetti for nothing.
- Copying any console maker's assets, symbols (△ ○ ✕ □), fonts, sounds or exact layout.
- Generic dark-mode SaaS cards with a purple gradient.

## Constraints that shape design

- Spanish default, English switch; text expands in Spanish, so no fixed-width labels.
- Phones: boards fit the viewport, thumb-reachable controls, no hover-only affordances.
- WCAG AA contrast. **Finding:** brand blue `#2972F5` on the ink background is 4.48:1 and white on brand blue is 4.36:1, both just under AA for normal-size text. Use brand blue for large type, graphics and glows; use a lighter blue for small link text and a deeper blue under white button labels.
- Audio is synthesized (Web Audio), starts only after a user gesture, is mutable and the choice is remembered.
- No third-party analytics, fonts or scripts that phone home beyond the pinned CDN assets listed in the brief.

## Success

A first-time visitor can open an invite link and make their first move in under 15 seconds, on a phone, without reading anything. A returning player can go from opening the site to a created room in three inputs.
