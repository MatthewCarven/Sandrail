using System.Globalization;
using System.Text.RegularExpressions;

namespace Sandrail.Core.Profiles;

/// <summary>
/// The autodriver's sweep of a car over a track (PROFILE-PLAN, P3, decision 2): the cautions it's driven at, 50% to 100%
/// by 5%, until a run isn't clean. The game sweeps with these, and Profile unprofiled cars reads the sheets with them.
/// </summary>
public static class ProfileSweep
{
    /// <summary>The first caution of a sweep (from 50, at his word).</summary>
    public const float First = 0.50f;

    /// <summary>The last: a run clean at 100% ends the sweep too.</summary>
    public const float Last = 1.00f;

    public const float Step = 0.05f;

    /// <summary>
    /// The caution after this one, on the 5% steps. Worked out from the step's count rather than added, so a caution read
    /// back from a sheet's <c>0.65</c> gives exactly the number a sweep that never stopped drives next.
    /// </summary>
    public static float Next(float caution) => MathF.Round((caution + Step) / Step) * Step;

    /// <summary>Whether a sweep ends after a run at this caution: one that isn't clean, or a clean one at the last.</summary>
    public static bool EndsAfter(float caution, bool clean) => !clean || caution >= Last - 0.001f;
}

/// <summary>What a car's sweep over a track has come to, from the track's sheets.</summary>
/// <param name="Ended">Whether the sweep finished: a run that wasn't clean, or a clean run at 100%.</param>
/// <param name="LastClean">The highest caution it ran clean at; null if none.</param>
public sealed record SweepState(bool Ended, float? LastClean)
{
    /// <summary>The caution the car's sweep goes on from: the first if it's never been driven, the one after its last clean run, or null if it's ended.</summary>
    public float? From => Ended ? null : LastClean is float clean ? ProfileSweep.Next(clean) : ProfileSweep.First;
}

/// <summary>
/// Which cars a track's sheets say still want profiling (2026-11-27, his idea: "Profile unprofiled cars", so a stopped
/// run of Profile every car on every track is picked up where it left off instead of started from scratch).
/// </summary>
/// <remarks>
/// <para>
/// <b>A car is known by its fingerprint and a track by its own</b>, as the sheets file them: every file of the track is read,
/// its own (<c>Shakedown 8a6323aa.csv</c>), any begun beside it when the header changed (<c>(2)</c>, <c>(3)</c>), and any
/// kept while a spreadsheet held it open (<c>(written while open)</c>), whatever name the track had when they were written.
/// </para>
/// <para>
/// <b>Only the autodriver's rows count</b>, from any version of the game: his own runs are no sweep. A run that wasn't
/// clean always ended its sweep, since Esc stops a profile without writing the run it was on.
/// </para>
/// </remarks>
public static class ProfileProgress
{
    /// <summary>
    /// Each car's sweep, by fingerprint, from every sheet in <paramref name="folder"/> for the track with this
    /// fingerprint, and from rows not yet written (a header and a row each, held while the sheet was open).
    /// </summary>
    public static IReadOnlyDictionary<string, SweepState> Of(string folder, string trackPrint, IEnumerable<(string Header, string Row)>? waiting = null)
    {
        var states = new Dictionary<string, SweepState>(StringComparer.Ordinal);
        foreach (string path in SheetsFor(folder, trackPrint))
        {
            Read(File.ReadLines(path), states);
        }

        foreach ((string header, string row) in waiting ?? [])
        {
            Read([header, row], states);
        }

        return states;
    }

    /// <summary>The sheet files for the track with this fingerprint, in name order.</summary>
    public static IReadOnlyList<string> SheetsFor(string folder, string trackPrint)
    {
        if (!Directory.Exists(folder))
        {
            return [];
        }

        var name = new Regex($@" {Regex.Escape(trackPrint)}( \((\d+|written while open)\))*$", RegexOptions.CultureInvariant);
        return Directory.GetFiles(folder, "*.csv")
            .Where(f => name.IsMatch(Path.GetFileNameWithoutExtension(f)))
            .OrderBy(f => f, StringComparer.Ordinal)
            .ToArray();
    }

    /// <summary>A sheet's lines, its header first, folded into the cars' sweeps; a file without the columns it needs is passed over.</summary>
    public static void Read(IEnumerable<string> lines, Dictionary<string, SweepState> into)
    {
        int driver = -1, car = -1, caution = -1, clean = -1;
        bool first = true;
        foreach (string line in lines)
        {
            IReadOnlyList<string> cells = CarSheet.Fields(line);
            if (first)
            {
                first = false;
                driver = IndexOf(cells, "driver");
                car = IndexOf(cells, "car fingerprint");
                caution = IndexOf(cells, "caution");
                clean = IndexOf(cells, "clean");
                if (driver < 0 || car < 0 || caution < 0 || clean < 0)
                {
                    return;
                }

                continue;
            }

            int needed = Math.Max(Math.Max(driver, car), Math.Max(caution, clean));
            if (cells.Count <= needed || cells[driver] != "auto"
                || !float.TryParse(cells[caution], NumberStyles.Float, CultureInfo.InvariantCulture, out float at))
            {
                continue;
            }

            bool wasClean = cells[clean] == "yes";
            SweepState was = into.GetValueOrDefault(cells[car]) ?? new SweepState(false, null);
            into[cells[car]] = new SweepState(
                was.Ended || ProfileSweep.EndsAfter(at, wasClean),
                wasClean && (was.LastClean is not float best || at > best) ? at : was.LastClean);
        }
    }

    private static int IndexOf(IReadOnlyList<string> cells, string name)
    {
        for (int i = 0; i < cells.Count; i++)
        {
            if (cells[i] == name)
            {
                return i;
            }
        }

        return -1;
    }
}
