using Webwild.Core;
using Xunit;

namespace Webwild.Core.Tests;

/// <summary>H2 contracts: a helper physically gathers procedural food, carries
/// it home and credits the ledger only on delivery, exactly once.</summary>
public class WorkerForagingTests
{
    private static GameSimulation NewSim() =>
        TestWorld.NewSim(tuning => tuning.SilkProduction.SilkPerFedWorkerEverySeconds = 0);

    private static WorkerState Hatch(GameSimulation sim)
    {
        var before = sim.Workers.Count;
        Assert.True(sim.TryStartHatch(out var reason), reason);
        sim.AdvanceSeconds(4);
        Assert.Equal(before + 1, sim.Workers.Count);
        return sim.Workers[^1];
    }

    private static FoodSourceDefinition FindFood(GameSimulation sim)
    {
        var generator = new WorldGenerator(sim.Tuning.Seed);
        foreach (var coordinate in new[]
                 {
                     new ChunkCoordinate(2, 0), new ChunkCoordinate(2, 1),
                     new ChunkCoordinate(1, 1), new ChunkCoordinate(0, 2),
                     new ChunkCoordinate(2, -1), new ChunkCoordinate(3, 0)
                 })
        {
            var source = generator.GenerateChunk(coordinate)?.FoodSources.FirstOrDefault();
            if (source is not null) return source;
        }
        throw new Xunit.Sdk.XunitException("No generated food source found for the test seed");
    }

    private static void AdvanceUntil(GameSimulation sim, Func<bool> condition, int seconds = 300)
    {
        for (var i = 0; i < seconds * 5 && !condition(); i++) sim.AdvanceSeconds(0.2);
        Assert.True(condition(), "condition did not become true");
    }

    [Fact]
    public void Helper_GathersWildFood_CarriesItHomeAndDeliversOnce()
    {
        var sim = NewSim();
        var worker = Hatch(sim);
        var source = FindFood(sim);
        var foodBefore = sim.Ledger.Food;

        Assert.True(sim.TryAssignFoodGather(worker.Id, source.Id, out var reason), reason);
        Assert.True(worker.TargetFoodId == source.Id);
        Assert.Contains(sim.Events, e => e.Type == "WorkerForageAssigned");

        AdvanceUntil(sim, () => worker.CarriedFood > 0 || worker.TargetFoodId is null);
        Assert.Equal(source.Amount, worker.CarriedFood);
        Assert.False(sim.IsFoodAvailable(source.Id));
        Assert.Equal(foodBefore, sim.Ledger.Food);
        Assert.Contains(sim.Events, e => e.Type == "WorkerFoodCollected");

        AdvanceUntil(sim, () => worker.TargetFoodId is null);
        Assert.Equal(foodBefore + source.Amount, sim.Ledger.Food);
        Assert.Equal(0, worker.CarriedFood);
        Assert.Single(sim.Events, e => e.Type == "WorkerFoodDelivered");

        sim.AdvanceSeconds(120);
        Assert.Equal(foodBefore + source.Amount, sim.Ledger.Food);
    }

    [Fact]
    public void SaveAndLoadMidDelivery_CreditsTheCarriedFoodExactlyOnce()
    {
        var sim = NewSim();
        var worker = Hatch(sim);
        var source = FindFood(sim);
        var foodBefore = sim.Ledger.Food;
        Assert.True(sim.TryAssignFoodGather(worker.Id, source.Id, out var reason), reason);
        AdvanceUntil(sim, () => worker.CarriedFood > 0);

        var data = sim.ExportSave(new Vec3(10, 0.55, 8.5), 0, new SettingsData());
        data.Seed = sim.Tuning.Seed;
        Assert.True(SaveValidator.Validate(data, out var error), error);
        var saved = data.Workers.Single();
        Assert.Equal(source.Amount, saved.CarriedFood);
        Assert.False(string.IsNullOrWhiteSpace(saved.FoodDeliveryId));

        var restored = GameSimulation.FromSave(data,
            TestWorld.RichTuning(t =>
            {
                t.Seed = sim.Tuning.Seed;
                t.SilkProduction.SilkPerFedWorkerEverySeconds = 0;
            }), MissionsConfig.Defaults());
        var restoredWorker = Assert.Single(restored.Workers);
        Assert.Equal(source.Amount, restoredWorker.CarriedFood);
        AdvanceUntil(restored, () => restoredWorker.TargetFoodId is null);
        Assert.Equal(foodBefore + source.Amount, restored.Ledger.Food);
        restored.AdvanceSeconds(60);
        Assert.Equal(foodBefore + source.Amount, restored.Ledger.Food);
    }

    [Fact]
    public void DepletedFood_WaitsForRegrowthBeforeCrediting()
    {
        var sim = NewSim();
        var worker = Hatch(sim);
        var source = FindFood(sim);
        var foodBefore = sim.Ledger.Food;
        Assert.True(sim.TryHarvestFood(source, out _, out var harvestReason), harvestReason);

        Assert.True(sim.TryAssignFoodGather(worker.Id, source.Id, out _));
        Assert.Equal(WorkerFoodPhase.WaitingForRegrowth, worker.FoodPhase);
        Assert.Equal(foodBefore + source.Amount, sim.Ledger.Food);
        sim.AdvanceSeconds(30);
        Assert.Equal(foodBefore + source.Amount, sim.Ledger.Food);

        AdvanceUntil(sim, () => worker.TargetFoodId is null, 240);
        Assert.Equal(foodBefore + source.Amount * 2, sim.Ledger.Food);
        Assert.True(sim.IsFoodAvailable(source.Id) == false);
    }

    [Fact]
    public void TwoHelpersOnTheSameSource_DoNotDuplicateFood()
    {
        var sim = NewSim();
        sim.Ledger.Restore(30, 30);
        var first = Hatch(sim);
        sim.Ledger.Restore(30, 30);
        var second = Hatch(sim);
        var source = FindFood(sim);
        var foodBefore = sim.Ledger.Food;
        Assert.True(sim.TryAssignFoodGather(first.Id, source.Id, out var firstReason), firstReason);
        Assert.True(sim.TryAssignFoodGather(second.Id, source.Id, out var secondReason), secondReason);
        sim.AdvanceSeconds(240);
        Assert.Equal(foodBefore + source.Amount, sim.Ledger.Food);
    }

    [Fact]
    public void SummonWhileCarrying_ReleaseDeliversTheSameFoodOnce()
    {
        var sim = NewSim();
        var worker = Hatch(sim);
        var source = FindFood(sim);
        var foodBefore = sim.Ledger.Food;
        Assert.True(sim.TryAssignFoodGather(worker.Id, source.Id, out var reason), reason);
        AdvanceUntil(sim, () => worker.CarriedFood > 0);
        var summon = new Vec3(40, 0.55, 8);
        Assert.True(sim.TrySummonWorker(worker.Id, summon, RegionIds.GardenClearing, out _));
        Assert.Equal(source.Amount, worker.CarriedFood);
        AdvanceUntil(sim, () => !worker.Travelling);
        Assert.Equal(source.Amount, worker.CarriedFood);
        Assert.True(sim.ReleaseWorkerSummon(worker.Id, out _));
        AdvanceUntil(sim, () => worker.TargetFoodId is null, 400);
        Assert.Equal(foodBefore + source.Amount, sim.Ledger.Food);
        Assert.Equal(0, worker.CarriedFood);
    }

    [Fact]
    public void AssigningUnknownFood_IsRefused()
    {
        var sim = NewSim();
        var worker = Hatch(sim);
        Assert.False(sim.TryAssignFoodGather(worker.Id, "food:0_0:9:berries", out var reason));
        Assert.Contains("not part", reason, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void MapMarkers_PersistAndRemove()
    {
        var sim = NewSim();
        var source = FindFood(sim);
        Assert.True(sim.MarkFoodSource(source, out var reason), reason);
        Assert.Single(sim.MapMarkers);
        Assert.False(sim.MarkFoodSource(source, out _));

        var data = sim.ExportSave(new Vec3(10, 0.55, 8.5), 0, new SettingsData());
        data.Seed = sim.Tuning.Seed;
        Assert.True(SaveValidator.Validate(data, out var error), error);
        var restored = GameSimulation.FromSave(data,
            TestWorld.RichTuning(t =>
            {
                t.Seed = sim.Tuning.Seed;
                t.SilkProduction.SilkPerFedWorkerEverySeconds = 0;
            }), MissionsConfig.Defaults());
        var marker = Assert.Single(restored.MapMarkers);
        Assert.Equal(WorldMarkerKind.Food, marker.Kind);
        Assert.Equal(source.Id, marker.FoodId);
        Assert.True(restored.RemoveMapMarker(marker.Id));
        Assert.Empty(restored.MapMarkers);
    }
}
