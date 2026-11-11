using Webwild.Core;
using Xunit;

namespace Webwild.Core.Tests;

public class WindTests
{
    [Fact]
    public void ExposedWebTakesMoreDamageThanShelteredWeb()
    {
        var sim = TestWorld.NewSim(t => t.Weather.WarningSeconds = 5);
        Assert.True(sim.TryBuildWeb(WorldLayout.TwigA, WorldLayout.TwigB, WebType.Capture, out var exposed, out _));
        Assert.True(sim.TryBuildWeb(WorldLayout.ShelterA, WorldLayout.ShelterB, WebType.Capture, out var sheltered, out _));
        Assert.True(sheltered!.Sheltered);

        sim.StartWindWarning();
        Assert.Equal(WeatherPhase.Warning, sim.WeatherPhase);
        sim.AdvanceSeconds(6);

        Assert.Equal(50, exposed!.Durability);
        Assert.Equal(95, sheltered.Durability);
        Assert.Equal(WeatherPhase.Aftermath, sim.WeatherPhase);
        Assert.True(sim.WindCompleted);
        Assert.Contains(sim.Events, e => e.Type == "WindEventCompleted");
    }

    [Fact]
    public void RepairCostsSilk_AndRestoresCapture()
    {
        var sim = TestWorld.NewSim(t => t.Weather.WarningSeconds = 5);
        Assert.True(sim.TryBuildWeb(WorldLayout.TwigA, WorldLayout.TwigB, WebType.Capture, out var web, out _));
        sim.StartWindWarning();
        sim.AdvanceSeconds(6);
        var silkBefore = sim.Ledger.Silk;
        Assert.True(sim.TryRepairWeb(web!.Id, false, out _));
        Assert.Equal(silkBefore - 2, sim.Ledger.Silk);
        Assert.Equal(100, web.Durability);

        var silkAfter = sim.Ledger.Silk;
        Assert.False(sim.TryRepairWeb(web.Id, false, out var reason));
        Assert.Contains("already strong", reason);
        Assert.Equal(silkAfter, sim.Ledger.Silk);
    }

    [Fact]
    public void WindNeverDestroysEveryCaptureRoute()
    {
        var sim = TestWorld.NewSim(t =>
        {
            t.Weather.WarningSeconds = 5;
            t.Weather.ExposedWebDamage = 100;
            t.Weather.ShelteredWebDamage = 100;
        });
        Assert.True(sim.TryBuildWeb(WorldLayout.WindA, WorldLayout.WindB, WebType.Capture, out _, out _));
        sim.StartWindWarning();
        sim.AdvanceSeconds(6);

        Assert.Contains(sim.Webs, w => w.Type == WebType.Capture && w.CapturesEnabled);
    }

    [Fact]
    public void WindOnlyStartsOnce_AndDoesNotKillWorkers()
    {
        var sim = TestWorld.NewSim(t => t.Weather.WarningSeconds = 5);
        Assert.True(sim.TryStartHatch(out _));
        sim.AdvanceSeconds(4);
        var worker = Assert.Single(sim.Workers);
        sim.StartWindWarning();
        sim.AdvanceSeconds(6);
        Assert.Single(sim.Workers);
        Assert.True(worker.Position.IsFinite);
        Assert.Equal(WeatherPhase.Aftermath, sim.WeatherPhase);
        sim.AdvanceSeconds(9);
        Assert.Equal(WeatherPhase.Calm, sim.WeatherPhase);
    }

    [Fact]
    public void AftermathSaveRemainsValid_WhenWeatherReturnsToCalm()
    {
        var sim = TestWorld.NewSim(t => t.Weather.WarningSeconds = 5);
        sim.StartWindWarning();
        sim.AdvanceSeconds(6);
        Assert.Equal(WeatherPhase.Aftermath, sim.WeatherPhase);
        sim.AdvanceSeconds(9);
        Assert.Equal(WeatherPhase.Calm, sim.WeatherPhase);

        var save = sim.ExportSave(new Vec3(-13.5, 0.45, 8.5), 0, new SettingsData());
        Assert.True(SaveValidator.Validate(save, out var error), error);
        Assert.Equal(0, save.Weather.AftermathRemaining);
        Assert.Equal(0, save.Weather.Remaining);
    }

    [Fact]
    public void ShelteredWebAfterStorm_SatisfiesRestoreStep()
    {
        var sim = TestWorld.NewSim(t => t.Weather.WarningSeconds = 5);
        // Complete M01 and M02 quickly via real events.
        Assert.True(sim.TryBuildWeb(WorldLayout.TwigA, WorldLayout.TwigB, WebType.Capture, out _, out _));
        sim.AdvanceSeconds(12);
        var insect = sim.Insects.First(i => i.CanBeCollected);
        sim.TryCollectInsect(insect.Id, "player", out _, out _);
        Assert.True(sim.TryBuildWeb(WorldLayout.GapWest, WorldLayout.GapEast, WebType.Bridge, out _, out _));
        sim.TryStartHatch(out _);
        sim.AdvanceSeconds(40);
        Assert.Equal("M03", sim.Missions.ActiveMissionId);

        sim.AdvanceSeconds(8);
        Assert.True(sim.WindCompleted);
        Assert.True(sim.TryBuildWeb(WorldLayout.ShelterA, WorldLayout.ShelterB, WebType.Capture, out _, out _));
        Assert.Contains(sim.Events, e => e.Type == "WebRestoredOrSheltered");
        Assert.Equal(2, sim.Missions.StepIndex);
    }
}
