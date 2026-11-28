namespace Sandrail.Core.Pieces;

/// <summary>
/// Centrelines laid end to end, each starting at the frame where the last one finished. How the
/// test ground's runs are written: <c>new Chain(start).Line(30).Pitch(8, 50).Line(35)</c>.
/// </summary>
/// <remarks>
/// M2's pieces will meet at connectors on a grid rather than by chaining, and the test that every
/// end frame is where the next start is will be how they're held to it.
/// </remarks>
public sealed class Chain(Frame start)
{
    private readonly List<ICentreline> _pieces = new();

    public Frame Start { get; } = start;

    /// <summary>Where the last piece ends, and the next one would start.</summary>
    public Frame End { get; private set; } = start;

    public IReadOnlyList<ICentreline> Pieces => _pieces;

    public Chain Line(float length) => Add(new LineCentreline(End, length));

    public Chain Pitch(float angleDeg, float radius) => Add(new PitchCentreline(Settle(End), angleDeg, radius));

    public Chain Turn(float angleDeg, float radius, float bankDeg = 0.0f, float bankPivotM = 0.0f, float bankEase = 0.25f) =>
        Add(new TurnCentreline(Settle(End), angleDeg, radius, bankDeg, bankPivotM, bankEase));

    /// <summary>A turn with gradual ends, standing in for an arc of <paramref name="radius"/> (<see cref="TransitionTurnCentreline"/>).</summary>
    public Chain TransitionTurn(float angleDeg, float radius, float transition, float bankDeg = 0.0f, float bankPivotM = 0.0f) =>
        Add(new TransitionTurnCentreline(Settle(End), angleDeg, radius, transition, bankDeg, bankPivotM));

    /// <summary>A centreline made from where the last one ends, squared up first as a bend's start is.</summary>
    public Chain AddFrom(Func<Frame, ICentreline> make) => Add(make(Settle(End)));

    public Chain Add(ICentreline piece)
    {
        _pieces.Add(piece);
        End = piece.FrameAt(piece.Length);
        return this;
    }

    /// <summary>
    /// Squares a frame up before a bend: a few arcs of float rounding would otherwise skew the next
    /// one's plane, and the error grows piece by piece.
    /// </summary>
    private static Frame Settle(Frame frame) => Frame.Of(frame.Position, frame.Forward, frame.Up);
}
