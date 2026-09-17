using System.Linq;
using Shoko.Abstractions.Metadata.Enums;
using Shoko.Plugin.Fanart.Mapping;
using Xunit;

namespace Shoko.Plugin.Fanart.Tests;

/// <summary>
/// Which Fanart.tv asset kinds become which Shoko image type, and which are
/// deliberately left behind.
/// </summary>
public class FanartArtworkMapTests
{
    [Theory]
    [InlineData("tvposter", ImageEntityType.Primary)]
    [InlineData("showbackground", ImageEntityType.Backdrop)]
    [InlineData("show4kbackground", ImageEntityType.Backdrop)]
    [InlineData("hdtvlogo", ImageEntityType.Logo)]
    [InlineData("clearlogo", ImageEntityType.Logo)]
    [InlineData("tvbanner", ImageEntityType.Banner)]
    public void ShowKinds_MapToTheExpectedImageType(string kind, ImageEntityType expected)
    {
        Assert.True(FanartArtworkMap.TryGet(FanartEntityKind.Show, kind, out var mapping));

        Assert.Equal(expected, mapping.ImageType);
    }

    [Theory]
    [InlineData("movieposter", ImageEntityType.Primary)]
    [InlineData("moviebackground", ImageEntityType.Backdrop)]
    [InlineData("movie4kbackground", ImageEntityType.Backdrop)]
    [InlineData("hdmovielogo", ImageEntityType.Logo)]
    [InlineData("movielogo", ImageEntityType.Logo)]
    [InlineData("moviebanner", ImageEntityType.Banner)]
    [InlineData("moviedisc", ImageEntityType.Disc)]
    public void MovieKinds_MapToTheExpectedImageType(string kind, ImageEntityType expected)
    {
        Assert.True(FanartArtworkMap.TryGet(FanartEntityKind.Movie, kind, out var mapping));

        Assert.Equal(expected, mapping.ImageType);
    }

    [Theory]
    [InlineData("tvthumb")]
    [InlineData("hdclearart")]
    [InlineData("clearart")]
    [InlineData("characterart")]
    [InlineData("seasonposter")]
    [InlineData("seasonthumb")]
    [InlineData("seasonbanner")]
    public void DroppedShowKinds_AreNotMappedAndSayWhy(string kind)
    {
        Assert.False(FanartArtworkMap.TryGet(FanartEntityKind.Show, kind, out _));

        Assert.True(FanartArtworkMap.DroppedShowKinds.TryGetValue(kind, out var reason));
        Assert.NotEmpty(reason);
    }

    [Theory]
    [InlineData("moviethumb")]
    [InlineData("hdmovieclearart")]
    [InlineData("movieart")]
    public void DroppedMovieKinds_AreNotMappedAndSayWhy(string kind)
    {
        Assert.False(FanartArtworkMap.TryGet(FanartEntityKind.Movie, kind, out _));

        Assert.True(FanartArtworkMap.DroppedMovieKinds.TryGetValue(kind, out var reason));
        Assert.NotEmpty(reason);
    }

    [Fact]
    public void MusicAndUnknownKinds_AreIgnoredRatherThanGuessedAt()
    {
        Assert.False(FanartArtworkMap.TryGet(FanartEntityKind.Show, "artistthumb", out _));
        Assert.False(FanartArtworkMap.TryGet(FanartEntityKind.Show, "somethingfanartaddsnextyear", out _));
    }

    [Fact]
    public void ShowAndMovieKindsDoNotOverlap()
    {
        // The two endpoints name every kind differently, and a movie kind
        // arriving from the TV endpoint would mean the response was not what it
        // claimed to be.
        var showKinds = FanartArtworkMap.ShowKinds.Select(kind => kind.Kind);
        var movieKinds = FanartArtworkMap.MovieKinds.Select(kind => kind.Kind);

        Assert.Empty(showKinds.Intersect(movieKinds, System.StringComparer.OrdinalIgnoreCase));
    }

    [Fact]
    public void NoKindIsBothMappedAndDropped()
    {
        foreach (var kind in FanartArtworkMap.ShowKinds)
            Assert.False(FanartArtworkMap.DroppedShowKinds.ContainsKey(kind.Kind));

        foreach (var kind in FanartArtworkMap.MovieKinds)
            Assert.False(FanartArtworkMap.DroppedMovieKinds.ContainsKey(kind.Kind));
    }

    [Fact]
    public void HigherResolutionKindsOutrankTheirOlderTwins()
    {
        Assert.True(FanartArtworkMap.TryGet(FanartEntityKind.Show, "show4kbackground", out var fourK));
        Assert.True(FanartArtworkMap.TryGet(FanartEntityKind.Show, "showbackground", out var background));
        Assert.True(FanartArtworkMap.TryGet(FanartEntityKind.Show, "hdtvlogo", out var hdLogo));
        Assert.True(FanartArtworkMap.TryGet(FanartEntityKind.Show, "clearlogo", out var sdLogo));

        Assert.True(fourK.Priority < background.Priority);
        Assert.True(hdLogo.Priority < sdLogo.Priority);
    }
}
