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

    public const string TracksPath = "res://content/base/tracks";

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

    /// <summary>
    /// A track by its ID from the game's own (<c>base:shakedown</c>), or from a file by its path (one
    /// ending <c>.json</c>, <c>user://</c> or not), laid on the catalogue and refused if it's unsound.
    /// </summary>
    /// <exception cref="System.IO.InvalidDataException">No such track, or it's refused; the message says why.</exception>
    public static LaidTrack LoadTrack(string idOrPath, PieceCatalogue catalogue)
    {
        if (idOrPath.EndsWith(".json", System.StringComparison.OrdinalIgnoreCase))
        {
            if (!FileAccess.FileExists(idOrPath))
            {
                throw new System.IO.InvalidDataException($"There's no track file at {idOrPath}.");
            }

            return Track.Load(FileAccess.GetFileAsString(idOrPath), catalogue);
        }

        var known = new List<string>();
        foreach (string file in DirAccess.GetFilesAt(TracksPath).Where(f => f.EndsWith(".json", System.StringComparison.Ordinal)).Order())
        {
            string json = FileAccess.GetFileAsString($"{TracksPath}/{file}");
            Track track = Track.Parse(json);
            if (track.Id == idOrPath)
            {
                return Track.Load(json, catalogue);
            }

            known.Add(track.Id);
        }

        throw new System.IO.InvalidDataException($"There's no track called {idOrPath}. Known: {string.Join(", ", known)}.");
    }

    /// <summary>Each placed piece as a body of its own, named for the piece and its tile, and an arch over each gate.</summary>
    public static void Build(Node3D parent, IEnumerable<PlacedPiece> pieces)
    {
        foreach (PlacedPiece piece in pieces)
        {
            Surface surface = Surfaces.Find(piece.Def.Surface) ?? Surfaces.Road;
            StaticBody3D body = TestGround.Road(
                $"{piece.Def.Id} at {piece.Placement.At}", piece.Road().Pieces, Profile.Slab(piece.Def.WidthM, piece.DepthM), surface);
            parent.AddChild(body);

            if (piece.Def.Gate is GateKind gate)
            {
                parent.AddChild(Arch(piece, gate));
            }
        }
    }

    /// <summary>
    /// A grey-box arch across a gate piece's middle: two posts clear of the road and a beam 5 m up, green
    /// for the start, blue for a checkpoint, red for the finish. No collider: it marks the line, it doesn't
    /// stop a car that goes wide.
    /// </summary>
    private static Node3D Arch(PlacedPiece piece, GateKind gate)
    {
        ICentreline line = piece.Road().Pieces[0];
        Frame middle = line.FrameAt(line.Length / 2.0f);
        Color colour = gate switch
        {
            GateKind.Start => new Color(0.2f, 0.75f, 0.3f),
            GateKind.Checkpoint => new Color(0.25f, 0.5f, 0.95f),
            _ => new Color(0.9f, 0.2f, 0.2f),
        };

        var material = new StandardMaterial3D { AlbedoColor = colour, Roughness = 0.6f };
        float half = (piece.Def.WidthM / 2.0f) + 0.6f;
        const float Height = 5.0f;
        var arch = new Node3D { Name = $"{gate} arch at {piece.Placement.At}" };
        arch.Transform = new Transform3D(
            new Basis(middle.Right.ToGodot(), middle.Up.ToGodot(), -middle.Forward.ToGodot()), middle.Position.ToGodot());
        foreach (float side in new[] { -half, half })
        {
            arch.AddChild(new MeshInstance3D
            {
                Mesh = new BoxMesh { Size = new Vector3(0.4f, Height, 0.4f) },
                MaterialOverride = material,
                Position = new Vector3(side, Height / 2.0f, 0.0f),
            });
        }

        arch.AddChild(new MeshInstance3D
        {
            Mesh = new BoxMesh { Size = new Vector3((2.0f * half) + 0.4f, 0.8f, 0.4f) },
            MaterialOverride = material,
            Position = new Vector3(0.0f, Height, 0.0f),
        });
        return arch;
    }
}
