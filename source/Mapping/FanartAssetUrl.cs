using System;
using System.Diagnostics.CodeAnalysis;

namespace Shoko.Plugin.Fanart.Mapping;

/// <summary>
/// Converts between a Fanart.tv asset URL and the resource ID Shoko stores for
/// it.
/// </summary>
/// <remarks>
/// Shoko stores one template URL per image source and a resource ID per image,
/// and rebuilds the remote URL as <c>string.Format(template, resourceID)</c>
/// whenever it downloads one. For Fanart.tv the template is
/// <see cref="TemplateUrl"/> and the resource ID is everything after it, which
/// is the whole path however Fanart.tv shapes it: a flat
/// <c>leave-her-to-heaven-5d557fb845928.png</c> as its own documentation shows,
/// or a nested <c>tv/121361/hdtvlogo/some-show-52fa0bcd9e66b.png</c>.
/// Shoko stores a resource ID in 128 characters, so a longer path is refused
/// rather than truncated into something that cannot be downloaded.
/// </remarks>
public static class FanartAssetUrl
{
    /// <summary>
    /// The prefix every full-size Fanart.tv asset URL starts with. The CDN also
    /// serves <c>/preview/</c> (200px) and <c>/bigpreview/</c> (400px) variants
    /// of the same path, which this plugin does not use: Shoko downloads and
    /// stores the full-size image.
    /// </summary>
    public const string AssetPrefix = "https://assets.fanart.tv/fanart/";

    /// <summary>
    /// The template URL registered with Shoko for
    /// <see cref="Shoko.Abstractions.Metadata.Enums.DataSource.FanartTV"/>.
    /// </summary>
    public const string TemplateUrl = AssetPrefix + "{0}";

    /// <summary>
    /// Derives the resource ID Shoko should store for a Fanart.tv asset URL.
    /// </summary>
    /// <param name="url">
    /// The asset URL, as it arrived in the API response.
    /// </param>
    /// <param name="resourceID">
    /// The resource ID, or <see langword="null"/> when the URL is not a
    /// full-size Fanart.tv asset URL.
    /// </param>
    /// <returns>
    /// <see langword="true"/> when a resource ID was derived.
    /// </returns>
    public static bool TryGetResourceID(string? url, [NotNullWhen(true)] out string? resourceID)
    {
        resourceID = null;
        if (string.IsNullOrWhiteSpace(url))
            return false;

        var trimmed = url.Trim();

        // Both schemes have been seen in the wild for these URLs, and the CDN
        // serves the same path either way.
        if (trimmed.StartsWith("http://assets.fanart.tv/fanart/", StringComparison.OrdinalIgnoreCase))
            trimmed = string.Concat("https://", trimmed.AsSpan("http://".Length));

        if (!trimmed.StartsWith(AssetPrefix, StringComparison.OrdinalIgnoreCase))
            return false;

        var candidate = trimmed[AssetPrefix.Length..];
        if (candidate.Length is 0 || candidate.Length > 128)
            return false;

        resourceID = candidate;
        return true;
    }

    /// <summary>
    /// Rebuilds the full-size asset URL for a resource ID. The server does this
    /// itself when downloading; this exists for logging and for tests.
    /// </summary>
    /// <param name="resourceID">The resource ID.</param>
    /// <returns>The full-size asset URL.</returns>
    public static string ToUrl(string resourceID)
        => AssetPrefix + resourceID;
}
