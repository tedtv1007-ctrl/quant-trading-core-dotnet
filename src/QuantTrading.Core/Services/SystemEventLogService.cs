using System.Collections.Concurrent;

namespace QuantTrading.Core.Services;

/// <summary>
/// 系統事件日誌服務 — 結構化紀錄交易系統的關鍵事件。
/// 參考 NoFx 的 AI Decision Log / Chain of Thought 設計。
/// </summary>
public class SystemEventLogService
{
    private readonly ConcurrentQueue<SystemEvent> _events = new();
    private const int MaxEvents = 2000;

    public event Action? OnEventLogged;

    public void Log(SystemEventLevel level, string category, string message, string? detail = null)
    {
        var evt = new SystemEvent
        {
            Timestamp = DateTime.UtcNow,
            Level = level,
            Category = category,
            Message = message,
            Detail = detail
        };

        _events.Enqueue(evt);
        while (_events.Count > MaxEvents)
            _events.TryDequeue(out _);

        OnEventLogged?.Invoke();
    }

    public void LogInfo(string category, string message, string? detail = null)
        => Log(SystemEventLevel.Info, category, message, detail);

    public void LogWarning(string category, string message, string? detail = null)
        => Log(SystemEventLevel.Warning, category, message, detail);

    public void LogError(string category, string message, string? detail = null)
        => Log(SystemEventLevel.Error, category, message, detail);

    public void LogSignal(string message, string? detail = null)
        => Log(SystemEventLevel.Info, "Signal", message, detail);

    public void LogRisk(string message, string? detail = null)
        => Log(SystemEventLevel.Warning, "Risk", message, detail);

    public void LogSystem(string message, string? detail = null)
        => Log(SystemEventLevel.Info, "System", message, detail);

    public List<SystemEvent> GetEvents(int count = 100, SystemEventLevel? levelFilter = null, string? categoryFilter = null)
    {
        var query = _events.AsEnumerable();
        if (levelFilter.HasValue) query = query.Where(e => e.Level == levelFilter.Value);
        if (!string.IsNullOrEmpty(categoryFilter))
            query = query.Where(e => e.Category.Equals(categoryFilter, StringComparison.OrdinalIgnoreCase));
        return query.Reverse().Take(count).ToList();
    }

    public int TotalCount => _events.Count;

    public void Clear() 
    {
        while (_events.TryDequeue(out _)) { }
    }
}

public enum SystemEventLevel { Info, Warning, Error }

public class SystemEvent
{
    public DateTime Timestamp { get; set; }
    public SystemEventLevel Level { get; set; }
    public string Category { get; set; } = "";
    public string Message { get; set; } = "";
    public string? Detail { get; set; }
}
