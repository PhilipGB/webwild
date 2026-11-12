using Webwild.Core;
using Xunit;

namespace Webwild.Core.Tests;

public class BridgeGeometryTests
{
    private static AnchorDefinition Anchor(string id) =>
        WorldLayout.CreateAnchors().Single(a => a.Id == id);

    [Fact]
    public void RavineBridge_WestToEast_PutsBothRampsOnSolidBanks()
    {
        var west = Anchor(WorldLayout.GapWest);
        var east = Anchor(WorldLayout.GapEast);

        var approach = BridgeGeometry.ComputeApproachPoints(
            west.Position, east.Position,
            WorldLayout.BridgeGroundPointFor(west.Id),
            WorldLayout.BridgeGroundPointFor(east.Id));

        Assert.True(approach.A.X <= -WorldLayout.RavineHalfWidth,
            $"west ramp x={approach.A.X}");
        Assert.True(approach.B.X >= WorldLayout.RavineHalfWidth,
            $"east ramp x={approach.B.X}");
        Assert.Equal(0, approach.A.Y, 3);
        Assert.Equal(0, approach.B.Y, 3);
    }

    [Fact]
    public void RavineBridge_EastToWest_UsesTheSameBanksRegardlessOfSelectionOrder()
    {
        var west = Anchor(WorldLayout.GapWest);
        var east = Anchor(WorldLayout.GapEast);

        // Anchor A/B and the authored points swap with the player's click order.
        var approach = BridgeGeometry.ComputeApproachPoints(
            east.Position, west.Position,
            WorldLayout.BridgeGroundPointFor(east.Id),
            WorldLayout.BridgeGroundPointFor(west.Id));

        Assert.True(approach.A.X >= WorldLayout.RavineHalfWidth,
            $"east-first ramp x={approach.A.X}");
        Assert.True(approach.B.X <= -WorldLayout.RavineHalfWidth,
            $"west-second ramp x={approach.B.X}");
        Assert.Equal(WorldLayout.GapEastGround, approach.A);
        Assert.Equal(WorldLayout.GapWestGround, approach.B);
    }

    [Fact]
    public void ZAxisBridge_InfersRampsAlongZAndOutsideBothAnchors()
    {
        var a = new Vec3(-9.5, 1.25, -1.4);
        var b = new Vec3(-9.5, 1.25, 1.6);

        var approach = BridgeGeometry.ComputeApproachPoints(a, b);

        Assert.Equal(a.X, approach.A.X, 3);
        Assert.Equal(b.X, approach.B.X, 3);
        Assert.True(approach.A.Z < a.Z - 0.5, $"a ramp z={approach.A.Z}");
        Assert.True(approach.B.Z > b.Z + 0.5, $"b ramp z={approach.B.Z}");
        Assert.True(Math.Abs(approach.A.X) > WorldLayout.RavineHalfWidth);
        Assert.True(Math.Abs(approach.B.X) > WorldLayout.RavineHalfWidth);
    }

    [Fact]
    public void AuthoredBankPoint_WinsWhenInferenceWouldPointAtTheRavine()
    {
        var west = Anchor(WorldLayout.GapWest);
        // Even if the other anchor is also west, the authored point keeps this
        // ramp on the west bank instead of extending towards the ravine.
        var other = new Vec3(-9.5, 1.25, 1.6);

        var inferred = BridgeGeometry.ComputeApproachPoints(west.Position, other);
        var authored = BridgeGeometry.ComputeApproachPoints(
            west.Position, other, WorldLayout.BridgeGroundPointFor(west.Id));

        Assert.True(inferred.A.X > west.Position.X);
        Assert.Equal(WorldLayout.GapWestGround, authored.A);
        Assert.True(authored.A.X < -WorldLayout.RavineHalfWidth);
    }

    [Fact]
    public void RampLengthIsClamped_AndCoincidentAnchorsDoNotThrow()
    {
        var point = new Vec3(4, 0.55, 4);
        var approach = BridgeGeometry.ComputeApproachPoints(point, point, rampLength: 0);
        Assert.True(double.IsFinite(approach.A.X));
        Assert.True(double.IsFinite(approach.B.X));
        Assert.NotEqual(approach.A, approach.B);
    }
}
