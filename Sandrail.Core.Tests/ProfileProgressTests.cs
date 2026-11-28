using System.Globalization;
using Sandrail.Core.Profiles;

namespace Sandrail.Core.Tests;

/// <summary>Profile unprofiled cars (2026-11-27): which cars a track's sheets say still want their sweep, and from where.</summary>
public class ProfileProgressTests
{
    private static readonly SheetShape Shape = new(["01 straight", "02 corner-30"], 0, 0);

    private static readonly string Header = CarSheet.Line(CarSheet.Header(Shape));

    /// <summary>A sheet row as the game writes it: a car, the driver, the caution, and a run clean or not.</summary>
    private static string Row(string car, float? caution, bool clean, string driver = "auto", DateTime? when = null)
    {
        var meter = new RunMeter(2, 120);
        for (int i = 0; i < 240; i++)
        {
            meter.Tick(new MeterTick(i < 120 ? 0 : 1, 0, 20.0f, false, 0.0f, i * 0.2f));
        }

        RunSummary run = meter.Summary(clean, clean ? [2.0] : []);
        Assert.Equal(clean, run.Clean);
        var row = new SheetRow(when ?? new DateTime(2026, 11, 27, 9, 0, 0), "Track", "0.0.1+abcdef0", driver, car, car, caution, "the stock car", run);
        return CarSheet.Line(CarSheet.Cells(Shape, row));
    }

    [Fact]
    public void ASweepGoesFromFiftyToAHundredByFive()
    {
        var cautions = new List<float> { ProfileSweep.First };
        while (!ProfileSweep.EndsAfter(cautions[^1], clean: true))
        {
            cautions.Add(ProfileSweep.Next(cautions[^1]));
        }

        Assert.Equal(11, cautions.Count);
        Assert.Equal(1.00f, cautions[^1], 5);
        Assert.True(ProfileSweep.EndsAfter(0.65f, clean: false));
        Assert.False(ProfileSweep.EndsAfter(0.95f, clean: true));
    }

    [Fact]
    public void ACautionReadBackFromTheSheetGoesOnToTheVerySameNumberASweepThatNeverStoppedDrives()
    {
        // The autodriver plans with the caution itself, so a sweep carried on from a sheet must drive the same float.
        float caution = ProfileSweep.First;
        while (!ProfileSweep.EndsAfter(caution, clean: true))
        {
            float written = float.Parse(caution.ToString("0.00", CultureInfo.InvariantCulture), CultureInfo.InvariantCulture);
            float next = ProfileSweep.Next(caution);
            Assert.Equal(BitConverter.SingleToInt32Bits(next), BitConverter.SingleToInt32Bits(ProfileSweep.Next(written)));
            caution = next;
        }
    }

    [Fact]
    public void ASweepIsEndedByARunThatIsntCleanOrACleanOneAtAHundredAndOtherwiseGoesOnAfterItsLastCleanRun()
    {
        var states = new Dictionary<string, SweepState>();
        ProfileProgress.Read(
        [
            Header,
            Row("aaaa0001", 0.50f, true), Row("aaaa0001", 0.55f, true), Row("aaaa0001", 0.60f, false),
            Row("bbbb0002", 0.50f, true), Row("bbbb0002", 0.55f, true),
            Row("cccc0003", 0.95f, true), Row("cccc0003", 1.00f, true),
            Row("dddd0004", null, true, driver: "you"),
            Row("eeee0005", 0.50f, false),
        ], states);

        Assert.Null(states["aaaa0001"].From);
        Assert.Equal(ProfileSweep.Next(ProfileSweep.Next(ProfileSweep.First)), states["bbbb0002"].From);
        Assert.Null(states["cccc0003"].From);
        Assert.False(states.ContainsKey("dddd0004"));
        Assert.Null(states["eeee0005"].From);
        Assert.Equal(ProfileSweep.First, new SweepState(false, null).From);
    }

    [Fact]
    public void AnEndedSweepStaysEndedThoughALaterStoppedSweepOfTheSameCarCameAfterIt()
    {
        var states = new Dictionary<string, SweepState>();
        ProfileProgress.Read([Header, Row("aaaa0001", 0.50f, true), Row("aaaa0001", 0.55f, false), Row("aaaa0001", 0.50f, true)], states);
        Assert.True(states["aaaa0001"].Ended);
        Assert.Equal(0.55f, ProfileSweep.Next(states["aaaa0001"].LastClean!.Value), 5);
    }

    [Fact]
    public void EveryFileOfTheTrackIsReadWhateverItsNameAndRowsStillWaitingCountToo()
    {
        string folder = Path.Combine(Path.GetTempPath(), "sandrail-progress-" + Guid.NewGuid().ToString("N"));
        try
        {
            Directory.CreateDirectory(folder);
            File.WriteAllLines(Path.Combine(folder, "Seed 5 0070ecd8.csv"), [Header, Row("aaaa0001", 0.50f, true)]);
            File.WriteAllLines(Path.Combine(folder, "Seed 5 0070ecd8 (2).csv"), [Header, Row("aaaa0001", 0.55f, true)]);
            File.WriteAllLines(Path.Combine(folder, "Seed 5 0070ecd8 (written while open).csv"), [Header, Row("bbbb0002", 0.50f, false)]);
            File.WriteAllLines(Path.Combine(folder, "Seed 5, code 5v1 0070ecd8.csv"), [Header, Row("cccc0003", 0.50f, true)]);
            File.WriteAllLines(Path.Combine(folder, "Shakedown 8a6323aa.csv"), [Header, Row("aaaa0001", 0.90f, false)]);
            File.WriteAllLines(Path.Combine(folder, "Other 0070ecd80.csv"), [Header, Row("aaaa0001", 0.90f, false)]);
            File.WriteAllLines(Path.Combine(folder, "cars.csv"), ["car,fingerprint,wing"]);

            Assert.Equal(4, ProfileProgress.SheetsFor(folder, "0070ecd8").Count);
            IReadOnlyDictionary<string, SweepState> states = ProfileProgress.Of(folder, "0070ecd8", [(Header, Row("cccc0003", 0.55f, false))]);

            Assert.Equal(ProfileSweep.Next(0.55f), states["aaaa0001"].From);
            Assert.Null(states["bbbb0002"].From);
            Assert.Null(states["cccc0003"].From);
            Assert.Empty(ProfileProgress.Of(Path.Combine(folder, "none"), "0070ecd8"));
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
        }
    }

    [Fact]
    public void SinceATimeOnlyTheRowsWrittenThenOrLaterCount()
    {
        // Every car on every track carried on in a fresh game (2026-11-28) counts only its own rows: a car profiled the
        // day before is driven again.
        var began = new DateTime(2026, 11, 27, 22, 43, 41);
        string[] lines =
        [
            Header,
            Row("aaaa0001", 0.50f, false, when: began.AddDays(-1)),
            Row("bbbb0002", 0.50f, true, when: began.AddDays(-1)), Row("bbbb0002", 0.55f, true, when: began),
            Row("cccc0003", 0.50f, true, when: began.AddSeconds(-1)), Row("cccc0003", 0.55f, false, when: began.AddHours(3)),
        ];

        var all = new Dictionary<string, SweepState>();
        ProfileProgress.Read(lines, all);
        Assert.Null(all["aaaa0001"].From);

        var since = new Dictionary<string, SweepState>();
        ProfileProgress.Read(lines, since, began);
        Assert.False(since.ContainsKey("aaaa0001"));
        Assert.Equal(ProfileSweep.Next(0.55f), since["bbbb0002"].From);
        Assert.True(since["cccc0003"].Ended);
        Assert.Null(since["cccc0003"].LastClean);

        // A sheet without a when can't say which of its rows are recent, so with a time it counts for nothing.
        var undated = new Dictionary<string, SweepState>();
        ProfileProgress.Read(["driver,car fingerprint,caution,clean", "auto,aaaa0001,0.50,no"], undated, began);
        Assert.Empty(undated);
    }

    [Fact]
    public void ASheetWithoutTheColumnsItNeedsIsPassedOver()
    {
        var states = new Dictionary<string, SweepState>();
        ProfileProgress.Read(["when,car,time s", "2026-11-27,aaaa0001,56.7"], states);
        Assert.Empty(states);
    }
}
