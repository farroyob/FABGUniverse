using FabGames.Games.Chess.Logic;
using FabGames.Games.Chess.Models;

namespace FabGames.Chess.Tests;

public class PerftTests
{
    private const string Kiwipete = "r3k2r/p1ppqpb1/bn2pnp1/3PN3/1p2P3/2N2Q1p/PPPBBPPP/R3K2R w KQkq - 0 1";

    [Theory]
    [InlineData(1, 20)]
    [InlineData(2, 400)]
    [InlineData(3, 8_902)]
    [InlineData(4, 197_281)]
    public void StartPosition(int depth, long expected) =>
        MoveGenerator.Perft(ChessBoard.CreateStart(), depth).Should().Be(expected);

    [Theory]
    [InlineData(1, 48)]
    [InlineData(2, 2_039)]
    [InlineData(3, 97_862)]
    public void Kiwipete_Position(int depth, long expected) =>
        MoveGenerator.Perft(FenParser.Parse(Kiwipete), depth).Should().Be(expected);

    // Well-known perft positions that stress en passant, promotion and pins.
    [Theory]
    [InlineData("8/2p5/3p4/KP5r/1R3p1k/8/4P1P1/8 w - - 0 1", 1, 14)]
    [InlineData("8/2p5/3p4/KP5r/1R3p1k/8/4P1P1/8 w - - 0 1", 2, 191)]
    [InlineData("8/2p5/3p4/KP5r/1R3p1k/8/4P1P1/8 w - - 0 1", 3, 2_812)]
    [InlineData("8/2p5/3p4/KP5r/1R3p1k/8/4P1P1/8 w - - 0 1", 4, 43_238)]
    [InlineData("r3k2r/Pppp1ppp/1b3nbN/nP6/BBP1P3/q4N2/Pp1P2PP/R2Q1RK1 w kq - 0 1", 1, 6)]
    [InlineData("r3k2r/Pppp1ppp/1b3nbN/nP6/BBP1P3/q4N2/Pp1P2PP/R2Q1RK1 w kq - 0 1", 2, 264)]
    [InlineData("r3k2r/Pppp1ppp/1b3nbN/nP6/BBP1P3/q4N2/Pp1P2PP/R2Q1RK1 w kq - 0 1", 3, 9_467)]
    [InlineData("rnbq1k1r/pp1Pbppp/2p5/8/2B5/8/PPP1NnPP/RNBQK2R w KQ - 1 8", 1, 44)]
    [InlineData("rnbq1k1r/pp1Pbppp/2p5/8/2B5/8/PPP1NnPP/RNBQK2R w KQ - 1 8", 2, 1_486)]
    [InlineData("rnbq1k1r/pp1Pbppp/2p5/8/2B5/8/PPP1NnPP/RNBQK2R w KQ - 1 8", 3, 62_379)]
    public void ReferencePositions(string fen, int depth, long expected) =>
        MoveGenerator.Perft(FenParser.Parse(fen), depth).Should().Be(expected);
}
