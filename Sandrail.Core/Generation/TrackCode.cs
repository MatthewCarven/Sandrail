using System.Globalization;

namespace Sandrail.Core.Generation;

/// <summary>
/// A track code (M4-PLAN, M4a, decision 2): a seed and the version of the generator that made its track,
/// written <c>5v1</c>. A seed alone is a track only for one version of the generator (fact 5); a code says
/// which, so a shared code is always the same track, and an old one says so instead of quietly driving a
/// different track.
/// </summary>
/// <remarks>
/// Only the seed and the version: a track made with other dials has no code, and is shared as its file.
/// </remarks>
public readonly record struct TrackCode(ulong Seed, int Version)
{
    /// <summary>What a generated track's ID starts with.</summary>
    public const string IdPrefix = "gen:";

    /// <summary>The code for a seed's track from this build's generator.</summary>
    public static TrackCode Of(ulong seed) => new(seed, TrackGenerator.Version);

    /// <summary>Whether this build's generator makes its track.</summary>
    public bool Current => Version == TrackGenerator.Version;

    /// <summary>The ID of the track it names: <c>gen:5v1</c>.</summary>
    public string TrackId => IdPrefix + ToString();

    public override string ToString() => $"{Seed.ToString(CultureInfo.InvariantCulture)}v{Version.ToString(CultureInfo.InvariantCulture)}";

    /// <summary>
    /// Reads a code as a person types it: <c>5v1</c>, or a bare seed, <c>5</c>, which means this build's
    /// version. Space around it and a capital V are fine; anything else is refused, and the problem says why.
    /// </summary>
    public static bool TryParse(string? text, out TrackCode code, out string problem)
    {
        code = default;
        string t = (text ?? string.Empty).Trim();
        if (t.Length == 0)
        {
            problem = "Type a seed, a whole number like 5, or a code like 5v1.";
            return false;
        }

        int v = t.IndexOfAny(['v', 'V']);
        string seedPart = v < 0 ? t : t[..v];
        if (!IsDigits(seedPart) || !ulong.TryParse(seedPart, NumberStyles.None, CultureInfo.InvariantCulture, out ulong seed))
        {
            problem = $"\"{t}\" isn't a seed or a code: a seed is a whole number, 0 or more, and a code is a seed, v and a version, like 5v1.";
            return false;
        }

        int version = TrackGenerator.Version;
        if (v >= 0)
        {
            string versionPart = t[(v + 1)..];
            if (!IsDigits(versionPart) || !int.TryParse(versionPart, NumberStyles.None, CultureInfo.InvariantCulture, out version) || version < 1)
            {
                problem = $"\"{t}\" isn't a code: after the v comes the generator's version, a whole number from 1, like 5v1.";
                return false;
            }
        }

        code = new TrackCode(seed, version);
        problem = string.Empty;
        return true;
    }

    /// <summary>
    /// The code a track's ID names: <c>gen:5v1</c>, or <c>gen:5</c>, written before there were codes, when the
    /// generator was version 1. Null for any other ID: the game's own tracks, or a track made with other dials.
    /// </summary>
    public static TrackCode? FromId(string id)
    {
        if (!id.StartsWith(IdPrefix, StringComparison.Ordinal))
        {
            return null;
        }

        string rest = id[IdPrefix.Length..];
        if (IsDigits(rest) && ulong.TryParse(rest, NumberStyles.None, CultureInfo.InvariantCulture, out ulong old))
        {
            return new TrackCode(old, 1);
        }

        int v = rest.IndexOf('v', StringComparison.Ordinal);
        return v > 0 && IsDigits(rest[..v]) && IsDigits(rest[(v + 1)..])
            && ulong.TryParse(rest[..v], NumberStyles.None, CultureInfo.InvariantCulture, out ulong seed)
            && int.TryParse(rest[(v + 1)..], NumberStyles.None, CultureInfo.InvariantCulture, out int version) && version >= 1
            ? new TrackCode(seed, version)
            : null;
    }

    private static bool IsDigits(string s) => s.Length > 0 && s.All(char.IsAsciiDigit);
}
