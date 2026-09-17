using System.ComponentModel.DataAnnotations;
using System.Linq;
using Shoko.Plugin.Fanart.Api;
using Xunit;

namespace Shoko.Plugin.Fanart.Tests;

/// <summary>
/// The configuration's defaults, and the one attribute that must never appear
/// on it.
/// </summary>
public class FanartConfigurationTests
{
    [Fact]
    public void NoPropertyIsMarkedRequired()
    {
        // A [Required] property that is unset fails configuration validation,
        // and a configuration that fails validation stops the whole plugin from
        // loading. The API key is checked for at runtime instead, so a plugin
        // with no key configured stays loaded and does nothing.
        var required = typeof(FanartConfiguration)
            .GetProperties()
            .Where(property => property.GetCustomAttributes(typeof(RequiredAttribute), inherit: true).Length > 0)
            .Select(property => property.Name)
            .ToArray();

        Assert.Empty(required);
    }

    [Fact]
    public void ADefaultConfigurationHasNoCredentials()
    {
        var configuration = new FanartConfiguration();

        Assert.Null(configuration.ApiKey);
        Assert.Null(configuration.PersonalApiKey);
    }

    [Fact]
    public void ADefaultConfigurationSweepsWeeklyBecauseThatIsHowOftenArtworkReachesAProjectKey()
    {
        var configuration = new FanartConfiguration();

        Assert.Equal(7, configuration.SweepInterval.TotalDays);
    }

    [Fact]
    public void ADefaultConfigurationIsConservativeAboutHowMuchItAdds()
    {
        var configuration = new FanartConfiguration();

        Assert.Equal(5, configuration.MaximumImagesPerType);
        Assert.True(configuration.DownloadArtwork);
        Assert.True(configuration.IncludeMovies);
        Assert.True(configuration.RemoveWithdrawnArtwork);
    }

    [Fact]
    public void TheClientReportsAMissingKeyWithoutThrowing()
    {
        var configurationService = new FakeConfigurationService(new FanartConfiguration());
        var client = new FanartApiClient(
            new System.Net.Http.HttpClient(new UnreachableHttpMessageHandler()) { BaseAddress = new System.Uri("https://webservice.fanart.tv/") },
            new FanartRateLimiter(),
            new Shoko.Abstractions.Config.ConfigurationProvider<FanartConfiguration>(configurationService),
            new RecordingLogger<FanartApiClient>()
        );

        Assert.False(client.HasApiKey);
    }
}
