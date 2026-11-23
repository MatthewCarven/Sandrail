using System.Numerics;
using Sandrail.Core.Pieces;

namespace Sandrail.Core.Tracks;

/// <summary>The four ways a tile's edge can face. North is -Z (heading 0); they run clockwise seen from above.</summary>
public enum Compass
{
    North,
    East,
    South,
    West,
}

/// <summary>A tile on the grid: its column runs along +X, its row along +Z, so north is a smaller row.</summary>
public readonly record struct Tile(int Column, int Row)
{
    /// <summary>The tile a number of steps away in a direction.</summary>
    public Tile Step(Compass way, int count = 1)
    {
        (int dc, int dr) = TrackGrid.Delta(way);
        return new Tile(Column + (dc * count), Row + (dr * count));
    }

    public override string ToString() => $"({Column}, {Row})";
}

/// <summary>
/// The grid tracks are laid on (M2-PLAN): 32 × 32 tiles of 20 m, height in levels of 2.5 m (decision 6),
/// sitting exactly on the 640 m plain.
/// </summary>
/// <remarks>
/// <para>
/// <b>Pieces meet at the middles of tile edges, level and square</b> (M2-PLAN fact 2), so a connector is a
/// tile, the way it faces and a level, and any two pieces that share an edge at one level meet exactly.
/// </para>
/// <para>
/// Level 0 is lifted <see cref="TestGroundLayout.Lift"/> above the plain, as the test ground's road is, so
/// the two never share a plane and flicker.
/// </para>
/// </remarks>
public static class TrackGrid
{
    public const float TileM = 20.0f;

    public const float LevelM = 2.5f;

    public const int Size = 32;

    /// <summary>The track frame's corner in the world: the grid's middle is the plain's.</summary>
    public static readonly Vector3 Origin = new(-Size * TileM / 2.0f, TestGroundLayout.Lift, -Size * TileM / 2.0f);

    /// <summary>One step in a direction, as (columns, rows).</summary>
    public static (int Columns, int Rows) Delta(Compass way) => way switch
    {
        Compass.North => (0, -1),
        Compass.East => (1, 0),
        Compass.South => (0, 1),
        _ => (-1, 0),
    };

    /// <summary>A direction turned a number of quarter turns clockwise seen from above.</summary>
    public static Compass Turned(Compass way, int quarters) => (Compass)((((int)way + quarters) % 4 + 4) % 4);

    public static Compass Opposite(Compass way) => Turned(way, 2);

    /// <summary>The heading a direction faces, degrees: north 0, east 90, as <see cref="Frame.Level"/> takes them.</summary>
    public static float HeadingDeg(Compass way) => (int)way * 90.0f;

    /// <summary>Whether a tile is on the grid.</summary>
    public static bool Contains(Tile tile, int size = Size) =>
        tile.Column >= 0 && tile.Row >= 0 && tile.Column < size && tile.Row < size;

    /// <summary>The middle of a tile at a level, on the road's surface.</summary>
    public static Vector3 Middle(Tile tile, int level) =>
        Origin + new Vector3((TileM * tile.Column) + (TileM / 2.0f), level * LevelM, (TileM * tile.Row) + (TileM / 2.0f));

    /// <summary>The middle of one of a tile's edges at a level: where pieces meet.</summary>
    public static Vector3 EdgeMiddle(Tile tile, Compass side, int level)
    {
        (int dc, int dr) = Delta(side);
        return Middle(tile, level) + new Vector3(dc * TileM / 2.0f, 0.0f, dr * TileM / 2.0f);
    }

    /// <summary>A level frame at the middle of a tile's edge, facing a way.</summary>
    public static Frame Edge(Tile tile, Compass side, int level, Compass facing) =>
        Frame.Level(EdgeMiddle(tile, side, level), HeadingDeg(facing));
}
