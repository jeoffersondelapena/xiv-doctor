using XIVDoctor;
using Xunit;

public class FrameWatchTests
{
    [Fact]
    public void A_ticking_frame_loop_says_nothing()
    {
        Assert.Null(FrameWatch.Verdict(now: 10_000, lastFrame: 9_000, reportedAt: 0, facts: "f"));
    }

    [Fact]
    public void A_stall_is_reported_once_it_passes_two_seconds()
    {
        Assert.Null(FrameWatch.Verdict(now: 11_900, lastFrame: 10_000, reportedAt: 0, facts: "f"));
        Assert.Equal("frame loop stalled 2s; f", FrameWatch.Verdict(now: 12_000, lastFrame: 10_000, reportedAt: 0, facts: "f"));
    }

    [Fact]
    public void A_lasting_stall_repeats_every_three_seconds_not_every_check()
    {
        Assert.Null(FrameWatch.Verdict(now: 14_000, lastFrame: 10_000, reportedAt: 12_000, facts: "f"));
        Assert.Equal("frame loop stalled 5s; f", FrameWatch.Verdict(now: 15_000, lastFrame: 10_000, reportedAt: 12_000, facts: "f"));
    }

    [Fact]
    public void The_end_of_a_reported_stall_is_said_once()
    {
        Assert.Equal("frame loop resumed", FrameWatch.Verdict(now: 40_000, lastFrame: 39_500, reportedAt: 25_000, facts: "f"));
        Assert.Null(FrameWatch.Verdict(now: 42_000, lastFrame: 41_500, reportedAt: 0, facts: "f"));
    }
}
