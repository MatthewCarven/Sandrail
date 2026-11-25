namespace Sandrail.Core.Tracks;

/// <summary>
/// A track being edited, and its history (M5-PLAN, M5a): pieces placed and erased, each change refused in
/// words if it can't be made, undone and redone; the track laid after every change, so its problems, the
/// editor's to-do list, are always current (fact 1: <see cref="Track.Lay"/> takes about a millisecond).
/// </summary>
/// <remarks>
/// <para>
/// <b>A track being built is unsound for most of its life</b> (fact 2), so nothing here refuses a track for
/// its problems: only a change that can't be made at all (a piece off the grid, or on another's tiles) is
/// refused. Whether the track can be raced is <see cref="LaidTrack.Problems"/> being empty.
/// </para>
/// <para>
/// <b><see cref="Next"/> lays track like model railway</b> (fact 5): for a piece and a way out nothing meets,
/// the placement that continues the road from it, its tile, facing and level worked out.
/// </para>
/// </remarks>
public sealed class TrackEditor
{
    private readonly PieceCatalogue _catalogue;
    private readonly Stack<Track> _undo = new();
    private readonly Stack<Track> _redo = new();
    private Dictionary<Tile, List<PlacedPiece>> _owner = new();

    public TrackEditor(Track track, PieceCatalogue catalogue)
    {
        _catalogue = catalogue;
        Track = Tidied(track);
        Laid = Relay();
    }

    /// <summary>A new, empty track on the plain (decision 5: on a seed's ground comes with M5d).</summary>
    public static Track Empty() => new() { Id = "user:untitled", Name = "Untitled", Pieces = [] };

    /// <summary>
    /// A track file opened for editing, its problems kept rather than refused (fact 2): the editor's way in,
    /// where <see cref="Track.Load"/> refuses a track that can't be raced.
    /// </summary>
    /// <exception cref="InvalidDataException">Not a track file this build reads at all.</exception>
    public static TrackEditor Open(string json, PieceCatalogue catalogue) => new(Track.Parse(json), catalogue);

    public Track Track { get; private set; }

    /// <summary>The track laid: its pieces, every way it's unsound, and its route once it's whole.</summary>
    public LaidTrack Laid { get; private set; }

    public bool CanUndo => _undo.Count > 0;

    public bool CanRedo => _redo.Count > 0;

    /// <summary>The piece covering a tile, the top one where pieces cross (what's seen from above), or null.</summary>
    public PlacedPiece? PieceAt(Tile tile) =>
        _owner.TryGetValue(tile, out List<PlacedPiece>? there) ? there.MaxBy(p => Stacking.Span(p, tile).LowM) : null;

    /// <summary>The piece covering a tile whose road there is nearest a level, or null: on a tile of one piece, that piece.</summary>
    public PlacedPiece? PieceAt(Tile tile, int level)
    {
        if (!_owner.TryGetValue(tile, out List<PlacedPiece>? there))
        {
            return null;
        }

        float height = TrackGrid.Origin.Y + (level * TrackGrid.LevelM);
        return there.MinBy(p =>
        {
            TileSpan span = Stacking.Span(p, tile);
            return height < span.LowM ? span.LowM - height : height > span.HighM ? height - span.HighM : 0.0f;
        });
    }

    /// <summary>Every piece covering a tile, lowest first: more than one only where roads cross.</summary>
    public IReadOnlyList<PlacedPiece> PiecesAt(Tile tile) =>
        _owner.TryGetValue(tile, out List<PlacedPiece>? there) ? there.OrderBy(p => Stacking.Span(p, tile).LowM).ToList() : [];

    /// <summary>Why a placement can't be made, in words, or null if it can.</summary>
    public string? Fits(Placement p)
    {
        if (_catalogue.Find(p.Piece) is not PieceDef def)
        {
            return $"there's no piece called \"{p.Piece}\"";
        }

        int lowest = Track.Ground is null ? 0 : -Track.MaxLevel;
        if (p.Level < lowest || p.Level > Track.MaxLevel)
        {
            return Track.Ground is null
                ? $"level {p.Level} is below the plain or too high: it's 0 to {Track.MaxLevel}"
                : $"level {p.Level} is out of reach: it's {lowest} to {Track.MaxLevel}";
        }

        var placed = new PlacedPiece(def, p);
        foreach (Tile tile in placed.Tiles)
        {
            if (!TrackGrid.Contains(tile, Track.Size))
            {
                return $"{def.Name.ToLowerInvariant()} would run off the grid at {tile}";
            }

            if (!_owner.TryGetValue(tile, out List<PlacedPiece>? there))
            {
                continue;
            }

            foreach (PlacedPiece other in there)
            {
                Stack stack = Stacking.Of(other, placed, tile);
                if (stack.Clear)
                {
                    continue;
                }

                if (stack.Overlap)
                {
                    return $"{other.Def.Name.ToLowerInvariant()} at {other.Placement.At} is in the way at {tile}";
                }

                // One above the other, but not far enough: by how much, and which way would do it.
                int levels = (int)MathF.Ceiling(((Stacking.ClearanceM - stack.GapM) / TrackGrid.LevelM) - 1e-3f);
                bool over = ReferenceEquals(stack.Upper, placed);
                string more = $"{levels} level{(levels == 1 ? string.Empty : "s")} {(over ? "higher" : "lower")}";
                return $"{def.Name.ToLowerInvariant()} would cross {(over ? "over" : "under")} {other.Def.Name.ToLowerInvariant()} at {other.Placement.At} "
                    + $"with {stack.GapM:0.0} m between them at {tile}, and a crossing needs {Stacking.ClearanceM:0} m: {more} would do it";
            }
        }

        return null;
    }

    /// <summary>The laps a circuit starts with when its start-finish line is laid (M3-PLAN decision 8).</summary>
    public const int DefaultLaps = 3;

    /// <summary>
    /// Puts a piece on the track: null if it's placed, or why it can't be. Laying a start-finish line makes a
    /// sprint a circuit of <see cref="DefaultLaps"/> laps (M5-PLAN decision 7), in the same change.
    /// </summary>
    public string? Place(Placement p)
    {
        if (Fits(p) is string why)
        {
            return why;
        }

        bool line = _catalogue.Find(p.Piece)?.Gate == GateKind.StartFinish;
        Change(Track with { Pieces = [.. Track.Pieces, TrackPiece.Of(p)], Laps = line && Track.Laps == 0 ? DefaultLaps : Track.Laps });
        return null;
    }

    /// <summary>
    /// Takes away the piece covering a tile (where roads cross, the one nearest a level, or the top one if no
    /// level is given): null if one was, or why not. Taking away a circuit's last start-finish line makes it a
    /// sprint again, in the same change.
    /// </summary>
    public string? Erase(Tile tile, int? level = null)
    {
        if ((level is int l ? PieceAt(tile, l) : PieceAt(tile)) is not PlacedPiece piece)
        {
            return $"there's nothing at {tile} to take away";
        }

        TrackPiece[] pieces = Track.Pieces;
        int index = Array.FindIndex(pieces, t => t.ToPlacement() == piece.Placement);
        TrackPiece[] left = [.. pieces[..index], .. pieces[(index + 1)..]];
        bool lineLeft = left.Any(t => _catalogue.Find(t.Piece)?.Gate == GateKind.StartFinish);
        Change(Track with { Pieces = left, Laps = lineLeft ? Track.Laps : 0 });
        return null;
    }

    /// <summary>A circuit's laps (decision 7): null if set, or why not. A change of its own, undone like any other.</summary>
    public string? SetLaps(int laps)
    {
        if (Track.Laps == 0)
        {
            return "laps are for a circuit: lay a start-finish line first";
        }

        if (laps < 1 || laps > Track.MaxLaps)
        {
            return $"a circuit is 1 to {Track.MaxLaps} laps";
        }

        if (laps != Track.Laps)
        {
            Change(Track with { Laps = laps });
        }

        return null;
    }

    /// <summary>
    /// The track's ID and name, as it's saved (decision 6): not a change to undo, since it's what the track is
    /// called, not what it is. A track's fingerprint doesn't read either.
    /// </summary>
    public void Rename(string id, string name)
    {
        Track = Track with { Id = id, Name = name };
        Laid = Relay();
    }

    /// <summary>Back one change; false if there's none.</summary>
    public bool Undo()
    {
        if (_undo.Count == 0)
        {
            return false;
        }

        _redo.Push(Track);
        Track = _undo.Pop();
        Laid = Relay();
        return true;
    }

    /// <summary>Forward one undone change; false if there's none.</summary>
    public bool Redo()
    {
        if (_redo.Count == 0)
        {
            return false;
        }

        _undo.Push(Track);
        Track = _redo.Pop();
        Laid = Relay();
        return true;
    }

    /// <summary>
    /// Every way out that nothing meets: a road's end with room on the tile beyond it (nothing there, or only
    /// road two levels above or below it), or a jump's lip with nothing where it would land. In the order the
    /// pieces were placed, so the last is the newest end.
    /// </summary>
    public IReadOnlyList<WorldConnector> OpenEnds()
    {
        var ends = new List<WorldConnector>();
        foreach (PlacedPiece p in Laid.Placed)
        {
            WorldConnector o = p.Out;
            Tile target = o.Kind switch
            {
                ConnectorKind.Road => o.Beyond,
                ConnectorKind.Lip => o.Landing,
                _ => default,
            };
            if (o.Kind is ConnectorKind.Road or ConnectorKind.Lip && Room(target, o))
            {
                ends.Add(o);
            }
        }

        return ends;
    }

    /// <summary>
    /// Whether a way out could be followed: the tile beyond it is empty, or everything on it is road that runs
    /// clear over or under the way out's level and none of it is entered from there at that level.
    /// </summary>
    private bool Room(Tile target, WorldConnector end)
    {
        if (!_owner.TryGetValue(target, out List<PlacedPiece>? there))
        {
            return true;
        }

        if (end.Kind == ConnectorKind.Lip)
        {
            return false;
        }

        float height = TrackGrid.Origin.Y + (end.Level * TrackGrid.LevelM);
        foreach (PlacedPiece p in there)
        {
            WorldConnector into = p.In;
            if (into.Tile == target && TrackGrid.Opposite(into.Facing) == end.Facing && into.Level == end.Level)
            {
                return false;
            }

            TileSpan span = Stacking.Span(p, target);
            bool above = span.LowM - Stacking.DeckM - height >= Stacking.ClearanceM - 1e-3f;
            bool below = height - Stacking.DeckM - span.HighM >= Stacking.ClearanceM - 1e-3f;
            if (!above && !below)
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>
    /// The placement that continues the road from a way out with a piece (fact 5): its first tile across the
    /// edge (across the gap, after a jump's lip), facing on the way the road goes, at the way out's level.
    /// Null, with why, for a piece that can't follow it: a landing only follows a jump, and only a landing
    /// does; nothing follows a piece that ends closed.
    /// </summary>
    public Placement? Next(WorldConnector end, string piece, bool mirrored, out string? why)
    {
        why = null;
        if (_catalogue.Find(piece) is not PieceDef def)
        {
            why = $"there's no piece called \"{piece}\"";
            return null;
        }

        switch (end.Kind)
        {
            case ConnectorKind.Road when def.In != ConnectorKind.Road:
                why = def.In == ConnectorKind.Landing ? "a landing only follows a jump" : $"nothing leads into {def.Name.ToLowerInvariant()}";
                return null;
            case ConnectorKind.Lip when def.In != ConnectorKind.Landing:
                why = "after a jump comes its landing";
                return null;
            case ConnectorKind.Road or ConnectorKind.Lip:
                break;
            default:
                why = "nothing follows that end";
                return null;
        }

        Tile first = end.Kind == ConnectorKind.Lip ? end.Landing : end.Beyond;
        return new Placement(piece, first, end.Level, (int)end.Facing, mirrored);
    }

    private void Change(Track next)
    {
        _undo.Push(Track);
        _redo.Clear();
        Track = Tidied(next);
        Laid = Relay();
    }

    /// <summary>The format the track now needs (a circuit 3, ground 2, else 1), so laying it never complains of an old one.</summary>
    private static Track Tidied(Track track) => track with { Format = track.FormatNeeded, Pieces = track.Pieces ?? [] };

    private LaidTrack Relay()
    {
        LaidTrack laid = Track.Lay(_catalogue);
        _owner = new Dictionary<Tile, List<PlacedPiece>>();
        foreach (PlacedPiece p in laid.Placed)
        {
            foreach (Tile tile in p.Tiles)
            {
                if (_owner.TryGetValue(tile, out List<PlacedPiece>? there))
                {
                    there.Add(p);
                }
                else
                {
                    _owner[tile] = [p];
                }
            }
        }

        return laid;
    }
}
