namespace FabGames.Games.Checkers.Logic;

public static class DrawDetector
{
    /// <summary>Moves per side without a capture or a man move before the game is drawn.</summary>
    public const int DefaultMoveLimitPerSide = 40;

    public static bool IsDrawn(int pliesSinceProgress, int moveLimitPerSide) =>
        pliesSinceProgress >= moveLimitPerSide * 2;
}
