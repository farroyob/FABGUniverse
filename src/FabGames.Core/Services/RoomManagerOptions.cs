namespace FabGames.Core.Services;

public sealed class RoomManagerOptions
{
    public const string SectionName = "Rooms";

    /// <summary>Maximum number of rooms alive at once.</summary>
    public int MaxRooms { get; set; } = 500;

    /// <summary>How long a disconnected player may come back before forfeiting.</summary>
    public int DisconnectGraceSeconds { get; set; } = 60;

    /// <summary>Rooms without any activity for this long are removed.</summary>
    public int IdleRoomMinutes { get; set; } = 30;

    /// <summary>Finished rooms without activity for this long are removed.</summary>
    public int FinishedRoomMinutes { get; set; } = 10;

    public int MaxChatLength { get; set; } = 200;
}
