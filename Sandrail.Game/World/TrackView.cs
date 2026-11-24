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

    /// <summary>Where generated tracks are saved from the Esc menu (M3e).</summary>
    public const string SavedPath = "user://tracks";

    /// <summary>
    /// Every track there is to load, for the Esc menu's list: the game's own, then the ones saved from the menu,
    /// each by its path, newest saved first. A file this build can't read is left off.
    /// </summary>
    public static IReadOnlyList<(string Path, Track Track, bool Saved)> AllTracks()
    {
        var all = new List<(string, Track, bool)>();
        foreach (string folder in new[] { TracksPath, SavedPath })
        {
            if (!DirAccess.DirExistsAbsolute(folder))
            {
                continue;
            }

            bool saved = folder == SavedPath;
            IEnumerable<string> files = DirAccess.GetFilesAt(folder).Where(f => f.EndsWith(".json", System.StringComparison.Ordinal));
            files = saved
                ? files.OrderByDescending(f => FileAccess.GetModifiedTime($"{folder}/{f}"))
                : files.Order(System.StringComparer.Ordinal);
            foreach (string file in files)
            {
                string path = $"{folder}/{file}";
                try
                {
                    all.Add((path, Track.Parse(FileAccess.GetFileAsString(path)), saved));
                }
                catch (System.IO.InvalidDataException)
                {
                    // Not a track this build reads.
                }
            }
        }

        return all;
    }

    /// <summary>
    /// The file of the track a code names (M4a), if the game has one: among its own tracks first, then the ones
    /// saved from the menu, a track saved before codes as <c>gen:5</c> counting as version 1's. Null if neither
    /// has it. This is how an old code still drives its track after the generator has moved on.
    /// </summary>
    public static string? FindCodeFile(Sandrail.Core.Generation.TrackCode code)
    {
        foreach (string folder in new[] { TracksPath, SavedPath })
        {
            if (!DirAccess.DirExistsAbsolute(folder))
            {
                continue;
            }

            foreach (string file in DirAccess.GetFilesAt(folder).Where(f => f.EndsWith(".json", System.StringComparison.Ordinal)).Order())
            {
                string path = $"{folder}/{file}";
                try
                {
                    if (Sandrail.Core.Generation.TrackCode.FromId(Track.Parse(FileAccess.GetFileAsString(path)).Id) == code)
                    {
                        return path;
                    }
                }
                catch (System.IO.InvalidDataException)
                {
                    // Not a track this build reads: it can't be the one.
                }
            }
        }

        return null;
    }

    /// <summary>
    /// What to say when a code is from another version of the generator and the game hasn't its file (M4a): the
    /// code isn't quietly made into this version's track.
    /// </summary>
    public static string NoFileFor(Sandrail.Core.Generation.TrackCode code) =>
        $"{code} was made by {(code.Version > Sandrail.Core.Generation.TrackGenerator.Version ? "a newer Sandrail's generator, " : "generator ")}version {code.Version}; "
        + $"this build makes version {Sandrail.Core.Generation.TrackGenerator.Version}, where seed {code.Seed} is a different track "
        + $"({Sandrail.Core.Generation.TrackCode.Of(code.Seed)}). Drive that, or ask for {code}'s file.";

    /// <summary>
    /// A track by its ID or path, read but not laid, so a track with problems comes back with them: the
    /// editor's way in (M5-PLAN fact 2), where <see cref="LoadTrack"/> refuses what can't be raced.
    /// </summary>
    /// <exception cref="System.IO.InvalidDataException">No such track, or it isn't a track file at all.</exception>
    public static Track ReadTrack(string idOrPath)
    {
        if (idOrPath.EndsWith(".json", System.StringComparison.OrdinalIgnoreCase))
        {
            return FileAccess.FileExists(idOrPath)
                ? Track.Parse(FileAccess.GetFileAsString(idOrPath))
                : throw new System.IO.InvalidDataException($"There's no track file at {idOrPath}.");
        }

        foreach ((string path, Track track, bool _) in AllTracks())
        {
            if (track.Id == idOrPath)
            {
                return track;
            }
        }

        throw new System.IO.InvalidDataException($"There's no track called {idOrPath}.");
    }

    /// <summary>One placed piece and its arch, if it has a gate, together under one node: the editor adds and takes them away one at a time (M5a).</summary>
    public static Node3D BuildOne(PlacedPiece piece, Sandrail.Core.Generation.Terrain? ground = null)
    {
        var node = new Node3D { Name = $"{piece.Def.Id} at {piece.Placement.At}" };
        Build(node, [piece], ground);
        return node;
    }

    /// <summary>
    /// Each placed piece as a body of its own, named for the piece and its tile, and an arch over each gate;
    /// on generated ground, each slab deep enough to meet it.
    /// </summary>
    public static void Build(Node3D parent, IEnumerable<PlacedPiece> pieces, Sandrail.Core.Generation.Terrain? ground = null)
    {
        foreach (PlacedPiece piece in pieces)
        {
            Surface surface = Surfaces.Find(piece.Def.Surface) ?? Surfaces.Road;
            float depth = ground is null ? piece.DepthM : piece.DepthTo(ground.HeightAt);
            StaticBody3D body = TestGround.Road(
                $"{piece.Def.Id} at {piece.Placement.At}", piece.Road().Pieces, Profile.Slab(piece.Def.WidthM, depth), surface);
            parent.AddChild(body);

            if (piece.Def.Gate is GateKind gate)
            {
                parent.AddChild(Arch(piece, gate));
            }
        }
    }

    /// <summary>
    /// A grey-box arch across a gate piece's middle: two posts clear of the road and a beam 5 m up, green
    /// for the start, blue for a checkpoint, red for the finish, white for a circuit's line. No collider: it marks the line, it doesn't
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
            GateKind.StartFinish => new Color(0.95f, 0.95f, 0.9f),
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
