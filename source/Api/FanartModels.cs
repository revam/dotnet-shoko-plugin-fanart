using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Shoko.Plugin.Fanart.Api;

/*
    UNVERIFIED RESPONSE SHAPES
    ==========================

    Every shape in this file was modelled from Fanart.tv's own published client
    (https://github.com/fanart-tv/fanart.tv-api, `src/index.d.ts` and its
    README) and NOT from a captured live response, because Fanart.tv requires an
    API key for all access and none was available while this was written. The
    test fixtures next to it are hand-written to match, and are labelled as
    invented rather than captured.

    Everything the plugin needs is therefore kept in this one file, so verifying
    it later is a contained job: get a key, capture a real /tv and /movies
    response, diff it against the fixtures, and fix what differs here.

    What is assumed, and what would break if it is wrong:

      * Numbers arrive as JSON strings ("likes": "3", "width": "1000"). The lax
        converters below accept a JSON number just as happily, so this one is
        already covered in both directions.
      * Artwork is grouped by a top-level key per asset kind, whose value is an
        array of image objects. Anything that is not an array (`image_count`,
        `<kind>_count`, `name`, the identifiers) is ignored, so a new scalar
        field cannot break parsing, and a new asset kind needs no code change
        here, only a mapping entry.
      * `lang` is an ISO 639-1 code, or "00" for artwork with no text in it.
      * Asset URLs live under https://assets.fanart.tv/fanart/. See
        FanartAssetUrl for what happens when one does not.
*/

/// <summary>
/// One Fanart.tv artwork response, for a TV show (<c>/v3.2/tv/{tvdb_id}</c>) or
/// a movie (<c>/v3.2/movies/{tmdb_id}</c>). Both endpoints answer with the same
/// envelope: a name, one or more identifiers, and a top-level key per asset
/// kind holding an array of images.
/// </summary>
public sealed class FanartArtworkSet
{
    /// <summary>
    /// The title Fanart.tv holds for the show or movie. Only ever used for log
    /// lines.
    /// </summary>
    [JsonPropertyName("name")]
    public string? Name { get; set; }

    /// <summary>
    /// Every field that is not <see cref="Name"/>, which is where the artwork
    /// itself and the identifiers arrive. Kept open on purpose: Fanart.tv adds
    /// asset kinds server-side and serves them to existing clients without
    /// notice.
    /// </summary>
    [JsonExtensionData]
    public Dictionary<string, JsonElement> Fields { get; set; } = [];

    /// <summary>
    /// The TheTVDB show ID echoed back by the TV endpoint, as a string.
    /// </summary>
    public string? TvdbShowID => ReadIdentifier("thetvdb_id");

    /// <summary>
    /// The TMDB movie ID echoed back by the movie endpoint, as a string.
    /// </summary>
    public string? TmdbMovieID => ReadIdentifier("tmdb_id");

    /// <summary>
    /// The IMDB movie ID echoed back by the movie endpoint, as a string. Not
    /// used for anything: the movie endpoint is queried by TMDB ID.
    /// </summary>
    public string? ImdbMovieID => ReadIdentifier("imdb_id");

    /// <summary>
    /// Every asset kind in the response, in the order it arrived, with the
    /// images under it. Fields that are not arrays of objects are skipped, as
    /// are images that fail to parse.
    /// </summary>
    /// <returns>
    /// The asset kinds and their images.
    /// </returns>
    public IReadOnlyList<FanartArtworkGroup> GetArtworkGroups()
    {
        var groups = new List<FanartArtworkGroup>();
        foreach (var (kind, value) in Fields)
        {
            if (value.ValueKind is not JsonValueKind.Array)
                continue;

            var images = new List<FanartImage>();
            foreach (var element in value.EnumerateArray())
            {
                if (element.ValueKind is not JsonValueKind.Object)
                    continue;

                FanartImage? image;
                try
                {
                    image = element.Deserialize<FanartImage>(FanartJson.Options);
                }
                catch (JsonException)
                {
                    continue;
                }

                if (image is not null)
                    images.Add(image);
            }

            if (images.Count > 0)
                groups.Add(new FanartArtworkGroup(kind, images));
        }

        return groups;
    }

    private string? ReadIdentifier(string key)
        => Fields.TryGetValue(key, out var value)
            ? value.ValueKind switch
            {
                JsonValueKind.String => value.GetString() is { Length: > 0 } text ? text : null,
                JsonValueKind.Number => value.ToString(),
                _ => null,
            }
            : null;
}

/// <summary>
/// The images Fanart.tv holds under one asset kind, such as <c>hdtvlogo</c> or
/// <c>movieposter</c>.
/// </summary>
/// <param name="Kind">
/// The asset kind, exactly as Fanart.tv spelled it.
/// </param>
/// <param name="Images">
/// The images under that kind.
/// </param>
public sealed record FanartArtworkGroup(string Kind, IReadOnlyList<FanartImage> Images);

/// <summary>
/// One image in a Fanart.tv response.
/// </summary>
public sealed class FanartImage
{
    /// <summary>
    /// Fanart.tv's own ID for the image. Not used as Shoko's resource ID, which
    /// is derived from the URL instead, since that is what a download needs.
    /// </summary>
    [JsonPropertyName("id")]
    [JsonConverter(typeof(LaxStringConverter))]
    public string? ID { get; set; }

    /// <summary>
    /// The full-size asset URL, under https://assets.fanart.tv/fanart/.
    /// </summary>
    [JsonPropertyName("url")]
    [JsonConverter(typeof(LaxStringConverter))]
    public string? Url { get; set; }

    /// <summary>
    /// The language of the text in the image as an ISO 639-1 code, or "00" when
    /// the image has no text in it. See <see cref="LanguageCode"/>.
    /// </summary>
    [JsonPropertyName("lang")]
    [JsonConverter(typeof(LaxStringConverter))]
    public string? Language { get; set; }

    /// <summary>
    /// How many people liked the image. Used to order artwork within its kind,
    /// most-liked first.
    /// </summary>
    [JsonPropertyName("likes")]
    [JsonConverter(typeof(LaxInt32Converter))]
    public int? Likes { get; set; }

    /// <summary>
    /// When the image was uploaded, as Fanart.tv formats it
    /// ("2019-08-16 22:52:46"). Present from API v3.1 onwards, and unused.
    /// </summary>
    [JsonPropertyName("added")]
    [JsonConverter(typeof(LaxStringConverter))]
    public string? Added { get; set; }

    /// <summary>
    /// The width of the full-size image in pixels. Present from API v3.2
    /// onwards.
    /// </summary>
    [JsonPropertyName("width")]
    [JsonConverter(typeof(LaxInt32Converter))]
    public int? Width { get; set; }

    /// <summary>
    /// The height of the full-size image in pixels. Present from API v3.2
    /// onwards.
    /// </summary>
    [JsonPropertyName("height")]
    [JsonConverter(typeof(LaxInt32Converter))]
    public int? Height { get; set; }

    /// <summary>
    /// The season the image belongs to, for season artwork. Numbered by
    /// TheTVDB's seasons, and can also be "all" or "0". Season artwork is out
    /// of scope for this plugin, so this exists to document why those kinds are
    /// dropped rather than to be used.
    /// </summary>
    [JsonPropertyName("season")]
    [JsonConverter(typeof(LaxStringConverter))]
    public string? Season { get; set; }

    /// <summary>
    /// Which disc of a multi-disc release the image is for, on <c>moviedisc</c>
    /// artwork.
    /// </summary>
    [JsonPropertyName("disc")]
    [JsonConverter(typeof(LaxInt32Converter))]
    public int? Disc { get; set; }

    /// <summary>
    /// The disc format ("bluray", "dvd", "3d"), on <c>moviedisc</c> artwork.
    /// </summary>
    [JsonPropertyName("disc_type")]
    [JsonConverter(typeof(LaxStringConverter))]
    public string? DiscType { get; set; }

    /// <summary>
    /// The ISO 639-1 language code for the text in the image, or
    /// <see langword="null"/> when the image has no text in it. Fanart.tv
    /// spells "no language" as "00", and occasionally as an empty string.
    /// </summary>
    public string? LanguageCode
        => Language is { Length: > 0 } language && !string.Equals(language, "00", StringComparison.Ordinal)
            ? language
            : null;
}

/// <summary>
/// Shared serializer options for Fanart.tv responses.
/// </summary>
public static class FanartJson
{
    /// <summary>
    /// The options every Fanart.tv response is read with. Property names are
    /// mapped explicitly, so no naming policy is set; unknown fields are
    /// collected rather than rejected.
    /// </summary>
    public static JsonSerializerOptions Options { get; } = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = false,
    };
}

/// <summary>
/// Reads a value that Fanart.tv sends as a JSON string, but that a future API
/// version might send as a number, into a <see cref="string"/>.
/// </summary>
public sealed class LaxStringConverter : JsonConverter<string?>
{
    /// <inheritdoc/>
    public override string? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        => reader.TokenType switch
        {
            JsonTokenType.String => reader.GetString(),
            JsonTokenType.Number => reader.TryGetInt64(out var number) ? number.ToString(CultureInfo.InvariantCulture) : reader.GetDouble().ToString(CultureInfo.InvariantCulture),
            JsonTokenType.True => "true",
            JsonTokenType.False => "false",
            JsonTokenType.Null => null,
            _ => null,
        };

    /// <inheritdoc/>
    public override void Write(Utf8JsonWriter writer, string? value, JsonSerializerOptions options)
    {
        if (value is null)
            writer.WriteNullValue();
        else
            writer.WriteStringValue(value);
    }
}

/// <summary>
/// Reads a value that Fanart.tv sends as a JSON string holding a number, but
/// that a future API version might send as a number, into an
/// <see cref="int"/>. Anything that is not a whole number reads as
/// <see langword="null"/>.
/// </summary>
public sealed class LaxInt32Converter : JsonConverter<int?>
{
    /// <inheritdoc/>
    public override int? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        => reader.TokenType switch
        {
            JsonTokenType.Number => reader.TryGetInt32(out var number) ? number : null,
            JsonTokenType.String => int.TryParse(reader.GetString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed) ? parsed : null,
            _ => null,
        };

    /// <inheritdoc/>
    public override void Write(Utf8JsonWriter writer, int? value, JsonSerializerOptions options)
    {
        if (value is null)
            writer.WriteNullValue();
        else
            writer.WriteNumberValue(value.Value);
    }
}
