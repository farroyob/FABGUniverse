using System.Security.Cryptography;
using System.Text;
using FabGames.Core.Domain.Enums;
using FabGames.Core.Domain.Models;
using FabGames.Core.Domain.Results;
using FabGames.Games.Chess.Logic;
using FabGames.Games.Chess.Models;
using FabGames.Games.Checkers.Models;
using FabGames.Web.DTOs;

namespace FabGames.Web.Mapping;

/// <summary>Public handles for player ids. Secret ids never leave the server.</summary>
public static class PlayerIdentity
{
    public static string PublicId(string playerId) =>
        Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes("fabgames:" + playerId)))[..16];
}

/// <summary>Turns an engine's internal state into the DTO clients render. One mapper per game.</summary>
public interface IGameStateMapper
{
    string GameTypeId { get; }

    /// <param name="state">The engine state object.</param>
    /// <param name="recipientPlayerId">Secret id of the recipient; legal moves are included only for them.</param>
    object Map(object state, string recipientPlayerId);
}

public sealed class ChessStateMapper : IGameStateMapper
{
    public string GameTypeId => "chess";

    public object Map(object state, string recipientPlayerId)
    {
        var s = (ChessGameState)state;
        var board = s.Board;

        var squares = new string[64];
        for (var i = 0; i < 64; i++)
        {
            squares[i] = PieceCode(board[i]);
        }

        var captured = new List<string>[2] { [], [] };
        foreach (var move in s.Moves)
        {
            if (!move.Captured.IsEmpty)
            {
                captured[(int)move.Color].Add(PieceCode(move.Captured));
            }
        }

        var mine = s.Result is null && s.PlayerIdOf(board.ActiveColor) == recipientPlayerId;
        var last = s.LastMove;
        return new ChessStateDto(
            squares,
            ColorName(board.ActiveColor),
            s.InCheck,
            s.InCheck ? new ChessPosition(board.KingSquare(board.ActiveColor)).ToString() : null,
            last is null ? null : new ChessLastMoveDto(last.From.ToString(), last.To.ToString(), last.San),
            [.. s.Moves.Select(m => new ChessMoveItemDto(m.Ply, ColorName(m.Color), m.San))],
            new ChessCapturedDto(captured[(int)PlayerColor.White], captured[(int)PlayerColor.Black]),
            mine
                ? [.. s.LegalMoves.Select(m => MoveValidator.ToAction(m))
                    .Select(a => new ChessLegalMoveDto(a.From, a.To, a.Promotion))]
                : [],
            FenParser.ToFen(board));
    }

    private static string PieceCode(ChessPiece piece) =>
        piece.IsEmpty ? string.Empty : $"{(piece.Color == PlayerColor.White ? 'w' : 'b')}{char.ToUpperInvariant(piece.ToFenChar())}";

    private static string ColorName(PlayerColor color) => color == PlayerColor.White ? "white" : "black";
}

public sealed class CheckersStateMapper : IGameStateMapper
{
    public string GameTypeId => "checkers";

    public object Map(object state, string recipientPlayerId)
    {
        var s = (CheckersGameState)state;

        var squares = new string[64];
        for (var i = 0; i < 64; i++)
        {
            var piece = s.Board[i];
            squares[i] = piece.IsEmpty
                ? string.Empty
                : $"{(piece.Color == PlayerColor.White ? 'w' : 'b')}{(piece.IsKing ? 'k' : 'm')}";
        }

        var captures = new int[2];
        foreach (var record in s.Moves)
        {
            captures[(int)record.Color] += record.Move.Captured.Length;
        }

        var mine = s.Result is null && s.PlayerIdOf(s.ActiveColor) == recipientPlayerId;
        var last = s.LastMove;
        return new CheckersStateDto(
            squares,
            ColorName(s.ActiveColor),
            s.Result is null && s.MustCapture,
            last is null
                ? null
                : new CheckersLastMoveDto(
                    new CheckersPosition(last.Move.From).ToString(),
                    [.. last.Move.Path.Select(p => new CheckersPosition(p).ToString())],
                    [.. last.Move.Captured.Select(p => new CheckersPosition(p).ToString())],
                    last.Notation),
            [.. s.Moves.Select(m => new CheckersMoveItemDto(m.Ply, ColorName(m.Color), m.Notation))],
            captures[(int)PlayerColor.White],
            captures[(int)PlayerColor.Black],
            mine
                ? [.. s.LegalMoves.Select(m => m.ToAction()).Select(a => new CheckersLegalMoveDto(a.From, a.Path))]
                : []);
    }

    private static string ColorName(PlayerColor color) => color == PlayerColor.White ? "white" : "black";
}

/// <summary>Builds the personalised <see cref="RoomViewDto"/> for each recipient.</summary>
public sealed class RoomViewMapper
{
    private readonly Dictionary<string, IGameStateMapper> _mappers;

    public RoomViewMapper(IEnumerable<IGameStateMapper> mappers)
    {
        _mappers = mappers.ToDictionary(m => m.GameTypeId, StringComparer.OrdinalIgnoreCase);
    }

    public RoomViewDto Map(RoomSnapshot room, string recipientPlayerId) =>
        new(MapRoom(room), PlayerIdentity.PublicId(recipientPlayerId), MapSession(room, recipientPlayerId));

    public static RoomDto MapRoom(RoomSnapshot room) =>
        new(
            room.Code,
            room.GameTypeId,
            room.Status.ToString().ToLowerInvariant(),
            new TimeControlDto(room.TimeControl.InitialMinutes, room.TimeControl.IncrementSeconds),
            PlayerIdentity.PublicId(room.HostPlayerId),
            room.MinPlayers,
            room.MaxPlayers,
            room.GamesPlayed,
            [.. room.Players.Select(p => MapPlayer(p))],
            [.. room.RematchVotes.Select(PlayerIdentity.PublicId)]);

    public static PlayerDto MapPlayer(PlayerSnapshot p) =>
        new(
            PlayerIdentity.PublicId(p.Player.PlayerId),
            p.Player.DisplayName,
            p.Player.AvatarId,
            p.Player.SeatNumber,
            p.IsReady,
            p.IsConnected,
            p.IsHost);

    public static GameResultDto MapResult(GameResult result) =>
        new(
            result.Outcome.ToString().ToLowerInvariant(),
            result.WinnerPlayerId is null ? null : PlayerIdentity.PublicId(result.WinnerPlayerId),
            result.Reason.ToString());

    private SessionDto? MapSession(RoomSnapshot room, string recipientPlayerId)
    {
        if (room.Session is not { } s)
        {
            return null;
        }

        var game = _mappers.TryGetValue(room.GameTypeId, out var mapper)
            ? mapper.Map(s.State, recipientPlayerId)
            : new { };

        var clock = s.Clock is null
            ? null
            : new ClockDto(
                s.Clock.RemainingMs.ToDictionary(kv => PlayerIdentity.PublicId(kv.Key), kv => kv.Value),
                s.Clock.RunningPlayerId is null ? null : PlayerIdentity.PublicId(s.Clock.RunningPlayerId),
                s.Clock.ServerTime.ToUnixTimeMilliseconds());

        return new SessionDto(
            s.StateVersion,
            PlayerIdentity.PublicId(s.CurrentTurnPlayerId),
            [.. s.PlayerIdsByColor.Select(PlayerIdentity.PublicId)],
            clock,
            s.DrawOfferedBy is null ? null : PlayerIdentity.PublicId(s.DrawOfferedBy),
            s.Result is null ? null : MapResult(s.Result),
            s.MoveCount,
            s.StartedAt.ToUnixTimeMilliseconds(),
            s.EndedAt?.ToUnixTimeMilliseconds(),
            game);
    }
}
