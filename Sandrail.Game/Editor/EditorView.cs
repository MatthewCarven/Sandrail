using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using Godot;
using Sandrail.Core.Tracks;
using Sandrail.Game.World;

namespace Sandrail.Game.Editor;

/// <summary>
/// The track editor (M5-PLAN, M5a): a camera above the grid, a cursor on a tile and a level, the piece in hand
/// shown there (see-through, green where it fits and red where it doesn't), and the track's problems listed as
/// they stand, the pieces at fault tinted red. Every change goes through Core's <see cref="TrackEditor"/>.
/// </summary>
/// <remarks>
/// <para>
/// <b>Keys</b> (decision 3; the mouse comes with M5b): the arrows move the cursor, away from the camera being
/// up; [ and ] pick the piece; Q and E turn it; M mirrors it; Page Up and Page Down change the level; Enter or
/// Space places it; Delete or Backspace takes away the piece under the cursor; Ctrl+Z and Ctrl+Y undo and redo;
/// C turns the view a quarter; - and + zoom. Esc is the menu's.
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

    /// <summary>A problem in the editor's words: "the corner-30 at (14, 13)" for the file's "pieces[3] (base:corner-30 at (14, 13))".</summary>
    private static string Plain(string problem)
    {
        string said = PieceLabel.Replace(problem, "the $1 at $2");
        return char.ToUpperInvariant(said[0]) + said[1..];
    }

    private readonly Dictionary<Placement, Node3D> _built = new();
    private List<PieceDef> _palette = new();
    private Camera3D _camera = null!;
    private MeshInstance3D _cursorMark = null!;
    private Node3D? _hand;
    private Placement? _handShown;
    private bool _handFits;
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
    private readonly StandardMaterial3D _fits = Glass(new Color(0.3f, 0.95f, 0.4f, 0.45f));
    private readonly StandardMaterial3D _blocked = Glass(new Color(1.0f, 0.25f, 0.2f, 0.45f));
    private readonly StandardMaterial3D _fault = Glass(new Color(1.0f, 0.15f, 0.1f, 0.35f));

    /// <summary>The track being edited and its history.</summary>
    public required TrackEditor Editor { get; init; }

    public required PieceCatalogue Catalogue { get; init; }

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

        // Start where the work is: the newest open end of the road, or the first piece, or the grid's middle.
        if (Editor.OpenEnds() is { Count: > 0 } ends)
        {
            WorldConnector end = ends[^1];
            _cursor = end.Kind == ConnectorKind.Lip ? end.Landing : end.Beyond;
            _level = end.Level;
            _turn = (int)end.Facing;
        }
        else if (Editor.Laid.Placed.Count > 0)
        {
            _cursor = Editor.Laid.Placed[0].Placement.At;
            _level = Editor.Laid.Placed[0].Placement.Level;
        }

        _cursor = Clamped(_cursor);
        Rebuild();
        Say("The editor. Arrows move, [ ] pick a piece, Enter places it. Esc for the menu.", 6.0);
    }

    public override void _UnhandledInput(InputEvent e)
    {
        if (e is not InputEventKey { Pressed: true } key)
        {
            return;
        }

        Compass ahead = TrackGrid.Turned(Compass.North, _yaw);
        bool handled = true;
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
            case Key.Bracketleft:
                _piece = (_piece + _palette.Count - 1) % _palette.Count;
                break;
            case Key.Bracketright:
                _piece = (_piece + 1) % _palette.Count;
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
                break;
            case Key.Pagedown:
                _level = Math.Max(Editor.Track.Ground is null ? 0 : -Track.MaxLevel, _level - 1);
                break;
            case Key.Enter or Key.KpEnter or Key.Space:
                Act(Editor.Place(Hand), $"Placed {Hand.Piece[5..]}.");
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
            case Key.Minus or Key.KpSubtract:
                _zoom = Math.Min(400.0f, _zoom * 1.25f);
                break;
            case Key.Equal or Key.KpAdd:
                _zoom = Math.Max(30.0f, _zoom / 1.25f);
                break;
            default:
                handled = false;
                break;
        }

        if (handled)
        {
            GetViewport().SetInputAsHandled();
        }
    }

    public override void _Process(double delta)
    {
        if (_noteLeft > 0.0)
        {
            _noteLeft -= delta;
            _note.Visible = _noteLeft > 0.0;
        }

        ShowHand();
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
        _status.Text = $"EDITOR   {Editor.Track.Name}\n"
            + $"PIECE  {_piece + 1} of {_palette.Count}: {def.Name}{(_mirrored ? " (mirrored)" : string.Empty)}, facing {TrackGrid.Turned(Compass.North, _turn).ToString().ToLowerInvariant()}, level {_level}\n"
            + $"AT     {_cursor}{(Editor.PieceAt(_cursor) is PlacedPiece under ? $", on {under.Def.Name.ToLowerInvariant()}" : string.Empty)}"
            + (_handFits ? string.Empty : $"\n       {Editor.Fits(Hand)}");
    }

    /// <summary>The piece in hand, where the cursor is, as it would be placed.</summary>
    private Placement Hand => new(_palette[_piece].Id, _cursor, _level, _turn, _mirrored);

    private void Move(Compass way) => _cursor = Clamped(_cursor.Step(way));

    private static Tile Clamped(Tile t) => new(Math.Clamp(t.Column, 0, TrackGrid.Size - 1), Math.Clamp(t.Row, 0, TrackGrid.Size - 1));

    /// <summary>A change made, or refused: the track rebuilt where it changed, and a word on the result.</summary>
    private void Act(string? refused, string done)
    {
        if (refused is not null)
        {
            Say(char.ToUpperInvariant(refused[0]) + refused[1..] + ".", 4.0);
            return;
        }

        Rebuild();
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
                Node3D node = TrackView.BuildOne(piece);
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

        IReadOnlyList<string> problems = Editor.Laid.Problems;
        _problems.Text = problems.Count == 0
            ? "SOUND: it can be raced."
            : $"TO DO ({problems.Count})\n" + string.Join("\n", problems.Take(8).Select(p => "· " + Plain(p))) + (problems.Count > 8 ? $"\n· and {problems.Count - 8} more" : string.Empty);
        _problems.Modulate = problems.Count == 0 ? new Color(0.6f, 1.0f, 0.6f) : Colors.White;
        _handShown = null;
    }

    /// <summary>The piece in hand at the cursor, built again only when it moves or changes.</summary>
    private void ShowHand()
    {
        Placement hand = Hand;
        if (hand == _handShown)
        {
            return;
        }

        _handShown = hand;
        _hand?.QueueFree();
        _handFits = Editor.Fits(hand) is null;
        var piece = new PlacedPiece(_palette[_piece], hand);
        _hand = TrackView.BuildOne(piece);
        foreach (CollisionObject3D body in _hand.FindChildren("*", "CollisionObject3D", true, false).OfType<CollisionObject3D>())
        {
            body.CollisionLayer = 0;
            body.CollisionMask = 0;
        }

        foreach (MeshInstance3D mesh in _hand.FindChildren("*", "MeshInstance3D", true, false).OfType<MeshInstance3D>())
        {
            mesh.MaterialOverride = _handFits ? _fits : _blocked;
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
        keys.Text = "Arrows: move  ·  [ ]: piece  ·  Q/E: turn  ·  M: mirror  ·  PgUp/PgDn: level  ·  Enter: place  ·  Delete: take away\n"
            + "Ctrl+Z / Ctrl+Y: undo / redo  ·  C: turn the view  ·  - / +: zoom  ·  Esc: menu";
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
