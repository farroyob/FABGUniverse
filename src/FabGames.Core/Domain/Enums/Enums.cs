namespace FabGames.Core.Domain.Enums;

/// <summary>Side a player controls. The numeric value is the index into per-color arrays.</summary>
public enum PlayerColor
{
    White = 0,
    Black = 1,
}

public static class PlayerColorExtensions
{
    public static PlayerColor Opposite(this PlayerColor color) =>
        color == PlayerColor.White ? PlayerColor.Black : PlayerColor.White;
}

public enum RoomStatus
{
    Waiting,
    InProgress,
    Finished,
}

public enum GameOutcome
{
    Win,
    Draw,
}

public enum GameEndReason
{
    Checkmate,
    Stalemate,
    InsufficientMaterial,
    ThreefoldRepetition,
    FiftyMoveRule,
    FortyMoveRule,
    NoPieces,
    NoLegalMoves,
    Resignation,
    DrawAgreement,
    Timeout,
    Abandonment,
}
