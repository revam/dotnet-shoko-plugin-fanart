using System;
using System.Net.Http.Headers;
using System.Threading;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using Shoko.Abstractions.Config;
using Shoko.Abstractions.Plugin;
using Shoko.Plugin.Fanart.Api;
using Shoko.Plugin.Fanart.Images;
using Shoko.Plugin.Fanart.Jobs;
using Shoko.QueueProcessor.Scheduling;

namespace Shoko.Plugin.Fanart;

/// <summary>
/// Plugin contributing series and movie artwork from
/// <see href="https://fanart.tv"/> through Shoko's image manager.
/// </summary>
/// <remarks>
/// This class carries the plugin's identity and nothing else. It is built twice
/// during start-up, and the first of the two, during discovery, uses
/// <c>Activator.CreateInstance</c> before any container exists, so it must keep
/// a public parameterless constructor and take no dependencies at all.
/// Everything the plugin needs is registered in <see cref="RegisterServices(IServiceCollection, IApplicationPaths)"/>
/// instead.
/// </remarks>
public class Plugin : IPlugin, IPluginServiceRegistration, IPluginApplicationRegistration
{
    /// <inheritdoc/>
    public Guid ID { get; private init; } = new("3eb9a6dd-eac7-48a9-9b55-ee2c7c23938b");

    /// <inheritdoc/>
    public string Name { get; private set; } = "Fanart.tv Artwork";

    /// <inheritdoc/>
    public string Description { get; private set; } = """
        Adds series and movie artwork from Fanart.tv to TMDB-linked entries, keyed through
        the TheTVDB ID for shows and the TMDB ID for movies. Requires your own Fanart.tv
        API key.
    """;

    /// <inheritdoc/>
    public static void RegisterServices(IServiceCollection serviceCollection, IApplicationPaths applicationPaths)
    {
        // Registered as concrete singletons because this plugin's own code
        // resolves them: the sweep job, the two actions and the API client all
        // share one rate limiter and one HTTP client. Nothing here is a
        // contract the server discovers, so nothing is registered under an
        // interface.
        serviceCollection.AddSingleton<FanartRateLimiter>();
        serviceCollection.AddSingleton<FanartImageService>();
        serviceCollection.AddSingleton<FanartArtworkService>();

        serviceCollection
            .AddHttpClient<FanartApiClient>(client =>
            {
                client.BaseAddress = new Uri("https://webservice.fanart.tv/");
                client.DefaultRequestHeaders.UserAgent.Add(
                    new ProductInfoHeaderValue("Shoko.Plugin.Fanart", typeof(Plugin).Assembly.GetName().Version?.ToString() ?? "0.1.0")
                );
                client.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("(+https://github.com/revam/dotnet-shoko-plugin-fanart)"));
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
    public static void RegisterServices(IApplicationBuilder application, IApplicationPaths applicationPaths)
    {
        var services = application.ApplicationServices;
        var configuration = services.GetRequiredService<ConfigurationProvider<FanartConfiguration>>().Load();

        // This is the first point at which the container is built, which is
        // what the recurring job registry needs.
        var registry = services.GetRequiredService<RecurringJobRegistry>();
        registry.Register<FanartSweepJob>(interval: configuration.SweepInterval, runImmediately: false);
    }
}
