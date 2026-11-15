using Webwild.Core;
using Xunit;

namespace Webwild.Core.Tests;

/// <summary>P4 river contracts: deterministic bands, carved channels, safe
/// banks, chunk-owned water and unique bank-to-bank crossings.</summary>
public class RiverTests
{
    private const int Seed = 20260214;

    [Fact]
    public void RiverField_IsDeterministic_AndContinuousAcrossChunkEdges()
    {
        var map = new RiverMap(Seed);
        for (var z = -520.0; z <= 520.0; z += 37)
        {
            var a = map.Sample(23.999, z);
            var b = map.Sample(24.001, z);
            Assert.True(a.HasRiver == b.HasRiver);
            if (!a.HasRiver) continue;
            Assert.Equal(a.Index, b.Index);
            Assert.InRange(Math.Abs(a.CenterZ - b.CenterZ), 0, 0.05);
            Assert.InRange(Math.Abs(a.WaterLevel - b.WaterLevel), 0, 0.05);
        }
    }

    [Fact]
    public void RiverChannel_IsBelowWater_AndBanksStayDryAndWalkable()
    {
        var generator = new WorldGenerator(Seed);
        var map = generator.Rivers;
        foreach (var x in new[] { 0.0, 37.0, -212.0, 411.0 })
        {
            var river = map.Sample(x, RiverMap.CenterAt(0, x));
            Assert.True(river.HasRiver);
            Assert.Equal(0, river.Index);
            Assert.Equal(river.CenterZ, map.Sample(x, river.CenterZ).CenterZ, 9);

            // Centre bed is safely below the surface.
            var bed = generator.HeightAt(x, river.CenterZ);
            Assert.True(bed < river.WaterLevel - 0.7,
                $"x={x} bed={bed:0.###} water={river.WaterLevel:0.###}");

            // Outside the bank taper the ground is back above water, and the
            // channel walls never exceed the player's slope limit.
            var bankOffset = river.HalfWidth + RiverMap.BankTaper + 1.5;
            var northBank = generator.HeightAt(x, river.CenterZ - bankOffset);
            var southBank = generator.HeightAt(x, river.CenterZ + bankOffset);
            var northWater = map.Sample(x, river.CenterZ - bankOffset).WaterLevel;
            var southWater = map.Sample(x, river.CenterZ + bankOffset).WaterLevel;
            Assert.True(northBank > northWater + 0.15,
                $"north bank={northBank:0.###} water={northWater:0.###}");
            Assert.True(southBank > southWater + 0.15,
                $"south bank={southBank:0.###} water={southWater:0.###}");

            var previous = generator.HeightAt(x, river.CenterZ - 12);
            for (var d = -12.0; d <= 12.0; d += 0.5)
            {
                var height = generator.HeightAt(x, river.CenterZ + d);
                Assert.True(Math.Abs(height - previous) < 0.6,
                    $"1 m span near x={x} d={d}: {previous:0.###} -> {height:0.###}");
                previous = height;
            }
        }
    }

    [Fact]
    public void AuthoredFootprints_AreNeverRivered()
    {
        var generator = new WorldGenerator(Seed);
        foreach (var rect in WorldGenerator.AuthoredRects)
        {
            for (var x = rect.MinX; x <= rect.MaxX; x += 4)
            for (var z = rect.MinZ; z <= rect.MaxZ; z += 4)
            {
                Assert.False(generator.RiverAt(x, z).HasRiver,
                    $"river at authored point {x},{z}");
                Assert.Equal(WorldGenerator.AuthoredGroundHeight,
                    generator.HeightAt(x, z), 12);
            }
        }
    }

    [Fact]
    public void SafeBankNear_RecoversAWaderToDryGround()
    {
        var map = new RiverMap(Seed);
        foreach (var x in new[] { 0.0, 37.0, -212.0 })
        {
            var river = map.Sample(x, RiverMap.CenterAt(0, x));
            var safe = map.SafeBankNear(x, river.CenterZ);
            var atSafe = map.Sample(safe.X, safe.Z);
            Assert.False(atSafe.IsHazard);
            Assert.True(safe.Y > atSafe.WaterLevel + 0.5,
                $"safe bank={safe.Y:0.###} water={atSafe.WaterLevel:0.###}");
        }
    }

    [Fact]
    public void WaterQuads_AreOwnedOnceAndChunkDeterministic()
    {
        var generator = new WorldGenerator(Seed);
        var seen = new HashSet<(double X, double Z)>();
        var total = 0;
        // Chunks around the river at z ~ 62 (chunk z = 2), plus its neighbours.
        for (var cx = -6; cx <= 6; cx++)
        for (var cz = 1; cz <= 3; cz++)
        {
            var coordinate = new ChunkCoordinate(cx, cz);
            var data = generator.GenerateChunk(coordinate)!;
            foreach (var quad in data.WaterQuads)
            {
                Assert.InRange(quad.CenterX, cx * WorldChunk.Size, (cx + 1) * WorldChunk.Size);
                Assert.InRange(quad.CenterZ, cz * WorldChunk.Size, (cz + 1) * WorldChunk.Size);
                Assert.True(seen.Add((quad.CenterX, quad.CenterZ)),
                    $"water quad drawn twice at {quad.CenterX},{quad.CenterZ}");
                total++;
            }
            var again = new WorldGenerator(Seed).GenerateChunk(coordinate)!;
            Assert.Equal(data.WaterQuads.Count, again.WaterQuads.Count);
            Assert.Equal(data.ContentHash(), again.ContentHash());
        }
        Assert.True(total > 100, $"expected a continuous river, got {total} quads");
    }

    [Fact]
    public void Crossings_RepeatAlongEveryRiver_AndMeetTheBanks()
    {
        var generator = new WorldGenerator(Seed);
        for (var crossing = -2; crossing <= 2; crossing++)
        {
            var x = crossing * RiverMap.CrossingSpacing;
            var centerZ = RiverMap.CenterAt(0, x);
            var owner = ChunkCoordinate.OfWorldPoint(x, centerZ);
            var data = generator.GenerateChunk(owner);
            Assert.NotNull(data);
            var bridge = Assert.Single(data!.Bridges);
            Assert.Equal($"river_0_{crossing}", bridge.Id);
            Assert.Equal(x, bridge.X, 9);
            Assert.Equal(centerZ, bridge.CenterZ, 9);
            Assert.True(bridge.HalfSpan > bridge.HalfWidth + RiverMap.BankTaper);
            Assert.Equal(generator.HeightAt(x, centerZ - bridge.HalfSpan),
                bridge.NorthGroundY, 9);
            Assert.Equal(generator.HeightAt(x, centerZ + bridge.HalfSpan),
                bridge.SouthGroundY, 9);
            var northWater = generator.Rivers.Sample(x, bridge.CenterZ - bridge.HalfSpan).WaterLevel;
            var southWater = generator.Rivers.Sample(x, bridge.CenterZ + bridge.HalfSpan).WaterLevel;
            Assert.True(bridge.NorthGroundY > northWater + 0.1 &&
                        bridge.SouthGroundY > southWater + 0.1,
                "crossing banks must stand above their local water");
        }
    }

    [Fact]
    public void Props_NeverStandInRiverWater()
    {
        var generator = new WorldGenerator(Seed);
        var checkedProps = 0;
        for (var cx = -2; cx <= 2; cx++)
        for (var cz = 1; cz <= 3; cz++)
        {
            var data = generator.GenerateChunk(new ChunkCoordinate(cx, cz));
            if (data is null) continue;
            foreach (var prop in data.Props)
            {
                Assert.False(generator.RiverAt(prop.Position.X, prop.Position.Z).IsWater,
                    $"prop {prop.Kind} at {prop.Position.X},{prop.Position.Z} in water");
                checkedProps++;
            }
        }
        Assert.True(checkedProps > 20, $"expected props on the banks, got {checkedProps}");
    }

    [Fact]
    public void EveryBridge_IsOwnedByExactlyOneChunk()
    {
        var generator = new WorldGenerator(Seed);
        var ids = new Dictionary<string, int>(StringComparer.Ordinal);
        for (var cx = -4; cx <= 4; cx++)
        for (var cz = 0; cz <= 4; cz++)
        {
            var data = generator.GenerateChunk(new ChunkCoordinate(cx, cz));
            if (data is null) continue;
            foreach (var bridge in data.Bridges)
                ids[bridge.Id] = ids.GetValueOrDefault(bridge.Id) + 1;
        }
        Assert.NotEmpty(ids);
        Assert.All(ids.Values, count => Assert.Equal(1, count));
    }
}
