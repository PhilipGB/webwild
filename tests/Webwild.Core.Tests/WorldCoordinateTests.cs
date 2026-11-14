using Webwild.Core;
using Xunit;

namespace Webwild.Core.Tests;

/// <summary>
/// Stable world coordinate contracts: chunk identity, floor semantics around
/// zero, round-trips and origin rebasing. These rules must hold at every scale,
/// including far negative coordinates, because the never-ending world relies on
/// them for saves, streaming and maps.
/// </summary>
public class WorldCoordinateTests
{
    [Fact]
    public void ChunkSize_UsesAgreedValue()
    {
        Assert.Equal(24.0, WorldChunk.Size);
        Assert.Equal(13, WorldChunk.TerrainResolution);
        Assert.Equal(2.0, WorldChunk.TerrainSpacing);
    }

    [Theory]
    [InlineData(0.0, 0.0, 0, 0)]
    [InlineData(23.99, 0.0, 0, 0)]
    [InlineData(24.0, -0.01, 1, -1)]
    [InlineData(-0.01, -0.01, -1, -1)]
    [InlineData(-24.0, 24.0, -1, 1)]
    [InlineData(-24.01, 48.0, -2, 2)]
    [InlineData(12345.6, -9876.5, 514, -412)]
    public void ChunkOfWorldPoint_FloorsAcrossZero(double x, double z, long cx, long cz)
    {
        var chunk = ChunkCoordinate.OfWorldPoint(x, z);
        Assert.Equal(cx, chunk.X);
        Assert.Equal(cz, chunk.Z);
        // A point exactly on a boundary belongs to the chunk whose local range
        // includes it; local coordinates stay in [0, Size).
        var pos = WorldPos.FromAbsolute(x, 0, z);
        Assert.True(pos.IsValid);
        Assert.Equal(chunk, pos.Chunk);
    }

    [Fact]
    public void WorldPos_RoundTripsNearBoundary()
    {
        foreach (var point in new[]
                 {
                     new Vec3(0, 0, 0), new Vec3(23.999999, 1.5, 0.000001),
                     new Vec3(-0.000001, -2, 23.999999), new Vec3(-24.5, 3, -0.5),
                     new Vec3(1_000_000.25, 0.5, -999_999.75)
                 })
        {
            var pos = WorldPos.FromAbsolute(point);
            Assert.True(pos.IsValid, $"pos invalid for {point}");
            var back = pos.Absolute;
            Assert.Equal(point.X, back.X, 9);
            Assert.Equal(point.Y, back.Y, 9);
            Assert.Equal(point.Z, back.Z, 9);
        }
    }

    [Fact]
    public void WorldPos_LocalCoordinatesAlwaysInsideChunk()
    {
        var random = new Random(7);
        for (var i = 0; i < 500; i++)
        {
            var x = random.NextDouble() * 400_000 - 200_000;
            var z = random.NextDouble() * 400_000 - 200_000;
            var pos = WorldPos.FromAbsolute(x, 0, z);
            Assert.InRange(pos.LocalX, 0, WorldChunk.Size);
            Assert.InRange(pos.LocalZ, 0, WorldChunk.Size);
        }
    }

    [Fact]
    public void ChunkCoordinate_PreservesSignAndId()
    {
        var chunk = new ChunkCoordinate(-3, 12);
        Assert.Equal("-3_12", chunk.Id);
        Assert.True(ChunkCoordinate.TryParse("-3_12", out var parsed));
        Assert.Equal(chunk, parsed);
        Assert.False(ChunkCoordinate.TryParse("not_a_chunk", out _));
        Assert.False(ChunkCoordinate.TryParse("1234567890123_0", out _));
    }

    [Fact]
    public void ChebyshevDistance_SupportsStreamingRadius()
    {
        var centre = new ChunkCoordinate(10, -4);
        Assert.Equal(0, centre.ChebyshevDistance(centre));
        Assert.Equal(3, centre.ChebyshevDistance(new ChunkCoordinate(13, -7)));
        Assert.Equal(2, centre.ChebyshevDistance(new ChunkCoordinate(8, -2)));
    }

    [Fact]
    public void OriginRebase_KeepsEngineSpaceSmall()
    {
        var far = new Vec3(1_048_576.5, 3, -2_097_152.25);
        var origin = WorldOrigin.Near(far);
        var engine = origin.ToEngineSpace(far);
        Assert.InRange(Math.Abs(engine.X), 0, 64);
        Assert.InRange(Math.Abs(engine.Z), 0, 64);
        // Round trip restores the logical value exactly (double arithmetic).
        var logical = origin.ToLogical(engine);
        Assert.Equal(far.X, logical.X, 9);
        Assert.Equal(far.Z, logical.Z, 9);
        Assert.False(origin.NeedsRebase(far));
        var zero = WorldOrigin.AtZero;
        Assert.True(zero.NeedsRebase(far));
    }

    [Fact]
    public void LandmarkOrigins_ConvertLegacyCoordinatesToWorld()
    {
        var gardenLocal = WorldLayout.PlayerSpawn;
        var gardenWorld = LandmarkCatalog.ToWorld(LandmarkCatalog.Garden, gardenLocal);
        Assert.Equal(gardenLocal.X, gardenWorld.Absolute.X, 9);
        Assert.Equal(gardenLocal.Z, gardenWorld.Absolute.Z, 9);

        var reedwaterLocal = ReedwaterLayout.PlayerSpawn;
        var reedwaterWorld = LandmarkCatalog.ToWorld(LandmarkCatalog.Reedwater, reedwaterLocal);
        Assert.Equal(96 + reedwaterLocal.X, reedwaterWorld.Absolute.X, 9);
        Assert.Equal(reedwaterLocal.Z, reedwaterWorld.Absolute.Z, 9);
        // Round trip back to local coordinates.
        var roundTrip = LandmarkCatalog.ToLocal(LandmarkCatalog.Reedwater, reedwaterWorld.Absolute);
        Assert.Equal(reedwaterLocal.X, roundTrip.X, 9);
        Assert.Equal(reedwaterLocal.Z, roundTrip.Z, 9);
    }

    [Fact]
    public void DeterministicHash_IsStableAndWellMixed()
    {
        Assert.Equal(DeterministicHash.Hash(1.5), DeterministicHash.Hash(1.5));
        Assert.NotEqual(DeterministicHash.Hash(1.5), DeterministicHash.Hash(1.6));
        Assert.NotEqual(DeterministicHash.Hash("a"), DeterministicHash.Hash("b"));
        Assert.Equal(DeterministicHash.Hash("seed"), DeterministicHash.Hash("seed"));
        // Unit range contract.
        for (var i = 0; i < 100; i++)
        {
            var unit = DeterministicHash.Unit(DeterministicHash.Mix((ulong)i));
            Assert.InRange(unit, 0, 1);
        }
    }
}
