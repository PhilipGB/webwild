using Webwild.Core;
using Xunit;

namespace Webwild.Core.Tests;

/// <summary>P3 biome contracts: deterministic continuous biome fields, safe
/// slopes and biome-aware chunk content.</summary>
public class BiomeTests
{
    private const int Seed = 20260214;

    [Fact]
    public void Weights_AreDeterministic_AndSumToOne()
    {
        var map = new BiomeMap(Seed);
        foreach (var (x, z) in new[]
                 {
                     (0.0, 0.0), (120.0, -44.0), (-730.0, 512.0), (1900.0, 40.0)
                 })
        {
            var a = map.WeightsAt(x, z);
            var b = map.WeightsAt(x, z);
            Assert.Equal(a, b);
            Assert.Equal(1.0, a.Meadow + a.Moss + a.Bloom, 12);
            Assert.InRange(a.Meadow, 0, 1);
            Assert.InRange(a.Moss, 0, 1);
            Assert.InRange(a.Bloom, 0, 1);
        }
    }

    [Fact]
    public void WeightField_IsContinuous()
    {
        var map = new BiomeMap(Seed);
        // A 0.5 m step can never flip the mix by more than a small amount.
        for (var x = -300.0; x <= 300.0; x += 37)
        {
            var a = map.WeightsAt(x, 20);
            var b = map.WeightsAt(x + 0.5, 20);
            Assert.InRange(b.Moss - a.Moss, -0.12, 0.12);
            Assert.InRange(b.Bloom - a.Bloom, -0.12, 0.12);
        }
    }

    [Fact]
    public void AllThreeBiomes_OccurInTheExplorableWorld()
    {
        var map = new BiomeMap(Seed);
        var seen = new HashSet<BiomeKind>();
        for (var x = -600.0; x <= 600.0; x += 16)
        for (var z = -600.0; z <= 600.0; z += 16)
            seen.Add(map.WeightsAt(x, z).Dominant);
        Assert.Equal(3, seen.Count);
        Assert.Contains(BiomeKind.Meadow, seen);
        Assert.Contains(BiomeKind.Moss, seen);
        Assert.Contains(BiomeKind.Bloom, seen);
    }

    [Fact]
    public void BiomeHeights_StayWalkable()
    {
        var generator = new WorldGenerator(Seed);
        var steepest = 0.0;
        for (var x = -240.0; x <= 240.0; x += 4)
        for (var z = -240.0; z <= 240.0; z += 4)
        {
            var h = generator.HeightAt(x, z);
            var dx = Math.Abs(generator.HeightAt(x + 1, z) - h);
            var dz = Math.Abs(generator.HeightAt(x, z + 1) - h);
            steepest = Math.Max(steepest, Math.Max(dx, dz));
        }
        // FloorMaxAngle is 55 degrees (rise/run 1.43); generated ground keeps a
        // wide safety margin so uphill movement never turns into a wall.
        Assert.True(steepest < 0.6, $"steepest 1 m rise was {steepest:0.###} m");
    }

    [Fact]
    public void BiomeContent_ChangesChunkHash_ButRegeneratesIdentically()
    {
        var generator = new WorldGenerator(Seed);
        // Find two generated chunks with different dominant biomes.
        ChunkData? first = null;
        ChunkData? second = null;
        for (var x = 0; x < 40 && second is null; x++)
        for (var z = 0; z < 40 && second is null; z++)
        {
            var chunk = generator.GenerateChunk(new ChunkCoordinate(x, z));
            if (chunk is null) continue;
            first ??= chunk;
            if (chunk.Biomes[6, 6].Dominant != first.Biomes[6, 6].Dominant)
                second = chunk;
        }
        Assert.NotNull(first);
        Assert.NotNull(second);
        Assert.NotEqual(first!.ContentHash(), second!.ContentHash());
        Assert.Equal(first.ContentHash(),
            generator.GenerateChunk(first.Coordinate)!.ContentHash());
        Assert.Equal(second.ContentHash(),
            generator.GenerateChunk(second.Coordinate)!.ContentHash());
    }

    [Fact]
    public void Palette_BlendsBiomeColours()
    {
        var moss = TerrainPalette.ColorOf(new BiomeWeights(0, 1, 0));
        var bloom = TerrainPalette.ColorOf(new BiomeWeights(0, 0, 1));
        Assert.Equal(TerrainPalette.Moss, moss);
        Assert.Equal(TerrainPalette.Bloom, bloom);
        Assert.NotEqual(moss, bloom);
        var half = TerrainPalette.ColorOf(new BiomeWeights(0.5, 0.5, 0));
        Assert.InRange(half.X, Math.Min(moss.X, TerrainPalette.Meadow.X),
            Math.Max(moss.X, TerrainPalette.Meadow.X));
    }
}
