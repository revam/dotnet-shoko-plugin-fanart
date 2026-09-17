using System;
using System.Collections.Generic;
using System.Linq;
using Shoko.Abstractions.Metadata.Enums;
using Shoko.Plugin.Fanart.Api;

namespace Shoko.Plugin.Fanart.Mapping;

/// <summary>
/// Turns a Fanart.tv response into the flat, ordered list of artwork this
/// plugin will attach to an entity.
/// </summary>
/// <remarks>
/// Pure and side effect free, so the decisions it makes (what is kept, in which
/// order, and how many) can be tested without a server, an image manager or a
/// network.
/// </remarks>
public static class FanartArtworkSelector
{
    /// <summary>
    /// Selects the artwork to attach from one Fanart.tv response.
    /// </summary>
    /// <param name="artworkSet">The response.</param>
    /// <param name="entityKind">Whether it came from the TV or the movie endpoint.</param>
    /// <param name="maximumPerType">
    /// How many images to keep per Shoko image type, most-liked first.
    /// </param>
    /// <returns>
    /// The artwork to attach, ordered by image type and then by the order it
    /// should appear in, with <see cref="FanartArtwork.Ordering"/> already
    /// assigned per type.
    /// </returns>
    /// <exception cref="ArgumentOutOfRangeException">
    /// Thrown when <paramref name="maximumPerType"/> is not positive.
    /// </exception>
    public static IReadOnlyList<FanartArtwork> Select(FanartArtworkSet artworkSet, FanartEntityKind entityKind, int maximumPerType)
    {
        ArgumentNullException.ThrowIfNull(artworkSet);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maximumPerType);

        var candidates = new List<Candidate>();
        var seen = new HashSet<(ImageEntityType, string)>();
        foreach (var group in artworkSet.GetArtworkGroups())
        {
            if (!FanartArtworkMap.TryGet(entityKind, group.Kind, out var mapping))
                continue;

            foreach (var image in group.Images)
            {
                if (!FanartAssetUrl.TryGetResourceID(image.Url, out var resourceID))
                    continue;

                // The same picture can be listed under two kinds that map to
                // the same image type, and Shoko keys an image by its resource
                // ID, so the second one would be a duplicate row.
                if (!seen.Add((mapping.ImageType, resourceID)))
                    continue;

                candidates.Add(new Candidate(mapping, resourceID, image));
            }
        }

        return candidates
            .GroupBy(candidate => candidate.Mapping.ImageType)
            .OrderBy(group => group.Key)
            .SelectMany(group => group
                .OrderBy(candidate => candidate.Mapping.Priority)
                .ThenByDescending(candidate => candidate.Image.Likes ?? 0)
                // Fanart.tv returns artwork in no documented order, and plenty
                // of images have zero likes, so the resource ID breaks ties to
                // keep a sweep from reshuffling the same artwork every run.
                .ThenBy(candidate => candidate.ResourceID, StringComparer.Ordinal)
                .Take(maximumPerType)
                .Select((candidate, index) => new FanartArtwork(
                    candidate.ResourceID,
                    candidate.Mapping.ImageType,
                    candidate.Mapping.Kind,
                    candidate.Image.LanguageCode,
                    candidate.Image.Width,
                    candidate.Image.Height,
                    index
                ))
            )
            .ToList();
    }

    private sealed record Candidate(FanartArtworkKind Mapping, string ResourceID, FanartImage Image);
}

/// <summary>
/// One piece of artwork, resolved down to what Shoko needs to store it.
/// </summary>
/// <param name="ResourceID">The resource ID, relative to the Fanart.tv template URL.</param>
/// <param name="ImageType">The Shoko image type.</param>
/// <param name="Kind">The Fanart.tv asset kind it came from, for logging.</param>
/// <param name="LanguageCode">
/// The ISO 639-1 code for the text in the image, or <see langword="null"/> when
/// it has none.
/// </param>
/// <param name="Width">The image width in pixels, when the API reported one.</param>
/// <param name="Height">The image height in pixels, when the API reported one.</param>
/// <param name="Ordering">
/// Where the artwork sorts among the artwork of the same type on the same
/// entity.
/// </param>
public sealed record FanartArtwork(
    string ResourceID,
    ImageEntityType ImageType,
    string Kind,
    string? LanguageCode,
    int? Width,
    int? Height,
    int Ordering
);
