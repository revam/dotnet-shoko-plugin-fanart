using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Diagnostics.CodeAnalysis;
using Shoko.Abstractions.Metadata.Enums;

namespace Shoko.Plugin.Fanart.Mapping;

/// <summary>
/// Which Fanart.tv asset kind becomes which Shoko image type.
/// </summary>
/// <remarks>
/// <para>
/// Shoko has five image types: <see cref="ImageEntityType.Primary"/>,
/// <see cref="ImageEntityType.Backdrop"/>, <see cref="ImageEntityType.Banner"/>,
/// <see cref="ImageEntityType.Logo"/> and <see cref="ImageEntityType.Disc"/>.
/// Fanart.tv has roughly twice as many asset kinds, so several of them have no
/// honest counterpart and are deliberately dropped rather than forced into the
/// nearest type. <see cref="DroppedShowKinds"/> and
/// <see cref="DroppedMovieKinds"/> say which, and why.
/// </para>
/// <para>
/// Kinds this table has never heard of are ignored. Fanart.tv adds asset kinds
/// server-side without notice, and guessing at one from its name is how artwork
/// ends up in the wrong slot.
/// </para>
/// </remarks>
public static class FanartArtworkMap
{
    /// <summary>
    /// The TV asset kinds this plugin imports, mapped to the Shoko image type
    /// they become.
    /// </summary>
    /// <remarks>
    /// Season artwork (<c>seasonposter</c>, <c>seasonthumb</c>,
    /// <c>seasonbanner</c>) is absent on purpose and is not a candidate for a
    /// later pass either: Fanart.tv numbers its season artwork by TheTVDB
    /// seasons, and a TMDB season with the same number is not necessarily the
    /// same season. Attaching one to the other would be a guess dressed up as
    /// a link.
    /// </remarks>
    public static ImmutableArray<FanartArtworkKind> ShowKinds { get; } =
    [
        // Not in Fanart.tv's own typed client, which lists every other kind
        // here, but it is a real asset kind on the site. UNVERIFIED: if the API
        // never sends it, this entry simply never matches.
        new("tvposter", ImageEntityType.Primary, Priority: 0),
        new("showbackground", ImageEntityType.Backdrop, Priority: 0),
        // A 4K background is the same picture at a higher resolution, so it
        // ranks above the 1080p one within the same type.
        new("show4kbackground", ImageEntityType.Backdrop, Priority: -1),
        new("hdtvlogo", ImageEntityType.Logo, Priority: 0),
        // The SD logo is the older, lower-resolution form of the same thing.
        new("clearlogo", ImageEntityType.Logo, Priority: 1),
        new("tvbanner", ImageEntityType.Banner, Priority: 0),
    ];

    /// <summary>
    /// The movie asset kinds this plugin imports, mapped to the Shoko image
    /// type they become.
    /// </summary>
    public static ImmutableArray<FanartArtworkKind> MovieKinds { get; } =
    [
        new("movieposter", ImageEntityType.Primary, Priority: 0),
        new("moviebackground", ImageEntityType.Backdrop, Priority: 0),
        new("movie4kbackground", ImageEntityType.Backdrop, Priority: -1),
        new("hdmovielogo", ImageEntityType.Logo, Priority: 0),
        new("movielogo", ImageEntityType.Logo, Priority: 1),
        new("moviebanner", ImageEntityType.Banner, Priority: 0),
        // The one kind that only exists for movies, and the only user of
        // Shoko's Disc type.
        new("moviedisc", ImageEntityType.Disc, Priority: 0),
    ];

    /// <summary>
    /// The TV asset kinds that are knowingly left behind, and the reason for
    /// each. Kept as data so the list is a decision on the record rather than
    /// an omission.
    /// </summary>
    public static ImmutableDictionary<string, string> DroppedShowKinds { get; } = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
    {
        ["tvthumb"] = "A 500x281 thumbnail. Shoko has no thumbnail type, and it is too small to pass off as a backdrop.",
        ["hdclearart"] = "Transparent key art with no type of its own in Shoko. It is not a logo, a banner or a backdrop.",
        ["clearart"] = "The older, lower-resolution form of hdclearart, and dropped for the same reason.",
        ["characterart"] = "Transparent art of a single character, with no counterpart in Shoko.",
        ["seasonposter"] = "Season artwork, numbered by TheTVDB seasons, which cannot be safely attached to a TMDB season.",
        ["seasonthumb"] = "Season artwork, numbered by TheTVDB seasons, which cannot be safely attached to a TMDB season.",
        ["seasonbanner"] = "Season artwork, numbered by TheTVDB seasons, which cannot be safely attached to a TMDB season.",
    }.ToImmutableDictionary(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// The movie asset kinds that are knowingly left behind, and the reason for
    /// each.
    /// </summary>
    public static ImmutableDictionary<string, string> DroppedMovieKinds { get; } = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
    {
        ["moviethumb"] = "A 1000x562 thumbnail. Shoko has no thumbnail type, and it is not a backdrop.",
        ["hdmovieclearart"] = "Transparent key art with no type of its own in Shoko.",
        ["movieart"] = "The older, lower-resolution form of hdmovieclearart, and dropped for the same reason.",
    }.ToImmutableDictionary(StringComparer.OrdinalIgnoreCase);

    private static readonly ImmutableDictionary<string, FanartArtworkKind> _showKindsByName = ToLookup(ShowKinds);

    private static readonly ImmutableDictionary<string, FanartArtworkKind> _movieKindsByName = ToLookup(MovieKinds);

    /// <summary>
    /// Looks up the mapping for one asset kind.
    /// </summary>
    /// <param name="entityKind">Whether the artwork came from the TV or the movie endpoint.</param>
    /// <param name="kind">The asset kind, as Fanart.tv spelled it.</param>
    /// <param name="mapping">The mapping, when there is one.</param>
    /// <returns>
    /// <see langword="true"/> when the asset kind maps to a Shoko image type.
    /// </returns>
    public static bool TryGet(FanartEntityKind entityKind, string kind, [NotNullWhen(true)] out FanartArtworkKind? mapping)
    {
        var lookup = entityKind is FanartEntityKind.Movie ? _movieKindsByName : _showKindsByName;
        return lookup.TryGetValue(kind, out mapping);
    }

    private static ImmutableDictionary<string, FanartArtworkKind> ToLookup(ImmutableArray<FanartArtworkKind> kinds)
    {
        var builder = ImmutableDictionary.CreateBuilder<string, FanartArtworkKind>(StringComparer.OrdinalIgnoreCase);
        foreach (var kind in kinds)
            builder[kind.Kind] = kind;
        return builder.ToImmutable();
    }
}

/// <summary>
/// One Fanart.tv asset kind and the Shoko image type it maps to.
/// </summary>
/// <param name="Kind">The asset kind, as Fanart.tv spells it.</param>
/// <param name="ImageType">The Shoko image type it becomes.</param>
/// <param name="Priority">
/// How this kind ranks against the other kinds that map to the same image type,
/// lowest first. It only orders kinds against each other; within one kind,
/// artwork is ordered by how many people liked it.
/// </param>
public sealed record FanartArtworkKind(string Kind, ImageEntityType ImageType, int Priority);

/// <summary>
/// Which of Fanart.tv's two artwork endpoints an entity is looked up through.
/// </summary>
public enum FanartEntityKind
{
    /// <summary>
    /// A TV show, looked up by its TheTVDB ID.
    /// </summary>
    Show = 0,

    /// <summary>
    /// A movie, looked up by its TMDB ID.
    /// </summary>
    Movie = 1,
}
