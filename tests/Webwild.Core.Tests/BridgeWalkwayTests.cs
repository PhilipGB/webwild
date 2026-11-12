using Webwild.Core;
using Xunit;

namespace Webwild.Core.Tests;

/// <summary>
/// Regression coverage for the physically walkable bridge surface. The old
/// collision was a chain of thin boxes; each segment's end cap became a steep
/// wall at a joint and wedged the player capsule. A single triangle strip has no
/// end caps, keeps every top face walkable, and is wide enough for the capsule.
/// </summary>
public class BridgeWalkwayTests
{

    private static BridgeGeometry.WalkwaySurface ReedwaterWalkway(string aId, string bId)
    {
        var anchors = ReedwaterLayout.CreateAnchors();
        var a = anchors.First(anchor => anchor.Id == aId);
        var b = anchors.First(anchor => anchor.Id == bId);
        var approach = BridgeGeometry.ComputeApproachPoints(
            a.Position, b.Position, a.BridgeGroundPoint, b.BridgeGroundPoint);
        return BridgeGeometry.BuildWalkwaySurface(a.Position, b.Position, approach.A, approach.B);
    }

    [Fact]
    public void WalkwaySurface_IsWideEnoughForThePlayerCapsule()
    {
        Assert.True(BridgeGeometry.WalkwayHalfWidth * 2 >= 1.4,
            $"walkway={BridgeGeometry.WalkwayHalfWidth * 2:0.00} m");

        var surface = ReedwaterWalkway(ReedwaterLayout.StartPost, ReedwaterLayout.IslandAPost);
        Assert.True(surface.Vertices.Count >= 4);
        Assert.Equal(0, surface.Indices.Count % 3);
        Assert.True(surface.Indices.Count >= 6);

        // Every centerline sample has a walkable edge on both sides.
        foreach (var point in surface.Centerline)
        {
            var edges = surface.Vertices.Count(vertex =>
                Math.Abs(vertex.Y - point.Y) < 0.001 &&
                Math.Abs(new Vec3(vertex.X - point.X, 0, vertex.Z - point.Z).HorizontalLength
                    - BridgeGeometry.WalkwayHalfWidth) < 0.02);
            Assert.True(edges >= 2, $"missing full width at {point}");
        }
    }

    [Fact]
    public void WalkwaySurface_HasOnlyUpwardFaces_AndNoThinEndCaps()
    {
        var surface = ReedwaterWalkway(ReedwaterLayout.IslandAPost, ReedwaterLayout.IslandBPost);
        Assert.All(surface.Vertices, vertex => Assert.True(vertex.IsFinite));

        var steepestNormal = 1.0;
        for (var i = 0; i + 2 < surface.Indices.Count; i += 3)
        {
            var a = surface.Vertices[surface.Indices[i]];
            var b = surface.Vertices[surface.Indices[i + 1]];
            var c = surface.Vertices[surface.Indices[i + 2]];
            var normal = Vec3.Cross(b - a, c - a).Normalized();
            Assert.True(normal.Y > 0, $"non-walkable winding at triangle {i / 3}: {normal}");
            steepestNormal = Math.Min(steepestNormal, normal.Y);
        }

        // Every face is a floor-like surface; none is a vertical end cap.
        Assert.True(steepestNormal > 0.55, $"steepest walkable normal Y={steepestNormal:0.00}");
    }

    [Fact]
    public void WalkwaySurface_IsContinuousAcrossRampAndDeckJoints()
    {
        // Island A is the worst case: the shared approach ramp doubles back under
        // bridge one, so the ramp/deck joint must not form a notch.
        var surface = ReedwaterWalkway(ReedwaterLayout.StartPost, ReedwaterLayout.IslandAPost);
        for (var i = 1; i < surface.Centerline.Count; i++)
        {
            var gap = Vec3.Distance(surface.Centerline[i - 1], surface.Centerline[i]);
            Assert.InRange(gap, 0.0001, 0.42);
        }

        // The strip reaches solid ground at both ends.
        Assert.True(surface.Centerline[0].Y <= 0.001);
        Assert.InRange(surface.Centerline[^1].Y, 0, 0.16);
        Assert.Contains(surface.Centerline, point => Math.Abs(point.Y - 0.55) < 0.02);
    }

    [Fact]
    public void WalkwaySurface_IsIndependentOfAnchorSelectionOrder()
    {
        var forward = ReedwaterWalkway(ReedwaterLayout.IslandBPost, ReedwaterLayout.FarPost);
        var reversed = ReedwaterWalkway(ReedwaterLayout.FarPost, ReedwaterLayout.IslandBPost);
        Assert.Equal(forward.Vertices.Count, reversed.Vertices.Count);
        Assert.Equal(forward.Indices.Count, reversed.Indices.Count);

        var forwardWidth = Vec3.Distance(forward.Vertices[0], forward.Vertices[1]);
        var reversedWidth = Vec3.Distance(reversed.Vertices[0], reversed.Vertices[1]);
        Assert.Equal(forwardWidth, reversedWidth, 6);
    }

    [Fact]
    public void WalkwaySurface_FollowsTheSameSagAsTheVisibleDeck()
    {
        var anchors = ReedwaterLayout.CreateAnchors();
        var start = anchors.First(a => a.Id == ReedwaterLayout.StartPost).Position;
        var island = anchors.First(a => a.Id == ReedwaterLayout.IslandAPost).Position;
        var surface = ReedwaterWalkway(ReedwaterLayout.StartPost, ReedwaterLayout.IslandAPost);
        var sag = BridgeGeometry.ComputeSag(Vec3.Distance(start, island));
        var middle = Vec3.Lerp(start, island, 0.5);
        var nearest = surface.Centerline
            .OrderBy(point => Vec3.Distance(new Vec3(point.X, 0, point.Z), new Vec3(middle.X, 0, middle.Z)))
            .First();
        Assert.Equal(0.55 - sag, nearest.Y, 2);
    }

    [Fact]
    public void BridgePlan_UsesResolvedGroundPointsInLocalSpace()
    {
        var anchors = ReedwaterLayout.CreateAnchors();
        var a = anchors.First(anchor => anchor.Id == ReedwaterLayout.IslandAPost);
        var b = anchors.First(anchor => anchor.Id == ReedwaterLayout.IslandBPost);
        var centre = new Vec3(
            (a.Position.X + b.Position.X) * 0.5,
            (a.Position.Y + b.Position.Y) * 0.5,
            (a.Position.Z + b.Position.Z) * 0.5);

        var world = BridgeGeometry.ComputeApproachPoints(
            a.Position, b.Position, a.BridgeGroundPoint, b.BridgeGroundPoint);
        var plan = BridgeLayout.Build(
            a.Position - centre, b.Position - centre,
            world.A - centre, world.B - centre,
            "bridge_local", resolveGround: false);

        // The visible planks must follow exactly the same ramp the collision
        // surface uses, even though the view works in bridge-local space.
        Assert.Equal(world.A.X - centre.X, plan.Centerline[0].X, 6);
        Assert.Equal(world.A.Y - centre.Y, plan.Centerline[0].Y, 6);
        Assert.Equal(world.A.Z - centre.Z, plan.Centerline[0].Z, 6);
        Assert.Equal(world.B.X - centre.X, plan.Centerline[^1].X, 6);
        Assert.Equal(world.B.Y - centre.Y, plan.Centerline[^1].Y, 6);
        Assert.Equal(world.B.Z - centre.Z, plan.Centerline[^1].Z, 6);
    }

    [Fact]
    public void WalkwaySurface_HandlesDegenerateAndAxisAlignedSpans()
    {        var zAxis = BridgeGeometry.BuildWalkwaySurface(
            new Vec3(0, 0.55, -2), new Vec3(0, 0.55, 2),
            new Vec3(0, 0, -3.2), new Vec3(0, 0, 3.2));
        Assert.NotEmpty(zAxis.Vertices);
        for (var i = 0; i < zAxis.Indices.Count; i += 3)
        {
            var a = zAxis.Vertices[zAxis.Indices[i]];
            var b = zAxis.Vertices[zAxis.Indices[i + 1]];
            var c = zAxis.Vertices[zAxis.Indices[i + 2]];
            Assert.True(Vec3.Cross(b - a, c - a).Y > 0);
        }

        var degenerate = BridgeGeometry.BuildWalkwaySurface(
            new Vec3(1, 0.5, 1), new Vec3(1, 0.5, 1),
            new Vec3(1, 0, 1), new Vec3(1, 0, 1));
        Assert.All(degenerate.Vertices, vertex => Assert.True(vertex.IsFinite));
    }
}
