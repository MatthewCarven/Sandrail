using Sandrail.Core.Cars;
using Sandrail.Core.Driving;
using Sandrail.Core.Generation;
using Sandrail.Core.Tracks;

namespace Sandrail.Core.Tests;

/// <summary>M3g: circuits, walked out and walked home, run in laps (M3-PLAN, M3g's checks).</summary>
public class CircuitTests
{
    private static readonly Lazy<PieceCatalogue> Catalogue = new(() => PieceCatalogue.Parse(
        Directory.GetFiles(Path.Combine(AppContext.BaseDirectory, "content", "base", "pieces"), "*.json")
            .OrderBy(f => f, StringComparer.Ordinal)
            .Select(f => (Path.GetFileName(f), File.ReadAllText(f)))));

    private static readonly GenSettings AllCircuits = new() { Route = new RouteSettings { CircuitShare = 1.0f } };

    /// <summary>Twenty seeds, every one a circuit.</summary>
    private static readonly Lazy<(Generated Made, LaidTrack Laid)[]> Circuits = new(() =>
        Enumerable.Range(1, 20).Select(s =>
        {
            Generated g = TrackGenerator.Generate((ulong)s, AllCircuits, Catalogue.Value);
            return (g, g.Track.Lay(Catalogue.Value));
        }).ToArray());

    /// <summary>Decision 10 and fact 7: half and half by a dial, seeds 1, 3, 5 and 7 sprints and 2, 4, 6 and 8 circuits.</summary>
    [Fact]
    public void TheSeedDecidesTheKindHalfAndHalf()
    {
        var route = new RouteSettings();
        Assert.All(new ulong[] { 1, 3, 5, 7 }, s => Assert.False(TrackGenerator.IsCircuit(s, route), $"seed {s}"));
        Assert.All(new ulong[] { 2, 4, 6, 8 }, s => Assert.True(TrackGenerator.IsCircuit(s, route), $"seed {s}"));

        int circuits = Enumerable.Range(1, 1000).Count(s => TrackGenerator.IsCircuit((ulong)s, route));
        Assert.InRange(circuits, 450, 550);

        Assert.DoesNotContain(Enumerable.Range(1, 200), s => TrackGenerator.IsCircuit((ulong)s, new RouteSettings { CircuitShare = 0.0f }));
        Assert.All(Enumerable.Range(1, 200), s => Assert.True(TrackGenerator.IsCircuit((ulong)s, new RouteSettings { CircuitShare = 1.0f })));
    }

    [Fact]
    public void EveryCircuitLaysCleanAndComesRound()
    {
        foreach ((Generated made, LaidTrack laid) in Circuits.Value)
        {
            Assert.Empty(laid.Problems);
            Assert.True(made.Stats.Circuit);
            Assert.Equal(3, made.Track.Laps);
            Assert.Equal("base:start-finish", laid.Route[0].Def.Id);

            // The last piece leads into the line, at its level: the levels came back round (the model, "Levels").
            WorldConnector last = laid.Route[^1].Out;
            WorldConnector line = laid.Route[0].In;
            Assert.Equal(line.Tile, last.Beyond);
            Assert.Equal(TrackGrid.Opposite(line.Facing), last.Facing);
            Assert.Equal(line.Level, last.Level);
            Assert.DoesNotContain(laid.Route, p => p.Def.Gate is GateKind.Start or GateKind.Finish);
        }
    }

    /// <summary>Decision 9: a lap is 0.8 to 1.5 km.</summary>
    [Fact]
    public void ALapIsPointEightToOnePointFiveKilometres()
    {
        Assert.All(Circuits.Value, c => Assert.InRange(c.Made.Stats.LengthM, 800.0f, 1500.0f));
    }

    /// <summary>The line stands well inside the grid, so the lap has room all round.</summary>
    [Fact]
    public void TheLineStandsWellInsideTheGrid()
    {
        foreach ((_, LaidTrack laid) in Circuits.Value)
        {
            Tile at = laid.Route[0].Placement.At;
            Assert.InRange(at.Column, 8, TrackGrid.Size - 9);
            Assert.InRange(at.Row, 8, TrackGrid.Size - 9);
        }
    }

    [Fact]
    public void TheSameSeedMakesTheSameCircuit()
    {
        Assert.Equal(
            TrackGenerator.Generate(4UL, new GenSettings(), Catalogue.Value).Track.Save(),
            TrackGenerator.Generate(4UL, new GenSettings(), Catalogue.Value).Track.Save());
    }

    /// <summary>Format 3: a circuit says its laps, saves and loads the same, and an older format can't carry laps.</summary>
    [Fact]
    public void ACircuitIsFormatThreeAndSurvivesASaveAndALoad()
    {
        Track track = Circuits.Value[0].Made.Track;
        Assert.Equal(3, track.FormatNeeded);
        string saved = track.Save();
        Assert.Contains("\"laps\": 3", saved);
        Track back = Track.Parse(saved);
        Assert.Equal(saved, back.Save());
        Assert.Empty(back.Lay(Catalogue.Value).Problems);

        Assert.Contains(track with { Format = 2 } is var two ? two.Lay(Catalogue.Value).Problems : [], p => p.Contains("laps came with format 3"));
        Assert.Throws<InvalidDataException>(() => Track.Parse(saved.Replace("\"format\": 3", "\"format\": 4")));
    }

    /// <summary>A sprint can't have a start-finish line, and a circuit can't have a start or a finish gate.</summary>
    [Fact]
    public void GatesBelongToTheirKind()
    {
        Track circuit = Circuits.Value[0].Made.Track;
        Track sprint = circuit with { Laps = 0, Format = 2 };
        Assert.Contains(sprint.Lay(Catalogue.Value).Problems, p => p.Contains("start-finish line, which is for a circuit"));

        TrackPiece[] pieces = circuit.Pieces.ToArray();
        pieces[0] = pieces[0] with { Piece = "base:start" };
        Assert.Contains((circuit with { Pieces = pieces }).Lay(Catalogue.Value).Problems, p => p.Contains("a circuit starts and finishes at its start-finish line"));
    }

    /// <summary>A circuit that doesn't come back round is refused, saying where it stops.</summary>
    [Fact]
    public void ACircuitThatDoesntComeRoundIsRefused()
    {
        Track circuit = Circuits.Value[0].Made.Track;
        Track broken = circuit with { Pieces = circuit.Pieces.Take(circuit.Pieces.Length - 1).ToArray() };
        Assert.Contains(broken.Lay(Catalogue.Value).Problems, p => p.Contains("doesn't come back round to its start-finish line"));
    }

    /// <summary>
    /// The race timer's gates: the line, then each lap's checkpoints and the line again, once a lap; the
    /// racing line goes round.
    /// </summary>
    [Fact]
    public void ACircuitIsRacedInLaps()
    {
        (Generated made, LaidTrack laid) = Circuits.Value[0];
        int perLap = laid.GatesPerLap();
        IReadOnlyList<RaceGate> gates = laid.Gates();
        Assert.Equal(1 + (3 * perLap), gates.Count);
        Assert.Equal(made.Stats.Stunts.Checkpoints + 1, perLap);
        Assert.All(Enumerable.Range(1, 3), lap => Assert.Equal(laid.Route[0].GateFrame, gates[lap * perLap].Frame));
        Assert.Equal(GateKind.Start, gates[0].Kind);
        Assert.Equal(GateKind.Finish, gates[^1].Kind);
        Assert.Equal(1, laid.LapOf(1));
        Assert.Equal(1, laid.LapOf(perLap));
        Assert.Equal(2, laid.LapOf(perLap + 1));
        Assert.Equal(3, laid.LapOf(3 * perLap));

        CarSpec spec = CarSpec.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "content", "base", "cars", "buggy.json")));
        Assert.True(laid.RouteLine(spec).Closed);
        Assert.True(laid.CentreLine(spec).Closed);
    }

    /// <summary>The race timer runs the laps: three times round the line, the finish the third.</summary>
    [Fact]
    public void TheTimerCountsTheLaps()
    {
        (_, LaidTrack laid) = Circuits.Value[0];
        var timer = new RaceTimer(laid.Gates());
        timer.Arm();
        timer.Go(0.0);
        double t = 0.0;
        RaceEvent last = default;
        for (int lap = 0; lap < 3; lap++)
        {
            foreach (RaceGate gate in laid.Gates().Skip(1).Take(laid.GatesPerLap()))
            {
                timer.Moved();
                timer.Update(gate.Frame.Position - (gate.Frame.Forward * 1.0f), t);
                t += 10.0;
                last = timer.Update(gate.Frame.Position + (gate.Frame.Forward * 1.0f), t);
            }
        }

        Assert.Equal(RaceEventKind.Finished, last.Kind);
        Assert.True(timer.Finished);
    }

    [Fact]
    public void UnsoundCircuitSettingsAreRefused()
    {
        Assert.NotEmpty(new RouteSettings { CircuitShare = 1.5f }.Problems());
        Assert.NotEmpty(new RouteSettings { MinLapM = 1400.0f, MaxLapM = 1300.0f }.Problems());
        Assert.NotEmpty(new RouteSettings { Laps = 0 }.Problems());
    }
}
