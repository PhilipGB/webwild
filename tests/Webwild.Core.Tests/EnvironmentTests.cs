using Webwild.Core;
using Xunit;

namespace Webwild.Core.Tests;

/// <summary>H4 contracts: the eight environments of generator version 2,
/// cliff faces that are steep enough to require climbing, and the version
/// isolation that keeps version-1 saves regenerating their old terrain.</summary>
public class EnvironmentTests
{
    private const int Seed = 20260214;

    private static WorldGenerator V1() => new(Seed, 1);
    private static WorldGenerator V2() => new(Seed, 2);

    private static double Slope(WorldGenerator generator, double x, double z, double step = 0.5)
    {
        var hx = generator.HeightAt(x + step, z) - generator.HeightAt(x - step, z);
        var hz = generator.HeightAt(x, z + step) - generator.HeightAt(x, z - step);
        return Math.Sqrt(hx * hx + hz * hz) / (2 * step);
    }

    [Fact]
    public void Version1_RegeneratesTheLegacyTerrainExactly()
    {
        // Version 1 must keep the exact pre-H4 height function: sampler
        // noise plus the meadow/moss/bloom offset.
        var sampler = new TerrainSampler(Seed);
        var biomes = new BiomeMap(Seed);
        var generator = V1();
        for (var x = -400; x <= 400; x += 37)
        for (var z = -400; z <= 400; z += 37)
        {
            var expected = sampler.HeightAt(x, z) +
                           BiomeMap.HeightOffset(biomes.WeightsAt(x, z));
            Assert.Equal(expected, generator.BaseHeightAt(x, z), 9);
        }
    }

    [Fact]
    public void Version2_ChangesTerrain_WhileVersion1DoesNot()
    {
        var v1 = V1();
        var v2 = V2();
        var changed = 0;
        for (var x = -600; x <= 600; x += 24)
        for (var z = -600; z <= 600; z += 24)
            if (Math.Abs(v1.BaseHeightAt(x, z) - v2.BaseHeightAt(x, z)) > 0.01) changed++;
        Assert.True(changed > 100,
            $"version 2 changed only {changed} samples; environments are not shaping terrain");
    }

    [Fact]
    public void Version2_WeightsSumToOne_AreDeterministic_AndContinuous()
    {
        var map = V2().Environments!;
        var first = map.WeightsAt(123.4, -567.8);
        var second = map.WeightsAt(123.4, -567.8);
        Assert.Equal(first, second);
        Assert.Equal(1.0, first.Sum, 6);

        for (double x = -300; x <= 300; x += 0.5)
        {
            var a = map.WeightsAt(x, 40.0);
            var b = map.WeightsAt(x + 0.5, 40.0);
            Assert.True(Math.Abs(a.Sum - b.Sum) < 0.02, $"weights jump at x={x}");
            // Each channel moves smoothly so colours and heights never band.
            Assert.True(Math.Abs(a.Forest - b.Forest) < 0.16, $"forest jumps at x={x}");
            Assert.True(Math.Abs(a.Woodland - b.Woodland) < 0.16, $"woodland jumps at x={x}");
            Assert.True(Math.Abs(a.Hills - b.Hills) < 0.16, $"hills jumps at x={x}");
            Assert.True(Math.Abs(a.Cliff - b.Cliff) < 0.16, $"cliff jumps at x={x}");
            Assert.True(Math.Abs(a.Valley - b.Valley) < 0.16, $"valley jumps at x={x}");
        }
    }

    [Fact]
    public void Version2_AllEightEnvironments_OccurNearTheOrigin()
    {
        var map = V2().Environments!;
        var seen = new HashSet<EnvironmentKind>();
        for (var x = -900; x <= 900; x += 6)
        for (var z = -900; z <= 900; z += 6)
        {
            if (WorldGenerator.AuthoredRects.Any(rect => rect.DistanceOutside(x, z) < 20))
                continue;
            seen.Add(map.WeightsAt(x, z).Dominant);
            if (seen.Count == 8) break;
            if (seen.Count == 8) break;
        }
        Assert.True(seen.Count >= 8,
            "expected all eight environments within ±900 m, saw: " +
            string.Join(",", seen.Select(EnvironmentMap.KindName)));
    }

    [Fact]
    public void Version2_HeightsStayFiniteAndBounded()
    {
        var generator = V2();
        for (var x = -1500; x <= 1500; x += 17)
        for (var z = -1500; z <= 1500; z += 17)
        {
            var height = generator.HeightAt(x, z);
            Assert.True(double.IsFinite(height), $"height not finite at {x},{z}");
            Assert.InRange(height, -30, 30);
        }
    }

    [Fact]
    public void Version2_CliffsHaveRealSteepFaces_AndShelvesBetweenThem()
    {
        var generator = V2();
        var map = generator.Environments!;
        var faceFound = false;
        var shelfFound = false;
        for (var x = -800; x <= 800 && !(faceFound && shelfFound); x += 4)
        for (var z = -800; z <= 800 && !(faceFound && shelfFound); z += 4)
        {
            if (map.WeightsAt(x, z).Cliff < 0.6) continue;
            var slope = Slope(generator, x, z, 0.5);
            if (slope > 1.4) faceFound = true;   // ≈55°+, unwalkable, climbable
            if (slope < 0.35) shelfFound = true; // a flat shelf to stand on
        }
        Assert.True(faceFound, "cliff regions contain no steep climbable face");
        Assert.True(shelfFound, "cliff regions contain no shelf between faces");
    }

    [Fact]
    public void Version2_MeadowTerrainRemainsGentlyWalkable()
    {
        var generator = V2();
        var map = generator.Environments!;
        for (var x = -800; x <= 800; x += 3)
        for (var z = -800; z <= 800; z += 3)
        {
            var weights = map.WeightsAt(x, z);
            if (weights.Dominant != EnvironmentKind.Meadow) continue;
            // Boundary strips mix two environments; pure meadow is what must
            // stay gentle, mixed edges may inherit rocky steepness.
            if (weights.Cliff + weights.Hills > 0.15) continue;
            var slope = Slope(generator, x, z, 0.5);
            Assert.True(slope < 0.65, $"meadow too steep at {x},{z}: slope={slope:0.00}");
        }
    }

    [Fact]
    public void Version2_WetlandsAndShoresStayFlat()
    {
        var generator = V2();
        var map = generator.Environments!;
        var checkedWet = 0;
        for (var x = -800; x <= 800; x += 3)
        for (var z = -800; z <= 800; z += 3)
        {
            var weights = map.WeightsAt(x, z);
            if (weights.Dominant is not (EnvironmentKind.Wetland or EnvironmentKind.Shore))
                continue;
            // Mixed edges near cliff country may carry a face; pure wet ground
            // is what must stay flat.
            if (weights.Cliff + weights.Hills > 0.15) continue;
            if (++checkedWet > 200) break;
            var slope = Slope(generator, x, z, 0.5);
            Assert.True(slope < 0.65, $"wet ground too steep at {x},{z}: slope={slope:0.00}");
        }
        Assert.True(checkedWet > 0, "no wetland or shore ground found to check");
    }

    [Fact]
    public void Version2_AuthoredEdgesKeepMeadowMargins()
    {
        var generator = V2();
        foreach (var rect in WorldGenerator.AuthoredRects)
        {
            for (var x = rect.MinX - 12; x <= rect.MaxX + 12; x += 2)
            for (var z = rect.MinZ - 12; z <= rect.MaxZ + 12; z += 2)
            {
                if (rect.DistanceOutside(x, z) > WorldGenerator.AuthoredBlendMetres + 1)
                    continue;
                Assert.Equal(EnvironmentKind.Meadow,
                    generator.EnvironmentAt(x, z).Dominant);
            }
        }
    }

    [Fact]
    public void Version2_RiversKeepDryWalkableBanks()
    {
        var generator = V2();
        for (var x = -600; x <= 600; x += 13)
        {
            var river = generator.RiverAt(x, 0);
            if (!river.HasRiver) continue;
            var bankZ = river.CenterZ + river.HalfWidth + RiverMap.BankTaper + 1.5;
            var bankY = generator.HeightAt(x, bankZ);
            Assert.True(bankY > river.WaterLevel + 0.15,
                $"v2 bank under water at x={x}: bank={bankY:0.00} water={river.WaterLevel:0.00}");
            for (var z = river.CenterZ - river.HalfWidth - 9;
                 z <= river.CenterZ + river.HalfWidth + 9; z += 0.5)
            {
                if (Math.Abs(z - river.CenterZ) < river.HalfWidth + 0.5) continue;
                var slope = Slope(generator, x, z, 0.5);
                Assert.True(slope < 1.1,
                    $"v2 river bank too steep at x={x} z={z:0.0}: slope={slope:0.00}");
            }
        }
    }

    [Fact]
    public void Version2_ChunksRegenerateIdentically_WithEnvironmentGrid()
    {
        var generator = V2();
        var first = generator.GenerateChunk(new ChunkCoordinate(12, -34))!;
        var second = generator.GenerateChunk(new ChunkCoordinate(12, -34))!;
        Assert.NotNull(first.Environments);
        Assert.Equal(first.ContentHash(), second.ContentHash());
        Assert.Equal(first.Environments![6, 6], second.Environments![6, 6]);
        Assert.True(first.Props.Count > 0, "v2 chunk has no dressing");
        foreach (var prop in first.Props)
        {
            var ground = generator.HeightAt(prop.Position.X, prop.Position.Z);
            Assert.InRange(prop.Position.Y, ground - 0.3, ground + 0.3);
            Assert.True(Slope(generator, prop.Position.X, prop.Position.Z, 0.5) < 0.85,
                $"prop on a cliff face at {prop.Position.X:0.0},{prop.Position.Z:0.0}");
        }
        Assert.All(first.Props, prop => Assert.False(
            generator.RiverAt(prop.Position.X, prop.Position.Z).IsWater));
    }

    [Fact]
    public void Version2_VocabularyIsDressed_WithKnownPropKinds()
    {
        var generator = V2();
        var allowed = new HashSet<string>(StringComparer.Ordinal)
        {
            "grass", "tallgrass", "clover", "clover_tall", "fern", "pebble",
            "pebble_cluster", "flat_stone", "small_log", "moss_patch",
            "mushroom", "mushroom_blue", "boulder", "moss_rock", "soil_mound",
            "bush", "broad_leaf", "leaf_pile", "fallen_leaf", "daisy",
            "reed", "marsh_grass", "cattail", "sapling", "flower_yellow",
            "flower_pink", "flower_purple"
        };
        var kinds = new HashSet<string>(StringComparer.Ordinal);
        for (var cx = -12; cx <= 12; cx += 3)
        for (var cz = -12; cz <= 12; cz += 3)
        {
            var data = generator.GenerateChunk(new ChunkCoordinate(cx, cz));
            if (data is null) continue;
            foreach (var prop in data.Props)
            {
                kinds.Add(prop.Kind);
                Assert.Contains(prop.Kind, allowed);
            }
        }
        Assert.True(kinds.Count >= 12,
            $"v2 dressing is too thin: {string.Join(",", kinds)}");
    }

    [Fact]
    public void Version2_FoodStillGrowsOnEveryBiome()
    {
        var generator = V2();
        var sources = 0;
        for (var cx = -10; cx <= 10; cx += 2)
        for (var cz = -10; cz <= 10; cz += 2)
        {
            var data = generator.GenerateChunk(new ChunkCoordinate(cx, cz));
            if (data is null) continue;
            foreach (var food in data.FoodSources)
            {
                sources++;
                var ground = generator.HeightAt(food.Position.X, food.Position.Z);
                Assert.InRange(food.Position.Y, ground - 0.1, ground + 0.2);
                Assert.False(generator.RiverAt(food.Position.X, food.Position.Z).IsWater);
            }
        }
        Assert.True(sources > 20, $"v2 food is too scarce: {sources}");
    }

    [Fact]
    public void Navigator_AvoidsClimbingCliffFaces()
    {
        var navigator = new WorkerNavigator(Seed, 2);
        var map = navigator.Generator.Environments!;
        // Find a cliff wall: two plateau points either side of a face.
        Vec3? cliffStart = null;
        Vec3? cliffEnd = null;
        for (var x = -700; x <= 700 && cliffStart is null; x += 5)
        for (var z = -700; z <= 700 && cliffStart is null; z += 5)
        {
            if (map.WeightsAt(x, z).Cliff < 0.55) continue;
            if (Slope(navigator.Generator, x, z, 0.5) > 1.4)
            {
                cliffStart = new Vec3(x - 12, 0, z);
                cliffEnd = new Vec3(x + 12, 0, z);
            }
        }
        Assert.NotNull(cliffStart);
        Assert.NotNull(cliffEnd);
        var start = cliffStart.Value;
        var end = cliffEnd.Value;
        var route = navigator.FindDryDirectRoute(start, end);
        // A helper either finds a way around the wall or waits — it never
        // walks the face itself.
        if (route is not null)
        {
            foreach (var point in route.Points)
            {
                var slope = Slope(navigator.Generator, point.X, point.Z, 1.0);
                Assert.True(slope < 1.0,
                    $"route climbs a cliff face at {point.X:0.0},{point.Z:0.0}: slope={slope:0.00}");
            }
        }
    }

    [Fact]
    public void GeneratorVersion_RoundTripsThroughSaveAndManifest()
    {
        var tuning = new Tuning { Seed = Seed, GeneratorVersion = 2 };
        var sim = new GameSimulation(tuning);
        Assert.Equal(2, sim.Tuning.GeneratorVersion);
        var data = sim.ExportSave(new Vec3(0, 0, 0), 0, new SettingsData());
        Assert.Equal(2, data.GeneratorVersion);

        var manifest = WorldSaveMigrator.ToManifest(data);
        Assert.Equal(2, manifest.GeneratorVersion);
        var extracted = WorldSaveMigrator.ExtractGame(manifest);
        Assert.Equal(2, extracted.GeneratorVersion);
        Assert.True(SaveValidator.Validate(data, out var valid), valid);

        var reloaded = GameSimulation.FromSave(data, sim.Tuning, sim.MissionsConfig);
        Assert.Equal(2, reloaded.Tuning.GeneratorVersion);
    }

    [Fact]
    public void LegacySaves_LoadAsVersion1_WithoutMigration()
    {
        var sim = new GameSimulation();
        var data = sim.ExportSave(new Vec3(0, 0, 0), 0, new SettingsData());
        data.GeneratorVersion = 0; // absent field deserialises as 0/absent
        Assert.True(SaveValidator.Validate(data, out _));
        var manifest = WorldSaveMigrator.ToManifest(data);
        Assert.Equal(1, manifest.GeneratorVersion);
    }

    [Fact]
    public void UnsupportedGeneratorVersions_AreRejected()
    {
        var sim = new GameSimulation();
        var data = sim.ExportSave(new Vec3(0, 0, 0), 0, new SettingsData());
        data.GeneratorVersion = WorldGenerator.CurrentGeneratorVersion + 1;
        Assert.False(SaveValidator.Validate(data, out var payloadError));
        Assert.Equal("The save uses an unsupported world generator.", payloadError);

        var manifest = WorldSaveMigrator.ToManifest(sim.ExportSave(
            new Vec3(0, 0, 0), 0, new SettingsData()));
        manifest.GeneratorVersion = 99;
        Assert.False(WorldSaveMigrator.Validate(manifest, out var manifestError));
        Assert.Contains("generator", manifestError, StringComparison.Ordinal);
    }
}
