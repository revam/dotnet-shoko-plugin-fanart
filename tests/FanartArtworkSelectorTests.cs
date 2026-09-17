using System.Linq;
using System.Text.Json;
using Shoko.Abstractions.Metadata.Enums;
using Shoko.Plugin.Fanart.Api;
using Shoko.Plugin.Fanart.Mapping;
using Xunit;

namespace Shoko.Plugin.Fanart.Tests;

/// <summary>
/// What is kept from a response, in what order, and how much of it.
/// </summary>
public class FanartArtworkSelectorTests
{
    private static FanartArtworkSet Parse(string fixture)
        => JsonSerializer.Deserialize<FanartArtworkSet>(Fixture.Read(fixture), FanartJson.Options)!;

    private static FanartArtwork[] Select(string fixture, FanartEntityKind kind, int maximumPerType = 10)
        => [.. FanartArtworkSelector.Select(Parse(fixture), kind, maximumPerType)];

    [Fact]
    public void Show_KeepsOnlyTheMappedKinds()
    {
        var artwork = Select("tv-76885.json", FanartEntityKind.Show);

        var kinds = artwork.Select(entry => entry.Kind).Distinct().Order().ToArray();

        Assert.Equal(["clearlogo", "hdtvlogo", "show4kbackground", "showbackground", "tvbanner", "tvposter"], kinds);
    }

    [Fact]
    public void Show_ProducesEveryImageTypeItHasArtworkFor()
    {
        var artwork = Select("tv-76885.json", FanartEntityKind.Show);

        var types = artwork.Select(entry => entry.ImageType).Distinct().Order().ToArray();

        // No Disc: Fanart.tv has no disc artwork for TV shows.
        Assert.Equal([ImageEntityType.Primary, ImageEntityType.Backdrop, ImageEntityType.Banner, ImageEntityType.Logo], types);
    }

    [Fact]
    public void Show_SkipsArtworkHostedOutsideTheFanartCdn()
    {
        var artwork = Select("tv-76885.json", FanartEntityKind.Show);

        // The most-liked background in the fixture is on another host, and
        // there is no resource ID that could be stored for it.
        Assert.DoesNotContain(artwork, entry => entry.ResourceID.Contains("not-a-fanart-asset", System.StringComparison.Ordinal));
    }

    [Fact]
    public void Backdrops_RankTheFourKVersionAboveTheOneWithMoreLikes()
    {
        var backdrops = Select("tv-76885.json", FanartEntityKind.Show)
            .Where(entry => entry.ImageType is ImageEntityType.Backdrop)
            .ToArray();

        Assert.Equal("show4kbackground", backdrops[0].Kind);
        Assert.Equal(0, backdrops[0].Ordering);
    }

    [Fact]
    public void Logos_RankTheHdKindAboveTheSdKindRegardlessOfLikes()
    {
        var logos = Select("tv-76885.json", FanartEntityKind.Show)
            .Where(entry => entry.ImageType is ImageEntityType.Logo)
            .ToArray();

        // clearlogo has 40 likes against hdtvlogo's 12 and 3, and still comes
        // last: the kind decides first, likes only order within a kind.
        Assert.Equal(["hdtvlogo", "hdtvlogo", "clearlogo"], logos.Select(entry => entry.Kind).ToArray());
        Assert.Equal([0, 1, 2], logos.Select(entry => entry.Ordering).ToArray());
    }

    [Fact]
    public void WithinAKind_MoreLikesComesFirst()
    {
        var logos = Select("tv-76885.json", FanartEntityKind.Show)
            .Where(entry => entry.Kind is "hdtvlogo")
            .ToArray();

        Assert.Equal("en", logos[0].LanguageCode);
        Assert.Equal("ja", logos[1].LanguageCode);
    }

    [Fact]
    public void LanguageFreeArtwork_CarriesNoLanguageCode()
    {
        var backdrops = Select("tv-76885.json", FanartEntityKind.Show)
            .Where(entry => entry.ImageType is ImageEntityType.Backdrop)
            .ToArray();

        Assert.All(backdrops, entry => Assert.Null(entry.LanguageCode));
    }

    [Fact]
    public void Dimensions_AreCarriedThroughWhenTheApiReportsThem()
    {
        var poster = Select("tv-76885.json", FanartEntityKind.Show).Single(entry => entry.ImageType is ImageEntityType.Primary);

        Assert.Equal(1000, poster.Width);
        Assert.Equal(1426, poster.Height);
    }

    [Fact]
    public void MaximumPerType_CapsEachTypeSeparatelyAndRenumbers()
    {
        var artwork = Select("tv-76885.json", FanartEntityKind.Show, maximumPerType: 1);

        Assert.Equal(4, artwork.Length);
        Assert.All(artwork, entry => Assert.Equal(0, entry.Ordering));
    }

    [Fact]
    public void Movie_KeepsTheMappedKindsIncludingDiscArtwork()
    {
        var artwork = Select("movie-129.json", FanartEntityKind.Movie);

        var types = artwork.Select(entry => entry.ImageType).Distinct().Order().ToArray();

        Assert.Equal([ImageEntityType.Primary, ImageEntityType.Backdrop, ImageEntityType.Banner, ImageEntityType.Logo, ImageEntityType.Disc], types);
        Assert.DoesNotContain(artwork, entry => entry.Kind is "moviethumb" or "movieart");
    }

    [Fact]
    public void ReadingAMovieResponseAsAShowKeepsNothing()
    {
        // The two endpoints share no asset kind, so this is what a response
        // arriving from the wrong endpoint looks like: empty, not mismapped.
        Assert.Empty(Select("movie-129.json", FanartEntityKind.Show));
    }

    [Fact]
    public void TheSameImageIsNeverAttachedTwiceUnderOneType()
    {
        var duplicated = JsonSerializer.Deserialize<FanartArtworkSet>(
            """
            {
                "name": "Duplicated",
                "thetvdb_id": "1",
                "hdtvlogo": [
                    { "id": "1", "url": "https://assets.fanart.tv/fanart/tv/1/hdtvlogo/same.png", "lang": "en", "likes": "5" }
                ],
                "clearlogo": [
                    { "id": "2", "url": "https://assets.fanart.tv/fanart/tv/1/hdtvlogo/same.png", "lang": "en", "likes": "9" }
                ]
            }
            """,
            FanartJson.Options
        )!;

        var artwork = FanartArtworkSelector.Select(duplicated, FanartEntityKind.Show, 10);

        Assert.Single(artwork);
    }

    [Fact]
    public void SelectionIsStableAcrossRuns()
    {
        // Two sweeps over unchanged artwork must produce the same ordering, or
        // every sweep rewrites rows that did not change.
        var first = Select("tv-76885.json", FanartEntityKind.Show);
        var second = Select("tv-76885.json", FanartEntityKind.Show);

        Assert.Equal(first, second);
    }
}
