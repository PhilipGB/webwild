using Webwild.Core;
using Xunit;

namespace Webwild.Core.Tests;

public class HardeningTests
{
    [Fact]
    public void LongSession_BoundsHistories_AndStillValidatesForSaving()
    {
        var sim = TestWorld.NewSim(t =>
        {
            t.Initial.Food = 5_000;
            t.Initial.Silk = 0;
            t.FixedSimulationTickSeconds = 0.2;
            t.SilkProduction.SilkPerFedWorkerEverySeconds = 0.2;
            t.SilkProduction.FoodCostPerSilkBatch = 1;
        });

        for (var i = 0; i < 3_000; i++)
        {
            sim.Tick(0.2);
            sim.Ledger.TrySpend(0, 1, $"long-session-spend-{i}");
        }

        var save = sim.ExportSave(new Vec3(-13.5, 0.45, 8.5), 0, new SettingsData());
        Assert.True(SaveValidator.Validate(save, out var error), error);
        Assert.InRange(save.Counters.IssuedEventKeys.Count, 1, GameSimulation.HistoryRetentionLimit);
        Assert.InRange(save.Counters.LedgerKeys.Count, 1, ResourceLedger.HistoryRetentionLimit);
        Assert.InRange(save.Missions.SeenEvents.Count, 0, MissionTracker.SeenEventRetentionLimit);

        var restored = GameSimulation.FromSave(save, sim.Tuning, MissionsConfig.Defaults());
        Assert.Equal(sim.TickCount, restored.TickCount);
        Assert.Equal(sim.Ledger.Food, restored.Ledger.Food);
        Assert.Equal(sim.Ledger.Silk, restored.Ledger.Silk);
    }

    [Fact]
    public void Ledger_TrimsOldKeys_ButKeepsRecentIdempotency()
    {
        var ledger = new ResourceLedger();
        for (var i = 0; i < 1_000; i++) ledger.TryGrantSilk(1, $"grant-{i}");

        Assert.InRange(ledger.ProcessedKeyCount, 1, ResourceLedger.HistoryRetentionLimit);
        Assert.False(ledger.HasProcessed("grant:grant-0"));
        Assert.True(ledger.HasProcessed("grant:grant-999"));
    }

    [Fact]
    public void SilkProduction_ConsumesFood_AndStopsWhenFoodRunsOut()
    {
        var sim = TestWorld.NewSim(t =>
        {
            t.Initial.Food = 2;
            t.Initial.Silk = 0;
            t.SilkProduction.SilkPerFedWorkerEverySeconds = 1;
            t.SilkProduction.FoodCostPerSilkBatch = 1;
        });

        sim.AdvanceSeconds(10);
        Assert.Equal(2, sim.Ledger.Silk);
        Assert.Equal(0, sim.Ledger.Food);

        sim.AdvanceSeconds(5);
        Assert.Equal(0, sim.Ledger.Food);
        Assert.Equal(2, sim.Ledger.Silk);
    }

    [Fact]
    public void SilkProduction_ScalesWithHatchedHelpers()
    {
        var solo = TestWorld.NewSim(t =>
        {
            t.Initial.Food = 200;
            t.Initial.Silk = 0;
            t.SilkProduction.SilkPerFedWorkerEverySeconds = 2;
            t.SilkProduction.FoodCostPerSilkBatch = 0;
        });
        solo.AdvanceSeconds(8);
        var soloSilk = solo.Ledger.Silk;

        var team = TestWorld.NewSim(t =>
        {
            t.Initial.Food = 200;
            t.Initial.Silk = 20;
            t.SilkProduction.SilkPerFedWorkerEverySeconds = 2;
            t.SilkProduction.FoodCostPerSilkBatch = 0;
        });
        Assert.True(team.TryStartHatch(out _));
        team.AdvanceSeconds(4);
        Assert.Single(team.Workers);
        team.Ledger.Restore(200, 0);
        team.AdvanceSeconds(8);

        Assert.True(team.Ledger.Silk > soloSilk,
            $"team={team.Ledger.Silk} solo={soloSilk}");
    }

    [Fact]
    public void GentleWeather_RestoresBaselineWhenSwitchedOff()
    {
        var baseline = TestWorld.RichTuning();
        var tuning = baseline.Clone();

        tuning.ApplyWeatherProfile(baseline.Weather, true);
        Assert.True(tuning.Weather.ExposedWebDamage <= 25);
        Assert.Equal(0, tuning.Weather.ShelteredWebDamage);
        Assert.True(tuning.Weather.WarningSeconds >= 30);

        tuning.ApplyWeatherProfile(baseline.Weather, false);
        Assert.Equal(baseline.Weather.ExposedWebDamage, tuning.Weather.ExposedWebDamage);
        Assert.Equal(baseline.Weather.ShelteredWebDamage, tuning.Weather.ShelteredWebDamage);
        Assert.Equal(baseline.Weather.WarningSeconds, tuning.Weather.WarningSeconds);
    }
}
