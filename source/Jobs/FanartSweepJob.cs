using System;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Shoko.Abstractions.Config;
using Shoko.Abstractions.Metadata.Services;
using Shoko.Plugin.Fanart.Api;
using Shoko.Plugin.Fanart.Images;
using Shoko.Plugin.Fanart.Mapping;
using Shoko.QueueProcessor.Abstractions;
using Shoko.QueueProcessor.Acquisition.Attributes;
using Shoko.QueueProcessor.Concurrency;

namespace Shoko.Plugin.Fanart.Jobs;

/// <summary>
/// The plugin's recurring sweep. Nothing in core asks an image contributor to
/// refresh anything, so this plugin owns its own cadence: the sweep walks every
/// TMDB show that carries a TheTVDB ID, and every TMDB movie, and refreshes the
/// artwork for each in turn.
/// </summary>
/// <remarks>
/// One instance runs at a time, and throughput is bounded by
/// <see cref="FanartRateLimiter"/> inside the shared <see cref="FanartApiClient"/>
/// rather than by this job, so a large collection takes longer per sweep
/// instead of arriving at Fanart.tv all at once.
/// </remarks>
[DatabaseRequired]
[NetworkRequired]
[DisallowConcurrentExecution]
public class FanartSweepJob(
    IMetadataService metadataService,
    FanartArtworkService artworkService,
    FanartApiClient apiClient,
    ConfigurationProvider<FanartConfiguration> configurationProvider,
    ILogger<FanartSweepJob> logger
) : IQueueJob
{
    /// <inheritdoc/>
    public string TypeName => "Fanart.tv Artwork Sweep";

    /// <inheritdoc/>
    public string Title => "Sweeping Fanart.tv for artwork...";

    /// <inheritdoc/>
    public async Task Process()
    {
        // No key, no access: Fanart.tv has no anonymous tier. This is a
        // perfectly ordinary state for a freshly installed plugin, so it is
        // said once per sweep at Information and nothing else happens.
        if (!apiClient.HasApiKey)
        {
            logger.LogInformation("Skipping the Fanart.tv sweep because no API key is configured. Add one under the plugin's settings.");
            return;
        }

        var configuration = configurationProvider.Load();
        var plan = FanartSweepPlanner.Plan(
            metadataService.GetAllSeriesForProvider(IMetadataService.ProviderName.TMDB),
            configuration.IncludeMovies ? metadataService.GetAllMoviesForProvider(IMetadataService.ProviderName.TMDB) : []
        );

        // A sweep that looks at very little has the same shape as a broken
        // sweep, so say what was left out and why, not only what is left.
        logger.LogInformation(
            "Sweeping {ShowCount} of {TotalSeries} TMDB series and {MovieCount} of {TotalMovies} TMDB movies for Fanart.tv artwork; left out {NotAShow} non-show series, {WithoutTvdbShowID} show(s) without a TheTVDB ID and {NotATmdbMovie} non-TMDB movie(s).",
            plan.Shows.Count,
            plan.TotalSeries,
            plan.Movies.Count,
            plan.TotalMovies,
            plan.NotAShow,
            plan.WithoutTvdbShowID,
            plan.NotATmdbMovie
        );

        if (plan.Count is 0)
            return;

        var added = 0;
        var withdrawn = 0;
        var failed = 0;
        foreach (var show in plan.Shows)
        {
            var (outcome, changes) = await Refresh(() => artworkService.RefreshShow(show), "TMDB show", show.ID).ConfigureAwait(false);
            if (outcome is RefreshOutcome.Aborted)
                return;
            if (outcome is RefreshOutcome.Failed)
                failed++;
            added += changes.Added;
            withdrawn += changes.Withdrawn;
        }

        foreach (var movie in plan.Movies)
        {
            var (outcome, changes) = await Refresh(() => artworkService.RefreshMovie(movie), "TMDB movie", movie.ID).ConfigureAwait(false);
            if (outcome is RefreshOutcome.Aborted)
                return;
            if (outcome is RefreshOutcome.Failed)
                failed++;
            added += changes.Added;
            withdrawn += changes.Withdrawn;
        }

        logger.LogInformation(
            "Fanart.tv sweep complete over {Count} entries: {Added} artwork added, {Withdrawn} withdrawn, {Failed} lookup(s) failed.",
            plan.Count,
            added,
            withdrawn,
            failed
        );
    }

    private async Task<(RefreshOutcome Outcome, FanartArtworkChanges Changes)> Refresh(
        Func<Task<FanartArtworkChanges?>> refresh,
        string entityKind,
        int entityID
    )
    {
        var nothing = new FanartArtworkChanges(0, 0, 0, 0);
        try
        {
            return (RefreshOutcome.Done, await refresh().ConfigureAwait(false) ?? nothing);
        }
        catch (FanartApiException ex) when (ex.IsAuthenticationFailure)
        {
            // A rejected key rejects every remaining request too, so stopping
            // here saves thousands of pointless calls.
            logger.LogError(ex, "Stopping the Fanart.tv sweep: the API key was rejected.");
            return (RefreshOutcome.Aborted, nothing);
        }
        catch (FanartApiException ex)
        {
            logger.LogWarning(ex, "Fanart.tv lookup failed for {EntityKind} {EntityID}.", entityKind, entityID);
            return (RefreshOutcome.Failed, nothing);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(ex, "Failed to refresh Fanart.tv artwork for {EntityKind} {EntityID}.", entityKind, entityID);
            return (RefreshOutcome.Failed, nothing);
        }
    }

    private enum RefreshOutcome
    {
        Done = 0,
        Failed = 1,
        Aborted = 2,
    }
}
