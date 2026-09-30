using System;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Moq;
using Shoko.Abstractions.Config;
using Shoko.Abstractions.Metadata;
using Shoko.Abstractions.Metadata.Enums;
using Shoko.Abstractions.Metadata.Tmdb;
using Shoko.Plugin.Fanart.Api;
using Shoko.Plugin.Fanart.Images;
using Shoko.Plugin.Fanart.Mapping;
using Xunit;

namespace Shoko.Plugin.Fanart.Tests;

/// <summary>
/// What the contributor tells the core: what it covers, how it finds an entity
/// on Fanart.tv, and what it answers for one.
/// </summary>
public class FanartImageContributorTests
{
    private static readonly MetadataSource _tvdb = MetadataSource.TryGet("tvdb", out var tvdb)
        ? tvdb
        : MetadataSource.Register("TheTVDB", "tvdb");

    private static (FanartImageContributor Contributor, RecordingLogger<FanartImageContributor> Logger) CreateContributor(
        HttpMessageHandler handler,
        string? apiKey = "project-key"
    )
    {
        var configurationService = new FakeConfigurationService(new FanartConfiguration() { ApiKey = apiKey });
        var client = new FanartApiClient(
            new HttpClient(handler) { BaseAddress = new Uri("https://webservice.fanart.tv/") },
            new FanartRateLimiter(),
            new ConfigurationProvider<FanartConfiguration>(configurationService),
            new RecordingLogger<FanartApiClient>()
        );
        var logger = new RecordingLogger<FanartImageContributor>();

        return (new FanartImageContributor(client, logger), logger);
    }

    private static ITmdbShow Show(int tmdbID, int? tvdbShowID)
    {
        var show = new Mock<ITmdbShow>();
        show.SetupGet(s => s.ID).Returns(new MetadataGuid(MetadataSource.TMDB, MetadataEntityType.Series, tmdbID.ToString()));
        show.SetupGet(s => s.TmdbID).Returns(tmdbID);
        show.SetupGet(s => s.TvdbShowID).Returns(tvdbShowID);
        show.SetupGet(s => s.CrossSourceIDs).Returns([]);
        return show.Object;
    }

    private static ISeries Series(MetadataGuid id, params MetadataGuid[] crossSourceIDs)
    {
        var series = new Mock<ISeries>();
        series.SetupGet(s => s.ID).Returns(id);
        series.SetupGet(s => s.CrossSourceIDs).Returns(crossSourceIDs);
        return series.Object;
    }

    private static ITmdbMovie TmdbMovie(int tmdbID)
    {
        var movie = new Mock<ITmdbMovie>();
        movie.SetupGet(m => m.ID).Returns(new MetadataGuid(MetadataSource.TMDB, MetadataEntityType.Movie, tmdbID.ToString()));
        movie.SetupGet(m => m.TmdbID).Returns(tmdbID);
        movie.SetupGet(m => m.CrossSourceIDs).Returns([]);
        return movie.Object;
    }

    private static IMovie Movie(MetadataGuid id, params MetadataGuid[] crossSourceIDs)
    {
        var movie = new Mock<IMovie>();
        movie.SetupGet(m => m.ID).Returns(id);
        movie.SetupGet(m => m.CrossSourceIDs).Returns(crossSourceIDs);
        return movie.Object;
    }

    private static MetadataSource Other => FanartSources.FanartTV;

    #region Registration

    [Fact]
    public void TheSourceIsOneTheCoreAccepts()
    {
        var (contributor, _) = CreateContributor(new UnreachableHttpMessageHandler());

        // The core refuses a contributor whose source is unregistered, local
        // or a core one.
        Assert.Same(FanartSources.FanartTV, contributor.Source);
        Assert.True(contributor.Source.IsRegistered);
        Assert.True(contributor.Source.IsRemote);
    }

    [Fact]
    public void TheJobPoolIsSmall()
    {
        var (contributor, _) = CreateContributor(new UnreachableHttpMessageHandler());

        Assert.Equal(2, contributor.MaxConcurrentJobs);
    }

    [Fact]
    public void WithoutTheTvdbSource_TheScopeIsTmdbSeriesAndMovies()
    {
        var scope = FanartImageContributor.GetScope(null);

        Assert.Equal(2, scope.Count);
        Assert.True(scope.Contains(MetadataSource.TMDB, MetadataEntityType.Series));
        Assert.True(scope.Contains(MetadataSource.TMDB, MetadataEntityType.Movie));
    }

    [Fact]
    public void WithTheTvdbSource_TheScopeAddsItsSeries()
    {
        var scope = FanartImageContributor.GetScope(_tvdb);

        Assert.Equal(3, scope.Count);
        Assert.True(scope.Contains(_tvdb, MetadataEntityType.Series));
        Assert.False(scope.Contains(_tvdb, MetadataEntityType.Movie));
    }

    [Fact]
    public void TheScopeFindsTheTvdbSourceByItsRegisteredName()
    {
        // Only the value is shared with the TheTVDB plugin, never its assembly.
        // Registered first, as the plugin does before the core reads the scope.
        var tvdb = _tvdb;
        var (contributor, _) = CreateContributor(new UnreachableHttpMessageHandler());

        Assert.True(contributor.Scope.Contains(tvdb, MetadataEntityType.Series));
        Assert.True(contributor.Scope.Contains(MetadataSource.TMDB, MetadataEntityType.Series));
        Assert.True(contributor.Scope.Contains(MetadataSource.TMDB, MetadataEntityType.Movie));
    }

    [Fact]
    public void TheScopeNeverHoldsTheContributorsOwnSource()
    {
        // The core drops such a pair, and refuses a contributor left with none.
        var (contributor, _) = CreateContributor(new UnreachableHttpMessageHandler());

        Assert.DoesNotContain(contributor.Source, contributor.Scope.Sources);
    }

    #endregion

    #region Lookup

    [Fact]
    public void ATmdbShow_IsLookedUpByItsTvdbID()
    {
        Assert.True(FanartImageContributor.TryGetLookup(Show(30991, 76885), out var kind, out var id));
        Assert.Equal(FanartEntityKind.Show, kind);
        Assert.Equal(76885, id);
    }

    [Fact]
    public void ATmdbShowWithoutATvdbID_IsLeftAlone()
    {
        Assert.False(FanartImageContributor.TryGetLookup(Show(30991, null), out _, out _));
    }

    [Fact]
    public void ATvdbSeries_IsLookedUpByItsOwnID()
    {
        var series = Series(new MetadataGuid(_tvdb, MetadataEntityType.Series, "76885"));

        Assert.True(FanartImageContributor.TryGetLookup(series, out var kind, out var id));
        Assert.Equal(FanartEntityKind.Show, kind);
        Assert.Equal(76885, id);
    }

    [Fact]
    public void AnySeries_IsLookedUpByTheTvdbIDItLists()
    {
        var series = Series(
            new MetadataGuid(Other, MetadataEntityType.Series, "abc"),
            new MetadataGuid(MetadataSource.TMDB, MetadataEntityType.Series, "30991"),
            new MetadataGuid(_tvdb, MetadataEntityType.Series, "76885")
        );

        Assert.True(FanartImageContributor.TryGetLookup(series, out var kind, out var id));
        Assert.Equal(FanartEntityKind.Show, kind);
        Assert.Equal(76885, id);
    }

    [Fact]
    public void ATvdbIDOfAnotherKind_IsNotUsedForASeries()
    {
        var series = Series(
            new MetadataGuid(Other, MetadataEntityType.Series, "abc"),
            new MetadataGuid(_tvdb, MetadataEntityType.Movie, "76885")
        );

        Assert.False(FanartImageContributor.TryGetLookup(series, out _, out _));
    }

    [Fact]
    public void ATmdbMovie_IsLookedUpByItsTmdbID()
    {
        Assert.True(FanartImageContributor.TryGetLookup(TmdbMovie(129), out var kind, out var id));
        Assert.Equal(FanartEntityKind.Movie, kind);
        Assert.Equal(129, id);
    }

    [Fact]
    public void AnyMovie_IsLookedUpByTheTmdbIDItLists()
    {
        var movie = Movie(
            new MetadataGuid(Other, MetadataEntityType.Movie, "abc"),
            new MetadataGuid(MetadataSource.TMDB, MetadataEntityType.Movie, "129")
        );

        Assert.True(FanartImageContributor.TryGetLookup(movie, out var kind, out var id));
        Assert.Equal(FanartEntityKind.Movie, kind);
        Assert.Equal(129, id);
    }

    [Fact]
    public void AnythingElse_IsLeftAlone()
    {
        var entity = new Mock<IMetadata>();
        entity.SetupGet(e => e.ID).Returns(new MetadataGuid(MetadataSource.TMDB, MetadataEntityType.Episode, "1"));

        Assert.False(FanartImageContributor.TryGetLookup(entity.Object, out _, out _));
    }

    #endregion

    #region Images

    [Fact]
    public async Task AShow_IsAnsweredWithEveryMappedImageInTheSelectorsOrder()
    {
        var handler = new StubHttpMessageHandler().Enqueue(HttpStatusCode.OK, Fixture.Read("tv-76885.json"));
        var (contributor, _) = CreateContributor(handler);

        var candidates = await contributor.GetImages(Show(30991, 76885), TestContext.Current.CancellationToken);

        Assert.Equal("https://webservice.fanart.tv/v3.2/tv/76885?api_key=project-key", Assert.Single(handler.Requests));
        var expected = FanartArtworkSelector.Select(
            System.Text.Json.JsonSerializer.Deserialize<FanartArtworkSet>(Fixture.Read("tv-76885.json"), FanartJson.Options)!,
            FanartEntityKind.Show
        );
        Assert.NotNull(candidates);
        Assert.Equal(expected.Select(entry => (entry.ResourceID, entry.ImageType)), candidates.Select(candidate => (candidate.ResourceID, candidate.ImageType)));
    }

    [Fact]
    public async Task ATvdbSeries_IsAskedForThroughTheTvEndpoint()
    {
        var handler = new StubHttpMessageHandler().Enqueue(HttpStatusCode.OK, Fixture.Read("tv-76885.json"));
        var (contributor, _) = CreateContributor(handler);

        var candidates = await contributor.GetImages(Series(new MetadataGuid(_tvdb, MetadataEntityType.Series, "76885")), TestContext.Current.CancellationToken);

        Assert.Equal("https://webservice.fanart.tv/v3.2/tv/76885?api_key=project-key", Assert.Single(handler.Requests));
        Assert.NotNull(candidates);
        Assert.NotEmpty(candidates);
        Assert.DoesNotContain(candidates, candidate => candidate.ImageType is ImageEntityType.Disc);
    }

    [Fact]
    public async Task AMovie_IsAskedForThroughTheMovieEndpoint()
    {
        var handler = new StubHttpMessageHandler().Enqueue(HttpStatusCode.OK, Fixture.Read("movie-129.json"));
        var (contributor, _) = CreateContributor(handler);

        var candidates = await contributor.GetImages(TmdbMovie(129), TestContext.Current.CancellationToken);

        Assert.Equal("https://webservice.fanart.tv/v3.2/movies/129?api_key=project-key", Assert.Single(handler.Requests));
        Assert.Contains(candidates!, candidate => candidate.ImageType is ImageEntityType.Disc);
    }

    [Fact]
    public void Candidates_CarryWhatTheCoreStores()
    {
        var candidate = Assert.Single(FanartImageContributor.ToCandidates([new("tv/1/hdtvlogo/a.png", ImageEntityType.Logo, "hdtvlogo", "en", 800, 310)]));

        Assert.Equal("tv/1/hdtvlogo/a.png", candidate.ResourceID);
        Assert.Equal(ImageEntityType.Logo, candidate.ImageType);
        Assert.Equal("en", candidate.LanguageCode);
        Assert.Equal(800, candidate.Width);
        Assert.Equal(310, candidate.Height);
        Assert.False(candidate.IsDefault);
    }

    [Fact]
    public async Task WithoutAnApiKey_NothingIsAskedAndTheLinksAreLeftAlone()
    {
        var (contributor, logger) = CreateContributor(new UnreachableHttpMessageHandler(), apiKey: null);

        Assert.Null(await contributor.GetImages(Show(30991, 76885), TestContext.Current.CancellationToken));
        Assert.Null(await contributor.GetImages(TmdbMovie(129), TestContext.Current.CancellationToken));

        // Said once, not once per entity.
        Assert.Equal([LogLevel.Information], logger.Entries);
    }

    [Fact]
    public async Task AnEntityWithoutALookupID_IsNotAskedAbout()
    {
        var (contributor, _) = CreateContributor(new UnreachableHttpMessageHandler());

        Assert.Null(await contributor.GetImages(Show(30991, null), TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task NotFound_LeavesTheLinksAlone()
    {
        var handler = new StubHttpMessageHandler().Enqueue(HttpStatusCode.NotFound, Fixture.Read("not-found.json"));
        var (contributor, _) = CreateContributor(handler);

        Assert.Null(await contributor.GetImages(Show(30991, 76885), TestContext.Current.CancellationToken));
    }

    [Theory]
    [InlineData(HttpStatusCode.Unauthorized)]
    [InlineData(HttpStatusCode.Forbidden)]
    public async Task ARejectedKey_LeavesTheLinksAloneWithoutFailingTheJob(HttpStatusCode statusCode)
    {
        var handler = new StubHttpMessageHandler().Enqueue(statusCode, "");
        var (contributor, logger) = CreateContributor(handler);

        Assert.Null(await contributor.GetImages(Show(30991, 76885), TestContext.Current.CancellationToken));
        Assert.Contains(LogLevel.Warning, logger.Entries);
    }

    [Fact]
    public async Task AnyOtherError_FailsTheJobSoTheCoreRetriesIt()
    {
        var handler = new StubHttpMessageHandler().Enqueue(HttpStatusCode.InternalServerError, "");
        var (contributor, _) = CreateContributor(handler);

        var exception = await Assert.ThrowsAsync<FanartApiException>(() => contributor.GetImages(Show(30991, 76885), TestContext.Current.CancellationToken));

        Assert.Equal(HttpStatusCode.InternalServerError, exception.StatusCode);
    }

    #endregion
}
