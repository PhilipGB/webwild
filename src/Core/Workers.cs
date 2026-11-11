namespace Webwild.Core;

public enum WorkerActivity
{
    Resting,
    Collecting,
    Repairing,
    Patrolling,
    Waiting
}

public sealed class WorkerState
{
    public required string Id { get; init; }
    public required string Name { get; init; }
    public required string Colour { get; init; }
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
    public bool IsMoving => Activity is WorkerActivity.Collecting or WorkerActivity.Repairing or WorkerActivity.Patrolling;
    public string DisplayState => Activity switch
    {
        WorkerActivity.Collecting => "Collecting",
        WorkerActivity.Repairing => "Repairing",
        WorkerActivity.Patrolling => "Exploring",
        WorkerActivity.Waiting => "Waiting",
        _ => "Resting"
    };
}
