using System;
using System.Collections.Generic;
using Shoko.Abstractions.Metadata;
using Shoko.Abstractions.Metadata.Tmdb;

namespace Shoko.Plugin.Fanart.Mapping;

/// <summary>
/// Works out which entries in the collection Fanart.tv can be asked about, and
/// counts the ones it cannot, so a sweep that looks at very little can say why.
/// </summary>
/// <remarks>
/// Pure and side effect free; the sweep job does the fetching.
/// </remarks>
public static class FanartSweepPlanner
{
    /// <summary>
    /// Plans one sweep.
    /// </summary>
    /// <param name="series">
    /// Every series known to the TMDB provider, as returned by
    /// <c>IMetadataService.GetAllSeriesForProvider</c>.
    /// </param>
    /// <param name="movies">
    /// Every movie known to the TMDB provider, or an empty sequence when movies
    /// are switched off.
    /// </param>
    /// <returns>The plan.</returns>
    /// <exception cref="ArgumentNullException">
    /// Thrown when <paramref name="series"/> or <paramref name="movies"/> is
    /// <see langword="null"/>.
    /// </exception>
    public static FanartSweepPlan Plan(IEnumerable<ISeries> series, IEnumerable<IMovie> movies)
    {
        ArgumentNullException.ThrowIfNull(series);
        ArgumentNullException.ThrowIfNull(movies);

        var shows = new List<ITmdbShow>();
        var totalSeries = 0;
        var notAShow = 0;
        var withoutTvdbShowID = 0;
        foreach (var entry in series)
        {
            totalSeries++;
            if (entry is not ITmdbShow show)
            {
                notAShow++;
                continue;
            }

            // Fanart.tv's TV API is keyed by TheTVDB ID and has no other way in.
            // TMDB carries the translation and Shoko stores it, but only for
            // about three quarters of shows, and the rest are simply out of
            // reach: there is no second lookup path worth adding for them.
            if (show.TvdbShowID is not > 0)
            {
                withoutTvdbShowID++;
                continue;
            }

            shows.Add(show);
        }

        var tmdbMovies = new List<ITmdbMovie>();
        var totalMovies = 0;
        var notATmdbMovie = 0;
        foreach (var entry in movies)
        {
            totalMovies++;
            if (entry is not ITmdbMovie movie)
            {
                notATmdbMovie++;
                continue;
            }

            tmdbMovies.Add(movie);
        }

        return new FanartSweepPlan(shows, tmdbMovies, totalSeries, notAShow, withoutTvdbShowID, totalMovies, notATmdbMovie);
    }
}

/// <summary>
/// What one sweep will look at, and what it left out.
/// </summary>
/// <param name="Shows">The TMDB shows that can be looked up.</param>
/// <param name="Movies">The TMDB movies that can be looked up.</param>
/// <param name="TotalSeries">How many series were considered.</param>
/// <param name="NotAShow">How many of them were not TMDB shows.</param>
/// <param name="WithoutTvdbShowID">How many TMDB shows carry no TheTVDB ID.</param>
/// <param name="TotalMovies">How many movies were considered.</param>
/// <param name="NotATmdbMovie">How many of them were not TMDB movies.</param>
public sealed record FanartSweepPlan(
    IReadOnlyList<ITmdbShow> Shows,
    IReadOnlyList<ITmdbMovie> Movies,
    int TotalSeries,
    int NotAShow,
    int WithoutTvdbShowID,
    int TotalMovies,
    int NotATmdbMovie
)
{
    /// <summary>
    /// How many entries the sweep will fetch artwork for.
    /// </summary>
    public int Count => Shows.Count + Movies.Count;
}
