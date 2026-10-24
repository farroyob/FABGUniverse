# Decisions and deviations

Every place where the build differs from, or adds to, `FAB-GAMES-BRIEF.md`, with the reason.

## Contracts (brief §2)

| # | Decision | Why |
|---|----------|-----|
| 1 | `ActionResult` is `GameActionResult` / `GameActionResult<TState>`. | Avoids the clash with `Microsoft.AspNetCore.Mvc.ActionResult`. |
| 2 | `IGameEngine` gained `GetCurrentTurnPlayerId`, `GetLegalActions`, `GetResult`, `Resign`, `DeclareTimeout`. Typed twin `IGameEngine<TState,TAction>` plus `GameEngineBase<TState,TAction>`. | The room layer enforces turn order and clocks without knowing any game. Engines only write typed logic; the base class parses JSON and casts state. |
| 3 | Typed methods are named `NewGame`, `Apply`, and overloads of the other members. | A typed `CreateInitialState` returning `TState` cannot coexist with the untyped one returning `object`. |
| 4 | Draw offers live in the room (`GameSession.DrawOfferedBy`), not in engine state. | Offers are a table concept, identical for every game. A move clears a pending offer; offering back to an opponent's offer accepts it. |
| 5 | `TimeProvider` replaces `IClock`. | The brief allows it; it ships with a fake (`FakeTimeProvider`) and `PeriodicTimer` supports it. |
| 6 | Game type is a string id (`"chess"`), not a `GameType` enum. | An enum would force a Core change per game and break "a new project plus one DI registration". |
| 7 | `TurnStatus` was dropped. | A turn is one atomic action (a whole multi-jump is one action), so there is no intermediate turn state to model. |
| 8 | `Room` and `GameSession` are internal to Core. Callers receive immutable `RoomSnapshot`s. | Nothing outside the room lock can observe or mutate half-applied state. |
| 9 | Player list order is colour order: index 0 plays White (chess) or the light side (checkers). Rematches reverse it. | One rule for every two-player game; the engine decides who moves first. |
| 10 | `TimeControl` is `(InitialMinutes, IncrementSeconds)` with minutes limited to 0/5/10/15 and increment 0–30 s. | Matches the brief's presets and is trivial to validate and serialise. |

## Checkers

- **Dark moves first.** The starting side is the constant `CheckersEngine.FirstMover = PlayerColor.Black` (dark). `PlayerColor.White` is the light side.
- Mandatory capture, free choice among capture sequences, men capture forward only, kings move and capture one square in any direction, crowning ends the turn even mid-capture.
- The draw counter is `DrawDetector.DefaultMoveLimitPerSide = 40` moves per side without a capture or a man move; `new CheckersEngine(n)` overrides it.
- A capture path is validated atomically. A client that sends a prefix of a longer sequence gets `INCOMPLETE_CAPTURE`; any other non-capture while a capture exists gets `MANDATORY_CAPTURE`.

## Chess

- Threefold repetition and the fifty-move rule are applied automatically (no claim needed). Checkmate outranks the fifty-move rule on the same move.
- Repetition keys include en passant only when an en passant capture is actually legal (FIDE definition).
- A flag fall is a draw when the opponent could never mate (no pawn, rook, queen, or two minor pieces); otherwise a loss.
- Insufficient material also covers any number of same-coloured bishops with no other pieces.

## Transport and security

- **Public ids.** Clients send a secret `PlayerId` (GUID, at least 16 characters). Servers never echo it: every DTO carries `PublicId = hex(SHA-256("fabgames:" + id))[..16]`. Without this an opponent could read the other player's id from a state message and rejoin as them.
- The hub rate limit is a token bucket per connection (burst 30, 10 per second; chat costs 4, create/join cost 5). Excess calls throw `HubException("RATE_LIMITED")`.
- A refresh keeps the seat: the old socket's late disconnect is ignored once the player reattached on a new connection.
- After any rejected move the caller receives the authoritative state so the UI snaps back.
- `ClockTick` from the brief is not sent. Snapshots carry remaining time net of elapsed time plus the server timestamp, and clients count down locally; the server still owns the clock and declares timeouts.
- Leaving a running game counts as resigning. A lobby or finished room is left automatically when the same player creates or joins another room; a running game blocks that with `ALREADY_IN_GAME`.
- The MVC template's bootstrap and jQuery files are still in `wwwroot/lib` and will be replaced in Phase 4.

## Process

- The brief's `git init` was a no-op (the repository already existed); work is on `feature/foundation`.
- `CLAUDE.md` in the repository root describes a different layered architecture (Domain/Application/Infrastructure, Dapper, SQL Server). This project has no database, and the brief specifies its own structure, so the brief was followed. The two should be reconciled.
- The skills named in the request (`impeccable`, `design-taste-frontend`, `emil-design-eng`) and the Playwright MCP were not available in this session, so Phase 3 was done by hand. See `DESIGN.md`.
