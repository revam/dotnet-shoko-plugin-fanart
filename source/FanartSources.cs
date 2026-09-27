using Shoko.Abstractions.Metadata;

namespace Shoko.Plugin.Fanart;

/// <summary>
/// The metadata source the plugin owns, registered once.
/// </summary>
/// <remarks>
/// The static constructor registers the source, and
/// <see cref="Plugin.RegisterServices(Microsoft.Extensions.DependencyInjection.IServiceCollection, Shoko.Abstractions.Plugin.IApplicationPaths)"/>
/// touches the class so that it runs before the core closes registration after
/// plugin setup. The value is the one the server moved the old
/// <c>DataSource.FanartTV</c> rows to, and the old <c>FanartTV</c> spelling is
/// kept as an alias so the API goes on sending it.
/// </remarks>
public static class FanartSources
{
    static FanartSources()
    {
        FanartTV = MetadataSource.Register("Fanart.tv", "fanart-tv", ["FanartTV"], description: "Community artwork at fanart.tv.");
    }

    /// <summary>
    /// Fanart.tv, as <c>fanart-tv</c>. Every image and cross-reference the
    /// plugin writes is attributed to it.
    /// </summary>
    public static MetadataSource FanartTV { get; }
}
