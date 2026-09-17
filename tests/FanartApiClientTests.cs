using System;
using System.Net;
using System.Net.Http;
using System.Threading.Tasks;
using Shoko.Abstractions.Config;
using Shoko.Plugin.Fanart.Api;
using Xunit;

namespace Shoko.Plugin.Fanart.Tests;

/// <summary>
/// How the client talks to Fanart.tv: what it sends, and what it makes of each
/// answer.
/// </summary>
public class FanartApiClientTests
{
    private static FanartApiClient CreateClient(
        HttpMessageHandler handler,
        string? apiKey = "project-key",
        string? personalApiKey = null,
        RecordingLogger<FanartApiClient>? logger = null
    )
    {
        var configurationService = new FakeConfigurationService(new FanartConfiguration()
        {
            ApiKey = apiKey,
            PersonalApiKey = personalApiKey,
        });
        var http = new HttpClient(handler) { BaseAddress = new Uri("https://webservice.fanart.tv/") };

        return new FanartApiClient(http, new FanartRateLimiter(), new ConfigurationProvider<FanartConfiguration>(configurationService), logger ?? new RecordingLogger<FanartApiClient>());
    }

    [Fact]
    public async Task WithoutAnApiKey_NothingIsSent()
    {
        var client = CreateClient(new UnreachableHttpMessageHandler(), apiKey: null);

        Assert.False(client.HasApiKey);
        Assert.Null(await client.GetShowArtwork(76885, TestContext.Current.CancellationToken));
        Assert.Null(await client.GetMovieArtwork(129, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task ShowRequest_UsesTheTvEndpointAndCarriesTheProjectKey()
    {
        var handler = new StubHttpMessageHandler().Enqueue(HttpStatusCode.OK, Fixture.Read("tv-76885.json"));
        var client = CreateClient(handler);

        var set = await client.GetShowArtwork(76885, TestContext.Current.CancellationToken);

        Assert.Equal("https://webservice.fanart.tv/v3.2/tv/76885?api_key=project-key", Assert.Single(handler.Requests));
        Assert.Equal("76885", set?.TvdbShowID);
    }

    [Fact]
    public async Task MovieRequest_UsesTheMovieEndpointWithTheTmdbID()
    {
        var handler = new StubHttpMessageHandler().Enqueue(HttpStatusCode.OK, Fixture.Read("movie-129.json"));
        var client = CreateClient(handler);

        var set = await client.GetMovieArtwork(129, TestContext.Current.CancellationToken);

        Assert.Equal("https://webservice.fanart.tv/v3.2/movies/129?api_key=project-key", Assert.Single(handler.Requests));
        Assert.Equal("129", set?.TmdbMovieID);
    }

    [Fact]
    public async Task PersonalKey_IsSentAlongsideTheProjectKey()
    {
        var handler = new StubHttpMessageHandler().Enqueue(HttpStatusCode.OK, Fixture.Read("tv-76885.json"));
        var client = CreateClient(handler, personalApiKey: "personal key/with+characters");

        await client.GetShowArtwork(76885, TestContext.Current.CancellationToken);

        Assert.Contains("client_key=personal%20key%2Fwith%2Bcharacters", Assert.Single(handler.Requests), StringComparison.Ordinal);
    }

    [Fact]
    public async Task NotFound_IsNotAnError()
    {
        var handler = new StubHttpMessageHandler().Enqueue(HttpStatusCode.NotFound, Fixture.Read("not-found.json"));
        var client = CreateClient(handler);

        Assert.Null(await client.GetShowArtwork(1, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task RateLimited_IsRetriedOnceTheServerSaysSo()
    {
        var handler = new StubHttpMessageHandler()
            .Enqueue(HttpStatusCode.TooManyRequests, "", retryAfter: TimeSpan.Zero)
            .Enqueue(HttpStatusCode.OK, Fixture.Read("tv-76885.json"));
        var client = CreateClient(handler);

        var set = await client.GetShowArtwork(76885, TestContext.Current.CancellationToken);

        Assert.Equal(2, handler.Requests.Count);
        Assert.Equal("Cowboy Bebop", set?.Name);
    }

    [Fact]
    public async Task RateLimited_EventuallyGivesUp()
    {
        var handler = new StubHttpMessageHandler();
        for (var i = 0; i < 4; i++)
            handler.Enqueue(HttpStatusCode.TooManyRequests, "", retryAfter: TimeSpan.Zero);
        var client = CreateClient(handler);

        var exception = await Assert.ThrowsAsync<FanartApiException>(() => client.GetShowArtwork(76885, TestContext.Current.CancellationToken));

        Assert.Equal(HttpStatusCode.TooManyRequests, exception.StatusCode);
        Assert.False(exception.IsAuthenticationFailure);
    }

    [Theory]
    [InlineData(HttpStatusCode.Unauthorized)]
    [InlineData(HttpStatusCode.Forbidden)]
    public async Task ARejectedKey_IsReportedAsSuch(HttpStatusCode statusCode)
    {
        var handler = new StubHttpMessageHandler().Enqueue(statusCode, "");
        var client = CreateClient(handler);

        var exception = await Assert.ThrowsAsync<FanartApiException>(() => client.GetShowArtwork(76885, TestContext.Current.CancellationToken));

        Assert.True(exception.IsAuthenticationFailure);
    }

    [Fact]
    public async Task AnUnparseableBody_IsReportedRatherThanSwallowed()
    {
        var handler = new StubHttpMessageHandler().Enqueue(HttpStatusCode.OK, "<html>not json</html>", contentType: "text/html");
        var client = CreateClient(handler);

        var exception = await Assert.ThrowsAsync<FanartApiException>(() => client.GetShowArtwork(76885, TestContext.Current.CancellationToken));

        Assert.IsType<System.Text.Json.JsonException>(exception.InnerException, exactMatch: false);
    }
}
