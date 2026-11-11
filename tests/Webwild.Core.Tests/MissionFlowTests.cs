using Webwild.Core;
using Xunit;

namespace Webwild.Core.Tests;

public class MissionFlowTests
{
    [Fact]
    public void HintsFollowTheCurrentMissionStep()
    {
        var sim = TestWorld.NewSim();
        Assert.Contains("glowing twin twigs", sim.GetHint());

        Assert.True(sim.TryBuildWeb(WorldLayout.TwigA, WorldLayout.TwigB, WebType.Capture, out _, out _));
        sim.AdvanceSeconds(12);
        Assert.Contains("fly", sim.GetHint(), StringComparison.OrdinalIgnoreCase);

        var trapped = sim.Insects.First(i => i.CanBeCollected);
        Assert.True(sim.TryCollectInsect(trapped.Id, "player", out _, out _));
        Assert.Contains("pebble posts", sim.GetHint());

        Assert.True(sim.TryBuildWeb(WorldLayout.GapWest, WorldLayout.GapEast, WebType.Bridge, out _, out _));
        Assert.Contains("nursery", sim.GetHint(), StringComparison.OrdinalIgnoreCase);

        Assert.True(sim.TryStartHatch(out _));
        sim.AdvanceSeconds(4);
        Assert.Contains("helper", sim.GetHint(), StringComparison.OrdinalIgnoreCase);
    }
}
