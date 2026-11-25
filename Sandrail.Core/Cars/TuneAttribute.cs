using System.Reflection;

namespace Sandrail.Core.Cars;

/// <summary>Which group a number's slider sits in on the tuning panel, and where the slider ends.</summary>
/// <remarks>
/// Narrower than <see cref="CarSpec.Problems"/>: validation refuses what can't be driven, while this is
/// the range worth exploring by hand. A number typed into the panel's box can go past the slider's ends,
/// as far as validation allows.
/// </remarks>
[AttributeUsage(AttributeTargets.Property)]
public sealed class TuneAttribute(string group, float min, float max) : Attribute
{
    public string Group { get; } = group;

    public float Min { get; } = min;

    public float Max { get; } = max;
}

/// <summary>One number in a <see cref="CarSpec"/> as the tuning panel and the recorder see it.</summary>
public sealed record Tunable(string Name, string Group, float Min, float Max, PropertyInfo Property)
{
    public float Get(CarSpec spec) => (float)Property.GetValue(spec)!;

    /// <summary>
    /// The slider's step on the tuning panel: a four-hundredth of its range, rounded down to 1, 2 or 5 times a power of
    /// ten, so a slider is always 200 to 400 steps long.
    /// </summary>
    public float Step => NiceStep((Max - Min) / 400.0f);

    /// <summary>
    /// A number put on the slider's steps, counted from its <see cref="Min"/> as Godot's slider counts them, and kept
    /// between its ends: a number the slider itself could have landed on, written without float dust (1.234, not
    /// 1.2340001). Random cars are made of these.
    /// </summary>
    public float OnStep(float value)
    {
        double step = Step;
        double steps = Math.Round((value - (double)Min) / step);
        int decimals = Math.Max(0, -(int)Math.Floor(Math.Log10(step) + 1e-9));
        double on = Math.Round(Min + (steps * step), Math.Min(decimals, 15));
        return (float)Math.Clamp(on, Min, Max);
    }

    /// <summary>The largest of 1, 2 or 5 times a power of ten that doesn't exceed <paramref name="rough"/>.</summary>
    public static float NiceStep(float rough)
    {
        float power = MathF.Pow(10.0f, MathF.Floor(MathF.Log10(rough)));
        float scaled = rough / power;
        return power * (scaled >= 5.0f ? 5.0f : scaled >= 2.0f ? 2.0f : 1.0f);
    }

    /// <summary>The number's name in words with its unit, as the tuning panel and the profile sheet show it: "Bump damping (N·s/m)".</summary>
    public string Words => WordsFor(Name);

    /// <summary>
    /// A property name as words with its unit: <c>BumpDampingNsPerM</c> becomes "Bump damping (N·s/m)".
    /// </summary>
    public static string WordsFor(string name)
    {
        (string words, string unit) = WordsAndUnit(name);
        return unit.Length == 0 ? words : $"{words} ({unit})";
    }

    /// <summary>A property name as words and its unit apart: <c>MassKg</c> is ("Mass", "kg"); the unit is empty if it has none.</summary>
    public static (string Words, string Unit) WordsAndUnit(string name)
    {
        (string Suffix, string Unit)[] units =
        [
            ("NPerMps2", "N per (m/s)²"), ("NsPerM", "N·s/m"), ("NPerM", "N/m"), ("DegPerS2", "°/s²"), ("DegPerS", "°/s"),
            ("PerS", "/s"), ("Mps", "m/s"), ("Deg", "°"), ("Kg", "kg"), ("W", "W"), ("N", "N"), ("M", "m"),
        ];

        string unit = string.Empty;
        foreach ((string suffix, string label) in units)
        {
            if (name.Length > suffix.Length
                && name.EndsWith(suffix, StringComparison.Ordinal)
                && char.IsLower(name[name.Length - suffix.Length - 1]))
            {
                name = name[..^suffix.Length];
                unit = label;
                break;
            }
        }

        var words = new System.Text.StringBuilder();
        for (int i = 0; i < name.Length; i++)
        {
            if (i > 0 && char.IsUpper(name[i]))
            {
                words.Append(' ');
                words.Append(char.ToLowerInvariant(name[i]));
            }
            else
            {
                words.Append(name[i]);
            }
        }

        return (words.ToString(), unit);
    }
}
