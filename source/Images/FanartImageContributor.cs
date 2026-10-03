using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Shoko.Abstractions.Metadata;
using Shoko.Abstractions.Metadata.Image;
using Shoko.Abstractions.Metadata.Providers;
using Shoko.Plugin.Fanart.Api;
using Shoko.Plugin.Fanart.Mapping;

namespace Shoko.Plugin.Fanart.Images;

/// <summary>
/// Adds Fanart.tv artwork to other sources' series and movies, as the core's
/// image contributor.
/// </summary>
/// <remarks>
/// <para>
/// The core decides when to ask: whenever it refreshes an entry's images, it
/// queues one job for this contributor, which calls <see cref="GetImages"/>
/// for each entity of an enabled pair. It also decides what becomes of the
/// answer: which images are downloaded, from the admin's image settings for
/// <see cref="FanartSources.FanartTV"/>, and which links to artwork no longer
/// listed are removed.
/// </para>
/// <para>
/// Fanart.tv's TV API is keyed by TheTVDB ID and its movie API by TMDB ID, so
/// a series is looked up by the TheTVDB ID it carries and a movie by its TMDB
/// ID. An entity with no such ID is left alone rather than guessed at from
/// its title.
/// </para>
/// </remarks>
public sealed class FanartImageContributor(FanartApiClient apiClient, ILogger<FanartImageContributor> logger) : IMetadataImageContributor
{
    #region Constants

    /// <summary>
    /// The value TheTVDB's IDs are kept under, whether the TheTVDB plugin is
    /// installed or not: TMDB shows list their TheTVDB ID under it too.
    /// </summary>
    internal const string TvdbSourceValue = "tvdb";

    /// <summary>
    /// How many of the core's jobs for this contributor may run at once. Each
    /// job makes one request per series or movie, and every request waits on
    /// the shared <see cref="FanartRateLimiter"/> anyway, so more would only
    /// queue up behind it.
    /// </summary>
    internal const int MaximumConcurrentJobs = 2;

    #endregion

    #region Fields

    private int _missingKeyReported;

    #endregion

    #region Identity

    /// <inheritdoc/>
    public string Name => "Fanart.tv";

    /// <inheritdoc/>
    public string? Description => """
        Adds posters, backdrops, logos, banners and movie disc art from Fanart.tv, looking up
        series by their TheTVDB ID and movies by their TMDB ID.
    """;

    /// <inheritdoc/>
    public MetadataSource Source => FanartSources.FanartTV;

    /// <inheritdoc/>
    public string? EmbeddedIconResourceName => Plugin.IconResourceName;

    /// <inheritdoc/>
    /// <remarks>
    /// TMDB's series and movies, and TheTVDB's series when a plugin registered
    /// that source. Read once by the core, after every plugin registered its
    /// sources.
    /// </remarks>
    public MetadataEntityScope Scope => GetScope(MetadataSource.TryGet(TvdbSourceValue, out var tvdb) ? tvdb : null);

    /// <inheritdoc/>
    public int? MaxConcurrentJobs => MaximumConcurrentJobs;

    #endregion

    #region Images

    /// <inheritdoc/>
    /// <exception cref="FanartApiException">
    /// Thrown when Fanart.tv answers with an error other than a rejected key,
    /// so the core's job fails and is retried.
    /// </exception>
    public async Task<IReadOnlyList<ImageCandidate>?> GetImages(IMetadata entity, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(entity);

        // No key, no access: Fanart.tv has no anonymous tier. That is the
        // ordinary state of a fresh install, so it is said once per start.
        if (!apiClient.HasApiKey)
        {
            if (Interlocked.Exchange(ref _missingKeyReported, 1) is 0)
                logger.LogInformation("Not adding Fanart.tv artwork because no API key is configured. Add one under the plugin's settings.");
            return null;
        }

        if (!TryGetLookup(entity, out var entityKind, out var lookupID))
        {
            logger.LogDebug("Not adding Fanart.tv artwork to {Entity}, which has no ID Fanart.tv is keyed by.", entity.ID);
            return null;
        }

        FanartArtworkSet? artworkSet;
        try
        {
            artworkSet = entityKind is FanartEntityKind.Movie
                ? await apiClient.GetMovieArtwork(lookupID, cancellationToken).ConfigureAwait(false)
                : await apiClient.GetShowArtwork(lookupID, cancellationToken).ConfigureAwait(false);
        }
        catch (FanartApiException ex) when (ex.IsAuthenticationFailure)
        {
            // Retrying will not fix a rejected key, so the job is not failed
            // over it and the links already there are left alone.
            logger.LogWarning(ex, "Not adding Fanart.tv artwork to {Entity}: the API key was rejected.", entity.ID);
            return null;
        }

        // Fanart.tv answers "not found" for an ID it has no artwork for, which
        // is most of a library. The links already there are left alone.
        if (artworkSet is null)
        {
            logger.LogDebug("Fanart.tv has no artwork for {Entity} ({EntityKind} {LookupID}).", entity.ID, entityKind, lookupID);
            return null;
        }

        return ToCandidates(FanartArtworkSelector.Select(artworkSet, entityKind));
    }

    #endregion

    #region Helpers

    /// <summary>
    /// The pairs the contributor adds images for.
    /// </summary>
    /// <param name="tvdb">
    /// TheTVDB's source, when a plugin registered it, or <see langword="null"/>.
    /// </param>
    /// <returns>The scope.</returns>
    internal static MetadataEntityScope GetScope(MetadataSource? tvdb)
    {
        var pairs = new List<(MetadataSource, MetadataEntityType)>
        {
            (MetadataSource.TMDB, MetadataEntityType.Series),
            (MetadataSource.TMDB, MetadataEntityType.Movie),
        };
        if (tvdb is { IsRegistered: true, IsRemote: true })
            pairs.Add((tvdb, MetadataEntityType.Series));

        return MetadataEntityScope.FromPairs(pairs);
    }

    /// <summary>
    /// Works out which Fanart.tv endpoint and ID an entity is looked up by.
    /// </summary>
    /// <param name="entity">The entity.</param>
    /// <param name="entityKind">The endpoint.</param>
    /// <param name="lookupID">
    /// The TheTVDB ID for a series, or the TMDB ID for a movie.
    /// </param>
    /// <returns>
    /// <see langword="true"/> when the entity is a series or a movie with an
    /// ID Fanart.tv is keyed by.
    /// </returns>
    internal static bool TryGetLookup(IMetadata entity, out FanartEntityKind entityKind, out int lookupID)
    {
        ArgumentNullException.ThrowIfNull(entity);
        switch (entity)
        {
            // A TheTVDB series, or any other series listing its TheTVDB ID,
            // as TMDB's shows do.
            case ISeries series when FindNumericID(series.ID, series.CrossSourceIDs, TvdbSourceValue, MetadataEntityType.Series) is { } tvdbShowID:
                entityKind = FanartEntityKind.Show;
                lookupID = tvdbShowID;
                return true;

            // A TMDB movie, or any other movie listing its TMDB ID, which the
            // movie endpoint takes as it is.
            case IMovie movie when FindNumericID(movie.ID, movie.CrossSourceIDs, MetadataSource.TMDB.Value, MetadataEntityType.Movie) is { } tmdbMovieID:
                entityKind = FanartEntityKind.Movie;
                lookupID = tmdbMovieID;
                return true;

            default:
                entityKind = default;
                lookupID = 0;
                return false;
        }
    }

    /// <summary>
    /// Turns the selected artwork into the candidates the core links, in the
    /// selector's order.
    /// </summary>
    /// <param name="artwork">The artwork.</param>
    /// <returns>The candidates.</returns>
    internal static IReadOnlyList<ImageCandidate> ToCandidates(IReadOnlyList<FanartArtwork> artwork)
        => [
            .. artwork.Select(entry => new ImageCandidate
            {
                ResourceID = entry.ResourceID,
                ImageType = entry.ImageType,
                Width = entry.Width,
                Height = entry.Height,
                LanguageCode = entry.LanguageCode,
            }),
        ];

    /// <summary>
    /// Finds an entity's positive numeric ID on one source, from its own ID
    /// or else from the IDs other sources gave it.
    /// </summary>
    /// <param name="ownID">The entity's own ID.</param>
    /// <param name="crossSourceIDs">The IDs other sources gave it.</param>
    /// <param name="sourceValue">The source's value.</param>
    /// <param name="entityType">The kind the ID must be of.</param>
    /// <returns>The ID, or <see langword="null"/> when it has none.</returns>
    private static int? FindNumericID(MetadataGuid ownID, IReadOnlyList<MetadataGuid>? crossSourceIDs, string sourceValue, MetadataEntityType entityType)
    {
        foreach (var id in (IEnumerable<MetadataGuid>)[ownID, .. crossSourceIDs ?? []])
        {
            if (id.Source.Value == sourceValue && id.EntityType == entityType && id.TryGetNumericID<int>(out var numericID) && numericID > 0)
                return numericID;
        }

        return null;
    }

    #endregion
}
