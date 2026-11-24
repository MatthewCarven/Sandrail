using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Sandrail.Core.Generation;

namespace Sandrail.Core.Tracks;

/// <summary>
/// Eight hex digits naming exactly what's driven on a track (M4-PLAN fact 4), as the car's fingerprint
/// names its numbers: the placements, the pieces' shapes, and on generated ground the ground's heights and
/// the rocks. A ghost names its track by this, so it's never played on a track that has moved under it.
/// </summary>
/// <remarks>
/// <para>
/// <b>Not the ID or the name</b>, which don't change what's driven: a track saved before codes as
/// <c>gen:5</c> and the same track as <c>gen:5v1</c> share their ghosts. <b>Not a piece's palette name</b>
/// either.
/// </para>
/// <para>
/// <b>Heights to the centimetre and rocks to the millimetre</b>, so a last-bit difference in the noise
/// between two machines doesn't count as another track, while anything a car could feel does.
/// </para>
/// </remarks>
public static class TrackFingerprint
{
    private static readonly JsonSerializerOptions PieceOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) },
    };

    /// <param name="track">The track, as its file holds it.</param>
    /// <param name="catalogue">What its pieces are.</param>
    /// <param name="ground">The ground it's driven on, bedded, if it has its own; null on the plain.</param>
    /// <param name="rocks">The rocks off its road, if it has any.</param>
    public static string Of(Track track, PieceCatalogue catalogue, Terrain? ground = null, IReadOnlyList<Rock>? rocks = null)
    {
        ulong hash = Text(0, (track with { Id = string.Empty, Name = string.Empty }).Save());

        // The pieces it uses, as the catalogue says they are: change a piece's shape and every track with it changes.
        foreach (string id in track.Pieces.Select(p => p.Piece).Distinct().Order(StringComparer.Ordinal))
        {
            hash = Text(hash, catalogue.Find(id) is PieceDef def ? JsonSerializer.Serialize(def with { Name = string.Empty }, PieceOptions) : id);
        }

        if (ground is not null)
        {
            hash = Hash64.Mix(hash ^ 0x67726F756E64UL);
            IReadOnlyList<float> heights = ground.Heights;
            if (heights is float[] all)
            {
                foreach (float h in all)
                {
                    hash = Hash64.Mix(hash ^ Quantised(h, 100.0f));
                }
            }
            else
            {
                for (int i = 0; i < heights.Count; i++)
                {
                    hash = Hash64.Mix(hash ^ Quantised(heights[i], 100.0f));
                }
            }
        }

        if (rocks is not null)
        {
            hash = Hash64.Mix(hash ^ 0x726F636B73UL);
            foreach (Rock r in rocks)
            {
                foreach (float f in (ReadOnlySpan<float>)[r.Position.X, r.Position.Y, r.Position.Z, r.Size.X, r.Size.Y, r.Size.Z])
                {
                    hash = Hash64.Mix(hash ^ Quantised(f, 1000.0f));
                }

                hash = Hash64.Mix(hash ^ Quantised(r.YawDeg, 10.0f));
            }
        }

        return (hash >> 32).ToString("x8");
    }

    private static ulong Text(ulong hash, string text)
    {
        foreach (byte b in Encoding.UTF8.GetBytes(text))
        {
            hash = Hash64.Mix(hash ^ b);
        }

        return Hash64.Mix(hash ^ 0xFFUL);
    }

    private static ulong Quantised(float value, float perUnit) => unchecked((ulong)(long)MathF.Round(value * perUnit));
}
