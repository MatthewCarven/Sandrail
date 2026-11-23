using System.Text.Json;
using System.Text.Json.Serialization;

namespace Sandrail.Core.Tracks;

/// <summary>
/// The pieces that exist, read from their files (<c>content/base/pieces/*.json</c>, one piece a file),
/// each refused by name if it's unsound (M2-PLAN, M2a).
/// </summary>
/// <remarks>
/// <b>Data, not code</b>, loaded the way content is (PLAN, "Pieces"): a track names its pieces by ID and
/// this says what they are (M2-PLAN fact 7). The loader reads text, not files, so Core needn't know
/// where Godot keeps them and the tests read the same ones the game does.
/// </remarks>
public sealed class PieceCatalogue
{
    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase, allowIntegerValues: false) },
    };

    private readonly Dictionary<string, PieceDef> _byId;

    private PieceCatalogue(IReadOnlyList<PieceDef> pieces)
    {
        All = pieces;
        _byId = pieces.ToDictionary(p => p.Id);
    }

    /// <summary>Every piece, in the order the files were given.</summary>
    public IReadOnlyList<PieceDef> All { get; }

    /// <summary>The piece with this ID, or null.</summary>
    public PieceDef? Find(string id) => _byId.GetValueOrDefault(id);

    /// <summary>Reads one piece, refusing an unknown field or a piece with any problem.</summary>
    /// <exception cref="InvalidDataException">Not JSON, an unknown field or value, or <see cref="PieceDef.Problems"/>.</exception>
    public static PieceDef ParsePiece(string json)
    {
        PieceDef? piece;
        try
        {
            piece = JsonSerializer.Deserialize<PieceDef>(json, Options);
        }
        catch (JsonException e)
        {
            throw new InvalidDataException($"Not a piece: {e.Message}", e);
        }

        if (piece is null)
        {
            throw new InvalidDataException("Not a piece: the file is JSON null.");
        }

        IReadOnlyList<string> problems = piece.Problems();
        if (problems.Count > 0)
        {
            throw new InvalidDataException($"Piece {piece.Id} refused: " + string.Join("; ", problems));
        }

        return piece;
    }

    /// <summary>Reads every piece, refusing the lot if any one is refused or two share an ID, and saying which.</summary>
    /// <param name="files">Each file's name, for the message, and its text.</param>
    public static PieceCatalogue Parse(IEnumerable<(string Name, string Json)> files)
    {
        var pieces = new List<PieceDef>();
        var problems = new List<string>();
        foreach ((string name, string json) in files)
        {
            try
            {
                pieces.Add(ParsePiece(json));
            }
            catch (InvalidDataException e)
            {
                problems.Add($"{name}: {e.Message}");
            }
        }

        foreach (IGrouping<string, PieceDef> twice in pieces.GroupBy(p => p.Id).Where(g => g.Count() > 1))
        {
            problems.Add($"{twice.Key} is defined {twice.Count()} times");
        }

        if (problems.Count > 0)
        {
            throw new InvalidDataException("The piece catalogue was refused: " + string.Join(" | ", problems));
        }

        return new PieceCatalogue(pieces);
    }
}
