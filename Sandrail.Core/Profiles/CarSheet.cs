using System.Globalization;
using System.Text;
using Sandrail.Core.Cars;
using Sandrail.Core.Tracks;

namespace Sandrail.Core.Profiles;

/// <summary>What a track's sheet has columns for: its route's pieces, and its checkpoints or its laps.</summary>
/// <param name="Pieces">Each route piece's label, in route order: <c>03 corner-30-banked</c>.</param>
/// <param name="Checkpoints">On a sprint, the checkpoints between start and finish; 0 on a circuit.</param>
/// <param name="Laps">On a circuit, its laps; 0 on a sprint.</param>
public sealed record SheetShape(IReadOnlyList<string> Pieces, int Checkpoints, int Laps)
{
    /// <summary>A laid track's shape: its route's pieces labelled in order, and its checkpoints or laps.</summary>
    public static SheetShape Of(LaidTrack track)
    {
        string format = track.Route.Count < 100 ? "00" : "000";
        string[] pieces = track.Route.Select((p, k) =>
        {
            string id = p.Def.Id.StartsWith("base:", StringComparison.Ordinal) ? p.Def.Id[5..] : p.Def.Id;
            return $"{(k + 1).ToString(format, CultureInfo.InvariantCulture)} {id}{(p.Placement.Mirrored ? " left" : string.Empty)}";
        }).ToArray();

        return track.Track.Circuit
            ? new SheetShape(pieces, 0, track.Track.Laps)
            : new SheetShape(pieces, track.Gates().Count - 2, 0);
    }
}

/// <summary>One run for the sheet: who drove what, and what the run came to.</summary>
/// <param name="When">When the run was driven.</param>
/// <param name="Version">The game's version, since a change to the car's physics changes every figure (PROFILE-PLAN fact 1).</param>
/// <param name="Driver"><c>auto</c> for the autodriver, <c>you</c> for him.</param>
/// <param name="Car">The car's name: its file's, in practice (fact 7).</param>
/// <param name="CarPrint">The car's fingerprint, which <c>cars.csv</c> traces to every one of its numbers.</param>
/// <param name="Caution">The autodriver's caution; null for his runs.</param>
/// <param name="Differences">The car against the stock car, in words (<see cref="CarSheet.Differences"/>).</param>
/// <param name="Run">What the run came to.</param>
public sealed record SheetRow(DateTime When, string Version, string Driver, string Car, string CarPrint, float? Caution, string Differences, RunSummary Run);

/// <summary>
/// The profile sheet (PROFILE-PLAN, P1, decision 3): runs as rows of plain numbers and words in a CSV a spreadsheet
/// opens as it is, one file per track, and every profiled car's numbers in <c>cars.csv</c>.
/// </summary>
/// <remarks>
/// <para>
/// <b>No formulas, decimal points whatever the machine's culture, UTF-8 with its mark</b> so Excel reads the units'
/// symbols, and Windows line ends. A row's cells always line up with the header made from the same track's shape.
/// </para>
/// <para>
/// <b>A file whose header no longer matches is left alone</b>, and a new one started beside it (<c>name (2).csv</c>),
/// so old rows never shift under new columns.
/// </para>
/// </remarks>
public static class CarSheet
{
    /// <summary>The file every profiled car's numbers go in.</summary>
    public const string CarsFile = "cars.csv";

    private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

    /// <summary>A track's file: its name and fingerprint, <c>Shakedown 8a6323aa.csv</c>, with nothing a file name can't hold.</summary>
    public static string FileName(string trackName, string fingerprint)
    {
        char[] bad = Path.GetInvalidFileNameChars();
        string name = new string(trackName.Select(c => bad.Contains(c) ? '-' : c).ToArray()).Trim();
        return $"{(name.Length == 0 ? "track" : name)} {fingerprint}.csv";
    }

    /// <summary>The header for a track of this shape.</summary>
    public static IReadOnlyList<string> Header(SheetShape shape)
    {
        var cells = new List<string>
        {
            "when", "version", "driver", "car", "car fingerprint", "caution", "clean", "differences from the stock car",
            "finished", "time s",
        };
        cells.AddRange(Enumerable.Range(1, shape.Checkpoints).Select(i => $"split {i} s"));
        if (shape.Laps > 0)
        {
            cells.AddRange(Enumerable.Range(1, shape.Laps).Select(i => $"lap {i} s"));
            cells.Add("best lap s");
        }

        cells.AddRange(["top km/h", "off road s", "worst slide deg", "spins", "respawns", "furthest m"]);
        cells.AddRange(shape.Pieces.Select(p => $"{p} s"));
        cells.AddRange(shape.Pieces.Select(p => $"{p} km/h"));
        return cells;
    }

    /// <summary>A run's cells, lined up with <see cref="Header"/> for the same shape.</summary>
    public static IReadOnlyList<string> Cells(SheetShape shape, SheetRow row)
    {
        RunSummary run = row.Run;
        if (run.Pieces.Count != shape.Pieces.Count)
        {
            throw new ArgumentException($"The run has {run.Pieces.Count} pieces and the sheet {shape.Pieces.Count}.", nameof(row));
        }

        var cells = new List<string>
        {
            row.When.ToString("yyyy-MM-dd HH:mm:ss", Inv),
            row.Version,
            row.Driver,
            row.Car,
            row.CarPrint,
            row.Caution is float c ? c.ToString("0.00", Inv) : string.Empty,
            run.Clean ? "yes" : "no",
            row.Differences,
            run.Finished ? "yes" : "no",
            Seconds(run.Seconds),
        };

        // A sprint's splits are its checkpoints'; the finish, after them, is the time.
        cells.AddRange(Enumerable.Range(0, shape.Checkpoints).Select(i => i < run.Splits.Count ? Seconds(run.Splits[i]) : string.Empty));
        if (shape.Laps > 0)
        {
            cells.AddRange(Enumerable.Range(0, shape.Laps).Select(i => i < run.Laps.Count ? Seconds(run.Laps[i]) : string.Empty));
            cells.Add(run.BestLap is int best ? Seconds(run.Laps[best]) : string.Empty);
        }

        cells.AddRange(
        [
            (run.TopMps * 3.6f).ToString("0.0", Inv),
            run.OffRoadSeconds.ToString("0.00", Inv),
            run.WorstSlideDeg.ToString("0", Inv),
            run.Spins.ToString(Inv),
            run.Respawns.ToString(Inv),
            run.FurthestM.ToString("0", Inv),
        ]);

        // A piece the run never reached is blank, not 0, so a spreadsheet's average skips it.
        cells.AddRange(run.Pieces.Select(p => float.IsNaN(p.SlowestMps) ? string.Empty : Seconds(p.Seconds)));
        cells.AddRange(run.Pieces.Select(p => float.IsNaN(p.SlowestMps) ? string.Empty : (p.SlowestMps * 3.6f).ToString("0.0", Inv)));
        return cells;
    }

    /// <summary>Cells as one CSV line: a cell with a comma, a quote or a line end is quoted, its quotes doubled.</summary>
    public static string Line(IEnumerable<string> cells) =>
        string.Join(",", cells.Select(c => c.IndexOfAny([',', '"', '\n', '\r']) < 0 ? c : $"\"{c.Replace("\"", "\"\"", StringComparison.Ordinal)}\""));

    /// <summary>A CSV line's cells, quotes undone: what <see cref="Line"/> wrote.</summary>
    public static IReadOnlyList<string> Fields(string line)
    {
        var fields = new List<string>();
        var cell = new StringBuilder();
        bool quoted = false;
        for (int i = 0; i < line.Length; i++)
        {
            char c = line[i];
            if (quoted)
            {
                if (c == '"' && i + 1 < line.Length && line[i + 1] == '"')
                {
                    cell.Append('"');
                    i++;
                }
                else if (c == '"')
                {
                    quoted = false;
                }
                else
                {
                    cell.Append(c);
                }
            }
            else if (c == '"')
            {
                quoted = true;
            }
            else if (c == ',')
            {
                fields.Add(cell.ToString());
                cell.Clear();
            }
            else
            {
                cell.Append(c);
            }
        }

        fields.Add(cell.ToString());
        return fields;
    }

    /// <summary>
    /// The file a row with this header goes in: <paramref name="fileName"/> if it's new or its header matches,
    /// else the first of <c>name (2).csv</c>, <c>name (3).csv</c> and on that is.
    /// </summary>
    public static string Target(string folder, string fileName, string header)
    {
        string stem = Path.GetFileNameWithoutExtension(fileName);
        string extension = Path.GetExtension(fileName);
        for (int n = 1; ; n++)
        {
            string path = Path.Combine(folder, n == 1 ? fileName : $"{stem} ({n}){extension}");
            if (!File.Exists(path) || File.ReadLines(path).FirstOrDefault() == header)
            {
                return path;
            }
        }
    }

    /// <summary>
    /// Writes a row under its header, starting the file (with the header) if it's new; says which file.
    /// An <see cref="IOException"/> (a spreadsheet holding the file open, fact 8) is the caller's to keep the row through.
    /// </summary>
    public static string Append(string folder, string fileName, string header, string row)
    {
        Directory.CreateDirectory(folder);
        string path = Target(folder, fileName, header);
        if (File.Exists(path))
        {
            File.AppendAllText(path, row + "\r\n", new UTF8Encoding(false));
        }
        else
        {
            File.WriteAllText(path, header + "\r\n" + row + "\r\n", new UTF8Encoding(true));
        }

        return path;
    }

    /// <summary>
    /// A car against the stock car in words, only the numbers that differ, in the panel's order with its words and
    /// units: <c>Wing: Twin-plane wing (stock: No wing); Mass 974 kg (stock 650); Grip 1.308 (stock 1.1)</c>.
    /// </summary>
    public static string Differences(CarSpec car, CarSpec stock)
    {
        var differences = new List<string>();
        if (car.WingId != stock.WingId)
        {
            differences.Add($"Wing: {car.FittedWing().Name} (stock: {stock.FittedWing().Name})");
        }

        foreach (Tunable t in CarSpec.Tunables)
        {
            float mine = t.Get(car);
            float theirs = t.Get(stock);
            if (mine != theirs)
            {
                (string words, string unit) = Tunable.WordsAndUnit(t.Name);
                differences.Add($"{words} {Number(mine)}{(unit.Length > 0 ? " " + unit : string.Empty)} (stock {Number(theirs)})");
            }
        }

        return differences.Count == 0 ? "the stock car" : string.Join("; ", differences);
    }

    /// <summary>The header of <c>cars.csv</c>: the car's name, fingerprint and wing, then every number in the panel's words.</summary>
    public static IReadOnlyList<string> CarHeader() =>
        new[] { "car", "fingerprint", "wing" }.Concat(CarSpec.Tunables.Select(t => t.Words)).ToArray();

    /// <summary>A car's row in <c>cars.csv</c>: every number, to the last digit it has.</summary>
    public static IReadOnlyList<string> CarCells(string name, CarSpec car) =>
        new[] { name, car.Fingerprint(), car.WingId }.Concat(CarSpec.Tunables.Select(t => t.Get(car).ToString(Inv))).ToArray();

    /// <summary>
    /// Writes a car into <c>cars.csv</c> the first time its fingerprint is seen there; says which file, or null if
    /// it was there already.
    /// </summary>
    public static string? AddCar(string folder, string name, CarSpec car)
    {
        string header = Line(CarHeader());
        string print = car.Fingerprint();
        string path = Target(folder, CarsFile, header);
        if (File.Exists(path) && File.ReadLines(path).Skip(1).Any(l => Fields(l) is { Count: > 1 } f && f[1] == print))
        {
            return null;
        }

        return Append(folder, CarsFile, header, Line(CarCells(name, car)));
    }

    private static string Seconds(double? seconds) => seconds is double s ? s.ToString("0.00", Inv) : string.Empty;

    private static string Number(float value) => value.ToString("0.####", Inv);
}
