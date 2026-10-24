using FabGames.Core.Domain.Enums;

namespace FabGames.Core.Domain.Results;

/// <summary>Final outcome of a game. <see cref="WinnerPlayerId"/> is null for draws.</summary>
public sealed record GameResult(GameOutcome Outcome, string? WinnerPlayerId, GameEndReason Reason)
{
    public static GameResult Win(string winnerPlayerId, GameEndReason reason) =>
        new(GameOutcome.Win, winnerPlayerId, reason);

    public static GameResult Draw(GameEndReason reason) => new(GameOutcome.Draw, null, reason);
}

/// <summary>Untyped result of applying an action, as seen by the room layer.</summary>
public sealed record GameActionResult(bool Success, string? ErrorCode, object? NewState, GameResult? Result)
{
    public static GameActionResult Ok(object newState, GameResult? result = null) =>
        new(true, null, newState, result);

    public static GameActionResult Fail(string errorCode) => new(false, errorCode, null, null);
}

/// <summary>Typed result of applying an action, as produced by an engine.</summary>
public sealed record GameActionResult<TState>(bool Success, string? ErrorCode, TState? NewState, GameResult? Result)
    where TState : class
{
    public static GameActionResult<TState> Ok(TState newState, GameResult? result = null) =>
        new(true, null, newState, result);

    public static GameActionResult<TState> Fail(string errorCode) => new(false, errorCode, null, null);
}

public sealed record OperationResult<T>(bool Success, string? ErrorCode, T? Value)
{
    public static OperationResult<T> Ok(T value) => new(true, null, value);

    public static OperationResult<T> Fail(string errorCode) => new(false, errorCode, default);
}

/// <summary>Stable machine-readable rejection reasons. Clients translate them.</summary>
public static class ErrorCodes
{
    public const string InvalidRequest = "INVALID_REQUEST";
    public const string InvalidAction = "INVALID_ACTION";
    public const string UnknownGame = "UNKNOWN_GAME";
    public const string RoomNotFound = "ROOM_NOT_FOUND";
    public const string RoomFull = "ROOM_FULL";
    public const string RoomLimitReached = "ROOM_LIMIT_REACHED";
    public const string GameInProgress = "GAME_IN_PROGRESS";
    public const string AlreadyInGame = "ALREADY_IN_GAME";
    public const string NotInRoom = "NOT_IN_ROOM";
    public const string NotHost = "NOT_HOST";
    public const string NotEnoughPlayers = "NOT_ENOUGH_PLAYERS";
    public const string GameNotStarted = "GAME_NOT_STARTED";
    public const string GameOver = "GAME_OVER";
    public const string NotYourTurn = "NOT_YOUR_TURN";
    public const string IllegalMove = "ILLEGAL_MOVE";
    public const string PromotionRequired = "PROMOTION_REQUIRED";
    public const string MandatoryCapture = "MANDATORY_CAPTURE";
    public const string IncompleteCapture = "INCOMPLETE_CAPTURE";
    public const string StaleState = "STALE_STATE";
    public const string NoDrawOffer = "NO_DRAW_OFFER";
    public const string DrawAlreadyOffered = "DRAW_ALREADY_OFFERED";
    public const string RematchNotAvailable = "REMATCH_NOT_AVAILABLE";
    public const string MessageInvalid = "MESSAGE_INVALID";
    public const string RateLimited = "RATE_LIMITED";
}
