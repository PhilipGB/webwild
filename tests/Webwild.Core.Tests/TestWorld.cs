using Webwild.Core;
using Xunit;

namespace Webwild.Core.Tests;

public class TestWorld
{
    public static Tuning RichTuning(Action<Tuning>? edit = null)
    {
        var tuning = new Tuning();
        tuning.Initial.Food = 100;
        tuning.Initial.Silk = 100;
        edit?.Invoke(tuning);
        tuning.Normalise();
        return tuning;
    }

    public static GameSimulation NewSim(Action<Tuning>? edit = null, IEnumerable<AnchorDefinition>? anchors = null)
    {
        return new GameSimulation(RichTuning(edit), MissionsConfig.Defaults(), anchors);
    }

    public static GameSimulation BuildTutorialWeb(GameSimulation sim)
    {
        Assert.True(sim.TryBuildWeb(WorldLayout.TwigA, WorldLayout.TwigB, WebType.Capture, out _, out var result), result.Reason);
        return sim;
    }
}
