namespace Webwild.Core;

public enum WorkerActivity
{
    Resting,
    Collecting,
    Repairing,
    Patrolling,
    Travelling,
    Waiting
}

public sealed class WorkerState
{
    public required string Id { get; init; }
    public required string Name { get; init; }
    public required string Colour { get; init; }
    /// <summary>The landmark where this helper was hatched. The colony home is
    /// independent of where the player travels or where the helper currently
    /// works, and only an explicit return changes the physical region.</summary>
    public string HomeRegion { get; set; } = RegionIds.GardenClearing;
    public string Region { get; set; } = RegionIds.GardenClearing;
    public Vec3 Position { get; set; }
    public string CurrentNode { get; set; } = WorldLayout.NurseryNode;
    public WorkerActivity Activity { get; set; } = WorkerActivity.Resting;
    public string? TargetInsectId { get; set; }
    public string? TargetWebId { get; set; }
    public string TargetNode { get; set; } = WorldLayout.NurseryNode;
    public List<string> PathNodes { get; set; } = new();
    public List<GroundEdge> PathEdges { get; set; } = new();
    public int PathIndex { get; set; }
    public double ArrivalDelay { get; set; }
    public double PatrolCooldown { get; set; } = 6;
    public int CollectCount { get; set; }
    public int RepairCount { get; set; }
    public int BridgeCrossings { get; set; }

    /// <summary>True while physically walking between landmarks. Tasks and
    /// regional bookkeeping pause until the journey arrives.</summary>
    public bool Travelling { get; set; }
    /// <summary>World-space position while travelling; local Position is a
    /// destination placeholder in that state.</summary>
    public Vec3 JourneyPosition { get; set; }
    public string JourneyTargetRegion { get; set; } = "";
    public string JourneyFromRegion { get; set; } = "";
    public List<Vec3> JourneyRoute { get; set; } = new();
    public int JourneyIndex { get; set; }
    public bool RouteBlocked { get; set; }
    public bool JourneyUsesBridge { get; set; }
    public Vec3 JourneyDestination { get; set; }
    public bool Summoned { get; set; }
    /// <summary>Transient throttle for re-routing a follower; not persisted.</summary>
    public double FollowCooldown { get; set; }

    /// <summary>Stable world position used for rendering and distance checks.</summary>
    public Vec3 WorldPosition => Travelling
        ? JourneyPosition
        : LandmarkCatalog.ToWorld(Region, Position).Absolute;

    public bool IsMoving => Activity is WorkerActivity.Collecting or WorkerActivity.Repairing
        or WorkerActivity.Patrolling or WorkerActivity.Travelling;
    public string DisplayState => Activity switch
    {
        WorkerActivity.Collecting => "Collecting",
        WorkerActivity.Repairing => "Repairing",
        WorkerActivity.Patrolling => "Exploring",
        WorkerActivity.Travelling => RouteBlocked ? "No safe route" : "Travelling",
        WorkerActivity.Waiting => RouteBlocked ? "No safe route" : "Waiting",
        _ => "Resting"
    };
}
