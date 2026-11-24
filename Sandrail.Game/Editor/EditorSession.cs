using Godot;
using Sandrail.Core.Tracks;

namespace Sandrail.Game.Editor;

/// <summary>Where the editor's view was: the cursor, the piece in hand, the camera. Put back after a test drive.</summary>
public sealed record EditorState(Tile Cursor, int Level, int Turn, bool Mirrored, int Piece, int Yaw, float Zoom, bool AtEnd, int EndIndex, Vector3 CameraAt, Vector3 Focus, bool Follow);

/// <summary>
/// The editor's work, kept outside the scene (M5c): Tab into a test drive starts the scene again as the game,
/// and Tab back starts it again as the editor, so the track, its undo history and the view live here, as the
/// Esc menu's next flags live in <c>Main.NextArgs</c>.
/// </summary>
/// <remarks>
/// <b>Test-driving reuses the game's own setup</b> rather than a second one: the car, its camera, R, the
/// autodriver, and on a sound track from its start gate the race, ghosts and replays, all as they are anywhere
/// else (M5-PLAN, decision 4). <c>--track=@editor</c> drives the track kept here.
/// </remarks>
public static class EditorSession
{
    /// <summary>What <c>--track=</c> and <c>--edit=</c> say for the track kept here.</summary>
    public const string Id = "@editor";

    /// <summary>The track being edited and its history, if the editor has been opened.</summary>
    public static TrackEditor? Editor { get; set; }

    /// <summary>Where the view was when it left for a test drive; null to start afresh.</summary>
    public static EditorState? State { get; set; }
}
