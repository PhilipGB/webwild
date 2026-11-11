namespace Webwild.Core;

/// <summary>Non-negative food and silk ledger with exactly-once event keys.</summary>
public sealed class ResourceLedger
{
    public const int HistoryRetentionLimit = 512;

    private readonly HashSet<string> _processed = new(StringComparer.Ordinal);
    private readonly Queue<string> _processedOrder = new();

    public int Food { get; private set; }
    public int Silk { get; private set; }
    public int Version { get; private set; }

    public ResourceLedger(int food = 0, int silk = 0)
    {
        Food = Math.Max(0, food);
        Silk = Math.Max(0, silk);
    }

    public bool CanSpend(int food, int silk) =>
        food >= 0 && silk >= 0 && Food >= food && Silk >= silk;

    private bool Remember(string key)
    {
        if (!_processed.Add(key)) return false;
        _processedOrder.Enqueue(key);
        TrimHistory(HistoryRetentionLimit);
        return true;
    }

    /// <summary>Keep only recent transaction keys so long sessions remain saveable.</summary>
    public void TrimHistory(int maxEntries)
    {
        while (_processedOrder.Count > Math.Max(1, maxEntries))
        {
            var oldest = _processedOrder.Dequeue();
            _processed.Remove(oldest);
        }
    }

    /// <summary>Atomic spend. A duplicate transaction ID never charges twice.</summary>
    public bool TrySpend(int food, int silk, string transactionId)
    {
        if (string.IsNullOrWhiteSpace(transactionId)) return false;
        if (food < 0 || silk < 0) return false;
        if (!CanSpend(food, silk)) return false;
        if (!Remember("spend:" + transactionId)) return false;
        Food -= food;
        Silk -= silk;
        Version++;
        return true;
    }

    /// <summary>Grant with a unique event key. Duplicate grants are ignored.</summary>
    public bool TryGrant(int food, int silk, string eventId)
    {
        if (food < 0 || silk < 0) return false;
        if (string.IsNullOrWhiteSpace(eventId)) return false;
        if (!Remember("grant:" + eventId)) return false;
        Food += food;
        Silk += silk;
        Version++;
        return true;
    }

    public bool TryGrantFood(int food, string eventId) => TryGrant(food, 0, eventId);
    public bool TryGrantSilk(int silk, string eventId) => TryGrant(0, silk, eventId);

    public bool HasProcessed(string key) => _processed.Contains(key);

    public void RestoreProcessed(IEnumerable<string> keys)
    {
        _processed.Clear();
        _processedOrder.Clear();
        foreach (var key in keys)
        {
            if (string.IsNullOrWhiteSpace(key)) continue;
            if (_processed.Add(key)) _processedOrder.Enqueue(key);
        }
        TrimHistory(HistoryRetentionLimit);
    }

    public void Restore(int food, int silk)
    {
        Food = Math.Max(0, food);
        Silk = Math.Max(0, silk);
        Version++;
    }

    public int ProcessedKeyCount => _processed.Count;
    public IReadOnlyCollection<string> ProcessedKeys => _processed;
}
