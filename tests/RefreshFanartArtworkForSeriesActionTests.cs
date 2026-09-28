using System;
using System.Net.Http;
using System.Threading.Tasks;
using Moq;
using Shoko.Abstractions.Actions;
using Shoko.Abstractions.Config;
using Shoko.Abstractions.Metadata;
using Shoko.Abstractions.Metadata.Services;
using Shoko.Abstractions.Metadata.Storage;
using Shoko.Plugin.Fanart.Actions;
using Shoko.Plugin.Fanart.Api;
using Xunit;

namespace Shoko.Plugin.Fanart.Tests;

/// <summary>
/// The series action that asks the core to run the contributor now.
/// </summary>
public class RefreshFanartArtworkForSeriesActionTests
{
    private static MetadataGuid Tmdb(MetadataEntityType entityType, int id)
        => new(MetadataSource.TMDB, entityType, id.ToString());

    private static RefreshFanartArtworkForSeriesAction CreateAction(string? apiKey)
    {
        var configurationService = new FakeConfigurationService(new FanartConfiguration() { ApiKey = apiKey });
        var client = new FanartApiClient(
            new HttpClient(new Mock<HttpMessageHandler>(MockBehavior.Strict).Object) { BaseAddress = new Uri("https://webservice.fanart.tv/") },
            new FanartRateLimiter(),
            new ConfigurationProvider<FanartConfiguration>(configurationService),
            new RecordingLogger<FanartApiClient>()
        );

        return new(
            Mock.Of<IMetadataCrossReferenceStore>(MockBehavior.Strict),
            Mock.Of<IMetadataImageContributorManager>(MockBehavior.Strict),
            Mock.Of<IMetadataRefreshService>(MockBehavior.Strict),
            client
        );
    }

    [Fact]
    public void IsOpenToEveryUser()
    {
        var action = CreateAction("project-key");

        Assert.Equal(ActionPermission.User, action.Permission);
        Assert.Equal(ActionCategory.Images, action.Category);
    }

    [Fact]
    public async Task WithoutAnApiKey_ItIsRefused()
    {
        var result = await CreateAction(null).Validate(TestContext.Current.CancellationToken);

        Assert.NotNull(result);
    }

    [Fact]
    public void PicksEachCoveredEntryOnce_InLinkOrder()
    {
        var show = Tmdb(MetadataEntityType.Series, 1);
        var otherShow = Tmdb(MetadataEntityType.Series, 2);
        var movie = Tmdb(MetadataEntityType.Movie, 3);

        var covered = RefreshFanartArtworkForSeriesAction.SelectCovered(
            [show, null, otherShow, movie, show],
            entryID => entryID != otherShow
        );

        Assert.Equal([show, movie], covered);
    }
}
