using Webwild.Core;
using Xunit;

namespace Webwild.Core.Tests;

/// <summary>
/// Reedwater used to leave the player waiting more than a minute between
/// catches. These bounds keep the loop responsive without instant completion.
/// </summary>
public class PacingTests
{
    private static GameSimulation ReedwaterSim()
    {
        var sim = TestWorld.NewSim();
        sim.Missions.Restore(
            3, 0,
            new[] { 2, 3, 3 },
            new[] { "M01", "M02", "M03" },
            Array.Empty<string>(),
            victoryAwarded: true);
        Assert.True(sim.TryTravelTo(RegionIds.ReedwaterBank, out var reason), reason);
        return sim;
    }

    [Fact]
    public void ReedwaterInsectRespawns_StayWithinAChildFriendlyWindow()
    {
        var routes = ReedwaterLayout.CreateInsectRoutes();
        Assert.NotEmpty(routes);
        Assert.All(routes, route => Assert.InRange(route.RespawnSeconds, 8, 16));
    }

    [Fact]
    public void R02_ProgressesWithinOneMinuteAfterTheCaptureWeb()
    {
        var sim = ReedwaterSim();
        Assert.True(sim.TryBuildWeb(ReedwaterLayout.StartPost, ReedwaterLayout.IslandAPost,
            WebType.Bridge, out _, out _));
        Assert.True(sim.TryBuildWeb(ReedwaterLayout.IslandAPost, ReedwaterLayout.IslandBPost,
            WebType.Bridge, out _, out _));
        Assert.True(sim.TryBuildWeb(ReedwaterLayout.EasyA, ReedwaterLayout.EasyB,
            WebType.Capture, out _, out var reason), reason.Reason);

        var seconds = 0;
        while (!sim.RegionalObjectives.CompletedObjectives.Contains("R02") && seconds < 75)
        {
            sim.AdvanceSeconds(1);
            seconds++;
            foreach (var insect in sim.RegionInsects.Where(i => i.CanBeCollected).ToList())
                sim.TryCollectInsect(insect.Id, "player", out _, out _);
        }

        Assert.Contains("R02", sim.RegionalObjectives.CompletedObjectives);
        Assert.InRange(seconds, 1, 65);
    }

    [Fact]
    public void R03_DeliveryDoesNotStallForOverAMinute()
    {
        var sim = ReedwaterSim();
        Assert.True(sim.TryBuildWeb(ReedwaterLayout.StartPost, ReedwaterLayout.IslandAPost,
            WebType.Bridge, out _, out _));
        Assert.True(sim.TryBuildWeb(ReedwaterLayout.IslandAPost, ReedwaterLayout.IslandBPost,
            WebType.Bridge, out _, out _));
        Assert.True(sim.TryBuildWeb(ReedwaterLayout.IslandBPost, ReedwaterLayout.FarPost,
            WebType.Bridge, out _, out _));
        Assert.True(sim.TryStartHatch(out var hatchReason), hatchReason);
        sim.AdvanceSeconds(5);
        Assert.True(sim.TryBuildWeb(ReedwaterLayout.FarA, ReedwaterLayout.FarB,
            WebType.Capture, out _, out var webReason), webReason.Reason);

        var seconds = 0;
        while (!sim.RegionalObjectives.CompletedObjectives.Contains("R03") && seconds < 90)
        {
            sim.AdvanceSeconds(1);
            seconds++;
        }

        Assert.Contains("R03", sim.RegionalObjectives.CompletedObjectives);
        Assert.InRange(seconds, 1, 75);
    }
}
