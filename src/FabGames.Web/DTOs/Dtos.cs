namespace FabGames.Web.DTOs;

// ── Requests ────────────────────────────────────────────────────

public sealed record TimeControlDto(int InitialMinutes, int IncrementSeconds);

public sealed record CreateRoomRequest(
    string GameTypeId, string DisplayName, string AvatarId, string PlayerId, TimeControlDto? TimeControl);

public sealed record JoinRoomRequest(string RoomCode, string DisplayName, string AvatarId, string PlayerId);

// ── Responses ───────────────────────────────────────────────────

/// <summary>Uniform hub return value. <c>Error</c> is a stable code such as <c>NOT_YOUR_TURN</c>.</summary>
public sealed record HubResponse<T>(bool Ok, string? Error, T? Data)
{
    public static HubResponse<T> Success(T data) => new(true, null, data);

    public static HubResponse<T> Fail(string error) => new(false, error, default);
}

public sealed record HubResponse(bool Ok, string? Error)
{
    public static HubResponse Success() => new(true, null);

    public static HubResponse Fail(string error) => new(false, error);
}

// ── Room state ──────────────────────────────────────────────────

/// <remarks>
/// <c>Id</c> is a public handle derived from the secret player id, so opponents never see the
/// secret they would need to impersonate someone.
/// </remarks>
public sealed record PlayerDto(
    string Id, string DisplayName, string AvatarId, int Seat, bool Ready, bool Connected, bool Host);

public sealed record RoomDto(
    string Code,
    string GameTypeId,
    string Status,
    TimeControlDto TimeControl,
    string HostId,
    int MinPlayers,
    int MaxPlayers,
    int GamesPlayed,
    IReadOnlyList<PlayerDto> Players,
    IReadOnlyList<string> RematchVotes);

public sealed record ClockDto(IReadOnlyDictionary<string, long> RemainingMs, string? RunningPlayerId, long ServerTimeMs);

public sealed record GameResultDto(string Outcome, string? WinnerId, string Reason);

/// <param name="ColorOrder">Public player ids indexed by color: <c>[white, black]</c>.</param>
/// <param name="Game">Per-game state (<see cref="ChessStateDto"/> or <see cref="CheckersStateDto"/>).</param>
public sealed record SessionDto(
    int StateVersion,
    string CurrentTurnPlayerId,
    IReadOnlyList<string> ColorOrder,
    ClockDto? Clock,
    string? DrawOfferedBy,
    GameResultDto? Result,
    int MoveCount,
    long StartedAtMs,
    long? EndedAtMs,
    object Game);

/// <summary>Everything one client needs to render a room, personalised for the recipient.</summary>
public sealed record RoomViewDto(RoomDto Room, string YouId, SessionDto? Session);

public sealed record ChatMessageDto(string PlayerId, string DisplayName, string Text, long SentAtMs);

// ── Per-game state ──────────────────────────────────────────────

/// <summary>Board squares are indexed 0..63 with a1 = 0; each entry is <c>""</c> or color + piece, e.g. <c>wP</c>, <c>bN</c>.</summary>
public sealed record ChessLastMoveDto(string From, string To, string San);

public sealed record ChessMoveItemDto(int Ply, string Color, string San);

public sealed record ChessLegalMoveDto(string From, string To, string? Promotion);

public sealed record ChessCapturedDto(IReadOnlyList<string> ByWhite, IReadOnlyList<string> ByBlack);

public sealed record ChessStateDto(
    IReadOnlyList<string> Board,
    string Turn,
    bool InCheck,
    string? CheckSquare,
    ChessLastMoveDto? LastMove,
    IReadOnlyList<ChessMoveItemDto> Moves,
    ChessCapturedDto Captured,
    IReadOnlyList<ChessLegalMoveDto> LegalMoves,
    string Fen);

/// <remarks>Checkers entries are <c>""</c>, <c>wm</c>, <c>wk</c>, <c>bm</c>, <c>bk</c> (man/king; w = light, b = dark).</remarks>
public sealed record CheckersLastMoveDto(string From, IReadOnlyList<string> Path, IReadOnlyList<string> Captured, string Notation);

public sealed record CheckersMoveItemDto(int Ply, string Color, string Notation);

public sealed record CheckersLegalMoveDto(string From, IReadOnlyList<string> Path);

public sealed record CheckersStateDto(
    IReadOnlyList<string> Board,
    string Turn,
    bool MustCapture,
    CheckersLastMoveDto? LastMove,
    IReadOnlyList<CheckersMoveItemDto> Moves,
    int CapturedByWhite,
    int CapturedByBlack,
    IReadOnlyList<CheckersLegalMoveDto> LegalMoves);
