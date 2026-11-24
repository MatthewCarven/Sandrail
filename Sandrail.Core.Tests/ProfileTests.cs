using System.Globalization;
using Sandrail.Core.Cars;
using Sandrail.Core.Profiles;
using Sandrail.Core.Tracks;

namespace Sandrail.Core.Tests;

/// <summary>The car profile's meter and sheet (PROFILE-PLAN, P1's checks).</summary>
public class ProfileTests
{
    private const int Tps = 120;

    private static readonly Lazy<PieceCatalogue> Catalogue = new(() => PieceCatalogue.Parse(
        Directory.GetFiles(Path.Combine(AppContext.BaseDirectory, "content", "base", "pieces"), "*.json")
            .OrderBy(f => f, StringComparer.Ordinal)
            .Select(f => (Path.GetFileName(f), File.ReadAllText(f)))));

    private static readonly Lazy<LaidTrack> Shakedown = new(() => Track.Load(
        File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "content", "base", "tracks", "shakedown.json")), Catalogue.Value));

    /// <summary>Feeds the meter so many ticks on a piece, at speeds from one to another.</summary>
    private static void Drive(RunMeter meter, int piece, int ticks, float from, float to, int lap = 0, bool off = false, float slide = 0.0f)
    {
        for (int i = 0; i < ticks; i++)
        {
            float speed = from + ((to - from) * i / Math.Max(1, ticks - 1));
            meter.Tick(new MeterTick(piece, lap, speed, off, slide, 0.0f));
        }
    }

    [Fact]
    public void TimeAndTheSlowestSpeedOnEachPieceAreCountedTickByTick()
    {
        var meter = new RunMeter(3, Tps);
        Drive(meter, 0, 120, 20.0f, 10.0f);
        Drive(meter, 1, 240, 15.0f, 15.0f);
        Drive(meter, 2, 60, 25.0f, 30.0f);
        RunSummary run = meter.Summary(true, [3.5]);

        Assert.Equal([1.0, 2.0, 0.5], run.Pieces.Select(p => p.Seconds));
        Assert.Equal([10.0f, 15.0f, 25.0f], run.Pieces.Select(p => p.SlowestMps));
        Assert.Equal(30.0f, run.TopMps);
        Assert.Equal(3.5, run.Seconds);
        Assert.True(run.Clean);
    }

    [Fact]
    public void OffRoadIsTimedPieceByPieceAndASpinIsCountedOnceAsItBegins()
    {
        var meter = new RunMeter(2, Tps);
        Drive(meter, 0, 60, 20.0f, 20.0f);
        Drive(meter, 1, 30, 20.0f, 20.0f, off: true);

        // Into a big slide three times, once after a loop's unread tick: three spins, the worst 50°.
        foreach (float slide in new[] { 10.0f, 35.0f, -40.0f, 20.0f, 31.0f, float.NaN, -50.0f, 5.0f })
        {
            meter.Tick(new MeterTick(1, 0, 20.0f, false, slide, 0.0f));
        }

        RunSummary run = meter.Summary(true, [1.0]);
        Assert.Equal(0.25, run.OffRoadSeconds);
        Assert.Equal([0.0, 0.25], run.OffRoadByPiece);
        Assert.Equal(0.25f, meter.OffRoadOn(1));
        Assert.Equal(3, run.Spins);
        Assert.Equal(50.0f, run.WorstSlideDeg);
        Assert.False(run.Clean);
    }

    [Fact]
    public void CleanNeedsTheFinishLittleOffRoadNoSpinAndNoRespawn()
    {
        RunSummary Run(int offTicks, bool finished = true, bool respawn = false)
        {
            var meter = new RunMeter(1, Tps);
            Drive(meter, 0, 120, 20.0f, 20.0f);
            Drive(meter, 0, offTicks, 20.0f, 20.0f, off: true);
            if (respawn)
            {
                meter.Respawned();
            }

            return meter.Summary(finished, finished ? [2.0] : []);
        }

        Assert.True(Run(59).Clean);
        Assert.False(Run(60).Clean);
        Assert.False(Run(0, finished: false).Clean);
        Assert.Null(Run(0, finished: false).Seconds);
        Assert.False(Run(0, respawn: true).Clean);
        Assert.Equal(1, Run(0, respawn: true).Respawns);
    }

    [Fact]
    public void ACircuitsPiecesAreTakenFromItsFastestLap()
    {
        var meter = new RunMeter(2, Tps);
        Drive(meter, 0, 120, 10.0f, 10.0f, lap: 0);
        Drive(meter, 1, 120, 12.0f, 12.0f, lap: 0);
        Drive(meter, 0, 60, 20.0f, 20.0f, lap: 1);
        Drive(meter, 1, 60, 22.0f, 22.0f, lap: 1);
        Drive(meter, 0, 90, 15.0f, 15.0f, lap: 2);
        Drive(meter, 1, 90, 16.0f, 16.0f, lap: 2);
        RunSummary run = meter.Summary(true, [2.0, 3.0, 4.5], gatesPerLap: 1);

        Assert.Equal([2.0, 1.0, 1.5], run.Laps);
        Assert.Equal(1, run.BestLap);
        Assert.Equal([0.5, 0.5], run.Pieces.Select(p => p.Seconds));
        Assert.Equal([20.0f, 22.0f], run.Pieces.Select(p => p.SlowestMps));
        Assert.Equal(4.5, run.Seconds);
    }

    [Fact]
    public void TheShakedownsSheetHasItsTwoSplitsAndTwoColumnsForEachOfItsPieces()
    {
        LaidTrack track = Shakedown.Value;
        SheetShape shape = SheetShape.Of(track);
        IReadOnlyList<string> header = CarSheet.Header(shape);

        Assert.Equal(track.Route.Count, shape.Pieces.Count);
        Assert.Equal(2, shape.Checkpoints);
        Assert.Equal(0, shape.Laps);
        Assert.StartsWith("01 ", shape.Pieces[0], StringComparison.Ordinal);
        Assert.Equal(10 + 2 + 6 + (2 * track.Route.Count), header.Count);
        Assert.Contains("split 2 s", header);
        Assert.DoesNotContain("best lap s", header);
        Assert.Equal($"{shape.Pieces[^1]} km/h", header[^1]);
        Assert.Equal("Shakedown 8a6323aa.csv", CarSheet.FileName(track.Track.Name, "8a6323aa"));
    }

    [Fact]
    public void ACircuitsSheetHasItsLapsAndTheBestInsteadOfSplits()
    {
        var shape = new SheetShape(["01 start-finish", "02 corner-50"], 0, 3);
        IReadOnlyList<string> header = CarSheet.Header(shape);
        Assert.Contains("lap 3 s", header);
        Assert.Contains("best lap s", header);
        Assert.DoesNotContain(header, h => h.StartsWith("split", StringComparison.Ordinal));
    }

    [Fact]
    public void ARowLinesUpWithItsHeaderWithDecimalPointsInAnyCultureAndAPieceNeverReachedBlank()
    {
        var shape = new SheetShape(["01 straight", "02 corner-30", "03 straight"], 1, 0);
        var meter = new RunMeter(3, Tps);
        Drive(meter, 0, 150, 20.0f, 30.0f);
        Drive(meter, 1, 90, 18.0f, 18.0f);
        RunSummary run = meter.Summary(false, [1.75]);
        var row = new SheetRow(new DateTime(2026, 11, 24, 23, 5, 0), "0.0.1+abc", "auto", "stock", "4f918acd", 0.5f, "the stock car", run);

        CultureInfo was = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = new CultureInfo("de-DE");
            IReadOnlyList<string> header = CarSheet.Header(shape);
            IReadOnlyList<string> cells = CarSheet.Cells(shape, row);
            Assert.Equal(header.Count, cells.Count);
            string Cell(string column) => cells[header.ToList().IndexOf(column)];

            Assert.Equal("2026-11-24 23:05:00", Cell("when"));
            Assert.Equal("0.50", Cell("caution"));
            Assert.Equal("no", Cell("finished"));
            Assert.Equal(string.Empty, Cell("time s"));
            Assert.Equal("1.75", Cell("split 1 s"));
            Assert.Equal("108.0", Cell("top km/h"));
            Assert.Equal("1.25", Cell("01 straight s"));
            Assert.Equal("64.8", Cell("02 corner-30 km/h"));
            Assert.Equal(string.Empty, Cell("03 straight s"));
            Assert.Equal(string.Empty, Cell("03 straight km/h"));
        }
        finally
        {
            CultureInfo.CurrentCulture = was;
        }
    }

    [Fact]
    public void ACellWithACommaOrAQuoteIsQuotedAndReadsBackAsItWas()
    {
        string[] cells = ["plain", "grip 1.3, mass 900", "his \"late\" tune", string.Empty, "end"];
        string line = CarSheet.Line(cells);
        Assert.Equal("plain,\"grip 1.3, mass 900\",\"his \"\"late\"\" tune\",,end", line);
        Assert.Equal(cells, CarSheet.Fields(line));
    }

    [Fact]
    public void AFileStartsWithItsHeaderTakesRowsUnderItAndANewHeaderStartsAFileBeside()
    {
        string folder = Path.Combine(Path.GetTempPath(), "sandrail-profile-" + Guid.NewGuid().ToString("N"));
        try
        {
            string first = CarSheet.Append(folder, "Track 1234abcd.csv", "a,b", "1,2");
            string again = CarSheet.Append(folder, "Track 1234abcd.csv", "a,b", "3,4");
            string other = CarSheet.Append(folder, "Track 1234abcd.csv", "a,b,c", "5,6,7");

            Assert.Equal(first, again);
            Assert.Equal(Path.Combine(folder, "Track 1234abcd (2).csv"), other);
            Assert.Equal(["a,b", "1,2", "3,4"], File.ReadAllLines(first));
            Assert.Equal(["a,b,c", "5,6,7"], File.ReadAllLines(other));

            // The mark that tells a spreadsheet it's UTF-8, once, at the start; Windows line ends.
            byte[] bytes = File.ReadAllBytes(first);
            Assert.Equal(new byte[] { 0xEF, 0xBB, 0xBF }, bytes[..3]);
            Assert.Equal(1, bytes.Count(b => b == 0xEF));
            Assert.Equal("a,b\r\n1,2\r\n3,4\r\n", File.ReadAllText(first));

            // A file whose header has since changed back is found again rather than a third started.
            Assert.Equal(first, CarSheet.Append(folder, "Track 1234abcd.csv", "a,b", "8,9"));
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
        }
    }

    [Fact]
    public void DifferencesFromTheStockCarNameOnlyWhatDiffersInThePanelsWords()
    {
        var stock = new CarSpec();
        CarSpec mine = stock.WithValue(nameof(CarSpec.MassKg), 974.0f).WithValue(nameof(CarSpec.Grip), 1.308f) with { WingId = Wings.TwinPlane.Id };

        Assert.Equal("Wing: Twin-plane wing (stock: No wing); Mass 974 kg (stock 650); Grip 1.308 (stock 1.1)", CarSheet.Differences(mine, stock));
        Assert.Equal("the stock car", CarSheet.Differences(stock, new CarSpec()));
        Assert.Equal("Bump damping (N·s/m)", Tunable.WordsFor(nameof(CarSpec.BumpDampingNsPerM)));
        Assert.Equal(("Mass", "kg"), Tunable.WordsAndUnit(nameof(CarSpec.MassKg)));
    }

    [Fact]
    public void ACarGoesIntoCarsCsvOnceWithEveryNumber()
    {
        string folder = Path.Combine(Path.GetTempPath(), "sandrail-cars-" + Guid.NewGuid().ToString("N"));
        try
        {
            var stock = new CarSpec();
            CarSpec heavier = stock.WithValue(nameof(CarSpec.MassKg), 974.0f);
            Assert.NotNull(CarSheet.AddCar(folder, "stock", stock));
            Assert.Null(CarSheet.AddCar(folder, "stock again", stock));
            Assert.NotNull(CarSheet.AddCar(folder, "heavy, isn't it", heavier));

            string[] lines = File.ReadAllLines(Path.Combine(folder, CarSheet.CarsFile));
            Assert.Equal(3, lines.Length);
            Assert.Equal(3 + CarSpec.Tunables.Count, CarSheet.Fields(lines[0]).Count);
            IReadOnlyList<string> row = CarSheet.Fields(lines[2]);
            Assert.Equal(["heavy, isn't it", heavier.Fingerprint(), Wings.None.Id], row.Take(3));
            Assert.Equal("974", row[3 + CarSpec.Tunables.ToList().FindIndex(t => t.Name == nameof(CarSpec.MassKg))]);
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
        }
    }
}
