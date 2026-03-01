using QuantTrading.Core.Models;

namespace QuantTrading.Web.Services;

// ── Watchlist Entry ─────────────────────────────────────────────────
/// <summary>監控清單中的個股項目。</summary>
public class WatchlistEntry
{
    public string Ticker { get; set; } = "";
    public decimal RefPrice { get; set; }
    public string Status { get; set; } = "Idle";
    public bool IsRunning { get; set; }
}
/// <summary>台面狀態快照 — 單一鎖內取得，避免不一致讀取。</summary>
public record StatusSnapshot(
    bool IsSimulationRunning,
    string SimulationStatus,
    int DailyTradeCount,
    decimal DailyRealizedLoss,
    int WatchlistCount,
    List<string> ActiveTickers);
/// <summary>
/// Singleton 狀態容器 — 保存監控清單、即時訊號、風控狀態，供 UI 讀取。
/// 支援同時監控多支股票。
/// </summary>
public class TradingStateService
{
    private readonly object _lock = new();

    // ── Watchlist (多股票監控) ───────────────────────────────────
    private readonly List<WatchlistEntry> _watchlist = new();

    // ── Signal Log (Queue for O(1) sliding window) ──────────────
    private readonly Queue<SignalContext> _signals = new();
    private readonly Queue<RejectedSignal> _rejections = new();
    private readonly Queue<TickData> _recentTicks = new();
    private readonly Queue<BarData> _recentBars = new();

    // ── Simulation State (thread-safe backing fields) ───────────
    private bool _isSimulationRunning;
    private string _simulationStatus = "Idle";
    private int _dailyTradeCount;
    private decimal _dailyRealizedLoss;

    public bool IsSimulationRunning
    {
        get { lock (_lock) return _isSimulationRunning; }
        set { lock (_lock) _isSimulationRunning = value; }
    }

    public string SimulationStatus
    {
        get { lock (_lock) return _simulationStatus; }
        set { lock (_lock) _simulationStatus = value; }
    }

    // ── Risk Snapshot (thread-safe) ─────────────────────────────
    public int DailyTradeCount
    {
        get { lock (_lock) return _dailyTradeCount; }
        set { lock (_lock) _dailyTradeCount = value; }
    }

    public decimal DailyRealizedLoss
    {
        get { lock (_lock) return _dailyRealizedLoss; }
        set { lock (_lock) _dailyRealizedLoss = value; }
    }

    // ── Events for UI refresh ───────────────────────────────────
    public event Action? OnStateChanged;

    public void NotifyStateChanged()
    {
        var handler = OnStateChanged;
        handler?.Invoke();
    }

    // ── Watchlist Management ────────────────────────────────────
    public void AddToWatchlist(string ticker, decimal refPrice)
    {
        lock (_lock)
        {
            if (_watchlist.Any(w => w.Ticker.Equals(ticker, StringComparison.OrdinalIgnoreCase)))
                return; // 已存在，不重複加入
            _watchlist.Add(new WatchlistEntry { Ticker = ticker.ToUpper(), RefPrice = refPrice });
        }
        NotifyStateChanged();
    }

    public void RemoveFromWatchlist(string ticker)
    {
        lock (_lock)
        {
            _watchlist.RemoveAll(w => w.Ticker.Equals(ticker, StringComparison.OrdinalIgnoreCase));
        }
        NotifyStateChanged();
    }

    public void UpdateWatchlistEntry(string ticker, decimal refPrice)
    {
        lock (_lock)
        {
            var entry = _watchlist.FirstOrDefault(w => w.Ticker.Equals(ticker, StringComparison.OrdinalIgnoreCase));
            if (entry != null) entry.RefPrice = refPrice;
        }
        NotifyStateChanged();
    }

    public void UpdateWatchlistStatus(string ticker, string status, bool isRunning)
    {
        lock (_lock)
        {
            var entry = _watchlist.FirstOrDefault(w => w.Ticker.Equals(ticker, StringComparison.OrdinalIgnoreCase));
            if (entry != null)
            {
                entry.Status = status;
                entry.IsRunning = isRunning;
            }
        }
        NotifyStateChanged();
    }

    public List<WatchlistEntry> GetWatchlist()
    {
        lock (_lock) return _watchlist.Select(w => new WatchlistEntry
        {
            Ticker = w.Ticker,
            RefPrice = w.RefPrice,
            Status = w.Status,
            IsRunning = w.IsRunning
        }).ToList();
    }

    // ── Signals ─────────────────────────────────────────────────
    public void AddSignal(SignalContext signal)
    {
        lock (_lock)
        {
            _signals.Enqueue(signal);
            if (_signals.Count > 500) _signals.Dequeue();
        }
        NotifyStateChanged();
    }

    public List<SignalContext> GetSignals(string? tickerFilter = null)
    {
        lock (_lock)
        {
            if (string.IsNullOrEmpty(tickerFilter))
                return _signals.ToList();
            return _signals.Where(s => s.Ticker.Equals(tickerFilter, StringComparison.OrdinalIgnoreCase)).ToList();
        }
    }

    // ── Rejections ──────────────────────────────────────────────
    public void AddRejection(RejectedSignal rejection)
    {
        lock (_lock)
        {
            _rejections.Enqueue(rejection);
            if (_rejections.Count > 500) _rejections.Dequeue();
        }
        NotifyStateChanged();
    }

    public List<RejectedSignal> GetRejections(string? tickerFilter = null)
    {
        lock (_lock)
        {
            if (string.IsNullOrEmpty(tickerFilter))
                return _rejections.ToList();
            return _rejections.Where(r => r.Ticker.Equals(tickerFilter, StringComparison.OrdinalIgnoreCase)).ToList();
        }
    }

    // ── Ticks ───────────────────────────────────────────────────
    public void AddTick(TickData tick)
    {
        lock (_lock)
        {
            _recentTicks.Enqueue(tick);
            if (_recentTicks.Count > 1000) _recentTicks.Dequeue();
        }
    }

    public List<TickData> GetRecentTicks(string? tickerFilter = null)
    {
        lock (_lock)
        {
            if (string.IsNullOrEmpty(tickerFilter))
                return _recentTicks.ToList();
            return _recentTicks.Where(t => t.Ticker.Equals(tickerFilter, StringComparison.OrdinalIgnoreCase)).ToList();
        }
    }

    // ── Bars ────────────────────────────────────────────────────
    public void AddBar(BarData bar)
    {
        lock (_lock)
        {
            _recentBars.Enqueue(bar);
            if (_recentBars.Count > 500) _recentBars.Dequeue();
        }
    }

    public List<BarData> GetRecentBars(string? tickerFilter = null)
    {
        lock (_lock)
        {
            if (string.IsNullOrEmpty(tickerFilter))
                return _recentBars.ToList();
            return _recentBars.Where(b => b.Ticker.Equals(tickerFilter, StringComparison.OrdinalIgnoreCase)).ToList();
        }
    }

    /// <summary>取得所有有資料的 Ticker 列表。</summary>
    public List<string> GetActiveTickers()
    {
        lock (_lock)
        {
            var tickers = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var w in _watchlist) tickers.Add(w.Ticker);
            foreach (var s in _signals) tickers.Add(s.Ticker);
            foreach (var t in _recentTicks) tickers.Add(t.Ticker);
            return tickers.OrderBy(t => t).ToList();
        }
    }

    // ── Atomic Status Snapshot ──────────────────────────────────
    /// <summary>單一鎖內取得一致的狀態快照，供 REST API 使用。</summary>
    public StatusSnapshot GetStatusSnapshot()
    {
        lock (_lock)
        {
            var tickers = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var w in _watchlist) tickers.Add(w.Ticker);
            foreach (var s in _signals) tickers.Add(s.Ticker);
            foreach (var t in _recentTicks) tickers.Add(t.Ticker);

            return new StatusSnapshot(
                _isSimulationRunning,
                _simulationStatus,
                _dailyTradeCount,
                _dailyRealizedLoss,
                _watchlist.Count,
                tickers.OrderBy(t => t).ToList());
        }
    }

    // ── Clear All ───────────────────────────────────────────────
    public void ClearAll()
    {
        lock (_lock)
        {
            _signals.Clear();
            _rejections.Clear();
            _recentTicks.Clear();
            _recentBars.Clear();
            foreach (var w in _watchlist)
            {
                w.Status = "Idle";
                w.IsRunning = false;
            }
            _dailyTradeCount = 0;
            _dailyRealizedLoss = 0;
            _simulationStatus = "Idle";
        }
        NotifyStateChanged();
    }
}
