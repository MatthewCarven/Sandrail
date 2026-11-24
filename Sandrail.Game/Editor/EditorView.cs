using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using Godot;
using Sandrail.Core.Tracks;
using Sandrail.Game.World;

namespace Sandrail.Game.Editor;

/// <summary>
/// The track editor (M5-PLAN, M5a and M5b): a camera above the grid, a cursor, the piece in hand shown where it
/// would go (see-through, green where it fits and red where it doesn't), the palette down the left, and the
/// track's problems listed as they stand, the pieces at fault tinted red. Every change goes through Core's
/// <see cref="TrackEditor"/>.
/// </summary>
/// <remarks>
/// <para>
/// <b>Laying track onto the road's end</b> (decision 2, M5b): whenever the road has an open end, the cursor
/// waits there and the piece in hand snaps onto it, facing along the road, by <see cref="TrackEditor.Next"/>.
/// Enter lays it and the cursor moves on to the new end, like model railway track. After a jump's lip the
/// landing comes into hand by itself, and after the landing a straight. Backspace takes back the
/// road's last piece.
/// </para>
/// <para>
/// <b>The free cursor</b> does everything else: the arrows or the mouse move it, Q and E turn the piece, Page Up
/// and Page Down change its level. F goes back to the road's end; laying a piece anywhere goes to its end.
/// </para>
/// <para>
/// <b>The mouse</b> (decision 3): pointing moves the free cursor, a click places, a right click takes away, the
/// wheel picks the piece (Ctrl and the wheel zoom), and the palette's names are buttons.
/// </para>
/// <para>
/// <b>Only what changed is built</b> (fact 3): a placed piece is built alone, in about a millisecond and a
/// half, and one taken away is freed; nothing else is touched.
/// </para>
/// </remarks>
public partial class EditorView : Node3D
{
    /// <summary>The pieces in the order the palette offers them: road first, then the stunts, then the gates.</summary>
    private static readonly string[] PaletteOrder =
    [
        "base:straight", "base:corner-30", "base:corner-50", "base:corner-30-banked", "base:corner-50-banked",
        "base:slope-up", "base:slope-down", "base:s-bend", "base:chicane",
        "base:launch-short", "base:launch-long", "base:landing", "base:loop",
        "base:start", "base:checkpoint", "base:finish", "base:start-finish",
    ];

    private static readonly Regex PieceIndex = new(@"pieces\[(\d+)\]", RegexOptions.Compiled);

    /// <summary>A piece as <see cref="Track.Lay"/> names it, "pieces[3] (base:corner-30 at (14, 13))".</summary>
    private static readonly Regex PieceLabel = new(@"pieces\[\d+\] \((?:base:)?([^ ]+) at (\(\d+, \d+\))\)", RegexOptions.Compiled);

    private readonly Dictionary<Placement, Node3D> _built = new();
    private readonly List<Button> _paletteButtons = new();
    private List<PieceDef> _palette = new();
    private Camera3D _camera = null!;
    private MeshInstance3D _cursorMark = null!;
    private Node3D? _hand;
    private Placement? _handShown;
    private bool _handShownFits;
    private Label _status = null!;
    private Label _problems = null!;
    private Label _note = null!;
    private double _noteLeft;
    private Tile _cursor = new(16, 16);
    private int _level;
    private int _turn;
    private bool _mirrored;
    private int _piece;

    private int _yaw;
    private float _zoom = 110.0f;
    private Vector3 _cameraAt;
    private bool _cameraPlaced;

    // The ground, on a seed's (M5d): the terrain before any bed, its looks with the bed, and the next bed being
    // worked out off the main thread, due a moment after the last change.
    private Sandrail.Core.Generation.Terrain? _bare;
    private Sandrail.Core.Generation.Terrain? _bedded;
    private readonly MeshInstance3D?[,] _squares = new MeshInstance3D?[Squares, Squares];
    private System.Threading.Tasks.Task<(Sandrail.Core.Generation.Terrain Bed, List<(int I, int J, TerrainView.MeshArrays Arrays)> Changed)>? _groundTask;

    /// <summary>The ground's looks are this many squares a side, each rebuilt only when its ground moves.</summary>
    private const int Squares = 8;

    /// <summary>Samples a square spans: 128 m.</summary>
    private const int SquareSamples = (Sandrail.Core.Generation.Terrain.Samples - 1) / Squares;
    private System.Diagnostics.Stopwatch? _groundClock;
    private bool _groundDirty;
    private double _groundDueIn;

    /// <summary>How long after the last change the ground starts to catch up, seconds.</summary>
    private const double GroundAfter = 0.5;

    /// <summary>On a seed's ground, how many levels above the ground under it the free cursor rides.</summary>
    private int _lift;

    // Saving (decision 6): the name being typed, and the frame typing ended, so the Esc that ends it isn't the menu's too.
    private PanelContainer _saveBox = null!;
    private LineEdit _saveName = null!;
    private ulong _typingEndedFrame = ulong.MaxValue;

    /// <summary>Whether a name is being typed, so Esc is the editor's (it cancels) and not the menu's.</summary>
    public bool Typing => _saveBox.Visible || _typingEndedFrame == Engine.GetProcessFrames();

    /// <summary>Whether the cursor waits at the road's end (chain mode) rather than going where it's moved.</summary>
    private bool _atEnd;

    /// <summary>Which open end, of <see cref="TrackEditor.OpenEnds"/>, the cursor waits at.</summary>
    private int _endIndex = -1;

    private readonly StandardMaterial3D _fits = Glass(new Color(0.3f, 0.95f, 0.4f, 0.45f));
    private readonly StandardMaterial3D _blocked = Glass(new Color(1.0f, 0.25f, 0.2f, 0.45f));
    private readonly StandardMaterial3D _fault = Glass(new Color(1.0f, 0.15f, 0.1f, 0.35f));

    /// <summary>The track being edited and its history.</summary>
    public required TrackEditor Editor { get; init; }

    public required PieceCatalogue Catalogue { get; init; }

    /// <summary>Tab (M5c): a test drive from the piece on a tile; the view is kept in <see cref="EditorSession"/> for coming back.</summary>
    public System.Action<Tile>? TestDrive { get; init; }

    /// <summary>Where the view is now, for <see cref="EditorSession.State"/>.</summary>
    public EditorState Capture() => new(_cursor, _level, _turn, _mirrored, _piece, _yaw, _zoom, _atEnd, _endIndex, _cameraAt);

    /// <summary>
    /// Tab: a test drive from the piece the cursor is on; at the road's end, from the road's last piece; on empty
    /// ground, from the track's start (its start gate once it has one), so Tab always drives.
    /// </summary>
    private void Drive()
    {
        Tile? from = _atEnd && CurrentEnd is WorldConnector end ? end.Tile
            : Editor.PieceAt(_cursor) is not null ? _cursor
            : Editor.Laid.Route.Count > 0 ? Editor.Laid.Route[0].Placement.At
            : Editor.Laid.Placed.Count > 0 ? Editor.Laid.Placed[0].Placement.At
            : null;
        if (from is not Tile tile || TestDrive is null)
        {
            Say("Lay a piece first: there's nothing to drive.", 3.0);
            return;
        }

        EditorSession.State = Capture();
        TestDrive(tile);
    }

    public override void _Ready()
    {
        _palette = PaletteOrder.Select(Catalogue.Find).OfType<PieceDef>()
            .Concat(Catalogue.All.Where(d => !PaletteOrder.Contains(d.Id))).ToList();

        _camera = new Camera3D { Current = true, Far = 3000.0f, Fov = 50.0f, PhysicsInterpolationMode = PhysicsInterpolationModeEnum.Off };
        AddChild(_camera);

        var tile = new PlaneMesh { Size = new Vector2(TrackGrid.TileM, TrackGrid.TileM) };
        _cursorMark = new MeshInstance3D { Mesh = tile, MaterialOverride = Glass(new Color(1.0f, 0.85f, 0.2f, 0.35f)) };
        AddChild(_cursorMark);

        BuildPanels();

        // On a seed's ground (M5d): the terrain as it is, and its bed under the pieces, built now, then caught up in
        // the background a moment after each change. Only its looks: the editor drives nothing, so no collider.
        if (Editor.Track.Ground is TrackGround ground)
        {
            _bare = new Sandrail.Core.Generation.Terrain(ground.Seed, ground.Terrain);
            _bedded = Sandrail.Core.Generation.Bed.Lay(_bare, Editor.Laid.Placed);
            for (int i = 0; i < Squares; i++)
            {
                for (int j = 0; j < Squares; j++)
                {
                    ShowSquare(i, j, TerrainView.Arrays(_bedded, i * SquareSamples, j * SquareSamples, SquareSamples + 1));
                }
            }
        }

        // Start where the work is: the road's newest open end; on an empty grid, with a start gate in hand.
        if (Editor.OpenEnds().Count > 0)
        {
            GoToEnd(Editor.OpenEnds().Count - 1);
        }
        else if (Editor.Laid.Placed.Count > 0)
        {
            _cursor = Editor.Laid.Placed[0].Placement.At;
            _level = Editor.Laid.Placed[0].Placement.Level;
        }
        else
        {
            Pick(_palette.FindIndex(d => d.Id == "base:start"));
            _level = GroundLevel(_cursor);
        }

        // Back from a test drive: the view as it was left.
        if (EditorSession.State is EditorState was)
        {
            (_cursor, _level, _turn, _mirrored, _yaw, _zoom, _atEnd, _endIndex) = (was.Cursor, was.Level, was.Turn, was.Mirrored, was.Yaw, was.Zoom, was.AtEnd, was.EndIndex);
            Pick(was.Piece);
            _cameraAt = was.CameraAt;
            _cameraPlaced = true;
            EditorSession.State = null;
            Rebuild();
            Say("Back in the editor, as you left it.", 3.0);
            return;
        }

        _cursor = Clamped(_cursor);
        Rebuild();
        Say(Editor.Laid.Placed.Count == 0
            ? "An empty grid, a start gate in hand: Enter puts it down, then each piece goes onto the road's end."
            : "Enter lays the piece in hand onto the road's end. Arrows or the mouse move a free cursor; F comes back.", 7.0);
    }

    public override void _UnhandledInput(InputEvent e)
    {
        switch (e)
        {
            case InputEventMouseMotion motion when motion.Relative.Length() > 2.0f:
                if (Pointed(motion.Position) is Tile pointed && (pointed != _cursor || _atEnd))
                {
                    GoFree();
                    _cursor = pointed;
                    FollowGround();
                }

                return;
            case InputEventMouseButton { Pressed: true } click:
                Clicked(click);
                return;
            case InputEventKey { Pressed: true } key:
                // Tab may have started the scene again, taking this out of the tree: then there's no viewport to tell.
                if (Pressed(key) && IsInsideTree())
                {
                    GetViewport().SetInputAsHandled();
                }

                return;
        }
    }

    private void Clicked(InputEventMouseButton click)
    {
        bool zoom = click.CtrlPressed;
        switch (click.ButtonIndex)
        {
            case MouseButton.Left when Pointed(click.Position) is Tile at:
                GoFree();
                _cursor = at;
                FollowGround();
                PlaceHand();
                break;
            case MouseButton.Right when Pointed(click.Position) is Tile at:
                Act(Editor.Erase(at), "Taken away.");
                break;
            case MouseButton.WheelUp when zoom:
                _zoom = Math.Max(30.0f, _zoom / 1.15f);
                break;
            case MouseButton.WheelDown when zoom:
                _zoom = Math.Min(400.0f, _zoom * 1.15f);
                break;
            case MouseButton.WheelUp:
                Pick((_piece + _palette.Count - 1) % _palette.Count);
                break;
            case MouseButton.WheelDown:
                Pick((_piece + 1) % _palette.Count);
                break;
            default:
                return;
        }

        GetViewport().SetInputAsHandled();
    }

    /// <summary>A key: true if the editor used it.</summary>
    private bool Pressed(InputEventKey key)
    {
        Compass ahead = TrackGrid.Turned(Compass.North, _yaw);
        switch (key.Keycode)
        {
            case Key.Up:
                Move(ahead);
                break;
            case Key.Down:
                Move(TrackGrid.Opposite(ahead));
                break;
            case Key.Right:
                Move(TrackGrid.Turned(ahead, 1));
                break;
            case Key.Left:
                Move(TrackGrid.Turned(ahead, -1));
                break;
            case Key.F:
                if (Editor.OpenEnds().Count == 0)
                {
                    Say("The road has no open end to go back to.", 3.0);
                }
                else
                {
                    GoToEnd(Editor.OpenEnds().Count - 1);
                }

                break;
            case Key.N:
                int ends = Editor.OpenEnds().Count;
                if (ends > 1)
                {
                    GoToEnd(((_atEnd ? _endIndex : ends - 1) + 1) % ends);
                    Say($"Open end {_endIndex + 1} of {ends}.", 2.0);
                }

                break;
            case Key.Bracketleft:
                Pick((_piece + _palette.Count - 1) % _palette.Count);
                break;
            case Key.Bracketright:
                Pick((_piece + 1) % _palette.Count);
                break;
            case >= Key.Key1 and <= Key.Key9:
                Pick((int)(key.Keycode - Key.Key1));
                break;
            case Key.Q:
                _turn = (_turn + 3) % 4;
                break;
            case Key.E:
                _turn = (_turn + 1) % 4;
                break;
            case Key.M:
                _mirrored = !_mirrored;
                break;
            case Key.Pageup:
                _level = Math.Min(Track.MaxLevel, _level + 1);
                _lift++;
                break;
            case Key.Pagedown:
                _level = Math.Max(Editor.Track.Ground is null ? 0 : -Track.MaxLevel, _level - 1);
                _lift--;
                break;
            case Key.S when key.CtrlPressed:
                StartSaving();
                break;
            case Key.L:
                NextLaps();
                break;
            case Key.Enter or Key.KpEnter or Key.Space:
                PlaceHand();
                break;
            case Key.Backspace when _atEnd && CurrentEnd is WorldConnector end:
                // At the road's end, Backspace takes back the piece the road ends on.
                Act(Editor.Erase(end.Tile), "Taken back.");
                break;
            case Key.Delete or Key.Backspace:
                Act(Editor.Erase(_cursor), "Taken away.");
                break;
            case Key.Z when key.CtrlPressed:
                Act(Editor.Undo() ? null : "Nothing to undo.", "Undone.");
                break;
            case Key.Y when key.CtrlPressed:
                Act(Editor.Redo() ? null : "Nothing to redo.", "Redone.");
                break;
            case Key.C:
                _yaw = (_yaw + 1) % 4;
                break;
            case Key.Tab:
                Drive();
                break;
            case Key.Minus or Key.KpSubtract:
                _zoom = Math.Min(400.0f, _zoom * 1.25f);
                break;
            case Key.Equal or Key.KpAdd:
                _zoom = Math.Max(30.0f, _zoom / 1.25f);
                break;
            default:
                return false;
        }

        return true;
    }

    public override void _Process(double delta)
    {
        if (_noteLeft > 0.0)
        {
            _noteLeft -= delta;
            _note.Visible = _noteLeft > 0.0;
        }

        // At the road's end the cursor is where the piece in hand would go, so a free cursor starts from there.
        (Placement hand, string? why) = Hand();
        if (_atEnd)
        {
            _cursor = Clamped(hand.At);
            _level = hand.Level;
            _turn = hand.Rotation;
        }

        ShowHand(hand, why is null);
        CatchUpGround(delta);

        if (_saveBox.Visible && Input.IsActionJustPressed(Controls.Menu))
        {
            EndTyping("Not saved.");
        }
        Vector3 middle = TrackGrid.Middle(_cursor, _level).ToGodot();
        _cursorMark.Position = middle + new Vector3(0.0f, 0.05f, 0.0f);

        // The camera behind and above the cursor, looking down at it, gliding there.
        (int dc, int dr) = TrackGrid.Delta(TrackGrid.Turned(Compass.North, _yaw));
        Vector3 wanted = middle + new Vector3(-dc * _zoom * 0.8f, _zoom * 0.9f, -dr * _zoom * 0.8f);
        _cameraAt = _cameraPlaced ? _cameraAt.Lerp(wanted, 1.0f - MathF.Exp(-8.0f * (float)delta)) : wanted;
        _cameraPlaced = true;
        _camera.GlobalPosition = _cameraAt;
        _camera.LookAt(_cameraAt + (middle - wanted), Vector3.Up);

        PieceDef def = _palette[_piece];
        string facing = TrackGrid.Turned(Compass.North, _turn).ToString().ToLowerInvariant();
        string where = _atEnd
            ? $"the road's end, {_cursor}, going {facing}, level {_level}{(Editor.OpenEnds().Count > 1 ? $" ({_endIndex + 1} of {Editor.OpenEnds().Count} ends)" : string.Empty)}"
            : $"{_cursor}, facing {facing}, level {_level}{(Editor.PieceAt(_cursor) is PlacedPiece under ? $", on the {Short(under.Def)}" : string.Empty)}";
        _status.Text = $"EDITOR   {Editor.Track.Name}\n"
            + (Editor.Track.Circuit ? $"       A circuit of {Editor.Track.Laps} laps (L for more)" : "       A sprint")
            + (Editor.Track.Ground is TrackGround g ? $", on seed {g.Seed}'s ground{(_groundTask is not null || _groundDirty ? ", catching up" : string.Empty)}\n" : "\n")
            + $"PIECE  {def.Name}{(_mirrored && Mirrors(def) ? (IsCorner(def) ? ", turning left" : ", mirrored") : string.Empty)}\n"
            + $"AT     {where}"
            + (why is null ? string.Empty : $"\n       {char.ToUpperInvariant(why[0])}{why[1..]}");
    }

    /// <summary>
    /// The piece in hand, where it would go, and why it can't if it can't: at the road's end, onto it; with the
    /// free cursor, on the cursor as turned. At an end a piece can't follow, it's shown there anyway, refused.
    /// </summary>
    private (Placement Placement, string? Why) Hand()
    {
        PieceDef def = _palette[_piece];
        if (_atEnd && CurrentEnd is WorldConnector end)
        {
            if (Editor.Next(end, def.Id, _mirrored, out string? cannot) is Placement next)
            {
                return (next, Editor.Fits(next));
            }

            Tile first = end.Kind == ConnectorKind.Lip ? end.Landing : end.Beyond;
            return (new Placement(def.Id, first, end.Level, (int)end.Facing, _mirrored), cannot);
        }

        var free = new Placement(def.Id, _cursor, _level, _turn, _mirrored);
        return (free, Editor.Fits(free));
    }

    /// <summary>The open end the cursor waits at, if it waits at one that's still open.</summary>
    private WorldConnector? CurrentEnd
    {
        get
        {
            IReadOnlyList<WorldConnector> ends = Editor.OpenEnds();
            if (ends.Count == 0)
            {
                return null;
            }

            _endIndex = Math.Clamp(_endIndex, 0, ends.Count - 1);
            return ends[_endIndex];
        }
    }

    private void GoToEnd(int index)
    {
        _atEnd = true;
        _endIndex = index;
    }

    /// <summary>Places the piece in hand; laid, the cursor goes on to its end if it has an open one.</summary>
    private void PlaceHand()
    {
        (Placement hand, string? why) = Hand();
        if (why is not null)
        {
            Say($"{char.ToUpperInvariant(why[0])}{why[1..]}.", 4.0);
            return;
        }

        string? refused = Editor.Place(hand);
        if (refused is not null)
        {
            Act(refused, string.Empty);
            return;
        }

        // On to the end of what was just laid, if it leaves one open; after a lip, the landing in hand.
        PlacedPiece? laid = Editor.PieceAt(hand.At);
        IReadOnlyList<WorldConnector> ends = Editor.OpenEnds();
        int index = laid is null ? -1 : ends.ToList().IndexOf(laid.Out);
        if (index >= 0)
        {
            GoToEnd(index);
        }
        else if (ends.Count > 0)
        {
            GoToEnd(ends.Count - 1);
        }
        else
        {
            _atEnd = false;
        }

        // After a lip, the landing in hand; after the landing, or the start gate, a straight.
        if (laid?.Out.Kind == ConnectorKind.Lip && _palette.FindIndex(d => d.Id == "base:landing") is int landing and >= 0)
        {
            Pick(landing);
        }
        else if (laid?.Def.In == ConnectorKind.Landing || laid?.Def.Id == "base:start")
        {
            Pick(0);
        }

        Rebuild();
        Changed();
        Say($"Laid the {Short(laid?.Def ?? _palette[_piece])}.", 2.0);
    }

    private void Pick(int index)
    {
        if (index < 0 || index >= _palette.Count)
        {
            return;
        }

        _piece = index;
        for (int i = 0; i < _paletteButtons.Count; i++)
        {
            _paletteButtons[i].Modulate = i == _piece ? new Color(1.0f, 0.9f, 0.3f) : Colors.White;
        }
    }

    /// <summary>
    /// Once a frame on a seed's ground: a bed finished in the background shown, and a new one begun once the
    /// track has stood still for a moment. The bed and the mesh's arrays are worked out off the main thread;
    /// only handing them to Godot happens on it (fact 4).
    /// </summary>
    private void CatchUpGround(double delta)
    {
        if (_bare is null)
        {
            return;
        }

        if (_groundTask is { IsCompleted: true } done)
        {
            _groundTask = null;
            if (done.IsFaulted)
            {
                GD.PrintErr($"The ground couldn't catch up: {done.Exception?.GetBaseException().Message}");
            }
            else
            {
                double behind = _groundClock!.Elapsed.TotalMilliseconds;
                var swap = System.Diagnostics.Stopwatch.StartNew();
                foreach ((int i, int j, TerrainView.MeshArrays arrays) in done.Result.Changed)
                {
                    ShowSquare(i, j, arrays);
                }

                _bedded = done.Result.Bed;
                GD.Print($"The editor's ground caught up: {behind:F0} ms in the background, {done.Result.Changed.Count} of {Squares * Squares} squares shown again in {swap.Elapsed.TotalMilliseconds:F1} ms");
            }
        }

        if (_groundDirty && _groundTask is null && (_groundDueIn -= delta) <= 0.0)
        {
            _groundDirty = false;
            IReadOnlyList<PlacedPiece> pieces = Editor.Laid.Placed;
            Sandrail.Core.Generation.Terrain bare = _bare;
            Sandrail.Core.Generation.Terrain before = _bedded!;
            _groundClock = System.Diagnostics.Stopwatch.StartNew();
            _groundTask = System.Threading.Tasks.Task.Run(() =>
            {
                Sandrail.Core.Generation.Terrain bed = Sandrail.Core.Generation.Bed.Lay(bare, pieces);
                var changed = new List<(int, int, TerrainView.MeshArrays)>();
                for (int i = 0; i < Squares; i++)
                {
                    for (int j = 0; j < Squares; j++)
                    {
                        if (SquareMoved(before, bed, i, j))
                        {
                            changed.Add((i, j, TerrainView.Arrays(bed, i * SquareSamples, j * SquareSamples, SquareSamples + 1)));
                        }
                    }
                }

                return (bed, changed);
            });
        }
    }

    /// <summary>Whether any of a square's ground (and the sample round it, which its edge's slope reads) moved.</summary>
    private static bool SquareMoved(Sandrail.Core.Generation.Terrain before, Sandrail.Core.Generation.Terrain after, int square, int squareJ)
    {
        for (int j = (squareJ * SquareSamples) - 1; j <= ((squareJ + 1) * SquareSamples) + 1; j++)
        {
            for (int i = (square * SquareSamples) - 1; i <= ((square + 1) * SquareSamples) + 1; i++)
            {
                if (before.At(i, j) != after.At(i, j))
                {
                    return true;
                }
            }
        }

        return false;
    }

    /// <summary>A square of the ground's looks, put in the place of the one before.</summary>
    private void ShowSquare(int i, int j, TerrainView.MeshArrays arrays)
    {
        MeshInstance3D square = TerrainView.Looks(arrays);
        AddChild(square);
        _squares[i, j]?.QueueFree();
        _squares[i, j] = square;
    }

    /// <summary>A change was made: the ground to catch up a moment from now, and a generated track renamed as edited (decision 6).</summary>
    private void Changed()
    {
        _groundDirty = _bare is not null;
        _groundDueIn = GroundAfter;
        if (Sandrail.Core.Generation.TrackCode.FromId(Editor.Track.Id) is Sandrail.Core.Generation.TrackCode code)
        {
            Editor.Rename($"user:seed-{code.Seed}-edited", $"Seed {code.Seed}, edited");
        }
    }

    /// <summary>The level of the ground under a tile's middle, on a seed's ground; 0 on the plain.</summary>
    private int GroundLevel(Tile tile)
    {
        if (_bare is null)
        {
            return 0;
        }

        System.Numerics.Vector3 middle = TrackGrid.Middle(tile, 0);
        return (int)MathF.Round((_bare.HeightAt(middle.X, middle.Z) - TrackGrid.Origin.Y) / TrackGrid.LevelM);
    }

    /// <summary>The free cursor, from the road's end if it was there: on a seed's ground, riding as far above the ground as it was.</summary>
    private void GoFree()
    {
        if (_atEnd && _bare is not null)
        {
            _lift = _level - GroundLevel(_cursor);
        }

        _atEnd = false;
    }

    /// <summary>The free cursor's level after a move: on a seed's ground, the ground's under it and the lift; on the plain, as it was.</summary>
    private void FollowGround()
    {
        if (_bare is not null)
        {
            _level = Math.Clamp(GroundLevel(_cursor) + _lift, -Track.MaxLevel, Track.MaxLevel);
        }
    }

    /// <summary>L: a circuit's laps, 1 to 9 and round again.</summary>
    private void NextLaps()
    {
        int laps = Editor.Track.Laps;
        Act(Editor.SetLaps(laps == 0 ? 1 : (laps % 9) + 1), $"{(laps % 9) + 1} laps.");
    }

    /// <summary>Ctrl+S: the name box, with a name in it to keep or type over.</summary>
    private void StartSaving()
    {
        _saveBox.Visible = true;
        _saveName.Text = DefaultName();
        _saveName.GrabFocus();
        _saveName.SelectAll();
    }

    /// <summary>The name a track is offered to be saved as: its own, unless it's still untitled.</summary>
    private string DefaultName()
    {
        if (Editor.Track.Name is { Length: > 0 } name && name != TrackEditor.Empty().Name)
        {
            return name;
        }

        for (int n = 1; ; n++)
        {
            string offered = n == 1 ? "My track" : $"My track {n}";
            if (!FileAccess.FileExists($"{TrackView.SavedPath}/{Slug(offered)}.json"))
            {
                return offered;
            }
        }
    }

    /// <summary>
    /// Saves the track under a name (decision 6): <c>user://tracks/&lt;name&gt;.json</c>, its ID <c>user:&lt;name&gt;</c>.
    /// An unfinished one saves too, and opens in the editor from the Load list.
    /// </summary>
    private void SaveAs(string typed)
    {
        string name = typed.Trim();
        if (name.Length == 0)
        {
            Say("A track needs a name to be saved under.", 3.0);
            return;
        }

        string slug = Slug(name);
        Editor.Rename($"user:{slug}", name);
        string path = $"{TrackView.SavedPath}/{slug}.json";
        DirAccess.MakeDirRecursiveAbsolute(TrackView.SavedPath);
        using (FileAccess? file = FileAccess.Open(path, FileAccess.ModeFlags.Write))
        {
            if (file is null)
            {
                EndTyping($"Couldn't save it: {FileAccess.GetOpenError()}.");
                return;
            }

            file.StoreString(Editor.Track.Save());
        }

        GD.Print($"Saved {Editor.Track.Id} to {ProjectSettings.GlobalizePath(path)}");
        EndTyping($"Saved as \"{name}\"{(Editor.Laid.Problems.Count > 0 ? ", unfinished: it opens in the editor from Load a track" : ": Load a track drives it")}.");
    }

    private void EndTyping(string said)
    {
        _saveBox.Visible = false;
        _saveName.ReleaseFocus();
        _typingEndedFrame = Engine.GetProcessFrames();
        Say(said, 5.0);
    }

    /// <summary>A name as a file and an ID: lower case, letters and digits, a dash for the rest.</summary>
    private static string Slug(string name)
    {
        string slug = Regex.Replace(name.ToLowerInvariant(), "[^a-z0-9]+", "-").Trim('-');
        return slug.Length == 0 ? "track" : slug;
    }

    private void Move(Compass way)
    {
        GoFree();
        _cursor = Clamped(_cursor.Step(way));
        FollowGround();
    }

    /// <summary>The tile the mouse points at, on the plane of the cursor's level, if it's on the grid.</summary>
    private Tile? Pointed(Vector2 screen)
    {
        Vector3 from = _camera.ProjectRayOrigin(screen);
        Vector3 ray = _camera.ProjectRayNormal(screen);
        float height = TrackGrid.Middle(_cursor, _level).Y;
        if (ray.Y > -1e-4f)
        {
            return null;
        }

        Vector3 hit = from + (ray * ((height - from.Y) / ray.Y));
        System.Numerics.Vector3 o = TrackGrid.Origin;
        var tile = new Tile((int)MathF.Floor((hit.X - o.X) / TrackGrid.TileM), (int)MathF.Floor((hit.Z - o.Z) / TrackGrid.TileM));
        return TrackGrid.Contains(tile) ? tile : null;
    }

    private static Tile Clamped(Tile t) => new(Math.Clamp(t.Column, 0, TrackGrid.Size - 1), Math.Clamp(t.Row, 0, TrackGrid.Size - 1));

    private static bool IsCorner(PieceDef def) => def.Path.Any(s => s.Turn is not null);

    /// <summary>Whether mirroring a piece changes it: one that turns, loops or reaches to a side; a straight or a slope doesn't.</summary>
    private static bool Mirrors(PieceDef def) =>
        IsCorner(def) || def.Path.Any(s => s.Loop is not null) || def.Tiles.Any(t => t[0] != 0) || def.Out.Tile[0] != 0;

    /// <summary>A piece's short name, its ID without the namespace: "corner-30".</summary>
    private static string Short(PieceDef def) => def.Id.StartsWith("base:", StringComparison.Ordinal) ? def.Id[5..] : def.Id;

    /// <summary>A problem in the editor's words: "the corner-30 at (14, 13)" for the file's "pieces[3] (base:corner-30 at (14, 13))".</summary>
    private static string Plain(string problem)
    {
        string said = PieceLabel.Replace(problem, "the $1 at $2");
        return char.ToUpperInvariant(said[0]) + said[1..];
    }

    /// <summary>A change made, or refused: the track rebuilt where it changed, and a word on the result.</summary>
    private void Act(string? refused, string done)
    {
        if (refused is not null)
        {
            Say(char.ToUpperInvariant(refused[0]) + refused[1..] + ".", 4.0);
            return;
        }

        Rebuild();
        Changed();
        Say(done, 2.0);
    }

    /// <summary>The pieces as the track now has them: the gone freed, the new built, the faulty tinted; and the problems.</summary>
    private void Rebuild()
    {
        var now = Editor.Laid.Placed.ToDictionary(p => p.Placement);
        foreach (Placement gone in _built.Keys.Where(k => !now.ContainsKey(k)).ToList())
        {
            _built[gone].QueueFree();
            _built.Remove(gone);
        }

        foreach ((Placement p, PlacedPiece piece) in now)
        {
            if (!_built.ContainsKey(p))
            {
                Node3D node = TrackView.BuildOne(piece, _bare);
                AddChild(node);
                _built[p] = node;
            }
        }

        // The pieces the problems name, by their place in the file, tinted red.
        var faulty = new HashSet<Placement>();
        foreach (string problem in Editor.Laid.Problems)
        {
            foreach (Match m in PieceIndex.Matches(problem))
            {
                int i = int.Parse(m.Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture);
                if (i < Editor.Track.Pieces.Length)
                {
                    faulty.Add(Editor.Track.Pieces[i].ToPlacement());
                }
            }
        }

        foreach ((Placement p, Node3D node) in _built)
        {
            foreach (MeshInstance3D mesh in node.FindChildren("*", "MeshInstance3D", true, false).OfType<MeshInstance3D>())
            {
                mesh.MaterialOverlay = faulty.Contains(p) ? _fault : null;
            }
        }

        // An end taken away (by undo, or Backspace) moves the cursor to the end that's newest now.
        if (_atEnd && Editor.OpenEnds().Count > 0 && _endIndex >= Editor.OpenEnds().Count)
        {
            _endIndex = Editor.OpenEnds().Count - 1;
        }

        IReadOnlyList<string> problems = Editor.Laid.Problems;
        _problems.Text = problems.Count == 0
            ? "SOUND: it can be raced."
            : $"TO DO ({problems.Count})\n" + string.Join("\n", problems.Take(8).Select(p => "· " + Plain(p))) + (problems.Count > 8 ? $"\n· and {problems.Count - 8} more" : string.Empty);
        _problems.Modulate = problems.Count == 0 ? new Color(0.6f, 1.0f, 0.6f) : Colors.White;
        _handShown = null;
    }

    /// <summary>The piece in hand where it would go, built again only when it moves or changes.</summary>
    private void ShowHand(Placement hand, bool fits)
    {
        if (hand == _handShown && fits == _handShownFits)
        {
            return;
        }

        _handShown = hand;
        _handShownFits = fits;
        _hand?.QueueFree();
        _hand = TrackView.BuildOne(new PlacedPiece(_palette[_piece], hand), _bare);
        foreach (CollisionObject3D body in _hand.FindChildren("*", "CollisionObject3D", true, false).OfType<CollisionObject3D>())
        {
            body.CollisionLayer = 0;
            body.CollisionMask = 0;
        }

        foreach (MeshInstance3D mesh in _hand.FindChildren("*", "MeshInstance3D", true, false).OfType<MeshInstance3D>())
        {
            mesh.MaterialOverride = fits ? _fits : _blocked;
            mesh.CastShadow = GeometryInstance3D.ShadowCastingSetting.Off;
        }

        AddChild(_hand);
    }

    private void Say(string words, double seconds)
    {
        _note.Text = words;
        _note.Visible = true;
        _noteLeft = seconds;
    }

    private void BuildPanels()
    {
        var layer = new CanvasLayer { Layer = 5 };
        AddChild(layer);

        _status = Panel(layer, 18, new Vector2(24.0f, 20.0f));
        _problems = Panel(layer, 15, new Vector2(-24.0f, 20.0f), right: true);
        _problems.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        _problems.CustomMinimumSize = new Vector2(520.0f, 0.0f);
        _note = Panel(layer, 18, new Vector2(24.0f, -70.0f), bottom: true);
        Label keys = Panel(layer, 15, new Vector2(24.0f, -20.0f), bottom: true);
        keys.Text = "Tab: drive it  ·  Ctrl+S: save  ·  Enter/click: lay  ·  Backspace: take back  ·  wheel, [ ], 1-9: piece  ·  M: mirror  ·  F: road's end  ·  N: next end\n"
            + "Arrows/mouse: free cursor  ·  Q/E: turn  ·  PgUp/PgDn: level  ·  Del/right click: take away  ·  Ctrl+Z/Y: undo/redo  ·  L: laps  ·  C: view  ·  -/+: zoom  ·  Esc: menu";

        // The name box for saving (decision 6), in the middle, hidden until Ctrl+S.
        _saveBox = new PanelContainer { Visible = false };
        _saveBox.SetAnchorsPreset(Control.LayoutPreset.Center);
        _saveBox.GrowHorizontal = Control.GrowDirection.Both;
        _saveBox.GrowVertical = Control.GrowDirection.Both;
        _saveBox.AddThemeStyleboxOverride("panel", new StyleBoxFlat
        {
            BgColor = new Color(0.08f, 0.08f, 0.1f, 0.92f),
            ContentMarginLeft = 20.0f,
            ContentMarginRight = 20.0f,
            ContentMarginTop = 16.0f,
            ContentMarginBottom = 16.0f,
        });
        var row = new HBoxContainer();
        row.AddThemeConstantOverride("separation", 10);
        var prompt = new Label { Text = "Save as" };
        prompt.AddThemeFontSizeOverride("font_size", 18);
        row.AddChild(prompt);
        _saveName = new LineEdit { CustomMinimumSize = new Vector2(320.0f, 0.0f), PlaceholderText = "a name for the track" };
        _saveName.AddThemeFontSizeOverride("font_size", 18);
        _saveName.TextSubmitted += SaveAs;
        row.AddChild(_saveName);
        var hint = new Label { Text = "Enter saves, Esc doesn't", Modulate = new Color(1.0f, 1.0f, 1.0f, 0.6f) };
        row.AddChild(hint);
        _saveBox.AddChild(row);
        layer.AddChild(_saveBox);

        // The palette down the left in two columns, a button a piece, the one in hand lit.
        var backing = new PanelContainer { OffsetLeft = 16.0f, OffsetTop = 162.0f };
        backing.AddThemeStyleboxOverride("panel", new StyleBoxFlat
        {
            BgColor = new Color(0.06f, 0.06f, 0.08f, 0.6f),
            ContentMarginLeft = 6.0f,
            ContentMarginRight = 6.0f,
            ContentMarginTop = 4.0f,
            ContentMarginBottom = 4.0f,
            CornerRadiusTopLeft = 4,
            CornerRadiusTopRight = 4,
            CornerRadiusBottomLeft = 4,
            CornerRadiusBottomRight = 4,
        });
        layer.AddChild(backing);
        var list = new GridContainer { Columns = 2 };
        list.AddThemeConstantOverride("v_separation", 0);
        list.AddThemeConstantOverride("h_separation", 6);
        backing.AddChild(list);
        for (int i = 0; i < _palette.Count; i++)
        {
            int index = i;
            var button = new Button
            {
                Text = $"{(i < 9 ? $"{i + 1} " : "   ")}{_palette[i].Name}",
                Flat = true,
                Alignment = HorizontalAlignment.Left,
                FocusMode = Control.FocusModeEnum.None,
            };
            button.AddThemeFontSizeOverride("font_size", 14);
            button.Pressed += () => Pick(index);
            list.AddChild(button);
            _paletteButtons.Add(button);
        }

        Pick(_piece);
    }

    private static Label Panel(CanvasLayer layer, int size, Vector2 offset, bool right = false, bool bottom = false)
    {
        var label = new Label
        {
            LabelSettings = new LabelSettings { FontSize = size, OutlineSize = 4, OutlineColor = new Color(0.0f, 0.0f, 0.0f, 0.85f) },
        };
        label.AnchorLeft = label.AnchorRight = right ? 1.0f : 0.0f;
        label.AnchorTop = label.AnchorBottom = bottom ? 1.0f : 0.0f;
        label.GrowHorizontal = right ? Control.GrowDirection.Begin : Control.GrowDirection.End;
        label.GrowVertical = bottom ? Control.GrowDirection.Begin : Control.GrowDirection.End;
        label.OffsetLeft = offset.X;
        label.OffsetRight = offset.X;
        label.OffsetTop = offset.Y;
        label.OffsetBottom = offset.Y;
        layer.AddChild(label);
        return label;
    }

    private static StandardMaterial3D Glass(Color colour) => new()
    {
        AlbedoColor = colour,
        Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
        ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
        CullMode = BaseMaterial3D.CullModeEnum.Disabled,
    };
}
