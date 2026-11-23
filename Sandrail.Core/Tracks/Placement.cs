using System.Numerics;
using Sandrail.Core.Pieces;

namespace Sandrail.Core.Tracks;

/// <summary>
/// A piece put on the grid (M2-PLAN, "The model"): which piece, the tile its first tile goes on, its
/// level, how many quarter turns clockwise it's turned (seen from above), and whether it's mirrored, so a
/// left corner is a right one mirrored and the catalogue needn't hold both.
/// </summary>
public sealed record Placement(string Piece, Tile At, int Level, int Rotation, bool Mirrored);

/// <summary>
/// A connector where it lands on the grid: a tile, the way out of it, a level, and what it meets. A lip
/// also has how high it stands above that level and how steeply it points up, and the empty tiles its jump
/// clears; its level is its landing's.
/// </summary>
public readonly record struct WorldConnector(Tile Tile, Compass Facing, int Level, ConnectorKind Kind, float HeightM = 0.0f, float PitchDeg = 0.0f, int Gap = 0)
{
    /// <summary>The middle of its edge, facing out of the piece: level, or for a lip, raised and pitched up.</summary>
    public Frame Frame => Kind == ConnectorKind.Lip
        ? Frame.Pitched(TrackGrid.EdgeMiddle(Tile, Facing, Level) + new Vector3(0.0f, HeightM, 0.0f), TrackGrid.HeadingDeg(Facing), PitchDeg)
        : TrackGrid.Edge(Tile, Facing, Level, Facing);

    /// <summary>The tile across its edge, where whatever it meets begins.</summary>
    public Tile Beyond => Tile.Step(Facing);

    /// <summary>For a lip, the tile its landing begins on: across the gap.</summary>
    public Tile Landing => Tile.Step(Facing, Gap + 1);
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
    public WorldConnector Out
    {
        get
        {
            PieceConnector o = Def.Out;
            Tile tile = TileAt(o.Tile[0], o.Tile[1]);
            int level = Placement.Level + o.Rise;
            return o.Kind == ConnectorKind.Lip
                ? new WorldConnector(tile, Facing(o.Side), level, o.Kind, o.LipHeightM - (o.Rise * TrackGrid.LevelM), o.LipPitchDeg, o.Gap)
                : new WorldConnector(tile, Facing(o.Side), level, o.Kind);
        }
    }

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
                AddLoop(chain, loop, sign);
            }
        }

        return chain;
    }

    /// <summary>
    /// How deep its slab goes: a raised piece sits on earthworks down past the plain (M2-PLAN fact 8), deep
    /// enough under its highest edge, a fifth more for a surface that's banked or pitched; a loop is a thin
    /// road, or its earthworks would fill it.
    /// </summary>
    public float DepthM
    {
        get
        {
            const float Slab = 0.5f;
            if (Def.Path.Any(s => s.Loop is not null))
            {
                return Slab;
            }

            float highest = 0.0f;
            float half = Def.WidthM / 2.0f;
            foreach (ICentreline line in Road().Pieces)
            {
                int samples = Math.Max(2, (int)MathF.Ceiling(line.Length / 2.0f));
                for (int k = 0; k <= samples; k++)
                {
                    Frame f = line.FrameAt(line.Length * k / samples);
                    highest = MathF.Max(highest, MathF.Max(f.Across(-half, 0.0f).Y, f.Across(half, 0.0f).Y) - TrackGrid.Origin.Y);
                }
            }

            return Slab + (highest * 1.2f);
        }
    }

    /// <summary>
    /// A loop step (see <see cref="LoopStep"/>): an S-bend onto the loop's skew, the loop on a plane turned
    /// back by that skew so its own axis is the piece's, an S-bend off, and the closing straight.
    /// </summary>
    private static void AddLoop(Chain chain, LoopStep loop, float sign)
    {
        (float skewDeg, float bend, float closure) = loop.Plan(sign);
        chain.Turn(skewDeg, bend);
        if (loop.Settle > 0.0f)
        {
            chain.Line(loop.Settle);
        }

        Frame end = chain.End;
        Vector3 right = Vector3.Normalize(Vector3.Cross(end.Forward, end.Up));
        float skew = skewDeg * (MathF.PI / 180.0f);
        Vector3 axis = (end.Forward * MathF.Cos(skew)) - (right * MathF.Sin(skew));
        chain.Add(new LoopCentreline(Frame.Of(end.Position, axis, end.Up), loop.Foot, loop.Top, sign * loop.Offset, loop.Sharpness));

        if (loop.Settle > 0.0f)
        {
            chain.Line(loop.Settle);
        }

        chain.Turn(-skewDeg, bend);
        float rest = loop.Length - (2.0f * bend * MathF.Sin(MathF.Abs(skew))) - (2.0f * loop.Settle * MathF.Cos(MathF.Abs(skew))) - closure;
        if (rest > 1e-4f)
        {
            chain.Line(rest);
        }
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
