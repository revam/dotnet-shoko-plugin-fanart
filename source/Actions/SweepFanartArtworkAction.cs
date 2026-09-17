using System.Threading;
using System.Threading.Tasks;
using Shoko.Abstractions.Actions;
using Shoko.Plugin.Fanart.Api;
using Shoko.Plugin.Fanart.Jobs;
using Shoko.QueueProcessor.Abstractions;

namespace Shoko.Plugin.Fanart.Actions;

/// <summary>
/// Runs the Fanart.tv sweep over the whole collection now, rather than waiting
/// for its next scheduled run.
/// </summary>
/// <remarks>
/// The action queues the sweep job rather than doing the work itself, so a
/// manual run and the recurring one are the same job and the queue collapses
/// the two instead of running both.
/// </remarks>
public class SweepFanartArtworkAction(IQueueScheduler scheduler, FanartApiClient apiClient) : IExecutableAction
{
    /// <inheritdoc/>
    public string Name => "Sweep Fanart.tv Artwork";

    /// <inheritdoc/>
    public string? Description => "Queues a pass over every TMDB show and movie in the collection, fetching artwork from Fanart.tv.";

    /// <inheritdoc/>
    public ActionCategory Category => ActionCategory.Images;

    /// <inheritdoc/>
    public ActionPermission Permission => ActionPermission.Admin;

    /// <inheritdoc/>
    public Task<ActionValidationResult?> Validate(CancellationToken token = default)
        => Task.FromResult<ActionValidationResult?>(apiClient.HasApiKey
            ? null
            : new ActionValidationResult("No Fanart.tv API key is configured."));

    /// <inheritdoc/>
    public Task Execute(CancellationToken token = default)
        => scheduler.Enqueue<FanartSweepJob>(prioritize: true, ct: token);
}
