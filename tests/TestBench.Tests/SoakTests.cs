namespace TestBench.Tests;

public class SoakTests
{
    [Fact]
    public void PercentileUsesNearestRankAndMeanUsesAllSuccessfulReplies()
    {
        var observations = Enumerable.Range(1, 100).Select(i => new TestObservation(true, true, i)).Reverse().ToArray();
        var result = SoakStatistics.From(observations);
        Assert.Equal(1, result.MinMs);
        Assert.Equal(50.5, result.MeanMs);
        Assert.Equal(95, result.P95Ms);
        Assert.Equal(100, result.MaxMs);
        Assert.Equal(0, result.FailureRatePercent);
    }

    [Fact]
    public void FailuresAndUnattemptedTestsAreNotHiddenOrMixedIntoLatency()
    {
        var result = SoakStatistics.From([new(true, true, 10), new(false, true, 500), new(false, false, 0)]);
        Assert.Equal(3, result.Requested);
        Assert.Equal(2, result.Attempted);
        Assert.Equal(1, result.Failed);
        Assert.Equal(1, result.NotRun);
        Assert.Equal(50, result.FailureRatePercent);
        Assert.Equal(10, result.P95Ms);
        Assert.Equal(10, result.MeanMs);
    }

    [Fact]
    public void NoConnectionDoesNotReportZeroPercentFailureOrFakeLatency()
    {
        var result = SoakStatistics.From([new(false, false, 0)]);
        Assert.Null(result.FailureRatePercent);
        Assert.Null(result.MinMs);
        Assert.Null(result.MeanMs);
        Assert.Null(result.P95Ms);
        Assert.Null(result.MaxMs);
    }

    [Theory]
    [InlineData("0")]
    [InlineData("-1")]
    [InlineData("100001")]
    [InlineData("oops")]
    public void InvalidCountIsRejected(string count) => Assert.Equal(2, SoakCommand.Run(["sim", count]));
}
