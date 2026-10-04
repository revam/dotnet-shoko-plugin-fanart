using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Shoko.Abstractions.Actions;
using Shoko.Abstractions.Metadata;
using Shoko.Abstractions.Metadata.Services;
using Shoko.Abstractions.Metadata.Storage;
using Shoko.Plugin.Fanart.Api;

namespace Shoko.Plugin.Fanart.Actions;

/// <summary>
/// Asks the core to refresh the images of every series and movie a series is
/// linked to that fanart.tv artwork is added to, so the contributor runs now.
/// </summary>
/// <remarks>
/// The core queues the owner's image job for each entry, unforced so nothing
/// already downloaded is fetched again, and that job queues the contributor.
/// </remarks>
public class RefreshFanartArtworkForSeriesAction(
    IMetadataCrossReferenceStore crossReferences,
    IMetadataImageContributorManager contributorManager,
    IMetadataRefreshService refreshService,
    FanartApiClient apiClient
) : SeriesAction
{
    /// <inheritdoc/>
    public override string Name => "Refresh fanart.tv Artwork";

    /// <inheritdoc/>
    public override string? Description => "Adds artwork from fanart.tv to every linked series and movie it covers.";

    /// <inheritdoc/>
    public override ActionCategory Category => ActionCategory.Images;

    /// <inheritdoc/>
    public override ActionPermission Permission => ActionPermission.User;

    /// <inheritdoc/>
    public override Task<ActionValidationResult?> Validate(CancellationToken token = default)
    {
        if (!apiClient.HasApiKey)
            return Task.FromResult<ActionValidationResult?>(new("No fanart.tv API key is configured."));
        if (GetCoveredEntries().Count is 0)
            return Task.FromResult<ActionValidationResult?>(new("The series is linked to nothing fanart.tv artwork is added to."));

        return Task.FromResult<ActionValidationResult?>(null);
    }

    /// <inheritdoc/>
    public override async Task Execute(CancellationToken token = default)
    {
        foreach (var entryID in GetCoveredEntries())
            await refreshService.DownloadImages(entryID, cancellationToken: token).ConfigureAwait(false);
    }

    /// <summary>
    /// The series and movies the series is linked to that the contributor is
    /// enabled for.
    /// </summary>
    /// <returns>The entries, each once, series links first.</returns>
    private IReadOnlyList<MetadataGuid> GetCoveredEntries()
        => SelectCovered(
            [
                .. crossReferences.GetSeriesLinks(Series.AnidbAnimeID).Select(link => link.ProviderID),
                .. crossReferences.GetMovieLinksForSeries(Series.AnidbAnimeID).Select(link => link.ProviderID),
            ],
            entryID => contributorManager.GetImageContributorsFor(entryID).Any(info => info.Source == FanartSources.FanartTV)
        );

    /// <summary>
    /// Picks the linked entries the contributor covers.
    /// </summary>
    /// <param name="linked">The linked entries, <see langword="null"/> for a link naming none.</param>
    /// <param name="isCovered">Whether the contributor is enabled for an entry.</param>
    /// <returns>The covered entries, each once, in the order given.</returns>
    internal static IReadOnlyList<MetadataGuid> SelectCovered(IEnumerable<MetadataGuid?> linked, Func<MetadataGuid, bool> isCovered)
        => [.. linked.OfType<MetadataGuid>().Distinct().Where(isCovered)];
}
