using System.Numerics;
using Sandrail.Core.Pieces;

namespace Sandrail.Core.Tracks;

/// <summary>
/// A piece put on the grid (M2-PLAN, "The model"): which piece, the tile its first tile goes on, its
/// level, how many quarter turns clockwise it's turned (seen from above), and whether it's mirrored, so a
/// left corner is a right one mirrored and the catalogue needn't hold both.
/// </summary>
public sealed record Placement(string Piece, Tile At, int Level, int Rotation, bool Mirrored);

/// <summary>A connector where it lands on the grid: a tile, the way out of it, a level, and what it meets.</summary>
public readonly record struct WorldConnector(Tile Tile, Compass Facing, int Level, ConnectorKind Kind)
{
    /// <summary>The middle of its edge, facing out of the piece.</summary>
    public Frame Frame => TrackGrid.Edge(Tile, Facing, Level, Facing);

    /// <summary>The tile across its edge, where whatever it meets begins.</summary>
    public Tile Beyond => Tile.Step(Facing);
}

/// <summary>
/// A piece and where it's placed: its tiles, its connectors and its road, all on the grid.
/// </summary>
/// <remarks>
/// <b>Turned, the piece's forward is north turned by its rotation; mirrored, its right is its left.</b>
/// A tile [across, along] is that many steps to its right and forward from its first tile. A mirrored
/// piece's corners turn the other way and its loop's offset goes to the other side; its climbs, banks and
/// lengths are the same.
/// </remarks>
public sealed class PlacedPiece(PieceDef def, Placement placement)
{
    public PieceDef Def { get; } = def;

    public Placement Placement { get; } = placement;

    /// <summary>The way the piece is driven, on the grid.</summary>
    public Compass Forward => TrackGrid.Turned(Compass.North, Placement.Rotation);

    /// <summary>The piece's right on the grid: its left, mirrored.</summary>
    public Compass RightWay => TrackGrid.Turned(Forward, Placement.Mirrored ? -1 : 1);

    /// <summary>The grid tile of one of the piece's own [across, along] tiles.</summary>
    public Tile TileAt(int across, int along) => Placement.At.Step(RightWay, across).Step(Forward, along);

    /// <summary>The tiles it covers on the grid.</summary>
    public IEnumerable<Tile> Tiles => Def.Tiles.Select(t => TileAt(t[0], t[1]));

    /// <summary>Which way one of the piece's sides faces on the grid.</summary>
    public Compass Facing(PieceSide side) => side switch
    {
        PieceSide.Front => Forward,
        PieceSide.Back => TrackGrid.Opposite(Forward),
        PieceSide.Right => RightWay,
        _ => TrackGrid.Opposite(RightWay),
    };

    /// <summary>Its way in: the back edge of its first tile, at its level.</summary>
    public WorldConnector In => new(Placement.At, Facing(PieceSide.Back), Placement.Level, Def.In);

    /// <summary>Its way out, where its declaration puts it.</summary>
    public WorldConnector Out =>
        new(TileAt(Def.Out.Tile[0], Def.Out.Tile[1]), Facing(Def.Out.Side), Placement.Level + Def.Out.Rise, Def.Out.Kind);

    /// <summary>Where its road starts: the way in, facing into the piece.</summary>
    public Frame Start => TrackGrid.Edge(Placement.At, Facing(PieceSide.Back), Placement.Level, Forward);

    /// <summary>The piece's road as centrelines, from its way in.</summary>
    public Chain Road()
    {
        float sign = Placement.Mirrored ? -1.0f : 1.0f;
        var chain = new Chain(Start);
        foreach (PathStep step in Def.Path)
        {
            if (step.Line is float line)
            {
                chain.Line(line);
            }
            else if (step.Pitch is PitchStep pitch)
            {
                chain.Pitch(pitch.Angle, pitch.Radius);
            }
            else if (step.Turn is TurnStep turn)
            {
                chain.Turn(sign * turn.Angle, turn.Radius, turn.Bank, turn.Bank > 0.0f ? Def.WidthM / 2.0f : 0.0f, turn.Ease);
            }
            else if (step.Loop is LoopStep loop)
            {
                Frame foot = Frame.Of(chain.End.Position, chain.End.Forward, chain.End.Up);
                chain.Add(new LoopCentreline(foot, loop.Foot, loop.Top, sign * loop.Offset, loop.Sharpness));
            }
        }

        return chain;
    }

    /// <summary>
    /// How far the road's end is from the way out it declares, metres, counting a heading or a tilt that
    /// differs as a miss of its own: a millimetre a hundredth of a degree, roughly what M1c's chains hold.
    /// </summary>
    public float PathMisfitM()
    {
        Frame end = Road().End;
        Frame target = Out.Frame;
        float apart = Vector3.Distance(end.Position, target.Position);
        float degrees = (Between(end.Forward, target.Forward) + Between(end.Up, target.Up)) * (180.0f / MathF.PI);
        return apart + (degrees * 0.1f);
    }

    /// <summary>The angle between two directions, radians: by atan2 of cross and dot, which a near-1 dot's acos can't resolve in floats.</summary>
    private static float Between(Vector3 a, Vector3 b) => MathF.Atan2(Vector3.Cross(a, b).Length(), Vector3.Dot(a, b));
}
