using Webwild.Core;
using Xunit;

namespace Webwild.Core.Tests;

/// <summary>P6 wild-food contracts: deterministic sources, once-per-generation
/// harvests, tick-based regeneration and per-chunk persisted depletion only.</summary>
public class FoodTests
{
    private const int Seed = 20260214;

    private static FoodSourceDefinition FindSource(WorldGenerator generator, out ChunkCoordinate chunk)
    {
        for (var x = -3; x <= 6; x++)
        for (var z = -3; z <= 6; z++)
        {
            var coordinate = new ChunkCoordinate(x, z);
            var sources = generator.GenerateFoodSources(coordinate);
            if (sources.Count == 0) continue;
            chunk = coordinate;
            return sources[0];
        }
        throw new InvalidOperationException("No generated food source found in the sampled area.");
    }

    [Fact]
    public void FoodSources_AreDeterministicGroundedAndDry()
    {
        var generator = new WorldGenerator(Seed);
        var found = 0;
        for (var x = -3; x <= 6; x++)
        for (var z = -3; z <= 6; z++)
        {
            var coordinate = new ChunkCoordinate(x, z);
            if (WorldGenerator.IsAuthoredChunk(coordinate)) continue;
            var sources = generator.GenerateFoodSources(coordinate);
            var again = new WorldGenerator(Seed).GenerateFoodSources(coordinate);
            Assert.Equal(sources.Select(source => source.Id), again.Select(source => source.Id));
            foreach (var source in sources)
            {
                found++;
                Assert.Equal(coordinate, source.Chunk);
                Assert.InRange(source.Position.X, x * WorldChunk.Size, (x + 1) * WorldChunk.Size);
                Assert.InRange(source.Position.Z, z * WorldChunk.Size, (z + 1) * WorldChunk.Size);
                Assert.Equal(generator.HeightAt(source.Position.X, source.Position.Z) + 0.02,
                    source.Position.Y, 9);
                Assert.InRange(source.Amount, 1, 2);
                Assert.False(generator.RiverAt(source.Position.X, source.Position.Z).IsWater,
                    "food must not stand in water");
            }
        }
        Assert.True(found > 5, $"expected several wild sources, got {found}");
    }

    [Fact]
    public void FoodIds_AreUniqueAndStable()
    {
        var generator = new WorldGenerator(Seed);
        var ids = new HashSet<string>(StringComparer.Ordinal);
        for (var x = -5; x <= 5; x++)
        for (var z = -5; z <= 5; z++)
        {
            if (WorldGenerator.IsAuthoredChunk(new ChunkCoordinate(x, z))) continue;
            foreach (var source in generator.GenerateFoodSources(new ChunkCoordinate(x, z)))
            {
                Assert.True(ids.Add(source.Id), $"duplicate food id {source.Id}");
                Assert.StartsWith("food:", source.Id);
            }
        }
        Assert.NotEmpty(ids);
    }

    [Fact]
    public void ChunkHashAndRegeneration_IncludeFoodContent()
    {
        var generator = new WorldGenerator(Seed);
        var coordinate = new ChunkCoordinate(2, 2);
        var first = generator.GenerateChunk(coordinate)!;
        var second = new WorldGenerator(Seed).GenerateChunk(coordinate)!;
        Assert.Equal(first.FoodSources.Select(source => source.Id),
            second.FoodSources.Select(source => source.Id));
        Assert.Equal(first.ContentHash(), second.ContentHash());
    }

    [Fact]
    public void Harvest_IsOncePerGeneration_AndRegenerates()
    {
        var generator = new WorldGenerator(Seed);
        var source = FindSource(generator, out _);
        var sim = TestWorld.NewSim();
        var foodBefore = sim.Ledger.Food;
        Assert.True(sim.IsFoodAvailable(source.Id));

        Assert.True(sim.TryHarvestFood(source, out var food, out var reason), reason);
        Assert.Equal(source.Amount, food);
        Assert.Equal(foodBefore + source.Amount, sim.Ledger.Food);
        Assert.False(sim.IsFoodAvailable(source.Id));
        Assert.True(sim.FoodSecondsRemaining(source.Id) > 0);

        // A second call in the same generation never credits food again.
        Assert.False(sim.TryHarvestFood(source, out _, out var blocked));
        Assert.Contains("regrow", blocked, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(foodBefore + source.Amount, sim.Ledger.Food);

        sim.AdvanceSeconds(sim.Tuning.Food.RegenerationSeconds + 1);
        Assert.True(sim.IsFoodAvailable(source.Id));
        Assert.True(sim.TryHarvestFood(source, out var secondFood, out var secondReason), secondReason);
        Assert.Equal(source.Amount, secondFood);
        Assert.Equal(foodBefore + source.Amount * 2, sim.Ledger.Food);
    }

    [Fact]
    public void DepletedStates_AreTheOnlyPersistedState_AndRoundTrip()
    {
        var generator = new WorldGenerator(Seed);
        var source = FindSource(generator, out var chunk);
        var sim = TestWorld.NewSim();
        Assert.Empty(sim.DepletedFoodStates());

        Assert.True(sim.TryHarvestFood(source, out _, out _));
        var states = sim.DepletedFoodStates();
        var snapshot = Assert.Single(states);
        Assert.Equal(chunk, snapshot.Chunk);
        Assert.Equal(source.Id, snapshot.Save.Id);
        Assert.True(snapshot.Save.AvailableAtTick > sim.TickCount);

        var restored = TestWorld.NewSim();
        restored.ImportFoodStates(chunk, new[] { snapshot.Save });
        Assert.False(restored.IsFoodAvailable(source.Id));

        // The persisted record expires with the simulation clock, not with
        // visibility or another walk.
        restored.AdvanceSeconds(restored.Tuning.Food.RegenerationSeconds + 1);
        Assert.True(restored.IsFoodAvailable(source.Id));
        Assert.Empty(restored.DepletedFoodStates());
    }

    [Fact]
    public void SaveLoad_DoesNotDuplicateACollectedSource()
    {
        var generator = new WorldGenerator(Seed);
        var source = FindSource(generator, out var chunk);
        var sim = TestWorld.NewSim();
        var before = sim.Ledger.Food;
        Assert.True(sim.TryHarvestFood(source, out var amount, out _));
        var data = sim.ExportSave(new Vec3(0, 0.4, 8.5), 0, new SettingsData());
        data.Seed = Seed;
        var snapshot = Assert.Single(sim.DepletedFoodStates());

        var restored = GameSimulation.FromSave(data,
            TestWorld.RichTuning(), MissionsConfig.Defaults());
        restored.ImportFoodStates(chunk, new[] { snapshot.Save });
        Assert.False(restored.IsFoodAvailable(source.Id));
        Assert.False(restored.TryHarvestFood(source, out _, out _));
        Assert.Equal(before + amount, restored.Ledger.Food);
    }

    [Fact]
    public void Import_KeepsLaterStateAndIgnoresExpiredRecords()
    {
        var sim = TestWorld.NewSim();
        var chunk = new ChunkCoordinate(0, 2);
        sim.ImportFoodStates(chunk, new[]
        {
            new FoodSourceStateSave
            {
                Id = "food:0_2:0:berries", Kind = "berries",
                X = 1, Y = 0, Z = 2, AvailableAtTick = sim.TickCount + 100
            },
            new FoodSourceStateSave
            {
                Id = "food:0_2:1:mushrooms", Kind = "mushrooms",
                X = 3, Y = 0, Z = 4, AvailableAtTick = 0
            }
        });
        Assert.False(sim.IsFoodAvailable("food:0_2:0:berries"));
        Assert.True(sim.IsFoodAvailable("food:0_2:1:mushrooms"));

        sim.ImportFoodStates(chunk, new[]
        {
            new FoodSourceStateSave
            {
                Id = "food:0_2:0:berries", Kind = "berries",
                X = 1, Y = 0, Z = 2, AvailableAtTick = sim.TickCount + 10
            }
        });
        Assert.True(sim.FoodSecondsRemaining("food:0_2:0:berries") >
                    sim.Tuning.FixedSimulationTickSeconds * 50);
    }

    [Fact]
    public void NewGardenArchiving_MovesChunkRecordsOutOfTheLiveWorld()
    {
        var root = Path.Combine(Path.GetTempPath(), "webwild-archive-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var worldDirectory = Path.Combine(root, "world");
            var service = new WorldSaveService(worldDirectory, Path.Combine(root, "legacy.json"));
            var sim = TestWorld.NewSim();
            var data = sim.ExportSave(new Vec3(0, 0.4, 8.5), 0, new SettingsData());
            data.Seed = Seed;
            Assert.True(service.Save(data).Success);
            var coordinate = new ChunkCoordinate(0, 2);
            var record = new ChunkChangeSave
            {
                ChunkX = coordinate.X,
                ChunkZ = coordinate.Z,
                WorldSeed = Seed,
                FoodSources = new List<FoodSourceStateSave>
                {
                    new() { Id = "food:0_2:0:berries", Kind = "berries", AvailableAtTick = 500 }
                }
            };
            Assert.True(service.WorldStore.SaveChunkChanges(record).Success);

            Assert.True(service.ArchiveExisting(out var message), message);
            Assert.Null(service.WorldStore.LoadChunkChanges(coordinate, out _));
            var archived = Path.Combine(worldDirectory, "previous-chunks",
                $"chunk_{coordinate.Id}.json");
            Assert.True(File.Exists(archived), $"missing archived record at {archived}");
        }
        finally
        {
            try { Directory.Delete(root, true); } catch { }
        }
    }

    [Fact]
    public void ChunkRecord_RoundTripsFoodState_AndRejectsNegativeTicks()
    {
        var root = Path.Combine(Path.GetTempPath(), "webwild-food-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var store = new WorldSaveStore(directory: root);
            var coordinate = new ChunkCoordinate(2, -1);
            var record = new ChunkChangeSave
            {
                ChunkX = coordinate.X,
                ChunkZ = coordinate.Z,
                WorldSeed = Seed,
                UpdatedUtc = DateTime.UtcNow.ToString("O"),
                FoodSources = new List<FoodSourceStateSave>
                {
                    new()
                    {
                        Id = "food:2_-1:0:berries", Kind = "berries",
                        X = 50, Y = 0.3, Z = -20, AvailableAtTick = 1234
                    }
                }
            };
            var saved = store.SaveChunkChanges(record);
            Assert.True(saved.Success, saved.Error);
            var loaded = store.LoadChunkChanges(coordinate, out var error);
            Assert.NotNull(loaded);
            Assert.Null(error);
            Assert.Equal(Seed, loaded!.WorldSeed);
            var food = Assert.Single(loaded.FoodSources);
            Assert.Equal("food:2_-1:0:berries", food.Id);
            Assert.Equal(1234, food.AvailableAtTick);

            var bad = new ChunkChangeSave
            {
                ChunkX = 0, ChunkZ = 0,
                FoodSources = new List<FoodSourceStateSave>
                {
                    new() { Id = "food:bad", Kind = "berries", AvailableAtTick = -1 }
                }
            };
            Assert.False(WorldSaveStore.ValidateChunkRecord(bad, out var validation));
            Assert.Contains("food", validation, StringComparison.OrdinalIgnoreCase);

            Assert.True(store.DeleteChunkChanges(coordinate));
            Assert.Null(store.LoadChunkChanges(coordinate, out _));
            Assert.Empty(store.ListChangedChunks());
        }
        finally
        {
            try { Directory.Delete(root, true); } catch { }
        }
    }
}
