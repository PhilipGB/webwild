using Webwild.Core;
using Xunit;

namespace Webwild.Core.Tests;

public class BridgeLayoutTests
{
    private static void AssertFrameIsFiniteAndOrthonormal(BridgeFrame frame)
    {
        Assert.True(frame.Position.IsFinite);
        Assert.True(frame.Along.IsFinite && frame.Up.IsFinite && frame.Across.IsFinite);
        Assert.InRange(frame.Along.Length, 0.999, 1.001);
        Assert.InRange(frame.Up.Length, 0.999, 1.001);
        Assert.InRange(frame.Across.Length, 0.999, 1.001);
        Assert.InRange(Math.Abs(Vec3.Dot(frame.Along, frame.Up)), 0, 0.001);
        Assert.InRange(Math.Abs(Vec3.Dot(frame.Along, frame.Across)), 0, 0.001);
        Assert.InRange(Math.Abs(Vec3.Dot(frame.Up, frame.Across)), 0, 0.001);
        Assert.True(frame.Up.Y > 0);
    }

    [Fact]
    public void SpanSag_IsSharedAndMatchesTheWalkableDeckContract()
    {
        var a = new Vec3(-4, 1.2, 0);
        var b = new Vec3(4, 1.2, 0);
        var plan = BridgeLayout.Build(a, b, null, null, "bridge_001");
        Assert.Equal(0.42, plan.Sag, 6);
        Assert.Equal(0.42, BridgeGeometry.ComputeSag(8.0), 6);
        var middle = BridgeGeometry.EvaluateSpan(a, b, 0.5);
        Assert.Equal(0.78, middle.Y, 6);
        Assert.Equal(-4, BridgeGeometry.EvaluateSpan(a, b, 0).X, 6);
        Assert.Equal(4, BridgeGeometry.EvaluateSpan(a, b, 1).X, 6);
    }

    [Fact]
    public void Planks_AreEvenlySpacedAndCycleThroughThreeVariants()
    {
        var plan = BridgeLayout.Build(
            new Vec3(-3, 1.0, 1.0), new Vec3(3, 1.0, 1.0), null, null, "bridge_002");
        Assert.False(plan.IsDegenerate);
        Assert.True(plan.Planks.Count >= 20);
        Assert.Equal(new[] { 0, 1, 2 }, plan.Planks.Take(3).Select(p => p.VariantIndex));
        for (var i = 1; i < plan.Planks.Count; i++)
        {
            var step = Vec3.Distance(
                plan.Planks[i - 1].Frame.Position,
                plan.Planks[i].Frame.Position);
            Assert.InRange(step, 0.17, BridgeLayout.PlankSpacing + 0.03);
        }
        Assert.All(plan.Planks, plank => AssertFrameIsFiniteAndOrthonormal(plank.Frame));
    }

    [Theory]
    [InlineData(-5.0, 0.0, 5.0, 0.0, 1.0, 0.0)]
    [InlineData(5.0, 0.0, -5.0, 0.0, -1.0, 0.0)]
    [InlineData(0.0, -4.0, 0.0, 4.0, 0.0, 1.0)]
    [InlineData(-4.0, -4.0, 4.0, 4.0, 1.0, 1.0)]
    public void PlankTravelAxis_FollowsEitherAnchorOrder(
        double ax, double az, double bx, double bz, double expectedX, double expectedZ)
    {
        var plan = BridgeLayout.Build(
            new Vec3(ax, 1.0, az), new Vec3(bx, 1.0, bz), null, null, "bridge_direction");
        Assert.NotEmpty(plan.Planks);
        var mid = plan.Planks[plan.Planks.Count / 2].Frame;
        AssertFrameIsFiniteAndOrthonormal(mid);
        if (expectedX != 0) Assert.True(Math.Sign(mid.Along.X) == Math.Sign(expectedX), $"along={mid.Along}");
        if (expectedZ != 0) Assert.True(Math.Sign(mid.Along.Z) == Math.Sign(expectedZ), $"along={mid.Along}");
        // Local +Z is the across-bridge axis, so it must stay perpendicular to travel.
        Assert.InRange(Math.Abs(Vec3.Dot(mid.Along, mid.Across)), 0, 0.001);
    }

    [Fact]
    public void AuthoredBankApproaches_AreIncludedAsWalkableRampPlanks()
    {
        var a = new Vec3(-2, 1.2, 0);
        var b = new Vec3(2, 1.2, 0);
        var groundA = new Vec3(-3.2, 0, 0);
        var groundB = new Vec3(3.2, 0, 0);
        var plan = BridgeLayout.Build(a, b, groundA, groundB, "bridge_003");
        Assert.Equal(groundA, plan.Centerline[0]);
        Assert.Equal(groundB, plan.Centerline[^1]);
        Assert.Contains(plan.Planks, plank => plank.OnRamp);
        Assert.Contains(plan.Planks, plank => !plank.OnRamp);
        Assert.All(plan.Planks, plank => AssertFrameIsFiniteAndOrthonormal(plank.Frame));
    }

    [Fact]
    public void SameBridgeId_IsDeterministicAcrossRebuilds()
    {
        var a = new Vec3(-4, 1.1, -2);
        var b = new Vec3(3, 1.4, 4);
        var one = BridgeLayout.Build(a, b, null, null, "bridge_004");
        var two = BridgeLayout.Build(a, b, null, null, "bridge_004");
        Assert.Equal(one.Planks.Count, two.Planks.Count);
        for (var i = 0; i < one.Planks.Count; i++)
        {
            Assert.Equal(one.Planks[i].Frame.Position, two.Planks[i].Frame.Position);
            Assert.Equal(one.Planks[i].LateralOffset, two.Planks[i].LateralOffset);
            Assert.Equal(one.Planks[i].YawOffset, two.Planks[i].YawOffset);
        }
        var other = BridgeLayout.Build(a, b, null, null, "bridge_005");
        Assert.Equal(one.Planks.Count, other.Planks.Count);
        Assert.NotEqual(one.Planks[0].LateralOffset, other.Planks[0].LateralOffset);
    }

    [Fact]
    public void DegenerateAndShortSpans_DoNotProduceNaNsOrEmptyGeometry()
    {
        var same = BridgeLayout.Build(
            new Vec3(1, 0.5, 1), new Vec3(1, 0.5, 1), null, null, "bridge_short");
        Assert.True(same.IsDegenerate);
        Assert.Single(same.Planks);
        AssertFrameIsFiniteAndOrthonormal(same.Planks[0].Frame);

        var tiny = BridgeLayout.Build(
            new Vec3(1, 0.5, 1), new Vec3(1.01, 0.5, 1), null, null, "bridge_tiny");
        Assert.NotEmpty(tiny.Planks);
        Assert.All(tiny.Planks, plank => AssertFrameIsFiniteAndOrthonormal(plank.Frame));
    }

    [Fact]
    public void SupportsAndRailSamples_StayAboveTheDeckAndWithinTheSpan()
    {
        var plan = BridgeLayout.Build(
            new Vec3(-3, 1.0, 0), new Vec3(3, 1.0, 0), null, null, "bridge_006");
        Assert.Equal(4, plan.Supports.Count);
        Assert.All(plan.Supports, support =>
        {
            AssertFrameIsFiniteAndOrthonormal(support);
            Assert.InRange(Math.Abs(support.Position.Z), 0.25, BridgeLayout.SupportInset + 0.02);
        });
        Assert.True(plan.RailFrames.Count >= 10);
        Assert.All(plan.RailFrames, frame =>
        {
            AssertFrameIsFiniteAndOrthonormal(frame);
            Assert.True(frame.Position.X >= -3.001 && frame.Position.X <= 3.001);
        });
    }
}
