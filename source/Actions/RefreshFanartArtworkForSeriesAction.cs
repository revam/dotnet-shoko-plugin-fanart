using System.Threading;
using System.Threading.Tasks;
using Shoko.Abstractions.Actions;
using Shoko.Plugin.Fanart.Api;
using Shoko.Plugin.Fanart.Images;

namespace Shoko.Plugin.Fanart.Actions;

/// <summary>
/// Refreshes the Fanart.tv artwork for one series now, rather than waiting for
/// the next sweep.
/// </summary>
/// <remarks>
/// Actions are resolved fresh from DI per execution and need no registration of
/// their own, so this is a thin shell over
/// <see cref="FanartArtworkService"/>, which is the singleton holding the HTTP
/// client and the rate limiter.
/// </remarks>
public class RefreshFanartArtworkForSeriesAction(FanartArtworkService artworkService, FanartApiClient apiClient) : SeriesAction
{
    /// <inheritdoc/>
    public override string Name => "Refresh Fanart.tv Artwork";

    /// <inheritdoc/>
    public override string? Description => "Fetches artwork from Fanart.tv for every TMDB show and movie this series is linked to.";

    /// <inheritdoc/>
    public override ActionCategory Category => ActionCategory.Images;

    /// <inheritdoc/>
    public override ActionPermission Permission => ActionPermission.User;

    /// <inheritdoc/>
    public override Task<ActionValidationResult?> Validate(CancellationToken token = default)
        => Task.FromResult<ActionValidationResult?>(apiClient.HasApiKey
            ? null
            : new ActionValidationResult("No Fanart.tv API key is configured."));

    /// <inheritdoc/>
    public override Task Execute(CancellationToken token = default)
        => artworkService.RefreshSeries(Series, token);
}
