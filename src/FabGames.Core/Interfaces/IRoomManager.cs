using FabGames.Core.Domain.Models;
using FabGames.Core.Domain.Results;

namespace FabGames.Core.Interfaces;

public interface IGameEngineRegistry
{
    IReadOnlyCollection<IGameEngine> Engines { get; }

    bool TryGet(string gameTypeId, out IGameEngine engine);
}

/// <summary>
/// Owns all rooms. Every mutation is serialized per room and returns an immutable snapshot, so
/// callers can broadcast without holding any lock. Rejections are reported as error codes.
/// </summary>
public interface IRoomManager
{
    int RoomCount { get; }

    Task<OperationResult<RoomUpdate>> CreateRoomAsync(
        string connectionId, string gameTypeId, string playerId, string displayName, string avatarId,
        TimeControl timeControl, CancellationToken ct = default);

    /// <summary>Joins a waiting room. A player already seated in the room is treated as rejoining.</summary>
    Task<OperationResult<RoomUpdate>> JoinRoomAsync(
        string connectionId, string roomCode, string playerId, string displayName, string avatarId,
        CancellationToken ct = default);

    Task<OperationResult<RoomUpdate>> RejoinRoomAsync(
        string connectionId, string roomCode, string playerId, CancellationToken ct = default);

    Task<OperationResult<RoomUpdate>> SetReadyAsync(
        string roomCode, string playerId, bool ready, CancellationToken ct = default);

    /// <summary>Host starts the game before everybody is ready.</summary>
    Task<OperationResult<RoomUpdate>> StartGameAsync(
        string roomCode, string playerId, CancellationToken ct = default);

    Task<OperationResult<RoomUpdate>> MakeMoveAsync(
        string roomCode, string playerId, string actionJson, int expectedStateVersion,
        CancellationToken ct = default);

    Task<OperationResult<RoomUpdate>> ResignAsync(
        string roomCode, string playerId, CancellationToken ct = default);

    /// <summary>Offers a draw; if the opponent already offered one, accepts it.</summary>
    Task<OperationResult<RoomUpdate>> OfferDrawAsync(
        string roomCode, string playerId, CancellationToken ct = default);

    Task<OperationResult<RoomUpdate>> RespondDrawAsync(
        string roomCode, string playerId, bool accept, CancellationToken ct = default);

    /// <summary>Records a rematch vote; when everybody voted, colors swap and a new game starts.</summary>
    Task<OperationResult<RoomUpdate>> RequestRematchAsync(
        string roomCode, string playerId, CancellationToken ct = default);

    Task<OperationResult<ChatMessage>> SendChatAsync(
        string roomCode, string playerId, string message, CancellationToken ct = default);

    /// <summary>Leaves the room. Leaving a running game counts as resigning.</summary>
    Task<OperationResult<PlayerDeparture>> LeaveRoomAsync(
        string roomCode, string playerId, CancellationToken ct = default);

    /// <summary>Starts the disconnect grace period for the player behind the connection.</summary>
    Task<OperationResult<DisconnectInfo>> HandleDisconnectAsync(
        string connectionId, CancellationToken ct = default);

    Task<OperationResult<RoomSnapshot>> GetRoomAsync(string roomCode, CancellationToken ct = default);

    string? GetRoomCodeOfPlayer(string playerId);

    string? GetConnectionId(string playerId);

    /// <summary>Declares timeouts, expires disconnect grace periods and reports countdowns.</summary>
    Task<TimerReport> ProcessTimersAsync(CancellationToken ct = default);

    /// <summary>Removes idle and long-finished rooms. Returns the removed room codes.</summary>
    Task<IReadOnlyList<string>> CleanupAsync(CancellationToken ct = default);
}
