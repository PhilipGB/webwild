using System.Text.Json;
using Webwild.Core;
using Xunit;

namespace Webwild.Core.Tests;

public class HistoryOrderTests
{
    private static GameSimulation LongSessionSim()
    {
        return TestWorld.NewSim(t =>
        {
            t.Initial.Food = 100;
            t.Initial.Silk = 2_000;
            t.SilkProduction.SilkPerFedWorkerEverySeconds = 0;
        });
    }

    private static void BuildAndRemoveBridge(
        GameSimulation sim, int count, string anchorA = WorldLayout.TwigA, string anchorB = WorldLayout.TwigB)
    {
        for (var i = 0; i < count; i++)
        {
            Assert.True(
                sim.TryBuildWeb(anchorA, anchorB, WebType.Bridge, out var bridge, out var build),
                build.Reason);
            Assert.True(sim.TryDismantleWeb(bridge!.Id, out _, out var remove), remove);
        }
    }

    [Fact]
    public void OrderedHistories_SurviveSerialisedSaveReload_AndContinueEvictingOldestFirst()
    {
        var sim = LongSessionSim();
        BuildAndRemoveBridge(sim, 320);

        // Each cycle emits one build and one remove event plus two ledger keys.
        Assert.Equal(GameSimulation.HistoryRetentionLimit, sim.OrderedIssuedEventKeys.Count);
        Assert.Equal(ResourceLedger.HistoryRetentionLimit, sim.Ledger.OrderedProcessedKeys.Count);
        Assert.Equal(MissionTracker.SeenEventRetentionLimit, sim.Missions.OrderedSeenEventKeys.Count);

        // Leave a bridge in place and add a worker so the save also carries
        // resource and worker state through the same round trip.
        Assert.True(sim.TryBuildWeb(WorldLayout.TwigA, WorldLayout.TwigB, WebType.Bridge, out _, out _));
        Assert.True(sim.TryStartHatch(out var hatchReason), hatchReason);
        sim.AdvanceSeconds(4);
        var worker = Assert.Single(sim.Workers);
        var workerPosition = worker.Position;
        var pathCount = worker.PathNodes.Count;
        var food = sim.Ledger.Food;
        var silk = sim.Ledger.Silk;

        var issuedBefore = sim.OrderedIssuedEventKeys.ToList();
        var ledgerBefore = sim.Ledger.OrderedProcessedKeys.ToList();
        var seenBefore = sim.Missions.OrderedSeenEventKeys.ToList();

        var save = sim.ExportSave(new Vec3(-13.5, 0.45, 8.5), 0, new SettingsData());
        Assert.Equal(issuedBefore, save.Counters.IssuedEventKeys);
        Assert.Equal(ledgerBefore, save.Counters.LedgerKeys);
        Assert.Equal(seenBefore, save.Missions.SeenEvents);

        var json = JsonSerializer.SerializeToUtf8Bytes(save, SaveValidator.JsonOptions);
        var data = JsonSerializer.Deserialize<SaveGameData>(json, SaveValidator.JsonOptions);
        Assert.NotNull(data);
        Assert.True(SaveValidator.Validate(data, out var validation), validation);

        var restored = GameSimulation.FromSave(data!, sim.Tuning, MissionsConfig.Defaults());

        Assert.Equal(issuedBefore, restored.OrderedIssuedEventKeys);
        Assert.Equal(ledgerBefore, restored.Ledger.OrderedProcessedKeys);
        Assert.Equal(seenBefore, restored.Missions.OrderedSeenEventKeys);

        Assert.Equal(food, restored.Ledger.Food);
        Assert.Equal(silk, restored.Ledger.Silk);
        var restoredWorker = Assert.Single(restored.Workers);
        Assert.Equal(worker.Id, restoredWorker.Id);
        Assert.Equal(workerPosition, restoredWorker.Position);
        Assert.Equal(pathCount, restoredWorker.PathNodes.Count);
        Assert.Equal(sim.Missions.ActiveMissionIndex, restored.Missions.ActiveMissionIndex);
        Assert.Equal(sim.Missions.StepIndex, restored.Missions.StepIndex);
        Assert.Equal(sim.Missions.CompletedMissions, restored.Missions.CompletedMissions);

        // New events after reload must evict the oldest retained entries first,
        // exactly as they would without the save/load boundary.
        BuildAndRemoveBridge(restored, 20, WorldLayout.FlowerA, WorldLayout.FlowerB);
        var after = restored.OrderedIssuedEventKeys;
        Assert.Equal(issuedBefore.Skip(40), after.Take(GameSimulation.HistoryRetentionLimit - 40));
        Assert.DoesNotContain(issuedBefore[0], after);
        Assert.Equal(issuedBefore[^1], after[GameSimulation.HistoryRetentionLimit - 41]);

        var ledgerAfter = restored.Ledger.OrderedProcessedKeys;
        Assert.Equal(ledgerBefore.Skip(40), ledgerAfter.Take(ResourceLedger.HistoryRetentionLimit - 40));
        Assert.DoesNotContain(ledgerBefore[0], ledgerAfter);

        var seenAfter = restored.Missions.OrderedSeenEventKeys;
        Assert.Equal(seenBefore.Skip(40), seenAfter.Take(MissionTracker.SeenEventRetentionLimit - 40));
        Assert.DoesNotContain(seenBefore[0], seenAfter);
    }

    [Fact]
    public void LegacyUnorderedHistory_LoadsAndDeduplicatesWithoutDiverging()
    {
        var sim = TestWorld.NewSim();
        var data = sim.ExportSave(new Vec3(-13.5, 0.45, 8.5), 0, new SettingsData());

        // A v0.1.1 save serialised sets, so order is arbitrary and duplicates or
        // blanks may appear. Restore must still produce one coherent queue/set.
        data.Counters.LedgerKeys = new List<string>
        {
            "grant:a", "", "grant:a", "spend:b", "grant:c"
        };
        data.Counters.IssuedEventKeys = new List<string> { "event:2", "", "event:1", "event:2" };
        data.Missions.SeenEvents = new List<string> { "mission:2", "mission:1", "mission:2" };

        Assert.True(SaveValidator.Validate(data, out var validation), validation);
        var restored = GameSimulation.FromSave(data, TestWorld.RichTuning(), MissionsConfig.Defaults());

        Assert.Equal(new[] { "grant:a", "spend:b", "grant:c" }, restored.Ledger.OrderedProcessedKeys);
        Assert.Equal(new[] { "event:2", "event:1" }, restored.OrderedIssuedEventKeys);
        Assert.Equal(new[] { "mission:2", "mission:1" }, restored.Missions.OrderedSeenEventKeys);
        Assert.Equal(3, restored.Ledger.ProcessedKeyCount);
    }

    [Fact]
    public void OversizedLegacyHistory_TrimsFromTheFrontAndKeepsNewestKeys()
    {
        var sim = TestWorld.NewSim();
        var data = sim.ExportSave(new Vec3(-13.5, 0.45, 8.5), 0, new SettingsData());
        data.Counters.LedgerKeys = Enumerable.Range(0, 600).Select(i => $"grant:legacy-{i}").ToList();
        data.Counters.IssuedEventKeys = Enumerable.Range(0, 600).Select(i => $"event:legacy-{i}").ToList();
        data.Missions.SeenEvents = Enumerable.Range(0, 600).Select(i => $"mission:legacy-{i}").ToList();

        Assert.True(SaveValidator.Validate(data, out var validation), validation);
        var restored = GameSimulation.FromSave(data, TestWorld.RichTuning(), MissionsConfig.Defaults());

        Assert.Equal(ResourceLedger.HistoryRetentionLimit, restored.Ledger.OrderedProcessedKeys.Count);
        Assert.DoesNotContain("grant:legacy-0", restored.Ledger.OrderedProcessedKeys);
        Assert.Equal("grant:legacy-599", restored.Ledger.OrderedProcessedKeys[^1]);

        Assert.Equal(GameSimulation.HistoryRetentionLimit, restored.OrderedIssuedEventKeys.Count);
        Assert.DoesNotContain("event:legacy-0", restored.OrderedIssuedEventKeys);
        Assert.Equal("event:legacy-599", restored.OrderedIssuedEventKeys[^1]);
    }
}
