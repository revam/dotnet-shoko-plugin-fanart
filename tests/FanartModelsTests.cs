using System.Linq;
using System.Text.Json;
using Shoko.Plugin.Fanart.Api;
using Xunit;

namespace Shoko.Plugin.Fanart.Tests;

/// <summary>
/// Reading a Fanart.tv response envelope.
/// </summary>
/// <remarks>
/// The fixtures behind these tests are hand-written rather than captured, so
/// what they pin down is the parser's behaviour for a given shape, not the
/// shape itself. See <c>tests/Fixtures/README.md</c>.
/// </remarks>
public class FanartModelsTests
{
    private static FanartArtworkSet Parse(string fixture)
        => JsonSerializer.Deserialize<FanartArtworkSet>(Fixture.Read(fixture), FanartJson.Options)!;

    [Fact]
    public void Show_ReadsNameAndIdentifier()
    {
        var set = Parse("tv-76885.json");

        Assert.Equal("Cowboy Bebop", set.Name);
        Assert.Equal("76885", set.TvdbShowID);
        Assert.Null(set.TmdbMovieID);
    }

    [Fact]
    public void Movie_ReadsBothIdentifiers()
    {
        var set = Parse("movie-129.json");

        Assert.Equal("129", set.TmdbMovieID);
        Assert.Equal("tt0245429", set.ImdbMovieID);
    }

    [Fact]
    public void ArtworkGroups_KeepEveryArrayFieldAndIgnoreTheRest()
    {
        var set = Parse("tv-76885.json");

        var kinds = set.GetArtworkGroups().Select(group => group.Kind).ToList();

        // Every array field, including the ones the mapping later drops: the
        // model stays open so a new asset kind needs no parser change.
        Assert.Contains("hdtvlogo", kinds);
        Assert.Contains("seasonposter", kinds);
        // Scalars are not artwork, including the per-kind counts and the note
        // this fixture carries.
        Assert.DoesNotContain("hdtvlogo_count", kinds);
        Assert.DoesNotContain("image_count", kinds);
        Assert.DoesNotContain("_fixture_note", kinds);
        Assert.DoesNotContain("thetvdb_id", kinds);
    }

    [Fact]
    public void Image_ReadsTheDocumentedStringEncodedNumbers()
    {
        var set = Parse("tv-76885.json");

        var logo = set.GetArtworkGroups().Single(group => group.Kind is "hdtvlogo").Images[0];

        Assert.Equal("51021", logo.ID);
        Assert.Equal(12, logo.Likes);
        Assert.Equal(800, logo.Width);
        Assert.Equal(310, logo.Height);
        Assert.Equal("en", logo.LanguageCode);
    }

    [Theory]
    [InlineData("00")]
    [InlineData("")]
    public void LanguageCode_IsNullForArtworkWithNoText(string language)
    {
        var image = new FanartImage() { Language = language };

        Assert.Null(image.LanguageCode);
    }

    [Fact]
    public void Image_AcceptsNumbersWhereStringsAreDocumented()
    {
        var set = Parse("tv-numeric-fields.json");

        Assert.Equal("76885", set.TvdbShowID);
        var logos = set.GetArtworkGroups().Single(group => group.Kind is "hdtvlogo").Images;

        // The third entry is not an object and is dropped; the first two are
        // kept, numbers and all.
        Assert.Equal(2, logos.Count);
        Assert.Equal("51021", logos[0].ID);
        Assert.Equal(12, logos[0].Likes);
        Assert.Equal(800, logos[0].Width);
    }

    [Fact]
    public void Image_ToleratesEveryOptionalFieldBeingAbsent()
    {
        var set = Parse("tv-numeric-fields.json");

        var sparse = set.GetArtworkGroups().Single(group => group.Kind is "hdtvlogo").Images[1];

        Assert.NotNull(sparse.Url);
        Assert.Null(sparse.ID);
        Assert.Null(sparse.Likes);
        Assert.Null(sparse.Width);
        Assert.Null(sparse.Height);
        Assert.Null(sparse.LanguageCode);
    }

    [Fact]
    public void EmptyArtworkGroupsAreNotReported()
    {
        var set = Parse("tv-numeric-fields.json");

        Assert.DoesNotContain(set.GetArtworkGroups(), group => group.Kind is "showbackground");
    }

    [Fact]
    public void ErrorEnvelope_ParsesToNoArtworkRatherThanThrowing()
    {
        var set = Parse("not-found.json");

        Assert.Null(set.Name);
        Assert.Null(set.TvdbShowID);
        Assert.Empty(set.GetArtworkGroups());
    }
}
