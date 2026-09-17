using Moq;
using Shoko.Abstractions.Metadata;
using Shoko.Abstractions.Metadata.Tmdb;
using Shoko.Plugin.Fanart.Mapping;
using Xunit;

namespace Shoko.Plugin.Fanart.Tests;

/// <summary>
/// Which entries a sweep can look up, and what it reports about the ones it
/// cannot.
/// </summary>
public class FanartSweepPlannerTests
{
    private static ITmdbShow Show(int id, int? tvdbShowID)
    {
        var show = new Mock<ITmdbShow>();
        show.SetupGet(s => s.ID).Returns(id);
        show.SetupGet(s => s.TvdbShowID).Returns(tvdbShowID);
        return show.Object;
    }

    private static ITmdbMovie Movie(int id)
    {
        var movie = new Mock<ITmdbMovie>();
        movie.SetupGet(m => m.ID).Returns(id);
        return movie.Object;
    }

    [Fact]
    public void OnlyShowsWithATvdbIDCanBeLookedUp()
    {
        var plan = FanartSweepPlanner.Plan([Show(1, 76885), Show(2, null), Show(3, 0)], []);

        Assert.Equal(3, plan.TotalSeries);
        Assert.Equal(2, plan.WithoutTvdbShowID);
        Assert.Equal(76885, Assert.Single(plan.Shows).TvdbShowID);
    }

    [Fact]
    public void SeriesFromOtherProvidersAreCountedSeparately()
    {
        // The planner is handed whatever the metadata service returns for the
        // TMDB provider, and says so rather than silently ignoring anything
        // that is not a TMDB show.
        var plan = FanartSweepPlanner.Plan([Mock.Of<ISeries>(), Show(1, 76885)], []);

        Assert.Equal(1, plan.NotAShow);
        Assert.Single(plan.Shows);
    }

    [Fact]
    public void MoviesNeedNoTranslationStep()
    {
        var plan = FanartSweepPlanner.Plan([], [Movie(129), Movie(130)]);

        Assert.Equal(2, plan.Movies.Count);
        Assert.Equal(2, plan.TotalMovies);
        Assert.Equal(0, plan.NotATmdbMovie);
    }

    [Fact]
    public void CountIsWhatTheSweepWillActuallyFetch()
    {
        var plan = FanartSweepPlanner.Plan([Show(1, 76885), Show(2, null)], [Movie(129)]);

        Assert.Equal(2, plan.Count);
    }

    [Fact]
    public void AnEmptyCollectionPlansNothing()
    {
        var plan = FanartSweepPlanner.Plan([], []);

        Assert.Equal(0, plan.Count);
        Assert.Empty(plan.Shows);
        Assert.Empty(plan.Movies);
    }
}
