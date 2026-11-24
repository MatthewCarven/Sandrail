using System;
using Godot;

namespace Sandrail.Game.Ui;

/// <summary>
/// A padlock button, drawn rather than a font's glyph so it looks the same on any machine: shut, or open with its
/// shackle lifted. It guards a button that only shows while it's open (Change car's Set inactive, 2026-11-25).
/// </summary>
/// <remarks>
/// It takes no keyboard focus, so a stray Enter or Space never opens it: it's opened with the mouse, on purpose.
/// </remarks>
public partial class LockButton : Button
{
    private static readonly Color Shut = new(0.85f, 0.85f, 0.85f);
    private static readonly Color Unlocked = new(1.0f, 0.75f, 0.3f);

    private bool _open;

    public LockButton()
    {
        FocusMode = FocusModeEnum.None;
        CustomMinimumSize = new Vector2(36.0f, 0.0f);
    }

    /// <summary>Whether it's open: the shackle lifted and drawn amber.</summary>
    public bool Open
    {
        get => _open;
        set
        {
            _open = value;
            QueueRedraw();
        }
    }

    public override void _Draw()
    {
        // The icon is a box about two thirds of the button's height: the body its lower half, the shackle above.
        Vector2 centre = Size / 2.0f;
        float height = MathF.Min(Size.X, Size.Y) * 0.62f;
        float width = height * 0.8f;
        float line = MathF.Max(2.0f, height * 0.13f);
        Color colour = _open ? Unlocked : Shut;

        float bodyHeight = height * 0.55f;
        float bottom = centre.Y + (height / 2.0f);
        var body = new Rect2(centre.X - (width / 2.0f), bottom - bodyHeight, width, bodyHeight);

        float radius = (width / 2.0f) - (line * 1.2f);
        float lift = _open ? height * 0.22f : 0.0f;
        var arc = new Vector2(centre.X, bottom - height + radius + (line / 2.0f) - lift);

        // The shackle: an arch, the right leg into the body, the left leg into it too when shut, free when open.
        DrawArc(arc, radius, MathF.PI, MathF.Tau, 16, colour, line, true);
        DrawLine(arc + new Vector2(radius, 0.0f), new Vector2(arc.X + radius, body.Position.Y), colour, line, true);
        float leftEnd = _open ? arc.Y + (radius * 0.6f) : body.Position.Y;
        DrawLine(arc - new Vector2(radius, 0.0f), new Vector2(arc.X - radius, leftEnd), colour, line, true);

        DrawRect(body, colour);
        DrawCircle(new Vector2(centre.X, body.Position.Y + (bodyHeight * 0.45f)), bodyHeight * 0.14f, new Color(0.1f, 0.1f, 0.12f));
    }
}
