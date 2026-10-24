using FabGames.Web.DTOs;

namespace FabGames.Web.Hubs;

/// <summary>Messages the server pushes to clients. Every room-scoped event carries the room code.</summary>
public interface IGameClient
{
    Task PlayerJoinedRoom(string roomCode, PlayerDto player);

    Task PlayerLeftRoom(string roomCode, string playerId);

    Task PlayerReconnecting(string roomCode, string playerId, int secondsLeft);

    Task PlayerReconnected(string roomCode, string playerId);

    /// <summary>Lobby state changed (ready flags, host, connections). Personalised like every view.</summary>
    Task ReadyStateChanged(RoomViewDto view);

    Task GameStarted(RoomViewDto view);

    /// <summary>Full snapshot after any change to the running game; ignore if <c>StateVersion</c> is older than the one you hold.</summary>
    Task StateUpdated(RoomViewDto view);

    Task ActionRejected(string roomCode, string reason);

    Task DrawOffered(string roomCode, string playerId);

    Task DrawDeclined(string roomCode, string playerId);

    Task RematchRequested(string roomCode, string playerId);

    Task GameOver(string roomCode, GameResultDto result);

    Task ChatMessageReceived(string roomCode, ChatMessageDto message);
}
