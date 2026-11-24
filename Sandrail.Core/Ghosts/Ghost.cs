using System.IO.Compression;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Sandrail.Core.Cars;

namespace Sandrail.Core.Ghosts;

/// <summary>Whose run a ghost is: his, or the autodriver's (a run it had the car for at any point is its, M2d).</summary>
public enum GhostDriver
{
    You,
    Autodriver,
}

/// <summary>
/// What a ghost file says about its run, on its first line, readable by eye (M4-PLAN, "The model"): the
/// track it was driven on by fingerprint, ID and name; who drove it, on which car; its time, splits and laps;
/// when; and how many snapshots follow, at what rate.
/// </summary>
public sealed record GhostHeader
{
    /// <summary>What every ghost file says it is, so a file that's some other JSON is refused as not a ghost.</summary>
    public const string KindName = "sandrail-ghost";

    public int Format { get; init; } = Ghost.CurrentFormat;

    public string Kind { get; init; } = KindName;

    /// <summary>The track's fingerprint (M4a): a ghost is only ever played on the track with this one.</summary>
    public string Track { get; init; } = string.Empty;

    public string TrackId { get; init; } = string.Empty;

    public string TrackName { get; init; } = string.Empty;

    public GhostDriver Driver { get; init; }

    /// <summary>The run's time on the race clock, seconds.</summary>
    public double Seconds { get; init; }

    /// <summary>The clock at each gate passed, the finish last, as the race timer has them.</summary>
    public double[] Splits { get; init; } = [];

    /// <summary>A circuit's laps; 0 for a sprint.</summary>
    public int Laps { get; init; }

    public DateTimeOffset When { get; init; }

    /// <summary>Snapshots a second.</summary>
    public int Rate { get; init; } = Ghost.Rate;

    public int Snapshots { get; init; }

    /// <summary>The car's fingerprint; the whole spec follows the header's other fields.</summary>
    public string Car { get; init; } = string.Empty;

    /// <summary>
    /// The car's name as the garage had it, for the board (PROFILE-PLAN, P4): <c>stock</c>, <c>matthew-2026-11-24</c>,
    /// or with <c>(changed)</c> for numbers changed in F2 and not saved. Left out of the file when there's none, so a
    /// ghost written before P4 reads and writes back the same.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? CarName { get; init; }

    /// <summary>The autodriver's caution for a profile run (P4), 0.5 to 1; left out for any other run.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public float? Caution { get; init; }
}

/// <summary>
/// A run as its snapshots, thirty a second from the race clock's zero, and what it was (M4-PLAN, M4b): what a
/// ghost car plays back beside you, and what a replay shows.
/// </summary>
/// <remarks>
/// <para>
/// <b>The file</b>: one line of JSON, the header with the car's whole spec in it (as runs save theirs beside
/// them, M1b), then the snapshots, column by column, each position and rotation as the change from the one
/// before, deflated. About 30 bytes a snapshot before deflating (fact 3).
/// </para>
/// <para>
/// <b>A file in a newer format is refused by name</b>, as track files are, rather than played wrongly.
/// </para>
/// </remarks>
public sealed class Ghost
{
    /// <summary>The newest ghost format this build reads.</summary>
    public const int CurrentFormat = 1;

    /// <summary>Snapshots a second (fact 2): every 4th tick at 120 ticks a second.</summary>
    public const int Rate = 30;

    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase, allowIntegerValues: false) },
    };

    private readonly Snapshot[] _snapshots;

    /// <param name="header">What the run was; its rate and count are set from the snapshots.</param>
    /// <param name="spec">The car it was driven on.</param>
    /// <param name="snapshots">Thirty a second from the clock's zero; each is quantised as it's kept.</param>
    public Ghost(GhostHeader header, CarSpec spec, IEnumerable<Snapshot> snapshots)
    {
        _snapshots = snapshots.Select(s => s.Quantised()).ToArray();
        if (_snapshots.Length == 0)
        {
            throw new ArgumentException("A ghost needs a snapshot at least.", nameof(snapshots));
        }

        Header = header with { Rate = Rate, Snapshots = _snapshots.Length, Car = spec.Fingerprint() };
        Spec = spec;
    }

    public GhostHeader Header { get; }

    /// <summary>The car it was driven on, as it was at the finish.</summary>
    public CarSpec Spec { get; }

    public IReadOnlyList<Snapshot> Snapshots => _snapshots;

    /// <summary>The time of its last snapshot, seconds: at or just after the finish.</summary>
    public double Duration => (_snapshots.Length - 1) / (double)Rate;

    /// <summary>
    /// The car at a time on the race clock: on a snapshot, that snapshot exactly; between two, part way along
    /// the straight line from one to the other, the rotation slerped; before a cut, the snapshot before held.
    /// Before the start, the first; after the end, the last.
    /// </summary>
    public Snapshot At(double seconds)
    {
        // A time that is a snapshot's, give or take the last bit (ticks ÷ 120 × 30 isn't always exact), is that snapshot's.
        double at = seconds * Rate;
        double nearest = Math.Round(at);
        if (Math.Abs(at - nearest) < 1e-6)
        {
            at = nearest;
        }

        if (!(at > 0.0))
        {
            return _snapshots[0];
        }

        int k = (int)Math.Floor(at);
        if (k >= _snapshots.Length - 1)
        {
            return _snapshots[^1];
        }

        float share = (float)(at - k);
        Snapshot a = _snapshots[k];
        Snapshot b = _snapshots[k + 1];
        return share == 0.0f || b.Cut ? a : Snapshot.Between(a, b, share);
    }

    /// <summary>
    /// How fast the car was going at a time, metres a second, from the snapshots either side; zero across a cut.
    /// It's what turns a ghost's wheels.
    /// </summary>
    public System.Numerics.Vector3 VelocityAt(double seconds)
    {
        int k = (int)Math.Clamp(Math.Floor(seconds * Rate), 0, _snapshots.Length - 2);
        if (_snapshots.Length < 2 || _snapshots[k + 1].Cut)
        {
            return System.Numerics.Vector3.Zero;
        }

        return (_snapshots[k + 1].Position - _snapshots[k].Position) * Rate;
    }

    /// <summary>The ghost as its file.</summary>
    public byte[] Save()
    {
        using var file = new MemoryStream();
        using (var writer = new Utf8JsonWriter(file))
        {
            using JsonDocument header = JsonSerializer.SerializeToDocument(Header, Options);
            using JsonDocument spec = JsonDocument.Parse(Spec.ToJson());
            writer.WriteStartObject();
            foreach (JsonProperty p in header.RootElement.EnumerateObject())
            {
                p.WriteTo(writer);
            }

            writer.WritePropertyName("spec");
            spec.RootElement.WriteTo(writer);
            writer.WriteEndObject();
        }

        file.WriteByte((byte)'\n');
        using (var deflate = new DeflateStream(file, CompressionLevel.Optimal, leaveOpen: true))
        {
            deflate.Write(Pack(_snapshots));
        }

        return file.ToArray();
    }

    /// <summary>Reads a ghost file, refusing what isn't one this build reads, and saying why.</summary>
    /// <exception cref="InvalidDataException">Not a ghost, a newer format, or cut short.</exception>
    public static Ghost Load(byte[] bytes)
    {
        int newline = Array.IndexOf(bytes, (byte)'\n');
        if (newline < 0)
        {
            throw new InvalidDataException("Not a ghost: it has no header line.");
        }

        GhostHeader header;
        CarSpec spec;
        try
        {
            using JsonDocument document = JsonDocument.Parse(bytes.AsMemory(0, newline));
            JsonElement root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object || !root.TryGetProperty("kind", out JsonElement kind) || kind.GetString() != GhostHeader.KindName)
            {
                throw new InvalidDataException("Not a ghost: its header doesn't say it is one.");
            }

            if (!root.TryGetProperty("format", out JsonElement format) || !format.TryGetInt32(out int number) || number < 1)
            {
                throw new InvalidDataException($"Not a ghost: it has no format number (this build reads format {CurrentFormat}).");
            }

            if (number > CurrentFormat)
            {
                throw new InvalidDataException($"The ghost is in format {number}, written by a newer Sandrail; this one reads format {CurrentFormat}.");
            }

            if (!root.TryGetProperty("spec", out JsonElement specElement))
            {
                throw new InvalidDataException("Not a ghost: its header has no car.");
            }

            spec = CarSpec.Parse(specElement.GetRawText());

            // The header's own fields; the spec beside them is the car's, read above.
            header = root.Deserialize<GhostHeader>(Options) ?? throw new InvalidDataException("Not a ghost: its header is null.");
        }
        catch (JsonException e)
        {
            throw new InvalidDataException($"Not a ghost: {e.Message}", e);
        }

        byte[] packed;
        try
        {
            using var deflated = new MemoryStream(bytes, newline + 1, bytes.Length - newline - 1);
            using var inflate = new DeflateStream(deflated, CompressionMode.Decompress);
            using var unpacked = new MemoryStream();
            inflate.CopyTo(unpacked);
            packed = unpacked.ToArray();
        }
        catch (InvalidDataException e)
        {
            throw new InvalidDataException($"The ghost's snapshots are damaged: {e.Message}", e);
        }

        if (header.Rate != Rate)
        {
            throw new InvalidDataException($"The ghost is at {header.Rate} snapshots a second, and this build plays {Rate}.");
        }

        return new Ghost(header, spec, Unpack(packed, header.Snapshots));
    }

    /// <summary>Bytes a snapshot takes before deflating.</summary>
    internal const int PackedBytes = (3 * 4) + (4 * 2) + 2 + (4 * 2) + 3 + 1;

    private static byte[] Pack(Snapshot[] snapshots)
    {
        using var stream = new MemoryStream(snapshots.Length * PackedBytes);
        using (var w = new BinaryWriter(stream, Encoding.UTF8, leaveOpen: true))
        {
            // Column by column, so like sits by like and deflates; position and rotation as changes.
            Column(snapshots, s => Snapshot.ToMillimetres(s.Position.X), w);
            Column(snapshots, s => Snapshot.ToMillimetres(s.Position.Y), w);
            Column(snapshots, s => Snapshot.ToMillimetres(s.Position.Z), w);
            Column16(snapshots, s => Snapshot.ToUnit(s.Rotation.X), w);
            Column16(snapshots, s => Snapshot.ToUnit(s.Rotation.Y), w);
            Column16(snapshots, s => Snapshot.ToUnit(s.Rotation.Z), w);
            Column16(snapshots, s => Snapshot.ToUnit(s.Rotation.W), w);
            foreach (Snapshot s in snapshots)
            {
                w.Write(Snapshot.ToCentidegrees(s.SteerDeg));
            }

            foreach (Func<Snapshot, float> droop in new Func<Snapshot, float>[] { s => s.Droop.X, s => s.Droop.Y, s => s.Droop.Z, s => s.Droop.W })
            {
                foreach (Snapshot s in snapshots)
                {
                    w.Write(Snapshot.ToDroopMm(droop(s)));
                }
            }

            foreach (Func<Snapshot, float> pedal in new Func<Snapshot, float>[] { s => s.Throttle, s => s.Brake, s => s.Handbrake })
            {
                foreach (Snapshot s in snapshots)
                {
                    w.Write(Snapshot.ToByte(pedal(s)));
                }
            }

            foreach (Snapshot s in snapshots)
            {
                w.Write((byte)((s.Cut ? 1 : 0) | (s.Reversing ? 2 : 0)));
            }
        }

        return stream.ToArray();
    }

    private static void Column(Snapshot[] snapshots, Func<Snapshot, int> value, BinaryWriter w)
    {
        int before = 0;
        foreach (Snapshot s in snapshots)
        {
            int v = value(s);
            w.Write(unchecked(v - before));
            before = v;
        }
    }

    private static void Column16(Snapshot[] snapshots, Func<Snapshot, short> value, BinaryWriter w)
    {
        short before = 0;
        foreach (Snapshot s in snapshots)
        {
            short v = value(s);
            w.Write(unchecked((short)(v - before)));
            before = v;
        }
    }

    private static Snapshot[] Unpack(byte[] packed, int count)
    {
        if (count < 1 || packed.Length != count * PackedBytes)
        {
            throw new InvalidDataException($"The ghost's snapshots are cut short: {packed.Length} bytes for {count} of them, which take {count * PackedBytes}.");
        }

        using var r = new BinaryReader(new MemoryStream(packed));
        int[] Ints()
        {
            var v = new int[count];
            int before = 0;
            for (int i = 0; i < count; i++)
            {
                before = unchecked(before + r.ReadInt32());
                v[i] = before;
            }

            return v;
        }

        short[] Shorts()
        {
            var v = new short[count];
            short before = 0;
            for (int i = 0; i < count; i++)
            {
                before = unchecked((short)(before + r.ReadInt16()));
                v[i] = before;
            }

            return v;
        }

        int[] x = Ints(), y = Ints(), z = Ints();
        short[] qx = Shorts(), qy = Shorts(), qz = Shorts(), qw = Shorts();
        short[] steer = Enumerable.Range(0, count).Select(_ => r.ReadInt16()).ToArray();
        ushort[][] droop = Enumerable.Range(0, 4).Select(_ => Enumerable.Range(0, count).Select(_ => r.ReadUInt16()).ToArray()).ToArray();
        byte[][] pedals = Enumerable.Range(0, 3).Select(_ => r.ReadBytes(count)).ToArray();
        byte[] flags = r.ReadBytes(count);

        var snapshots = new Snapshot[count];
        for (int i = 0; i < count; i++)
        {
            snapshots[i] = new Snapshot
            {
                Position = new System.Numerics.Vector3(Snapshot.FromMillimetres(x[i]), Snapshot.FromMillimetres(y[i]), Snapshot.FromMillimetres(z[i])),
                Rotation = new System.Numerics.Quaternion(Snapshot.FromUnit(qx[i]), Snapshot.FromUnit(qy[i]), Snapshot.FromUnit(qz[i]), Snapshot.FromUnit(qw[i])),
                SteerDeg = Snapshot.FromCentidegrees(steer[i]),
                Droop = new System.Numerics.Vector4(
                    Snapshot.FromDroopMm(droop[0][i]), Snapshot.FromDroopMm(droop[1][i]), Snapshot.FromDroopMm(droop[2][i]), Snapshot.FromDroopMm(droop[3][i])),
                Throttle = Snapshot.FromByte(pedals[0][i]),
                Brake = Snapshot.FromByte(pedals[1][i]),
                Handbrake = Snapshot.FromByte(pedals[2][i]),
                Cut = (flags[i] & 1) != 0,
                Reversing = (flags[i] & 2) != 0,
            };
        }

        return snapshots;
    }
}

/// <summary>
/// Makes a ghost of a run as it's driven (M4b): a snapshot every 4th tick from the race clock's zero, the
/// tick it starts on included, until the first one at or after the finish; a car put back marks the next
/// snapshot as a cut. Pure, so the game and the <c>ghost</c> oracle record the same way.
/// </summary>
public sealed class GhostRecorder
{
    private readonly int _every;
    private readonly List<Snapshot> _snapshots = new();
    private int _tick = -1;
    private bool _cut;

    /// <param name="ticksPerSecond">The physics rate, which must be a whole number of snapshots' worth.</param>
    public GhostRecorder(int ticksPerSecond)
    {
        if (ticksPerSecond < Ghost.Rate || ticksPerSecond % Ghost.Rate != 0)
        {
            throw new ArgumentException($"{ticksPerSecond} ticks a second isn't a whole number of {Ghost.Rate}ths.", nameof(ticksPerSecond));
        }

        _every = ticksPerSecond / Ghost.Rate;
    }

    /// <summary>Whether a run is being recorded: from <see cref="Go"/> until the snapshot after the finish.</summary>
    public bool Recording => _tick >= 0 && !Done;

    /// <summary>Whether the run's last snapshot is in, the finish passed.</summary>
    public bool Done { get; private set; }

    public IReadOnlyList<Snapshot> Snapshots => _snapshots;

    /// <summary>The clock started: a new run, from nothing.</summary>
    public void Go()
    {
        _snapshots.Clear();
        _tick = 0;
        _cut = false;
        Done = false;
    }

    /// <summary>The run was abandoned (started again): nothing is recorded until the next <see cref="Go"/>.</summary>
    public void Stop()
    {
        _snapshots.Clear();
        _tick = -1;
        _cut = false;
        Done = false;
    }

    /// <summary>The car has been put back somewhere else: the next snapshot isn't to be reached by a straight line.</summary>
    public void Cut() => _cut = true;

    /// <summary>Once a physics tick from the clock's start, with the car's pose and whether the run has finished.</summary>
    public void Tick(Snapshot pose, bool finished)
    {
        if (!Recording)
        {
            return;
        }

        if (_tick % _every == 0)
        {
            _snapshots.Add((pose with { Cut = _cut && _snapshots.Count > 0 }).Quantised());
            _cut = false;
            Done = finished;
        }

        _tick++;
    }

    /// <summary>The run as a ghost.</summary>
    public Ghost ToGhost(GhostHeader header, CarSpec spec) => new(header, spec, _snapshots);
}
