using System.Collections.Generic;
using System.Linq;
using Godot;
using Sandrail.Core.Cars;
using Sandrail.Core.Pieces;
using Sandrail.Core.Tracks;

namespace Sandrail.Game.World;

/// <summary>
/// M2's pieces in the world: the catalogue read from the game's own files, and placed pieces swept into
/// road (M2-PLAN, M2a).
/// </summary>
/// <remarks>
/// <b>A raised piece sits on earthworks</b> (M2-PLAN fact 8): its slab runs down past the plain, as the
/// test ground's jump and plateau do, because there's no terrain to stand pillars on until M3.
/// </remarks>
public static class TrackView
{
    public const string PiecesPath = "res://content/base/pieces";

    /// <summary>Every piece file in the game's content, read and checked by Core.</summary>
    /// <exception cref="System.IO.InvalidDataException">A piece is refused; the message says which and why.</exception>
    public static PieceCatalogue LoadCatalogue()
    {
        IEnumerable<(string, string)> files = DirAccess.GetFilesAt(PiecesPath)
            .Where(f => f.EndsWith(".json", System.StringComparison.Ordinal))
            .OrderBy(f => f, System.StringComparer.Ordinal)
            .Select(f => (f, FileAccess.GetFileAsString($"{PiecesPath}/{f}")));
        return PieceCatalogue.Parse(files);
    }

    /// <summary>Each placed piece as a body of its own, named for the piece and its tile.</summary>
    public static void Build(Node3D parent, IEnumerable<PlacedPiece> pieces)
    {
        foreach (PlacedPiece piece in pieces)
        {
            float depth = 0.5f + (Mathf.Max(piece.Placement.Level, piece.Out.Level) * TrackGrid.LevelM);
            Surface surface = Surfaces.Find(piece.Def.Surface) ?? Surfaces.Road;
            parent.AddChild(TestGround.Road(
                $"{piece.Def.Id} at {piece.Placement.At}", piece.Road().Pieces, Profile.Slab(piece.Def.WidthM, depth), surface));
        }
    }
}
