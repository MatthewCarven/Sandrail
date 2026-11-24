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

    /// <summary>The editor on a track: an empty one, or one read from its file with its problems kept (fact 2).</summary>
    private void OpenEditor(string[] args, string edit)
    {
        PieceCatalogue catalogue;
        Track track;
        try
        {
            catalogue = TrackView.LoadCatalogue();
            track = edit == "new" ? TrackEditor.Empty() : TrackView.ReadTrack(edit);
        }
        catch (System.IO.InvalidDataException e)
        {
            Refuse($"--edit={edit}: {e.Message}");
            return;
        }

        if (track.Ground is not null)
        {
            Refuse($"--edit={edit}: {track.Name} is on generated ground, which the editor takes on in M5d; for now it edits tracks on the plain.");
            return;
        }

        _editorView = new EditorView { Editor = new TrackEditor(track, catalogue), Catalogue = catalogue };
        AddChild(_editorView);
        AddChild(new SeedMenu
        {
            Where = $"Editing {track.Name}",
            Generate = c => GoToCode(args, c),
            Tracks = () => TrackView.AllTracks()
                .Select(t => ($"{TrackLabel(t.Track)}{(t.Saved ? ", saved" : string.Empty)}", (Action)(() => StartAgainOn(args, $"--track={t.Path}"))))
                .ToList(),
            NewTrack = () => StartAgainOn(args, "--edit=new"),
        });
        GD.Print($"Editing {track.Id} ({track.Name}): {track.Pieces.Length} pieces");
    }
}
