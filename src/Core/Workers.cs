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
