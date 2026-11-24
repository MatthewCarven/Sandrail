using System;
using System.Linq;
using Godot;
using Sandrail.Core.Tracks;
using Sandrail.Game.Editor;
using Sandrail.Game.Ui;
using Sandrail.Game.World;

namespace Sandrail.Game;

/// <summary>
/// The editor's way in (M5-PLAN, decision 1): the Esc menu's New track and Edit this track start the scene
/// again with <c>--edit=</c>, and the editor takes the place of the car and the race.
/// </summary>
public partial class Main
{
    private EditorView? _editorView;

    /// <summary>The flags the scene started with, for Tab back to the editor.</summary>
    private string[] _args = [];

    /// <summary>Whether this is a test drive from the editor (M5c): Tab goes back to it.</summary>
    private bool _testDrive;

    /// <summary>What Tab back to the editor opens: the editor's own track, or the file driven.</summary>
    private string? _editBack;

    /// <summary>The piece an untimed test drive began on; null in a race, timed from the start gate.</summary>
    private PlacedPiece? _testDriveFrom;

    /// <summary>The piece a test drive begins on: the one covering <c>--drive-from=</c>'s tile, else the route's first, else the first placed.</summary>
    private static PlacedPiece? DriveFromPiece(LaidTrack track, string? flag)
    {
        string[] parts = (flag ?? string.Empty).Split(',');
        if (parts.Length == 2 && int.TryParse(parts[0], out int column) && int.TryParse(parts[1], out int row)
            && track.Placed.FirstOrDefault(p => p.Tiles.Contains(new Tile(column, row))) is PlacedPiece on)
        {
            return on;
        }

        return track.Route.Count > 0 ? track.Route[0] : track.Placed.FirstOrDefault();
    }

    /// <summary>Once a frame on a test drive: Tab goes back to the editor, as it was. True if it did, the scene leaving the tree.</summary>
    private bool WatchTabBack()
    {
        if (_testDrive && _editBack is not null && Input.IsActionJustPressed(Controls.TestDrive))
        {
            StartAgainOn(_args, $"--edit={_editBack}");
            return true;
        }

        return false;
    }

    /// <summary>The board on an untimed test drive.</summary>
    private string TestDriveBoard() =>
        $"TEST DRIVE   {_track!.Track.Name}, from the {Short(_testDriveFrom!.Def)} at {_testDriveFrom.Placement.At}\n"
        + (_track.Problems.Count > 0 ? $"UNTIMED      the track has {_track.Problems.Count} thing{(_track.Problems.Count == 1 ? string.Empty : "s")} to do" : "UNTIMED      the race is from the start gate")
        + "\nKEYS         R: back to here  ·  Tab: back to the editor";

    /// <summary>The piece catalogue for the game's Load list, read the first time it's opened.</summary>
    private PieceCatalogue? _menuCatalogue;

    /// <summary>
    /// The game menu's Edit this track (M5a, M5d): a generated track is handed to the editor as it is, through
    /// its session (it has no file); a test drive goes back to its editor; any other track opens by its ID or path.
    /// </summary>
    private Action? EditThisTrack(string[] args, string? trackName)
    {
        if (_generated is Sandrail.Core.Generation.Generated made)
        {
            return () =>
            {
                EditorSession.Editor = new TrackEditor(made.Track, _menuCatalogue ??= TrackView.LoadCatalogue());
                EditorSession.State = null;
                StartAgainOn(args, $"--edit={EditorSession.Id}");
            };
        }

        return _track is not null && trackName is not null ? () => StartAgainOn(args, $"--edit={trackName}") : null;
    }

    /// <summary>
    /// The Esc menu's Load list (M4, M5d): every track there is, a finished one to drive, an unfinished one (a
    /// saved track still being built) to open in the editor, since the game can't race it.
    /// </summary>
    private System.Collections.Generic.List<(string Label, Action Load)> TracksToLoad(string[] args, PieceCatalogue catalogue) =>
        TrackView.AllTracks().Select(t =>
        {
            bool unfinished = t.Track.Lay(catalogue).Problems.Count > 0;
            string label = $"{TrackLabel(t.Track)}{(t.Saved ? ", saved" : string.Empty)}{(unfinished ? ", unfinished: opens in the editor" : string.Empty)}";
            return (label, (Action)(() => StartAgainOn(args, unfinished ? $"--edit={t.Path}" : $"--track={t.Path}")));
        }).ToList();

    /// <summary>The grid tile under a point.</summary>
    private static Tile TileUnder(Vector3 at) =>
        new((int)MathF.Floor((at.X - TrackGrid.Origin.X) / TrackGrid.TileM), (int)MathF.Floor((at.Z - TrackGrid.Origin.Z) / TrackGrid.TileM));

    private static string Short(PieceDef def) => def.Id.StartsWith("base:", StringComparison.Ordinal) ? def.Id[5..] : def.Id;

    /// <summary>
    /// The editor on a track: an empty one, one read from its file with its problems kept (fact 2), or, back from
    /// a test drive (<c>--edit=@editor</c>), the one it was working on, its history and view and all (M5c).
    /// </summary>
    private void OpenEditor(string[] args, string edit)
    {
        PieceCatalogue catalogue;
        TrackEditor editor;
        try
        {
            catalogue = TrackView.LoadCatalogue();
            if (edit == EditorSession.Id && EditorSession.Editor is TrackEditor kept)
            {
                editor = kept;
            }
            else
            {
                // new, new:<seed> for an empty grid on a seed's ground (M5d), or a track by its ID or path.
                Track fresh = edit == "new" ? TrackEditor.Empty()
                    : edit.StartsWith("new:", StringComparison.Ordinal) && ulong.TryParse(edit[4..], out ulong seed)
                        ? TrackEditor.Empty() with { Ground = new TrackGround { Seed = seed } }
                    : TrackView.ReadTrack(edit);
                editor = new TrackEditor(fresh, catalogue);
                EditorSession.Editor = editor;
                EditorSession.State = null;
            }
        }
        catch (System.IO.InvalidDataException e)
        {
            Refuse($"--edit={edit}: {e.Message}");
            return;
        }

        // On a seed's ground the editor shows the terrain, so the plain goes, as it does for a generated track.
        Track track = editor.Track;
        if (track.Ground is not null)
        {
            var plain = GetNode<StaticBody3D>("Ground");
            plain.GetNode<MeshInstance3D>("Mesh").Visible = false;
            plain.GetNode<CollisionShape3D>("Shape").Disabled = true;
        }

        _editorView = new EditorView
        {
            Editor = editor,
            Catalogue = catalogue,
            TestDrive = tile => StartAgainOn(args, $"--track={EditorSession.Id}", $"--drive-from={tile.Column},{tile.Row}"),
        };
        AddChild(_editorView);
        AddChild(new SeedMenu
        {
            Where = $"Editing {track.Name}",
            Generate = c => GoToCode(args, c),
            Tracks = () => TracksToLoad(args, catalogue),
            NewTrack = () => StartAgainOn(args, "--edit=new"),
            NewOnGround = track.Ground is TrackGround g ? () => StartAgainOn(args, $"--edit=new:{g.Seed}") : null,
            EscTaken = () => _editorView?.Typing == true,
        });
        GD.Print($"Editing {track.Id} ({track.Name}): {track.Pieces.Length} pieces");
    }
}
