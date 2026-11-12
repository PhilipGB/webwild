namespace Webwild.Core;

public sealed class RegionalStepDefinition
{
    public string Event { get; set; } = "";
    public string Region { get; set; } = ReedwaterLayout.RegionId;
    /// <summary>Optional event detail filter (for example a landmark zone).</summary>
    public string? Detail { get; set; }
    public int Count { get; set; } = 1;
    public string Description { get; set; } = "";
}

public sealed class RegionalObjectiveDefinition
{
    public string Id { get; set; } = "";
    public string Title { get; set; } = "";
    public string Hint { get; set; } = "";
    public List<RegionalStepDefinition> Steps { get; set; } = new();
}

public sealed class RegionalMasteryDefinition
{
    public string Id { get; set; } = "RM01";
    public string Title { get; set; } = "Clever Crossing";
    public string Description { get; set; } = "Reach the lily island with only two silk bridges.";
    public string Reward { get; set; } = "Clever Crossing badge";
    public bool RequiredForVictory { get; set; }
}

public sealed class RegionalObjectivesConfig
{
    public List<RegionalObjectiveDefinition> Objectives { get; set; } = new();
    public RegionalMasteryDefinition? Mastery { get; set; }

    public static RegionalObjectivesConfig Defaults() => new()
    {
        Objectives = new List<RegionalObjectiveDefinition>
        {
            new()
            {
                Id = "R01", Title = "Across the Water",
                Hint = "Build two silk bridges: reed bank to mossy island, then to the lily island.",
                Steps = new List<RegionalStepDefinition>
                {
                    new()
                    {
                        Event = "RegionRouteOpened", Region = ReedwaterLayout.RegionId,
                        Detail = ReedwaterLayout.FirstDestinationNode,
                        Description = "Connect the bank to the lily island"
                    }
                }
            },
            new()
            {
                Id = "R02", Title = "The Reed Harvest",
                Hint = "Catch three insects in Reedwater Bank to feed the colony.",
                Steps = new List<RegionalStepDefinition>
                {
                    new()
                    {
                        Event = "FoodCollected", Region = ReedwaterLayout.RegionId, Count = 3,
                        Description = "Collect food from Reedwater insects"
                    }
                }
            },
            new()
            {
                Id = "R03", Title = "The Far Bank",
                Hint = "Build the last bridge, then let a helper collect prey on the far bank.",
                Steps = new List<RegionalStepDefinition>
                {
                    new()
                    {
                        Event = "FarBankDelivery", Region = ReedwaterLayout.RegionId, Count = 1,
                        Description = "A helper collects prey on the far bank"
                    }
                }
            }
        },
        Mastery = new RegionalMasteryDefinition()
    };
}

public sealed record RegionalObjectiveEventResult(
    bool Counted,
    bool ObjectiveCompleted,
    string? CompletedObjectiveId,
    bool AllComplete,
    bool MasteryJustEarned);

/// <summary>
/// Separate sequential objective track for a secondary region. The original
/// M01-M03 story tracker remains untouched, so legacy mission progress and saves
/// keep their exact meaning.
/// </summary>
public sealed class RegionalObjectiveTracker
{
    public const int SeenEventRetentionLimit = 512;

    private readonly RegionalObjectivesConfig _config;
    private readonly HashSet<string> _seenEvents = new(StringComparer.Ordinal);
    private readonly Queue<string> _seenOrder = new();
    private readonly List<string> _completed = new();
    private readonly List<int> _stepProgress = new();

    public string ActiveObjectiveId { get; private set; } = "";
    public string ActiveTitle { get; private set; } = "";
    public string Hint { get; private set; } = "";
    public int ActiveObjectiveIndex { get; private set; }
    public int StepIndex { get; private set; }
    public int StepProgress { get; private set; }
    public bool AllComplete { get; private set; }
    public bool MasteryAwarded { get; private set; }
    public IReadOnlyList<string> CompletedObjectives => _completed;
    public IReadOnlyList<int> StepProgressList => _stepProgress;
    public IReadOnlyList<string> OrderedSeenEventKeys => _seenOrder.ToArray();
    public RegionalObjectivesConfig Config => _config;

    public RegionalObjectiveTracker(RegionalObjectivesConfig? config = null)
    {
        _config = config?.Objectives.Count > 0 ? config : RegionalObjectivesConfig.Defaults();
        for (var i = 0; i < _config.Objectives.Count; i++) _stepProgress.Add(0);
        SyncActive();
    }

    public RegionalObjectiveDefinition? ActiveObjective =>
        ActiveObjectiveIndex >= 0 && ActiveObjectiveIndex < _config.Objectives.Count
            ? _config.Objectives[ActiveObjectiveIndex]
            : null;

    public RegionalStepDefinition? ActiveStep
    {
        get
        {
            var objective = ActiveObjective;
            return objective is null || StepIndex < 0 || StepIndex >= objective.Steps.Count
                ? null
                : objective.Steps[StepIndex];
        }
    }

    private void SyncActive()
    {
        var objective = ActiveObjective;
        if (objective is null)
        {
            AllComplete = true;
            ActiveObjectiveId = "";
            ActiveTitle = "Reedwater Bank complete";
            Hint = "Keep exploring, weaving and catching insects.";
            return;
        }
        ActiveObjectiveId = objective.Id;
        ActiveTitle = objective.Title;
        Hint = objective.Hint;
        if (StepIndex >= objective.Steps.Count) StepIndex = 0;
        StepProgress = StepIndex < _stepProgress.Count ? _stepProgress[StepIndex] : 0;
    }

    public RegionalObjectiveEventResult Record(GameplayEvent evt)
    {
        if (AllComplete) return new RegionalObjectiveEventResult(false, false, null, true, false);
        if (string.IsNullOrWhiteSpace(evt.Key)) return new RegionalObjectiveEventResult(false, false, null, false, false);
        if (_seenEvents.Contains(evt.Key)) return new RegionalObjectiveEventResult(false, false, null, false, false);
        _seenEvents.Add(evt.Key);
        _seenOrder.Enqueue(evt.Key);
        TrimSeenEvents(SeenEventRetentionLimit);

        var objective = ActiveObjective;
        var step = ActiveStep;
        if (objective is null || step is null) return new RegionalObjectiveEventResult(false, false, null, AllComplete, false);
        if (!string.Equals(step.Region, evt.Region, StringComparison.Ordinal))
            return new RegionalObjectiveEventResult(false, false, null, false, false);
        if (step.Event != evt.Type)
            return new RegionalObjectiveEventResult(false, false, null, false, false);
        if (!string.IsNullOrWhiteSpace(step.Detail) &&
            !string.Equals(step.Detail, evt.Detail, StringComparison.Ordinal))
            return new RegionalObjectiveEventResult(false, false, null, false, false);

        StepProgress++;
        _stepProgress[StepIndex] = StepProgress;
        if (StepProgress < Math.Max(1, step.Count))
            return new RegionalObjectiveEventResult(true, false, null, false, false);

        StepIndex++;
        if (StepIndex < objective.Steps.Count)
        {
            StepProgress = _stepProgress[StepIndex];
            return new RegionalObjectiveEventResult(true, false, null, false, false);
        }

        _completed.Add(objective.Id);
        ActiveObjectiveIndex++;
        StepIndex = 0;
        AllComplete = ActiveObjectiveIndex >= _config.Objectives.Count;
        var completedId = objective.Id;
        SyncActive();
        return new RegionalObjectiveEventResult(true, true, completedId, AllComplete, false);
    }

    /// <summary>
    /// Award the optional crossing badge exactly once. The caller supplies the
    /// bridge count so the rule remains deterministic and testable.
    /// </summary>
    public bool TryAwardMastery(int reedwaterBridgesBuilt)
    {
        if (MasteryAwarded) return false;
        if (_config.Mastery is null) return false;
        if (!_completed.Contains("R01")) return false;
        if (reedwaterBridgesBuilt > 2) return false;
        MasteryAwarded = true;
        return true;
    }

    public void MarkMasteryAwarded() => MasteryAwarded = true;

    public void TrimSeenEvents(int maxEntries)
    {
        while (_seenOrder.Count > Math.Max(1, maxEntries))
        {
            var oldest = _seenOrder.Dequeue();
            _seenEvents.Remove(oldest);
        }
    }

    public void Restore(
        int activeObjectiveIndex,
        int stepIndex,
        IReadOnlyList<int> progress,
        IReadOnlyList<string> completed,
        IReadOnlyCollection<string> seenEvents,
        bool masteryAwarded)
    {
        ActiveObjectiveIndex = Math.Clamp(activeObjectiveIndex, 0, _config.Objectives.Count);
        StepIndex = Math.Max(0, stepIndex);
        _completed.Clear();
        _completed.AddRange(completed);
        _stepProgress.Clear();
        for (var i = 0; i < _config.Objectives.Count; i++)
            _stepProgress.Add(i < progress.Count ? Math.Max(0, progress[i]) : 0);
        _seenEvents.Clear();
        _seenOrder.Clear();
        foreach (var key in seenEvents)
        {
            if (string.IsNullOrWhiteSpace(key)) continue;
            if (_seenEvents.Add(key)) _seenOrder.Enqueue(key);
        }
        TrimSeenEvents(SeenEventRetentionLimit);
        MasteryAwarded = masteryAwarded;
        AllComplete = ActiveObjectiveIndex >= _config.Objectives.Count;
        var objective = ActiveObjective;
        if (objective is not null && StepIndex < objective.Steps.Count)
            _stepProgress[StepIndex] = Math.Max(_stepProgress[StepIndex], progress.ElementAtOrDefault(StepIndex));
        SyncActive();
    }

    public static string DescribeStep(RegionalStepDefinition step) =>
        !string.IsNullOrWhiteSpace(step.Description) ? step.Description : step.Event;
}
