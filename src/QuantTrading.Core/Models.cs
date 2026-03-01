using System;

namespace QuantTrading.Core.Models;

// ── Enums ───────────────────────────────────────────────────────────────

public enum StrategyType { OpenGap, IntradayDip }
public enum MarketDataType { Simulate, Realtime }
public enum OrderType { MarketBuy, LimitBuy }
public enum SignalResult { Accept, RejectRisk, RejectMaxTrades, RejectDailyLoss }
public enum TradeDirection { Buy, Sell }

// ── Market Data Records ─────────────────────────────────────────────────

public record TickData(
    string Ticker,
    decimal Price,
    long Volume,
    DateTime Timestamp
);

public record BarData(
    string Ticker,
    decimal Open,
    decimal High,
    decimal Low,
    decimal Close,
    long Volume,
    DateTime Timestamp
);

// ── Signal Context ──────────────────────────────────────────────────────

public record SignalContext(
    StrategyType Strategy,
    OrderType OrderType,
    string Ticker,
    decimal EntryPrice,
    decimal StopLossPrice,
    double VolumeRatio,
    int PositionSize,
    DateTime Timestamp
);

public record RejectedSignal(
    SignalResult Reason,
    StrategyType Strategy,
    string Ticker,
    DateTime Timestamp
);

// ── Strategy Configuration ──────────────────────────────────────────────

/// <summary>
/// Strategy A 參數 — 開盤試搓策略 (Pre-Market Gap)
/// </summary>
public record PreMarketGapConfig
{
    /// <summary>試搓監控開始時間 (default 08:30)</summary>
    public TimeSpan MonitorStart { get; init; } = new(8, 30, 0);

    /// <summary>試搓監控結束/判定時間 (default 08:59:55)</summary>
    public TimeSpan MonitorEnd { get; init; } = new(8, 59, 55);

    /// <summary>開盤送單時間 (default 09:00:00)</summary>
    public TimeSpan ExecutionTime { get; init; } = new(9, 0, 0);

    /// <summary>跳空強度門檻 — SimPrice > RefPrice * (1 + GapStrengthPercent)</summary>
    public decimal GapStrengthPercent { get; init; } = 0.01m; // 1%

    /// <summary>防假突破回檔容許幅度 — 最高試搓價回落超過此比率即判定為 Fakeout</summary>
    public decimal FakeoutPullbackPercent { get; init; } = 0.005m; // 0.5%

    /// <summary>停損距離比率 (EntryPrice * StopLossOffsetPercent)</summary>
    public decimal StopLossOffsetPercent { get; init; } = 0.01m; // 1%
}

/// <summary>
/// Strategy B 參數 — 盤中低接反彈策略 (Intraday Dip &amp; Volume Surge)
/// </summary>
public record IntradayDipConfig
{
    /// <summary>盤中策略啟動時間 (default 09:01)</summary>
    public TimeSpan ActiveStart { get; init; } = new(9, 1, 0);

    /// <summary>盤中策略結束時間 (default 13:25)</summary>
    public TimeSpan ActiveEnd { get; init; } = new(13, 25, 0);

    /// <summary>低接門檻 — Price &lt; VWAP * (1 - DipThresholdPercent)</summary>
    public decimal DipThresholdPercent { get; init; } = 0.02m; // 2%

    /// <summary>爆量倍數 — 當前 1 分 K 量 > 過去 N 根平均量 * VolumeSpikeMultiplier</summary>
    public double VolumeSpikeMultiplier { get; init; } = 3.0;

    /// <summary>爆量回看 K 棒數量</summary>
    public int VolumeLookbackBars { get; init; } = 5;

    /// <summary>停損距離比率</summary>
    public decimal StopLossOffsetPercent { get; init; } = 0.01m; // 1%
}

/// <summary>
/// 風控參數設定
/// </summary>
public record RiskConfig
{
    /// <summary>單筆最大停損金額 (TWD)</summary>
    public decimal RiskPerTrade { get; init; } = 1_100m;

    /// <summary>單日最大虧損金額 (TWD)</summary>
    public decimal MaxDailyLoss { get; init; } = 5_000m;

    /// <summary>單日最大交易筆數</summary>
    public int MaxDailyTrades { get; init; } = 5;
}

// ── Configuration Holder ────────────────────────────────────────────

/// <summary>
/// 交易系統組態容器 — 包含所有策略與風控參數。
/// 作為 Singleton 註冊，支援執行時動態更新。
/// </summary>
public class TradingConfiguration
{
    private readonly object _lock = new();

    private PreMarketGapConfig _gapConfig = new();
    private IntradayDipConfig _dipConfig = new();
    private RiskConfig _riskConfig = new();

    public PreMarketGapConfig GapConfig
    {
        get { lock (_lock) return _gapConfig; }
        set { lock (_lock) _gapConfig = value; }
    }

    public IntradayDipConfig DipConfig
    {
        get { lock (_lock) return _dipConfig; }
        set { lock (_lock) _dipConfig = value; }
    }

    public RiskConfig RiskConfig
    {
        get { lock (_lock) return _riskConfig; }
        set { lock (_lock) _riskConfig = value; }
    }

    /// <summary>組態變更事件（通知 UI 刷新）</summary>
    public event Action? OnConfigChanged;
    public void NotifyChanged()
    {
        var handler = OnConfigChanged;
        handler?.Invoke();
    }
}

// ── Trade Journal Record ────────────────────────────────────────────

/// <summary>
/// 每日實際成交紀錄 — 手動輸入的真實交易。
/// </summary>
public class TradeRecord
{
    /// <summary>唯一識別碼</summary>
    public string Id { get; set; } = Guid.NewGuid().ToString("N")[..12];

    /// <summary>交易日期</summary>
    public DateTime TradeDate { get; set; } = DateTime.Today;

    /// <summary>股票代號</summary>
    public string Ticker { get; set; } = "";

    /// <summary>買 / 賣</summary>
    public TradeDirection Direction { get; set; } = TradeDirection.Buy;

    /// <summary>成交價格</summary>
    public decimal Price { get; set; }

    /// <summary>成交股數</summary>
    public int Quantity { get; set; }

    /// <summary>使用的策略 (手動標記)</summary>
    public string? Strategy { get; set; }

    /// <summary>備註</summary>
    public string? Note { get; set; }

    /// <summary>記錄建立時間 (UTC)</summary>
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    /// <summary>成交金額 = Price × Quantity</summary>
    public decimal Amount => Price * Quantity;
}
