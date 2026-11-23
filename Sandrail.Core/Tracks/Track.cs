using System.Globalization;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Sandrail.Core.Tracks;

/// <summary>
/// One placement as a track file writes it: the piece, the tile its first tile goes on, its level, the way
/// it's driven and whether it's mirrored (M2-PLAN, "The model").
/// </summary>
/// <remarks>
/// <b>The file says which way a piece faces, not how many quarter turns it's turned.</b> The two are the
/// same thing (a piece facing east is turned one quarter clockwise from north), and "east" is what a person
/// editing the file by hand can check against the grid.
/// </remarks>
public sealed record TrackPiece
{
    public required string Piece { get; init; }

    /// <summary>[column, row]: column runs east, row runs south, so north is a smaller row.</summary>
    public required int[] At { get; init; }

    public int Level { get; init; }

    /// <summary>The way the piece is driven on the grid; north unless the file says.</summary>
    public Compass Facing { get; init; } = Compass.North;

    public bool Mirrored { get; init; }

    public Placement ToPlacement() => new(Piece, new Tile(At[0], At[1]), Level, (int)Facing, Mirrored);

    public static TrackPiece Of(Placement p) => new()
    {
        Piece = p.Piece,
        At = [p.At.Column, p.At.Row],
        Level = p.Level,
        Facing = TrackGrid.Turned(Compass.North, p.Rotation),
        Mirrored = p.Mirrored,
    };
}

/// <summary>
/// A track as its file holds it (M2-PLAN, "The model" and M2c): a format number, an ID and a name, the
/// grid's size and the placements. <see cref="Lay"/> puts it on the grid against a catalogue, says every
/// way it's unsound, and finds its route.
/// </summary>
/// <remarks>
/// <para>
/// <b>A track names its pieces and the catalogue says what they are</b> (M2-PLAN fact 7), so changing a
/// piece's shape changes every track that uses it, and a name the catalogue doesn't know is refused by
/// name, never dropped.
/// </para>
/// <para>
/// <b>The file is written to be read and edited by hand</b>, as the car's is: one placement a line,
/// unknown fields refused, and <see cref="Save"/> writes the same layout a person would, so a file written
/// by hand in that layout survives a load and a save unchanged, which is what the editor will do to it.
/// </para>
/// </remarks>
public sealed record Track
{
    /// <summary>The format this build reads and writes. A file says which it's written in.</summary>
    public const int CurrentFormat = 1;

    /// <summary>How high a piece may be placed, in levels: 50 m, far above anything the plain needs.</summary>
    public const int MaxLevel = 20;

    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase, allowIntegerValues: false) },
    };

    private static readonly JsonSerializerOptions TextOptions = new() { Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping };

    public int Format { get; init; } = CurrentFormat;

    public string Id { get; init; } = string.Empty;

    public string Name { get; init; } = string.Empty;

    /// <summary>Tiles a side. Only the one grid exists until the editor (M2-PLAN decision 6), and a file says so in case that changes.</summary>
    public int Size { get; init; } = TrackGrid.Size;

    public TrackPiece[] Pieces { get; init; } = [];

    /// <summary>Reads a track file, refusing what isn't one: not JSON, another format, or an unknown field or value.</summary>
    /// <remarks>Whether its pieces fit together is <see cref="Lay"/>'s question, which needs the catalogue.</remarks>
    /// <exception cref="InvalidDataException">Not a track file this build reads.</exception>
    public static Track Parse(string json)
    {
        // The format first, before the fields: a newer file may have fields this build has never heard of,
        // and "written by a newer Sandrail" is the useful thing to say about it.
        try
        {
            using JsonDocument document = JsonDocument.Parse(json, new JsonDocumentOptions
            {
                CommentHandling = JsonCommentHandling.Skip,
                AllowTrailingCommas = true,
            });
            if (document.RootElement.ValueKind != JsonValueKind.Object)
            {
                throw new InvalidDataException("Not a track: the file isn't a JSON object.");
            }

            if (!document.RootElement.TryGetProperty("format", out JsonElement format))
            {
                throw new InvalidDataException($"Not a track: it has no format number (this build reads format {CurrentFormat}).");
            }

            if (format.ValueKind != JsonValueKind.Number || !format.TryGetInt32(out int number) || number != CurrentFormat)
            {
                string said = format.ToString();
                throw new InvalidDataException(format.ValueKind == JsonValueKind.Number && format.TryGetInt32(out int n) && n > CurrentFormat
                    ? $"The track is in format {n}, written by a newer Sandrail; this one reads format {CurrentFormat}."
                    : $"The track is in format {said}, and this build reads format {CurrentFormat}.");
            }
        }
        catch (JsonException e)
        {
            throw new InvalidDataException($"Not a track: {e.Message}", e);
        }

        Track? track;
        try
        {
            track = JsonSerializer.Deserialize<Track>(json, Options);
        }
        catch (JsonException e)
        {
            throw new InvalidDataException($"Not a track: {e.Message}", e);
        }

        return track ?? throw new InvalidDataException("Not a track: the file is JSON null.");
    }

    /// <summary>Reads a track file and lays it, refusing it if it's unsound in any way and saying every way.</summary>
    /// <exception cref="InvalidDataException">Not a track file, or <see cref="LaidTrack.Problems"/>.</exception>
    public static LaidTrack Load(string json, PieceCatalogue catalogue)
    {
        LaidTrack laid = Parse(json).Lay(catalogue);
        if (laid.Problems.Count > 0)
        {
            string name = laid.Track.Id.Length > 0 ? laid.Track.Id : "without an id";
            throw new InvalidDataException($"Track {name} refused: " + string.Join("; ", laid.Problems));
        }

        return laid;
    }

    /// <summary>
    /// The track as a file: two-space indents and one placement a line, a level only when it isn't 0 and
    /// mirrored only when it is, which is how the game's own tracks are written by hand.
    /// </summary>
    public string Save()
    {
        var text = new StringBuilder();
        text.Append("{\n");
        text.Append(CultureInfo.InvariantCulture, $"  \"format\": {Format},\n");
        text.Append(CultureInfo.InvariantCulture, $"  \"id\": {Quote(Id)},\n");
        text.Append(CultureInfo.InvariantCulture, $"  \"name\": {Quote(Name)},\n");
        text.Append(CultureInfo.InvariantCulture, $"  \"size\": {Size},\n");
        if (Pieces.Length == 0)
        {
            text.Append("  \"pieces\": []\n");
        }
        else
        {
            text.Append("  \"pieces\": [\n");
            for (int i = 0; i < Pieces.Length; i++)
            {
                TrackPiece p = Pieces[i];
                text.Append(CultureInfo.InvariantCulture, $"    {{ \"piece\": {Quote(p.Piece)}, \"at\": [{string.Join(", ", p.At)}]");
                if (p.Level != 0)
                {
                    text.Append(CultureInfo.InvariantCulture, $", \"level\": {p.Level}");
                }

                text.Append(CultureInfo.InvariantCulture, $", \"facing\": \"{p.Facing.ToString().ToLowerInvariant()}\"");
                if (p.Mirrored)
                {
                    text.Append(", \"mirrored\": true");
                }

                text.Append(i + 1 < Pieces.Length ? " },\n" : " }\n");
            }

            text.Append("  ]\n");
        }

        text.Append("}\n");
        return text.ToString();
    }

    /// <summary>
    /// Puts the track on the grid against a catalogue: every placement whose piece exists, every way the
    /// track is unsound, in words, and its route from the start to the finish.
    /// </summary>
    public LaidTrack Lay(PieceCatalogue catalogue) => new TrackLayer(this, catalogue).Lay();

    private static string Quote(string text) => JsonSerializer.Serialize(text, TextOptions);
}

/// <summary>
/// A track put on the grid: its pieces, what's wrong with it, and its route, the chain of pieces from the
/// start's way out to the finish's way in, which the racing line, the timer and the autodriver read
/// (M2-PLAN, "The model").
/// </summary>
public sealed class LaidTrack(Track track, IReadOnlyList<PlacedPiece> placed, IReadOnlyList<string> problems, IReadOnlyList<PlacedPiece> route)
{
    public Track Track { get; } = track;

    /// <summary>Every placement whose piece the catalogue has, in the file's order.</summary>
    public IReadOnlyList<PlacedPiece> Placed { get; } = placed;

    /// <summary>Every way the track is unsound, in words. Empty means it can be raced.</summary>
    public IReadOnlyList<string> Problems { get; } = problems;

    /// <summary>The pieces from the start to the finish in the order they're driven; empty unless the route is whole.</summary>
    public IReadOnlyList<PlacedPiece> Route { get; } = route;

    /// <summary>How far the route's road runs, start gate to finish, metres; a jump's gap isn't road.</summary>
    public float LengthM => Route.Sum(p => p.Road().Pieces.Sum(c => c.Length));
}

/// <summary>The validation, written out one rule at a time (M2-PLAN, "The model": a track's refusals).</summary>
internal sealed class TrackLayer(Track track, PieceCatalogue catalogue)
{
    private readonly List<string> _problems = new();
    private readonly List<PlacedPiece> _placed = new();
    private readonly List<int> _index = new();
    private readonly Dictionary<Tile, int> _owner = new();
    private readonly Dictionary<int, int> _next = new();

    public LaidTrack Lay()
    {
        Header();
        Place();
        Cover();
        Connect();
        List<PlacedPiece> route = Route();
        return new LaidTrack(track, _placed, _problems, route);
    }

    /// <summary>How a message names a placement: its index in the file, its piece and its tile.</summary>
    private string Label(int k) => $"pieces[{_index[k]}] ({_placed[k].Def.Id} at {_placed[k].Placement.At})";

    private void Header()
    {
        if (track.Format != Track.CurrentFormat)
        {
            _problems.Add($"format is {track.Format}, and this build reads format {Track.CurrentFormat}");
        }

        int colon = track.Id.IndexOf(':', StringComparison.Ordinal);
        if (colon <= 0 || colon == track.Id.Length - 1)
        {
            _problems.Add($"id is \"{track.Id}\", and must be namespaced, like base:shakedown");
        }

        if (string.IsNullOrWhiteSpace(track.Name))
        {
            _problems.Add("name is empty");
        }

        if (track.Size != TrackGrid.Size)
        {
            _problems.Add($"size is {track.Size}, and the only grid there is is {TrackGrid.Size} tiles a side");
        }

        if (track.Pieces is null || track.Pieces.Length == 0)
        {
            _problems.Add("pieces is empty");
        }
    }

    /// <summary>Each placement against the catalogue: an unknown piece is refused by name.</summary>
    private void Place()
    {
        TrackPiece[] pieces = track.Pieces ?? [];
        for (int i = 0; i < pieces.Length; i++)
        {
            TrackPiece entry = pieces[i];
            if (entry is null || entry.At is null || entry.At.Length != 2)
            {
                _problems.Add($"pieces[{i}]: at must be [column, row]");
                continue;
            }

            if (entry.Level is < 0 or > Track.MaxLevel)
            {
                _problems.Add($"pieces[{i}] ({entry.Piece} at ({entry.At[0]}, {entry.At[1]})): level is {entry.Level}, and must be from 0 (the plain) to {Track.MaxLevel}");
            }

            PieceDef? def = catalogue.Find(entry.Piece);
            if (def is null)
            {
                _problems.Add($"pieces[{i}]: there's no piece called \"{entry.Piece}\"");
                continue;
            }

            _placed.Add(new PlacedPiece(def, entry.ToPlacement()));
            _index.Add(i);
        }
    }

    /// <summary>Every tile covered once and on the grid.</summary>
    private void Cover()
    {
        for (int k = 0; k < _placed.Count; k++)
        {
            Tile? off = null;
            foreach (Tile tile in _placed[k].Tiles)
            {
                if (!TrackGrid.Contains(tile, track.Size))
                {
                    off ??= tile;
                }

                if (_owner.TryGetValue(tile, out int other))
                {
                    _problems.Add($"{Label(other)} and {Label(k)} both cover {tile}");
                }
                else
                {
                    _owner[tile] = k;
                }
            }

            if (off is Tile t)
            {
                _problems.Add($"{Label(k)} covers {t}, off the {track.Size} × {track.Size} grid");
            }
        }
    }

    /// <summary>
    /// Every way out meets a way in, and every way in is met (M2-PLAN fact 6): road to road on the next
    /// tile at the same level, a lip to a landing straight ahead across empty tiles. A mismatch is said once,
    /// from the way out.
    /// </summary>
    private void Connect()
    {
        // Each piece's way in, by its tile and the way into the piece across that edge.
        var entries = new Dictionary<(Tile, Compass), int>();
        for (int k = 0; k < _placed.Count; k++)
        {
            WorldConnector into = _placed[k].In;
            entries.TryAdd((into.Tile, TrackGrid.Opposite(into.Facing)), k);
        }

        // What each way out points at: the next tile for road, the landing's tile for a lip.
        var arrivals = new HashSet<(Tile, Compass)>();
        for (int k = 0; k < _placed.Count; k++)
        {
            WorldConnector o = _placed[k].Out;
            switch (o.Kind)
            {
                case ConnectorKind.Road:
                    arrivals.Add((o.Beyond, o.Facing));
                    MeetRoad(k, o, entries);
                    break;
                case ConnectorKind.Lip:
                    arrivals.Add((o.Landing, o.Facing));
                    MeetLanding(k, o, entries);
                    break;
            }
        }

        for (int m = 0; m < _placed.Count; m++)
        {
            WorldConnector into = _placed[m].In;
            if (into.Kind is ConnectorKind.Closed || arrivals.Contains((into.Tile, TrackGrid.Opposite(into.Facing))))
            {
                continue;
            }

            if (into.Kind == ConnectorKind.Landing)
            {
                _problems.Add($"{Label(m)} is a landing that no jump comes down on");
                continue;
            }

            Tile behind = into.Tile.Step(into.Facing);
            _problems.Add(_owner.TryGetValue(behind, out int b)
                ? $"nothing leads into {Label(m)}: {Label(b)}, behind it, doesn't lead there"
                : $"nothing leads into {Label(m)}: the tile behind it, {behind}, is empty");
        }
    }

    private void MeetRoad(int k, WorldConnector o, Dictionary<(Tile, Compass), int> entries)
    {
        Tile t = o.Beyond;
        if (!_owner.TryGetValue(t, out int m))
        {
            _problems.Add(TrackGrid.Contains(t, track.Size)
                ? $"{Label(k)} leads onto empty ground at {t}"
                : $"{Label(k)} leads off the grid at {t}");
            return;
        }

        if (!entries.TryGetValue((t, o.Facing), out int entered) || entered != m)
        {
            _problems.Add($"{Label(k)} runs into {Label(m)} at {t}, which isn't entered from there");
            return;
        }

        WorldConnector into = _placed[m].In;
        if (into.Level != o.Level)
        {
            _problems.Add($"{Label(k)} leaves at level {o.Level}, and {Label(m)} is entered at level {into.Level}");
        }
        else if (into.Kind == ConnectorKind.Landing)
        {
            _problems.Add($"{Label(k)} runs onto {Label(m)}, a landing, which only a jump comes down on");
        }
        else if (into.Kind == ConnectorKind.Closed)
        {
            _problems.Add($"{Label(k)} runs into the back of {Label(m)}, which nothing leads into");
        }
        else
        {
            _next[k] = m;
        }
    }

    private void MeetLanding(int k, WorldConnector o, Dictionary<(Tile, Compass), int> entries)
    {
        bool clear = true;
        for (int g = 1; g <= o.Gap; g++)
        {
            Tile t = o.Tile.Step(o.Facing, g);
            if (_owner.TryGetValue(t, out int m))
            {
                _problems.Add($"{Label(m)} is in the gap {Label(k)} jumps, at {t}");
                clear = false;
            }
        }

        Tile at = o.Landing;
        if (!_owner.TryGetValue(at, out int landing))
        {
            _problems.Add($"{Label(k)} jumps {o.Gap} tile{(o.Gap == 1 ? string.Empty : "s")} toward {at}, and there's no landing there");
            return;
        }

        WorldConnector into = _placed[landing].In;
        if (!entries.TryGetValue((at, o.Facing), out int entered) || entered != landing || into.Kind != ConnectorKind.Landing)
        {
            _problems.Add($"{Label(k)} jumps toward {at}, and {Label(landing)} there isn't a landing facing it");
        }
        else if (into.Level != o.Level)
        {
            _problems.Add($"{Label(k)} lands at level {o.Level}, and {Label(landing)} is at level {into.Level}");
        }
        else if (clear)
        {
            _next[k] = landing;
        }
    }

    /// <summary>
    /// One start and one finish, and the chain of pieces from one to the other, which must be every piece:
    /// a checkpoint off it could never be passed in order, and anything else off it is road nobody drives.
    /// </summary>
    private List<PlacedPiece> Route()
    {
        List<int> starts = Gates(GateKind.Start);
        List<int> finishes = Gates(GateKind.Finish);
        GateCount(starts, "start gate");
        GateCount(finishes, "finish gate");
        if (starts.Count != 1 || finishes.Count != 1)
        {
            return [];
        }

        var route = new List<int>();
        var seen = new HashSet<int>();
        int at = starts[0];
        while (true)
        {
            route.Add(at);
            seen.Add(at);
            if (_placed[at].Def.Out.Kind == ConnectorKind.Closed)
            {
                break;
            }

            if (!_next.TryGetValue(at, out int next))
            {
                _problems.Add($"there's no route from the start to the finish: it stops after {Label(at)}");
                return [];
            }

            if (seen.Contains(next))
            {
                _problems.Add($"the route from the start comes back round to {Label(next)} and never reaches the finish");
                return [];
            }

            at = next;
        }

        if (at != finishes[0])
        {
            _problems.Add($"the route from the start ends at {Label(at)}, not at the finish");
            return [];
        }

        var off = Enumerable.Range(0, _placed.Count).Where(k => !seen.Contains(k)).ToList();
        foreach (int k in off.Where(k => _placed[k].Def.Gate == GateKind.Checkpoint))
        {
            _problems.Add($"{Label(k)} is a checkpoint off the route, which could never be passed in order");
        }

        var others = off.Where(k => _placed[k].Def.Gate != GateKind.Checkpoint).ToList();
        if (others.Count > 0)
        {
            string listed = string.Join(", ", others.Take(5).Select(Label)) + (others.Count > 5 ? $" and {others.Count - 5} more" : string.Empty);
            _problems.Add($"{others.Count} piece{(others.Count == 1 ? " isn't" : "s aren't")} on the route from the start to the finish: {listed}");
        }

        return _problems.Count == 0 ? route.Select(k => _placed[k]).ToList() : [];
    }

    private List<int> Gates(GateKind gate) => Enumerable.Range(0, _placed.Count).Where(k => _placed[k].Def.Gate == gate).ToList();

    private void GateCount(List<int> gates, string what)
    {
        if (gates.Count == 0)
        {
            _problems.Add($"there's no {what}");
        }
        else if (gates.Count > 1)
        {
            _problems.Add($"there are {gates.Count} {what}s ({string.Join(", ", gates.Select(Label))}), and a track has one");
        }
    }
}
