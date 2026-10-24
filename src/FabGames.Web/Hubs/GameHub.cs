using FabGames.Core.Domain.Enums;
using FabGames.Core.Domain.Models;
using FabGames.Core.Domain.Results;
using FabGames.Core.Interfaces;
using FabGames.Web.DTOs;
using FabGames.Web.Mapping;
using Microsoft.AspNetCore.SignalR;

namespace FabGames.Web.Hubs;

/// <summary>
/// Thin transport over <see cref="IRoomManager"/>: clients send intents, the room manager validates and
/// applies them, and the hub fans the result out. The hub never decides game legality.
/// </summary>
public sealed class GameHub : Hub<IGameClient>
{
    /// <summary>Player ids are client-generated GUIDs; refuse trivially guessable ones.</summary>
    public const int MinPlayerIdLength = 16;

    private readonly IRoomManager _rooms;
    private readonly RoomBroadcaster _broadcaster;
    private readonly RoomViewMapper _views;

    public GameHub(IRoomManager rooms, RoomBroadcaster broadcaster, RoomViewMapper views)
    {
        _rooms = rooms;
        _broadcaster = broadcaster;
        _views = views;
    }

    private CancellationToken Ct => Context.ConnectionAborted;

    // ── Entering and leaving rooms ──────────────────────────────

    public async Task<HubResponse<RoomViewDto>> CreateRoom(CreateRoomRequest request)
    {
        if (request is null || !IsValidPlayerId(request.PlayerId))
        {
            return HubResponse<RoomViewDto>.Fail(ErrorCodes.InvalidRequest);
        }

        var departure = await LeaveStaleRoomAsync(request.PlayerId, null).ConfigureAwait(false);
        if (departure is not null)
        {
            return HubResponse<RoomViewDto>.Fail(departure);
        }

        var timeControl = request.TimeControl is null
            ? TimeControl.None
            : new TimeControl(request.TimeControl.InitialMinutes, request.TimeControl.IncrementSeconds);
        var result = await _rooms.CreateRoomAsync(
            Context.ConnectionId, request.GameTypeId, request.PlayerId, request.DisplayName, request.AvatarId,
            timeControl, Ct).ConfigureAwait(false);
        if (!result.Success)
        {
            return HubResponse<RoomViewDto>.Fail(result.ErrorCode!);
        }

        var snapshot = result.Value!.Snapshot;
        await Groups.AddToGroupAsync(Context.ConnectionId, RoomBroadcaster.GroupName(snapshot.Code), Ct).ConfigureAwait(false);
        return HubResponse<RoomViewDto>.Success(_views.Map(snapshot, request.PlayerId));
    }

    public async Task<HubResponse<RoomViewDto>> JoinRoom(JoinRoomRequest request)
    {
        if (request is null || !IsValidPlayerId(request.PlayerId))
        {
            return HubResponse<RoomViewDto>.Fail(ErrorCodes.InvalidRequest);
        }

        var code = FabGames.Core.Services.RoomCodeGenerator.Normalize(request.RoomCode);
        var departure = await LeaveStaleRoomAsync(request.PlayerId, code).ConfigureAwait(false);
        if (departure is not null)
        {
            return HubResponse<RoomViewDto>.Fail(departure);
        }

        var result = await _rooms.JoinRoomAsync(
            Context.ConnectionId, request.RoomCode, request.PlayerId, request.DisplayName, request.AvatarId, Ct)
            .ConfigureAwait(false);
        if (!result.Success)
        {
            return HubResponse<RoomViewDto>.Fail(result.ErrorCode!);
        }

        var snapshot = result.Value!.Snapshot;
        await Groups.AddToGroupAsync(Context.ConnectionId, RoomBroadcaster.GroupName(snapshot.Code), Ct).ConfigureAwait(false);

        var me = snapshot.Players.First(p => p.Player.PlayerId == request.PlayerId);
        await Clients.OthersInGroup(RoomBroadcaster.GroupName(snapshot.Code))
            .PlayerJoinedRoom(snapshot.Code, RoomViewMapper.MapPlayer(me)).ConfigureAwait(false);
        await _broadcaster.LobbyChangedAsync(snapshot).ConfigureAwait(false);
        return HubResponse<RoomViewDto>.Success(_views.Map(snapshot, request.PlayerId));
    }

    /// <summary>Reattaches a refreshed or reconnected client to its seat.</summary>
    public async Task<HubResponse<RoomViewDto>> RejoinRoom(string roomCode, string playerId)
    {
        if (!IsValidPlayerId(playerId))
        {
            return HubResponse<RoomViewDto>.Fail(ErrorCodes.InvalidRequest);
        }

        var result = await _rooms.RejoinRoomAsync(Context.ConnectionId, roomCode, playerId, Ct).ConfigureAwait(false);
        if (!result.Success)
        {
            return HubResponse<RoomViewDto>.Fail(result.ErrorCode!);
        }

        var snapshot = result.Value!.Snapshot;
        await Groups.AddToGroupAsync(Context.ConnectionId, RoomBroadcaster.GroupName(snapshot.Code), Ct).ConfigureAwait(false);
        await _broadcaster.PlayerReconnectedAsync(snapshot.Code, playerId).ConfigureAwait(false);
        await _broadcaster.StateUpdatedAsync(snapshot).ConfigureAwait(false);
        return HubResponse<RoomViewDto>.Success(_views.Map(snapshot, playerId));
    }

    public async Task<HubResponse> LeaveRoom(string roomCode)
    {
        if (CurrentPlayer() is not { } playerId)
        {
            return HubResponse.Fail(ErrorCodes.NotInRoom);
        }

        var result = await _rooms.LeaveRoomAsync(roomCode, playerId, Ct).ConfigureAwait(false);
        if (!result.Success)
        {
            return HubResponse.Fail(result.ErrorCode!);
        }

        var departure = result.Value!;
        await _broadcaster.RemoveFromGroupAsync(Context.ConnectionId, departure.RoomCode).ConfigureAwait(false);
        await _broadcaster.PlayerLeftAsync(departure).ConfigureAwait(false);
        return HubResponse.Success();
    }

    // ── Lobby ───────────────────────────────────────────────────

    public Task<HubResponse> SetReady(string roomCode, bool ready) =>
        RunAsync(roomCode, (id, ct) => _rooms.SetReadyAsync(roomCode, id, ready, ct));

    public Task<HubResponse> StartGame(string roomCode) =>
        RunAsync(roomCode, (id, ct) => _rooms.StartGameAsync(roomCode, id, ct));

    // ── Gameplay ────────────────────────────────────────────────

    public async Task<HubResponse> MakeMove(string roomCode, string actionJson, int expectedStateVersion)
    {
        var response = await RunAsync(
            roomCode, (id, ct) => _rooms.MakeMoveAsync(roomCode, id, actionJson, expectedStateVersion, ct))
            .ConfigureAwait(false);
        if (!response.Ok)
        {
            await ResyncCallerAsync(roomCode).ConfigureAwait(false);
        }

        return response;
    }

    public Task<HubResponse> Resign(string roomCode) =>
        RunAsync(roomCode, (id, ct) => _rooms.ResignAsync(roomCode, id, ct));

    public async Task<HubResponse> OfferDraw(string roomCode)
    {
        var playerId = CurrentPlayer();
        if (playerId is null)
        {
            return HubResponse.Fail(ErrorCodes.NotInRoom);
        }

        var result = await _rooms.OfferDrawAsync(roomCode, playerId, Ct).ConfigureAwait(false);
        if (!result.Success)
        {
            return await RejectAsync(roomCode, result.ErrorCode!).ConfigureAwait(false);
        }

        var update = result.Value!;
        if (update.NewResult is null)
        {
            await Clients.OthersInGroup(RoomBroadcaster.GroupName(update.Snapshot.Code))
                .DrawOffered(update.Snapshot.Code, PlayerIdentity.PublicId(playerId)).ConfigureAwait(false);
        }

        await _broadcaster.UpdateAsync(update).ConfigureAwait(false);
        return HubResponse.Success();
    }

    public async Task<HubResponse> RespondDraw(string roomCode, bool accept)
    {
        var playerId = CurrentPlayer();
        if (playerId is null)
        {
            return HubResponse.Fail(ErrorCodes.NotInRoom);
        }

        var result = await _rooms.RespondDrawAsync(roomCode, playerId, accept, Ct).ConfigureAwait(false);
        if (!result.Success)
        {
            return await RejectAsync(roomCode, result.ErrorCode!).ConfigureAwait(false);
        }

        var update = result.Value!;
        if (!accept)
        {
            await Clients.Group(RoomBroadcaster.GroupName(update.Snapshot.Code))
                .DrawDeclined(update.Snapshot.Code, PlayerIdentity.PublicId(playerId)).ConfigureAwait(false);
        }

        await _broadcaster.UpdateAsync(update).ConfigureAwait(false);
        return HubResponse.Success();
    }

    public async Task<HubResponse> RequestRematch(string roomCode)
    {
        var playerId = CurrentPlayer();
        if (playerId is null)
        {
            return HubResponse.Fail(ErrorCodes.NotInRoom);
        }

        var result = await _rooms.RequestRematchAsync(roomCode, playerId, Ct).ConfigureAwait(false);
        if (!result.Success)
        {
            return await RejectAsync(roomCode, result.ErrorCode!).ConfigureAwait(false);
        }

        var update = result.Value!;
        if (!update.GameStarted)
        {
            await Clients.OthersInGroup(RoomBroadcaster.GroupName(update.Snapshot.Code))
                .RematchRequested(update.Snapshot.Code, PlayerIdentity.PublicId(playerId)).ConfigureAwait(false);
        }

        await _broadcaster.UpdateAsync(update).ConfigureAwait(false);
        return HubResponse.Success();
    }

    // ── Chat ────────────────────────────────────────────────────

    /// <summary>Messages are plain text, 200 characters at most. Clients must render them as text, never HTML.</summary>
    public async Task<HubResponse> SendChat(string roomCode, string message)
    {
        var playerId = CurrentPlayer();
        if (playerId is null)
        {
            return HubResponse.Fail(ErrorCodes.NotInRoom);
        }

        var result = await _rooms.SendChatAsync(roomCode, playerId, message, Ct).ConfigureAwait(false);
        if (!result.Success)
        {
            return await RejectAsync(roomCode, result.ErrorCode!).ConfigureAwait(false);
        }

        var chat = result.Value!;
        var code = FabGames.Core.Services.RoomCodeGenerator.Normalize(roomCode);
        await Clients.Group(RoomBroadcaster.GroupName(code)).ChatMessageReceived(
            code,
            new ChatMessageDto(PlayerIdentity.PublicId(chat.PlayerId), chat.DisplayName, chat.Text, chat.SentAt.ToUnixTimeMilliseconds()))
            .ConfigureAwait(false);
        return HubResponse.Success();
    }

    // ── Connection lifetime ─────────────────────────────────────

    public override async Task OnDisconnectedAsync(Exception? exception)
    {
        var result = await _rooms.HandleDisconnectAsync(Context.ConnectionId).ConfigureAwait(false);
        if (result.Success)
        {
            var info = result.Value!;
            await _broadcaster.PlayerReconnectingAsync(info.Snapshot.Code, info.PlayerId, info.GraceSeconds).ConfigureAwait(false);
            await _broadcaster.StateUpdatedAsync(info.Snapshot).ConfigureAwait(false);
        }

        await base.OnDisconnectedAsync(exception).ConfigureAwait(false);
    }

    // ── Helpers ─────────────────────────────────────────────────

    private string? CurrentPlayer() => _rooms.GetPlayerIdOfConnection(Context.ConnectionId);

    private static bool IsValidPlayerId(string? playerId) =>
        playerId is { Length: >= MinPlayerIdLength and <= 64 };

    /// <summary>Runs a room operation as the connection's player and broadcasts the outcome.</summary>
    private async Task<HubResponse> RunAsync(
        string roomCode, Func<string, CancellationToken, Task<OperationResult<RoomUpdate>>> operation)
    {
        var playerId = CurrentPlayer();
        if (playerId is null)
        {
            return HubResponse.Fail(ErrorCodes.NotInRoom);
        }

        var result = await operation(playerId, Ct).ConfigureAwait(false);
        if (!result.Success)
        {
            return await RejectAsync(roomCode, result.ErrorCode!).ConfigureAwait(false);
        }

        await _broadcaster.UpdateAsync(result.Value!).ConfigureAwait(false);
        return HubResponse.Success();
    }

    private async Task<HubResponse> RejectAsync(string roomCode, string errorCode)
    {
        await Clients.Caller.ActionRejected(roomCode, errorCode).ConfigureAwait(false);
        return HubResponse.Fail(errorCode);
    }

    /// <summary>After a rejected move, send the caller the authoritative state so the board snaps back.</summary>
    private async Task ResyncCallerAsync(string roomCode)
    {
        if (CurrentPlayer() is not { } playerId)
        {
            return;
        }

        var room = await _rooms.GetRoomAsync(roomCode, Ct).ConfigureAwait(false);
        if (room.Success)
        {
            await Clients.Caller.StateUpdated(_views.Map(room.Value!, playerId)).ConfigureAwait(false);
        }
    }

    /// <summary>
    /// A player may sit in one room at a time. Leaving a lobby or finished room to start another is
    /// automatic; abandoning a running game is not.
    /// </summary>
    private async Task<string?> LeaveStaleRoomAsync(string playerId, string? targetRoomCode)
    {
        var current = _rooms.GetRoomCodeOfPlayer(playerId);
        if (current is null || current == targetRoomCode)
        {
            return null;
        }

        var room = await _rooms.GetRoomAsync(current, Ct).ConfigureAwait(false);
        if (!room.Success)
        {
            return null;
        }

        if (room.Value!.Status == RoomStatus.InProgress)
        {
            return ErrorCodes.AlreadyInGame;
        }

        var left = await _rooms.LeaveRoomAsync(current, playerId, Ct).ConfigureAwait(false);
        if (left.Success)
        {
            await _broadcaster.RemoveFromGroupAsync(Context.ConnectionId, current).ConfigureAwait(false);
            await _broadcaster.PlayerLeftAsync(left.Value!).ConfigureAwait(false);
        }

        return null;
    }
}
