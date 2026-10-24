using FabGames.Core.Domain.Enums;
using FabGames.Core.Domain.Models;
using FabGames.Core.Domain.Results;
using FabGames.Core.Interfaces;
using FabGames.Web.DTOs;
using FabGames.Web.Mapping;
using Microsoft.AspNetCore.SignalR;

namespace FabGames.Web.Hubs;

/// <summary>
/// Pushes room events to clients. Used by the hub and by background services, so personalised views
/// are sent to each player's own connection and shared events go to the room group.
/// </summary>
public sealed class RoomBroadcaster
{
    private readonly IHubContext<GameHub, IGameClient> _hub;
    private readonly IRoomManager _rooms;
    private readonly RoomViewMapper _views;

    public RoomBroadcaster(IHubContext<GameHub, IGameClient> hub, IRoomManager rooms, RoomViewMapper views)
    {
        _hub = hub;
        _rooms = rooms;
        _views = views;
    }

    public static string GroupName(string roomCode) => $"room:{roomCode}";

    public Task SendViewsAsync(RoomSnapshot room, Func<IGameClient, RoomViewDto, Task> send) =>
        Task.WhenAll(room.Players.Select(p =>
        {
            var connectionId = _rooms.GetConnectionId(p.Player.PlayerId);
            return connectionId is null
                ? Task.CompletedTask
                : send(_hub.Clients.Client(connectionId), _views.Map(room, p.Player.PlayerId));
        }));

    public Task StateUpdatedAsync(RoomSnapshot room) => SendViewsAsync(room, (c, v) => c.StateUpdated(v));

    public Task GameStartedAsync(RoomSnapshot room) => SendViewsAsync(room, (c, v) => c.GameStarted(v));

    public Task LobbyChangedAsync(RoomSnapshot room) => SendViewsAsync(room, (c, v) => c.ReadyStateChanged(v));

    public Task GameOverAsync(RoomSnapshot room, GameResult result) =>
        _hub.Clients.Group(GroupName(room.Code)).GameOver(room.Code, RoomViewMapper.MapResult(result));

    /// <summary>Sends the state, and the game-over notice when this update ended the game.</summary>
    public async Task UpdateAsync(RoomUpdate update)
    {
        if (update.GameStarted)
        {
            await GameStartedAsync(update.Snapshot).ConfigureAwait(false);
        }
        else if (update.Snapshot.Status == RoomStatus.Waiting)
        {
            await LobbyChangedAsync(update.Snapshot).ConfigureAwait(false);
        }
        else
        {
            await StateUpdatedAsync(update.Snapshot).ConfigureAwait(false);
        }

        if (update.NewResult is not null)
        {
            await GameOverAsync(update.Snapshot, update.NewResult).ConfigureAwait(false);
        }
    }

    public Task PlayerReconnectingAsync(string roomCode, string playerId, int secondsLeft) =>
        _hub.Clients.Group(GroupName(roomCode))
            .PlayerReconnecting(roomCode, PlayerIdentity.PublicId(playerId), secondsLeft);

    public Task PlayerReconnectedAsync(string roomCode, string playerId) =>
        _hub.Clients.Group(GroupName(roomCode)).PlayerReconnected(roomCode, PlayerIdentity.PublicId(playerId));

    public async Task PlayerLeftAsync(PlayerDeparture departure)
    {
        await _hub.Clients.Group(GroupName(departure.RoomCode))
            .PlayerLeftRoom(departure.RoomCode, PlayerIdentity.PublicId(departure.PlayerId)).ConfigureAwait(false);

        if (departure.Snapshot is { } room)
        {
            await StateUpdatedAsync(room).ConfigureAwait(false);
            if (departure.NewResult is not null)
            {
                await GameOverAsync(room, departure.NewResult).ConfigureAwait(false);
            }
        }
    }

    /// <summary>Drops a player's connection from the room group (for example after leaving).</summary>
    public Task RemoveFromGroupAsync(string connectionId, string roomCode) =>
        _hub.Groups.RemoveFromGroupAsync(connectionId, GroupName(roomCode));
}
