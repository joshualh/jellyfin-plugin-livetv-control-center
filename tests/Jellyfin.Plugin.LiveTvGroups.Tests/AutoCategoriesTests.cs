using Jellyfin.Plugin.LiveTvGroups.Services;
using MediaBrowser.Model.LiveTv;
using Xunit;

namespace Jellyfin.Plugin.LiveTvGroups.Tests;

public class AutoCategoriesTests
{
    [Fact]
    public void ParsesQuotedGroupsAndNames()
    {
        var item = AutoCategories.Parse("#EXTINF:-1 tvg-chno=\"101\" group-title=\"Sports, UK\",Sky Sports");
        Assert.NotNull(item);
        Assert.Equal("Sports, UK", item.Group);
        Assert.Equal("101", item.Number);
        Assert.Equal("Sky Sports", item.Name);
    }

    [Fact]
    public void IgnoresUngroupedLines() => Assert.Null(AutoCategories.Parse("#EXTINF:-1,Channel"));

    [Fact]
    public void MapsDispatcharrProfile()
    {
        var tuner = new TunerHostInfo { Type = "hdhomerun", Url = "http://localhost:9191/hdhr/UK/discover.json" };
        Assert.Equal("http://localhost:9191/output/m3u/UK", AutoCategories.Source(tuner));
    }

    [Fact]
    public void DoesNotProbePhysicalTuner()
        => Assert.Null(AutoCategories.Source(new TunerHostInfo { Type = "hdhomerun", Url = "http://localhost" }));
}
