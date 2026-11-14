using Webwild.Core;
using Xunit;

namespace Webwild.Core.Tests;

/// <summary>
/// World generator contracts for P1 grassland: authored-footprint exclusion,
/// seam-free heights against the garden meadow level, gentle walkable slopes,
/// stable prop generation and identical regeneration at any distance.
/// </summary>
public class WorldGeneratorTests
{
    private static WorldGenerator New() => new(20260214);

    [Fact]
    public void AuthoredFootprint_ChunksAreSkipped()
    {
        var generator = New();
        foreach (var chunk in WorldGenerator.AuthoredFootprint)
            Assert.Null(generator.GenerateChunk(chunk));
        // Immediate neighbours generate normally.
        Assert.NotNull(generator.GenerateChunk(new ChunkCoordinate(1, 0)));
        Assert.NotNull(generator.GenerateChunk(new ChunkCoordinate(-2, -1)));
        Assert.NotNull(generator.GenerateChunk(new ChunkCoordinate(0, 1)));
    }

    [Fact]
    public void InsideAuthoredRect_HeightIsMeadowLevel()
    {
        var generator = New();
        Assert.Equal(WorldGenerator.AuthoredGroundHeight, generator.HeightAt(0, 0), 12);
        Assert.Equal(WorldGenerator.AuthoredGroundHeight, generator.HeightAt(-23.9, 23.9), 12);
        // Just outside the rect the blend keeps the surface near the meadow level.
        var edge = generator.HeightAt(WorldGenerator.AuthoredMaxX + 0.1, 0);
        Assert.InRange(edge, -0.05, 0.05);
    }

    [Fact]
    public void BlendRing_IsContinuousAndMonotoneTowardNoise()
    {
        var generator = New();
        var step = 0.5;
        var previous = generator.HeightAt(WorldGenerator.AuthoredMaxX, 40.0);
        for (var x = WorldGenerator.AuthoredMaxX; x <= WorldGenerator.AuthoredMaxX + WorldGenerator.AuthoredBlendMetres + 2; x += step)
        {
            var height = generator.HeightAt(x, 40.0);
            Assert.True(double.IsFinite(height));
            // The ramp never jumps more than a walking-friendly amount per metre.
            Assert.InRange(Math.Abs(height - previous), 0, 0.35);
            previous = height;
        }
        // Beyond the ring the pure noise height is reached.
        var far = generator.HeightAt(WorldGenerator.AuthoredMaxX + WorldGenerator.AuthoredBlendMetres + 5, 40.0);
        var noise = new TerrainSampler(20260214).HeightAt(
            WorldGenerator.AuthoredMaxX + WorldGenerator.AuthoredBlendMetres + 5, 40.0);
        Assert.Equal(noise, far, 12);
    }

    [Fact]
    public void SharedChunkBoundaries_GenerateIdenticalHeights()
    {
        var generator = New();
        var west = generator.GenerateChunk(new ChunkCoordinate(0, 2))!;
        var east = generator.GenerateChunk(new ChunkCoordinate(1, 2))!;
        for (var z = 0; z < WorldChunk.TerrainResolution; z++)
            Assert.Equal(west.Heights[WorldChunk.TerrainResolution - 1, z], east.Heights[0, z], 12);
    }

    [Fact]
    public void TerrainSlopes_AreWalkable()
    {
        var generator = New();
        var random = new Random(5);
        for (var i = 0; i < 300; i++)
        {
            var x = random.NextDouble() * 2400 - 1200;
            var z = random.NextDouble() * 2400 - 1200;
            var h1 = generator.HeightAt(x, z);
            var h2 = generator.HeightAt(x + 0.5, z);
            var h3 = generator.HeightAt(x, z + 0.5);
            foreach (var delta in new[] { Math.Abs(h2 - h1) / 0.5, Math.Abs(h3 - h1) / 0.5 })
            {
                Assert.True(double.IsFinite(delta));
                // Max ~26° slope anywhere in the P1 meadow.
                Assert.True(delta < 0.5, $"slope {delta:0.00} at ({x:0},{z:0})");
            }
        }
    }

    [Fact]
    public void GeneratedChunks_RegenerateIdentically()
    {
        var first = New().GenerateChunk(new ChunkCoordinate(3, -7))!;
        var second = new WorldGenerator(20260214).GenerateChunk(new ChunkCoordinate(3, -7))!;
        Assert.Equal(first.ContentHash(), second.ContentHash());
        // A different seed generates different content somewhere nearby.
        var other = new WorldGenerator(1).GenerateChunk(new ChunkCoordinate(3, -7))!;
        Assert.NotEqual(first.ContentHash(), other.ContentHash());
    }

    [Fact]
    public void Props_AreDeterministicOutsideFootprintAndGrounded()
    {
        var generator = New();
        var chunk = new ChunkCoordinate(2, -2);
        var data = generator.GenerateChunk(chunk)!;
        Assert.NotEmpty(data.Props);
        foreach (var prop in data.Props)
        {
            var worldX = prop.Position.X;
            var worldZ = prop.Position.Z;
            Assert.InRange(worldX, chunk.X * WorldChunk.Size, (chunk.X + 1) * WorldChunk.Size);
            Assert.InRange(worldZ, chunk.Z * WorldChunk.Size, (chunk.Z + 1) * WorldChunk.Size);
            // Props never sit inside the authored garden rect.
            Assert.True(worldX < WorldGenerator.AuthoredMinX || worldX > WorldGenerator.AuthoredMaxX ||
                        worldZ < WorldGenerator.AuthoredMinZ || worldZ > WorldGenerator.AuthoredMaxZ,
                $"prop at {worldX:0.0},{worldZ:0.0} inside authored rect");
            var ground = generator.HeightAt(worldX, worldZ);
            Assert.InRange(prop.Position.Y, ground - 0.3, ground + 0.3);
        }
        // Identical regeneration includes props.
        var again = new WorldGenerator(20260214).GenerateChunk(chunk)!;
        Assert.Equal(data.Props.Count, again.Props.Count);
        Assert.Equal(data.ContentHash(), again.ContentHash());
    }

    [Fact]
    public void VeryDistantChunks_GenerateStably()
    {
        var generator = New();
        foreach (var coordinate in new[]
                 {
                     new ChunkCoordinate(1000, 1000), new ChunkCoordinate(-1000, 1000),
                     new ChunkCoordinate(1000, -1000), new ChunkCoordinate(-1000, -1000),
                     new ChunkCoordinate(41666, -41666)
                 })
        {
            var chunk = generator.GenerateChunk(coordinate);
            Assert.NotNull(chunk);
            foreach (var height in chunk!.Heights)
                Assert.True(double.IsFinite(height));
            var repeat = New().GenerateChunk(coordinate)!;
            Assert.Equal(chunk.ContentHash(), repeat.ContentHash());
        }
    }
}

/// <summary>
/// Streaming lifecycle: bounded active set, deterministic nearest-first load
/// order, retention band prevents thrash, and unloading/reloading a chunk is a
/// pure generator operation.
/// </summary>
public class WorldStreamerTests
{
    private static readonly ChunkCoordinate Start = new(0, 0);

    private static ChunkStreamPlan Step(WorldStreamer streamer, ChunkCoordinate centre) =>
        streamer.Update(centre);

    [Fact]
    public void InitialUpdate_LoadsRingAroundStartOrderedNearestFirst()
    {
        var streamer = new WorldStreamer(3, 5);
        var plan = Step(streamer, Start);
        // 7×7 ring minus 4 authored chunks.
        Assert.Equal(49 - 4, plan.ToLoad.Count);
        // Nearest first: all distance-1 chunks precede distance-3 chunks.
        var distances = plan.ToLoad.Select(c => c.ChebyshevDistance(Start)).ToList();
        Assert.Equal(distances, distances.OrderBy(d => d).ToList());
        Assert.Equal(streamer.ActiveCount, streamer.Active.Count);
    }

    [Fact]
    public void AuthoredChunks_AreNeverStreamed()
    {
        var streamer = new WorldStreamer(3, 5);
        Step(streamer, Start);
        foreach (var chunk in WorldGenerator.AuthoredFootprint)
            Assert.False(streamer.IsLoaded(chunk));
        // Walking into the footprint does not request it either.
        var plan = Step(streamer, new ChunkCoordinate(0, 0));
        Assert.DoesNotContain(new ChunkCoordinate(-1, 0), plan.ToLoad);
        Assert.DoesNotContain(new ChunkCoordinate(0, -1), plan.ToLoad);
    }

    [Fact]
    public void SecondUpdate_IsStable_WhenPlayerStays()
    {
        var streamer = new WorldStreamer(3, 5);
        Step(streamer, Start);
        var plan = Step(streamer, Start);
        Assert.Empty(plan.ToLoad);
        Assert.Empty(plan.ToUnload);
    }

    [Fact]
    public void MovingOneChunk_LoadsOnlyNewEdge()
    {
        var streamer = new WorldStreamer(3, 5);
        Step(streamer, Start);
        var plan = Step(streamer, Start.Offset(1, 0));
        // The new east edge column: seven chunks, all three steps from the new centre.
        Assert.Equal(7, plan.ToLoad.Count);
        Assert.All(plan.ToLoad, c => Assert.Equal(3, c.ChebyshevDistance(Start.Offset(1, 0))));
        Assert.Empty(plan.ToUnload);
    }

    [Fact]
    public void RetentionBand_PreventsUnloadThrash()
    {
        var streamer = new WorldStreamer(3, 5);
        Step(streamer, Start);
        Step(streamer, Start.Offset(2, 0));
        // Walk back and forth across the load edge; nothing unloads while inside
        // the retention radius.
        for (var i = 0; i < 6; i++)
        {
            var plan = Step(streamer, Start.Offset(i % 2, 0));
            Assert.Empty(plan.ToUnload);
        }
        // Beyond the unload radius chunks are retired deterministically; each
        // retired chunk is far from the new centre, and the old centre itself
        // is dropped even though it was loaded a moment ago.
        var far = Step(streamer, Start.Offset(9, 0));
        Assert.NotEmpty(far.ToUnload);
        Assert.All(far.ToUnload, c => Assert.True(c.ChebyshevDistance(Start.Offset(9, 0)) > 5));
        Assert.False(streamer.IsLoaded(Start));
    }

    [Fact]
    public void AfterEveryUpdate_TheLoadRingIsComplete()
    {
        var streamer = new WorldStreamer(3, 5);
        var random = new Random(9);
        for (var i = 0; i < 100; i++)
        {
            var centre = Start.Offset(random.Next(-15, 15), random.Next(-15, 15));
            Step(streamer, centre);
            foreach (var required in streamer.RequiredFor(centre))
                Assert.True(streamer.IsLoaded(required),
                    $"load-ring chunk {required.Id} missing after update");
        }
    }

    [Fact]
    public void ActiveSet_IsBounded()
    {
        var streamer = new WorldStreamer(3, 5);
        var random = new Random(3);
        for (var i = 0; i < 400; i++)
        {
            Step(streamer, Start.Offset(random.Next(-20, 20), random.Next(-20, 20)));
            // Retention keeps the unload radius populated, so the bound is the
            // full unload square: (2·5+1)² = 121, never more.
            Assert.True(streamer.ActiveCount <= 121,
                $"active set grew to {streamer.ActiveCount}");
        }
    }

    [Fact]
    public void DiagonalCornerMovement_PreparesAllFourQuadrants()
    {
        var streamer = new WorldStreamer(3, 5);
        Step(streamer, Start);
        var centre = Start.Offset(2, 2);
        var plan = Step(streamer, centre);
        // The never-loaded quadrants are all queued, nearest first.
        Assert.Contains(plan.ToLoad, c => c == Start.Offset(5, 2));
        Assert.Contains(plan.ToLoad, c => c == Start.Offset(2, 5));
        Assert.Contains(plan.ToLoad, c => c == Start.Offset(5, 5));
        var distances = plan.ToLoad.Select(c => c.ChebyshevDistance(centre)).ToList();
        Assert.Equal(distances, distances.OrderBy(d => d).ToList());
        Assert.All(distances, d => Assert.InRange(d, 1, 3));
    }

    [Fact]
    public void UnloadAndReload_IsPureGeneration()
    {
        var generator = new WorldGenerator(99);
        var streamer = new WorldStreamer(2, 3);
        Step(streamer, Start);
        var first = generator.GenerateChunk(new ChunkCoordinate(2, 0))!;
        var hash = first.ContentHash();
        Step(streamer, Start.Offset(6, 0));
        Assert.False(streamer.IsLoaded(new ChunkCoordinate(2, 0)));
        Step(streamer, Start);
        // Regenerating after unload returns identical content.
        var again = generator.GenerateChunk(new ChunkCoordinate(2, 0))!;
        Assert.Equal(hash, again.ContentHash());
    }
}
