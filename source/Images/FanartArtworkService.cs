using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Shoko.Abstractions.Config;
using Shoko.Abstractions.Metadata.Containers;
using Shoko.Abstractions.Metadata.Shoko;
using Shoko.Abstractions.Metadata.Tmdb;
using Shoko.Plugin.Fanart.Api;
using Shoko.Plugin.Fanart.Mapping;

namespace Shoko.Plugin.Fanart.Images;

/// <summary>
/// Fetches artwork for one entry and writes it into Shoko. This is where the
/// lookup path lives: a TMDB show is keyed to Fanart.tv by its TheTVDB ID, and
/// a TMDB movie by its TMDB ID.
/// </summary>
public sealed class FanartArtworkService(
    FanartApiClient apiClient,
    FanartImageService imageService,
    ConfigurationProvider<FanartConfiguration> configurationProvider,
    ILogger<FanartArtworkService> logger
)
{
    /// <summary>
    /// Refreshes the artwork for a TMDB show.
    /// </summary>
    /// <param name="show">The TMDB show.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>
    /// What changed, or <see langword="null"/> when the show could not be
    /// looked up at all: no API key, no TheTVDB ID, or nothing on Fanart.tv.
    /// </returns>
    /// <exception cref="ArgumentNullException">
    /// Thrown when <paramref name="show"/> is <see langword="null"/>.
    /// </exception>
    /// <exception cref="FanartApiException">
    /// Thrown when Fanart.tv answers with an error.
    /// </exception>
    public async Task<FanartArtworkChanges?> RefreshShow(ITmdbShow show, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(show);

        // Fanart.tv's TV API is keyed by TheTVDB ID. TMDB is what supplies that
        // translation, and Shoko already stores it on the show, so there is
        // nothing else to resolve. A show without one has no lookup path here
        // and is left alone rather than guessed at from its title.
        if (show.TvdbShowID is not > 0)
        {
            logger.LogDebug("Skipping TMDB show {TmdbShowID} (\"{Title}\") because it has no TheTVDB ID.", show.ID, show.Title);
            return null;
        }

        var artworkSet = await apiClient.GetShowArtwork(show.TvdbShowID.Value, cancellationToken).ConfigureAwait(false);
        if (artworkSet is null)
        {
            logger.LogDebug("Fanart.tv has no artwork for TheTVDB show {TvdbShowID} (TMDB show {TmdbShowID}).", show.TvdbShowID, show.ID);
            return null;
        }

        return Apply(show, artworkSet, FanartEntityKind.Show, show.ID, show.Title);
    }

    /// <summary>
    /// Refreshes the artwork for a TMDB movie.
    /// </summary>
    /// <param name="movie">The TMDB movie.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>
    /// What changed, or <see langword="null"/> when the movie could not be
    /// looked up at all.
    /// </returns>
    /// <exception cref="ArgumentNullException">
    /// Thrown when <paramref name="movie"/> is <see langword="null"/>.
    /// </exception>
    /// <exception cref="FanartApiException">
    /// Thrown when Fanart.tv answers with an error.
    /// </exception>
    public async Task<FanartArtworkChanges?> RefreshMovie(ITmdbMovie movie, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(movie);

        // The movie endpoint takes a TMDB or an IMDB ID, and Shoko always has
        // the TMDB one for a TMDB movie, so movies need no translation step at
        // all. ImdbMovieID is there as well, and is not needed.
        var artworkSet = await apiClient.GetMovieArtwork(movie.ID, cancellationToken).ConfigureAwait(false);
        if (artworkSet is null)
        {
            logger.LogDebug("Fanart.tv has no artwork for TMDB movie {TmdbMovieID}.", movie.ID);
            return null;
        }

        return Apply(movie, artworkSet, FanartEntityKind.Movie, movie.ID, movie.Title);
    }

    /// <summary>
    /// Refreshes the artwork for every TMDB show and movie a shoko series is
    /// linked to.
    /// </summary>
    /// <param name="series">The shoko series.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>What changed across all of them.</returns>
    /// <exception cref="ArgumentNullException">
    /// Thrown when <paramref name="series"/> is <see langword="null"/>.
    /// </exception>
    public async Task<FanartArtworkChanges> RefreshSeries(IShokoSeries series, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(series);

        var configuration = configurationProvider.Load();
        var total = new FanartArtworkChanges(0, 0, 0, 0);

        // One anime can be linked to several TMDB shows, most often a split
        // cour listed as one show per cour, and each of them carries its own
        // TheTVDB ID.
        foreach (var show in series.TmdbShows)
        {
            cancellationToken.ThrowIfCancellationRequested();
            total = Combine(total, await RefreshShow(show, cancellationToken).ConfigureAwait(false));
        }

        if (configuration.IncludeMovies)
        {
            foreach (var movie in series.TmdbMovies)
            {
                cancellationToken.ThrowIfCancellationRequested();
                total = Combine(total, await RefreshMovie(movie, cancellationToken).ConfigureAwait(false));
            }
        }

        return total;
    }

    private FanartArtworkChanges Apply(
        IWithImages entity,
        FanartArtworkSet artworkSet,
        FanartEntityKind entityKind,
        int entityID,
        string entityTitle
    )
    {
        var configuration = configurationProvider.Load();
        var artwork = FanartArtworkSelector.Select(artworkSet, entityKind, configuration.MaximumImagesPerType);
        var changes = imageService.Apply(entity, artwork);
        if (changes.HasChanges || changes.Failed > 0)
        {
            logger.LogInformation(
                "Fanart.tv artwork for {EntityKind} {EntityID} (\"{Title}\"): {Added} added, {Kept} already there, {Withdrawn} withdrawn, {Failed} failed.",
                entityKind,
                entityID,
                entityTitle,
                changes.Added,
                changes.Kept,
                changes.Withdrawn,
                changes.Failed
            );
        }

        return changes;
    }

    private static FanartArtworkChanges Combine(FanartArtworkChanges total, FanartArtworkChanges? changes)
        => changes is null
            ? total
            : new FanartArtworkChanges(
                total.Added + changes.Added,
                total.Kept + changes.Kept,
                total.Withdrawn + changes.Withdrawn,
                total.Failed + changes.Failed
            );
}
