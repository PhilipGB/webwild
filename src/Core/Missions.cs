namespace Webwild.Core;

/// <summary>Immutable domain event. Keys make exactly-once progress testable.</summary>
public sealed record GameplayEvent(
    string Key,
    string Type,
    long Tick,
    string? SubjectId = null,
    string? WebType = null,
    bool AfterWind = false,
    int Amount = 0,
    string? Detail = null);

public sealed record MissionEventResult(
    bool Counted,
    bool MissionCompleted,
    string? CompletedMissionId,
    bool AllComplete);

public sealed class MissionTracker
{
    public const int SeenEventRetentionLimit = 512;

    private readonly MissionsConfig _config;
    private readonly HashSet<string> _seenEvents = new(StringComparer.Ordinal);
    private readonly Queue<string> _seenOrder = new();
    private readonly List<string> _completed = new();
    private readonly List<int> _stepProgress = new();

    public string ActiveMissionId { get; private set; } = "";
    public string ActiveTitle { get; private set; } = "";
    public string Hint { get; private set; } = "";
    public int ActiveMissionIndex { get; private set; }
    public int StepIndex { get; private set; }
    public int StepProgress { get; private set; }
    public bool AllComplete { get; private set; }
    public bool VictoryAwarded { get; private set; }
    public IReadOnlyList<string> CompletedMissions => _completed;
    public IReadOnlyList<int> StepProgressList => _stepProgress;
    public IReadOnlyCollection<string> SeenEventKeys => _seenEvents;
    public MissionsConfig Config => _config;

    public MissionTracker(MissionsConfig config)
    {
        _config = config.Missions.Count > 0 ? config : MissionsConfig.Defaults();
        for (var i = 0; i < _config.Missions.Count; i++) _stepProgress.Add(0);
        ActiveMissionIndex = 0;
        SyncActive();
    }

    public MissionDefinition? ActiveMission =>
        ActiveMissionIndex >= 0 && ActiveMissionIndex < _config.Missions.Count
            ? _config.Missions[ActiveMissionIndex]
            : null;

    public MissionStepDefinition? ActiveStep
    {
        get
        {
            var mission = ActiveMission;
            if (mission is null || StepIndex < 0 || StepIndex >= mission.Steps.Count) return null;
            return mission.Steps[StepIndex];
        }
    }

    private void SyncActive()
    {
        var mission = ActiveMission;
        if (mission is null)
        {
            AllComplete = true;
            ActiveMissionId = "";
            ActiveTitle = "Garden complete";
            Hint = "Keep exploring and catching insects.";
            return;
        }
        ActiveMissionId = mission.Id;
        ActiveTitle = mission.Title;
        Hint = mission.Hint;
        if (StepIndex >= mission.Steps.Count) StepIndex = 0;
        StepProgress = StepIndex < _stepProgress.Count ? _stepProgress[StepIndex] : 0;
    }

    public MissionEventResult Record(GameplayEvent evt)
    {
        if (AllComplete) return new MissionEventResult(false, false, null, true);
        if (string.IsNullOrWhiteSpace(evt.Key)) return new MissionEventResult(false, false, null, false);
        if (_seenEvents.Contains(evt.Key)) return new MissionEventResult(false, false, null, false);
        _seenEvents.Add(evt.Key);
        _seenOrder.Enqueue(evt.Key);
        TrimSeenEvents(SeenEventRetentionLimit);

        var mission = ActiveMission;
        var step = ActiveStep;
        if (mission is null || step is null) return new MissionEventResult(false, false, null, AllComplete);

        if (step.Event != evt.Type) return new MissionEventResult(false, false, null, false);
        if (!string.IsNullOrWhiteSpace(step.Filter?.Type) && step.Filter.Type != evt.WebType)
            return new MissionEventResult(false, false, null, false);

        StepProgress++;
        _stepProgress[StepIndex] = StepProgress;
        var counted = true;
        if (StepProgress < Math.Max(1, step.Count))
            return new MissionEventResult(counted, false, null, false);

        // Step complete.
        StepIndex++;
        if (StepIndex < mission.Steps.Count)
        {
            StepProgress = _stepProgress[StepIndex];
            return new MissionEventResult(counted, false, null, false);
        }

        // Mission complete; advance sequentially.
        _completed.Add(mission.Id);
        ActiveMissionIndex++;
        StepIndex = 0;
        var all = ActiveMissionIndex >= _config.Missions.Count;
        AllComplete = all;
        var completedId = mission.Id;
        SyncActive();
        return new MissionEventResult(counted, true, completedId, all);
    }

    public int TotalSteps => _config.Missions.Sum(m => m.Steps.Count);
    public int CompletedStepCount
    {
        get
        {
            var count = 0;
            for (var i = 0; i < _config.Missions.Count; i++)
            {
                if (i < ActiveMissionIndex) count += _config.Missions[i].Steps.Count;
                else if (i == ActiveMissionIndex) count += Math.Min(StepIndex, _config.Missions[i].Steps.Count);
            }
            return count;
        }
    }

    public void MarkVictoryAwarded() => VictoryAwarded = true;

    /// <summary>Bound the idempotency history so a long session can always be saved.</summary>
    public void TrimSeenEvents(int maxEntries)
    {
        while (_seenOrder.Count > Math.Max(1, maxEntries))
        {
            var oldest = _seenOrder.Dequeue();
            _seenEvents.Remove(oldest);
        }
    }

    public void Restore(
        int activeMissionIndex,
        int stepIndex,
        IReadOnlyList<int> progress,
        IReadOnlyList<string> completed,
        IReadOnlyCollection<string> seenEvents,
        bool victoryAwarded)
    {
        ActiveMissionIndex = Math.Clamp(activeMissionIndex, 0, _config.Missions.Count);
        StepIndex = Math.Max(0, stepIndex);
        _completed.Clear();
        _completed.AddRange(completed);
        _stepProgress.Clear();
        for (var i = 0; i < _config.Missions.Count; i++)
            _stepProgress.Add(i < progress.Count ? Math.Max(0, progress[i]) : 0);
        _seenEvents.Clear();
        _seenOrder.Clear();
        foreach (var key in seenEvents)
        {
            if (string.IsNullOrWhiteSpace(key)) continue;
            if (_seenEvents.Add(key)) _seenOrder.Enqueue(key);
        }
        TrimSeenEvents(SeenEventRetentionLimit);
        VictoryAwarded = victoryAwarded;
        AllComplete = ActiveMissionIndex >= _config.Missions.Count;
        var mission = ActiveMission;
        if (mission is not null && StepIndex < mission.Steps.Count)
            _stepProgress[StepIndex] = Math.Max(_stepProgress[StepIndex], progress.ElementAtOrDefault(StepIndex));
        SyncActive();
    }

    public static string DescribeStep(MissionStepDefinition step) => step.Event switch
    {
        "WebBuilt" when step.Filter?.Type == "capture" => "Build a capture web",
        "WebBuilt" when step.Filter?.Type == "bridge" => "Build a silk bridge",
        "WebBuilt" => "Build a web",
        "FoodCollected" => "Collect trapped food",
        "SpiderHatched" => "Hatch a spider helper",
        "WorkerUsedBridge" => "See a helper cross a bridge",
        "WindEventCompleted" => "Weather the wind",
        "WebRestoredOrSheltered" => "Repair or shelter a web",
        "FoodCollectedAfterWind" => "Collect food after the wind",
        _ => step.Event
    };
}
