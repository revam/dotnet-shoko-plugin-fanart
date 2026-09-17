using Shoko.Plugin.Fanart.Mapping;
using Xunit;

namespace Shoko.Plugin.Fanart.Tests;

/// <summary>
/// Turning an asset URL into the resource ID Shoko stores, and back.
/// </summary>
public class FanartAssetUrlTests
{
    [Fact]
    public void TemplateUrl_HasTheSubstitutionTargetTheServerRequires()
    {
        // SetTemplateUrlForSource rejects anything that is not an absolute
        // http(s) URL containing {0}.
        Assert.StartsWith("https://", FanartAssetUrl.TemplateUrl, System.StringComparison.Ordinal);
        Assert.Contains("{0}", FanartAssetUrl.TemplateUrl, System.StringComparison.Ordinal);
    }

    [Fact]
    public void ResourceID_IsEverythingAfterTheAssetPrefix()
    {
        Assert.True(FanartAssetUrl.TryGetResourceID("https://assets.fanart.tv/fanart/tv/76885/hdtvlogo/show-abc123.png", out var resourceID));

        Assert.Equal("tv/76885/hdtvlogo/show-abc123.png", resourceID);
    }

    [Fact]
    public void ResourceID_HandlesTheFlatPathsTheVendorDocuments()
    {
        Assert.True(FanartAssetUrl.TryGetResourceID("https://assets.fanart.tv/fanart/leave-her-to-heaven-5d557fb845928.png", out var resourceID));

        Assert.Equal("leave-her-to-heaven-5d557fb845928.png", resourceID);
    }

    [Fact]
    public void ResourceID_RebuildsTheSameUrl()
    {
        const string url = "https://assets.fanart.tv/fanart/movies/129/moviedisc/spirited-away-abc.png";

        Assert.True(FanartAssetUrl.TryGetResourceID(url, out var resourceID));
        Assert.Equal(url, FanartAssetUrl.ToUrl(resourceID));
    }

    [Fact]
    public void ResourceID_UpgradesAPlainHttpAssetUrl()
    {
        Assert.True(FanartAssetUrl.TryGetResourceID("http://assets.fanart.tv/fanart/tv/76885/tvbanner/show-abc.jpg", out var resourceID));

        Assert.Equal("tv/76885/tvbanner/show-abc.jpg", resourceID);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("https://images.example.test/not-a-fanart-asset.jpg")]
    // The CDN's own thumbnail paths are not stored: Shoko downloads the
    // full-size image, and a preview URL would silently give it a 200px one.
    [InlineData("https://assets.fanart.tv/preview/tv/76885/hdtvlogo/show-abc.png")]
    [InlineData("https://assets.fanart.tv/bigpreview/tv/76885/hdtvlogo/show-abc.png")]
    public void ResourceID_IsRefusedForAnythingElse(string? url)
    {
        Assert.False(FanartAssetUrl.TryGetResourceID(url, out var resourceID));

        Assert.Null(resourceID);
    }

    [Fact]
    public void ResourceID_IsRefusedWhenItWouldNotFitTheColumn()
    {
        // ShokoImage.ResourceID is NVARCHAR(128). Truncating would store a path
        // that cannot be downloaded, so an over-long one is refused outright.
        var tooLong = FanartAssetUrl.AssetPrefix + new string('a', 125) + ".png";

        Assert.False(FanartAssetUrl.TryGetResourceID(tooLong, out _));
        Assert.True(FanartAssetUrl.TryGetResourceID(FanartAssetUrl.AssetPrefix + new string('a', 124) + ".png", out var fits));
        Assert.Equal(128, fits.Length);
    }
}
