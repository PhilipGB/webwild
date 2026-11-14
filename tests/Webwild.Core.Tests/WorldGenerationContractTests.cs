using Webwild.Core;
using Xunit;

namespace Webwild.Core.Tests;

/// <summary>
/// Deterministic generation contracts. Terrain sampling is coordinate-based, so
/// the same seed always produces identical heights regardless of exploration
/// order, chunk load order or process lifetime, and neighbouring chunks agree
/// along shared boundaries without special-casing.
/// </summary>
public class WorldGenerationContractTests
{
    private static double[,] SampleGrid(TerrainSampler sampler, ChunkCoordinate chunk)
    {
        var heights = new double[WorldChunk.TerrainResolution, WorldChunk.TerrainResolution];
        for (var x = 0; x < WorldChunk.TerrainResolution; x++)
        for (var z = 0; z < WorldChunk.TerrainResolution; z++)
            heights[x, z] = sampler.HeightAt(
                chunk.X * WorldChunk.Size + x * WorldChunk.TerrainSpacing,
                chunk.Z * WorldChunk.Size + z * WorldChunk.TerrainSpacing);
        return heights;
    }

    [Fact]
    public void SameSeed_ProducesIdenticalTerrain()
    {
        var a = new TerrainSampler(4242);
        var b = new TerrainSampler(4242);
        var random = new Random(11);
        for (var i = 0; i < 200; i++)
        {
            var x = random.NextDouble() * 20_000 - 10_000;
            var z = random.NextDouble() * 20_000 - 10_000;
            Assert.Equal(a.HeightAt(x, z), b.HeightAt(x, z), 12);
        }
    }

    [Fact]
    public void DifferentSeeds_DifferSomewhere()
    {
        var a = new TerrainSampler(1);
        var b = new TerrainSampler(2);
        var differences = 0;
        for (var i = 0; i < 100; i++)
        {
            var x = i * 7.3;
            if (Math.Abs(a.HeightAt(x, x * 0.5) - b.HeightAt(x, x * 0.5)) > 1e-9) differences++;
        }
        Assert.True(differences > 50, $"Seeds produced nearly identical terrain ({differences}/100 differ).");
    }

    [Fact]
    public void SharedChunkBoundaries_AgreeBetweenNeighbours()
    {
        var sampler = new TerrainSampler(77);
        var here = new ChunkCoordinate(0, 0);
        var east = new ChunkCoordinate(1, 0);
        var south = new ChunkCoordinate(0, 1);
        var corner = new ChunkCoordinate(1, 1);

        var heightGrid = SampleGrid(sampler, here);
        var eastGrid = SampleGrid(sampler, east);
        // Here's last column is at world x = 24.0; east's first column is too.
        for (var z = 0; z < WorldChunk.TerrainResolution; z++)
        {
            var worldZ = z * WorldChunk.TerrainSpacing;
            Assert.Equal(heightGrid[WorldChunk.TerrainResolution - 1, z], eastGrid[0, z], 12);
            Assert.Equal(
                sampler.HeightAt(24.0, worldZ),
                heightGrid[WorldChunk.TerrainResolution - 1, z], 12);
        }
        // South edge (world z = 24) and the diagonal corner agree with both neighbours.
        var southGrid = SampleGrid(sampler, south);
        var cornerGrid = SampleGrid(sampler, corner);
        for (var x = 0; x < WorldChunk.TerrainResolution; x++)
        {
            Assert.Equal(heightGrid[x, WorldChunk.TerrainResolution - 1], southGrid[x, 0], 12);
            Assert.Equal(
                sampler.HeightAt(x * WorldChunk.TerrainSpacing, 24.0),
                heightGrid[x, WorldChunk.TerrainResolution - 1], 12);
        }
        Assert.Equal(
            heightGrid[WorldChunk.TerrainResolution - 1, WorldChunk.TerrainResolution - 1],
            cornerGrid[0, 0], 12);
        Assert.Equal(southGrid[WorldChunk.TerrainResolution - 1, 0], cornerGrid[0, 0], 12);
        Assert.Equal(eastGrid[0, WorldChunk.TerrainResolution - 1], cornerGrid[0, 0], 12);
    }

    [Fact]
    public void RegeneratedChunk_HasIdenticalContentHash()
    {
        var sampler = new TerrainSampler(31337);
        var chunk = new ChunkCoordinate(2, -3);
        var build = () =>
        {
            var heights = SampleGrid(sampler, chunk);
            return new ChunkData { Coordinate = chunk, Heights = heights };
        };
        var first = build();
        var second = build();
        Assert.Equal(first.ContentHash(), second.ContentHash());
        Assert.NotEqual(first.ContentHash(), new ChunkData
        {
            Coordinate = chunk.Offset(1, 0),
            Heights = SampleGrid(sampler, chunk.Offset(1, 0))
        }.ContentHash());
    }

    [Fact]
    public void ChunkData_HeightAtWorld_InterpolatesGrid()
    {
        var sampler = new TerrainSampler(5);
        var chunk = new ChunkCoordinate(-1, 2);
        var data = new ChunkData { Coordinate = chunk, Heights = SampleGrid(sampler, chunk) };
        // Grid points reproduce sampler values exactly.
        Assert.Equal(
            sampler.HeightAt(chunk.X * WorldChunk.Size, chunk.Z * WorldChunk.Size),
            data.HeightAtWorld(chunk.X * WorldChunk.Size, chunk.Z * WorldChunk.Size), 12);
        // Interior points stay within the corner height range.
        var centreX = chunk.X * WorldChunk.Size + 10.0;
        var centreZ = chunk.Z * WorldChunk.Size + 10.0;
        var cornerMin = new[]
        {
            sampler.HeightAt(chunk.X * WorldChunk.Size, chunk.Z * WorldChunk.Size),
            sampler.HeightAt(chunk.X * WorldChunk.Size + WorldChunk.Size, chunk.Z * WorldChunk.Size),
            sampler.HeightAt(chunk.X * WorldChunk.Size, chunk.Z * WorldChunk.Size + WorldChunk.Size),
            sampler.HeightAt(chunk.X * WorldChunk.Size + WorldChunk.Size, chunk.Z * WorldChunk.Size + WorldChunk.Size)
        }.Min();
        var cornerMax = new[]
        {
            sampler.HeightAt(chunk.X * WorldChunk.Size, chunk.Z * WorldChunk.Size),
            sampler.HeightAt(chunk.X * WorldChunk.Size + WorldChunk.Size, chunk.Z * WorldChunk.Size),
            sampler.HeightAt(chunk.X * WorldChunk.Size, chunk.Z * WorldChunk.Size + WorldChunk.Size),
            sampler.HeightAt(chunk.X * WorldChunk.Size + WorldChunk.Size, chunk.Z * WorldChunk.Size + WorldChunk.Size)
        }.Max();
        var centre = data.HeightAtWorld(centreX, centreZ);
        Assert.InRange(centre, cornerMin, cornerMax);
    }

    [Fact]
    public void TerrainHeights_AreGentleAndFinite()
    {
        var sampler = new TerrainSampler(9);
        for (var x = -500; x <= 500; x += 37)
        for (var z = -500; z <= 500; z += 41)
        {
            var height = sampler.HeightAt(x * 3.1, z * 2.7);
            Assert.True(double.IsFinite(height));
            // Baseline grassland stays walkable: no mountain walls or pits.
            Assert.InRange(height, -6.0, 6.0);
        }
    }
}
