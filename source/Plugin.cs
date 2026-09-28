using System;
using System.Net.Http.Headers;
using System.Threading;
using Microsoft.Extensions.DependencyInjection;
using Shoko.Abstractions.Metadata.Services;
using Shoko.Abstractions.Plugin;
using Shoko.Plugin.Fanart.Api;
using Shoko.Plugin.Fanart.Mapping;

namespace Shoko.Plugin.Fanart;

/// <summary>
/// Plugin contributing series and movie artwork from
/// <see href="https://fanart.tv"/>, as an image contributor the core asks
/// whenever it refreshes an entry's images.
/// </summary>
/// <remarks>
/// This class carries the plugin's identity and nothing else. It is built twice
/// during start-up, and the first of the two, during discovery, uses
/// <c>Activator.CreateInstance</c> before any container exists, so it must keep
/// a public parameterless constructor and take no dependencies at all.
/// Everything the plugin needs is registered in <see cref="RegisterServices(IServiceCollection, IApplicationPaths)"/>
/// instead, and the template URL in <see cref="Setup(IServiceProvider)"/>.
/// </remarks>
public class Plugin : IPlugin, IPluginServiceRegistration
{
    /// <inheritdoc/>
    public Guid ID { get; private init; } = new("3eb9a6dd-eac7-48a9-9b55-ee2c7c23938b");

    /// <inheritdoc/>
    public string Name { get; private set; } = "Fanart.tv Artwork";

    /// <inheritdoc/>
    public string Description { get; private set; } = """
        Adds series and movie artwork from Fanart.tv to TMDB's series and movies, and to
        TheTVDB's series when a plugin provides them, keyed through the TheTVDB ID for series
        and the TMDB ID for movies. Requires your own Fanart.tv API key.
    """;

    /// <inheritdoc/>
    public static void RegisterServices(IServiceCollection serviceCollection, IApplicationPaths applicationPaths)
    {
        // Touching the class runs its static constructor, which registers the
        // source before the server closes registration.
        _ = FanartSources.FanartTV;

        // The image contributor is found and built by the server itself; the
        // rate limiter is registered here so every request it makes shares
        // one budget.
        serviceCollection.AddSingleton<FanartRateLimiter>();

        serviceCollection
            // The contact URL is read back from the plugin's own registered info
            // rather than written here, so it names wherever this build was
            // published from instead of hard-coding one host into the source. A
            // local build has no repository URL stamped, so the comment is left
            // off rather than sent empty.
            .AddHttpClient<FanartApiClient>((provider, client) =>
            {
                var info = provider.GetRequiredService<IPluginManager>().GetPluginInfo<Plugin>();
                client.BaseAddress = new Uri("https://webservice.fanart.tv/");
                client.DefaultRequestHeaders.UserAgent.Add(
                    new ProductInfoHeaderValue("Shoko.Plugin.Fanart", info?.Version.Version.ToString(3) ?? typeof(Plugin).Assembly.GetName().Version?.ToString() ?? "0.1.0")
                );
                if (info?.RepositoryUrl is { Length: > 0 } repositoryUrl)
                    client.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue($"(+{repositoryUrl})"));
                client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
                client.Timeout = TimeSpan.FromSeconds(30);
            })
            .SetHandlerLifetime(Timeout.InfiniteTimeSpan)
            .UseSocketsHttpHandler((handler, _) =>
            {
                handler.PooledConnectionLifetime = TimeSpan.FromMinutes(5);
                handler.PooledConnectionIdleTimeout = TimeSpan.FromMinutes(2);
            });
    }

    /// <inheritdoc/>
    public void Setup(IServiceProvider serviceProvider)
    {
        // The server keeps the template URL in memory only, and downloads of
        // artwork added before this start need it as much as new artwork does.
        // A template the user set for a mirror of their own takes precedence
        // over it on the server's side.
        serviceProvider.GetRequiredService<IImageManager>().RegisterTemplateUrl(FanartSources.FanartTV, FanartAssetUrl.TemplateUrl);
    }
}
