using System;
using System.Threading.Tasks;
using QuantTrading.Core.Models;

namespace QuantTrading.Core.Interfaces;

// ── 行情資料源介面 ────────────────────────────────────────────────────

/// <summary>
/// 行情資料源 — 支援試搓 (Simulate) 與即時 (Realtime) 兩種模式。
/// </summary>
public interface IMarketDataFeed
{
    /// <summary>收到逐筆成交 / 試搓 Tick</summary>
    event Action<TickData> OnTickReceived;

    /// <summary>K 棒收盤 (例如 1-Min Bar closed)</summary>
    event Action<BarData> OnBarClosed;

    /// <summary>訂閱指定標的的行情資料</summary>
    void Subscribe(string ticker, MarketDataType dataType);

    /// <summary>取消訂閱</summary>
    void Unsubscribe(string ticker);

    /// <summary>當前是否處於連線中</summary>
    bool IsConnected { get; }

    /// <summary>啟動連線 (WebSocket / 模擬 / …)</summary>
    Task StartAsync(CancellationToken cancellationToken = default);

    /// <summary>關閉連線並釋放資源</summary>
    Task StopAsync(CancellationToken cancellationToken = default);
}

// ── 策略引擎介面 ──────────────────────────────────────────────────────

/// <summary>
/// 策略引擎 — 處理行情、產出交易訊號。
/// </summary>
public interface IStrategyEngine
{
    /// <summary>處理即時 Tick（含試搓）</summary>
    Task ProcessTickAsync(TickData tick);

    /// <summary>處理 K 棒資料 (1-Min Bar)</summary>
    Task ProcessBarAsync(BarData bar);

    /// <summary>訊號觸發事件</summary>
    event Action<SignalContext> OnSignalGenerated;

    /// <summary>訊號被風控拒絕事件</summary>
    event Action<RejectedSignal> OnSignalRejected;

    /// <summary>設定昨日收盤參考價 (供 Strategy A 使用)</summary>
    void SetReferencePrice(string ticker, decimal refPrice);
}

// ── 風控管理器介面 ────────────────────────────────────────────────────

/// <summary>
/// 風控管理器 — 計算部位、驗證是否允許交易。
/// </summary>
public interface IRiskManager
{
    /// <summary>
    /// 計算部位大小並驗證風控規則。
    /// 回傳 (Signal, Result)：Signal 非 null 表示允許交易，Result 為拒絕原因。
    /// </summary>
    (SignalContext? Signal, SignalResult Result) EvaluateSignal(
        StrategyType strategy,
        OrderType orderType,
        TickData tick,
        decimal stopLossPrice,
        double volumeRatio);

    /// <summary>回報一筆已實現虧損 (用於追蹤當日累計損失)</summary>
    void RecordRealizedLoss(decimal lossAmount);

    /// <summary>每日重置 (交易日開始時呼叫)</summary>
    void ResetDaily();

    /// <summary>取得當日 Reject 原因 (若有)</summary>
    SignalResult GetCurrentStatus();

    /// <summary>今日已執行交易筆數</summary>
    int DailyTradeCount { get; }

    /// <summary>今日累計虧損金額</summary>
    decimal DailyRealizedLoss { get; }
}

// ── 交易引擎工廠介面 ────────────────────────────────────────────────────

/// <summary>
/// 建立一對共用 RiskManager 的 StrategyEngine + RiskManager 實例。
/// 每次模擬應建立新的一組，避免跨次模擬的狀態殘留。
/// </summary>
public interface ITradingEngineFactory
{
    (IStrategyEngine Engine, IRiskManager RiskManager) Create(
        RiskConfig? riskConfig = null,
        PreMarketGapConfig? gapConfig = null,
        IntradayDipConfig? dipConfig = null);
}

// ── 組態持久化介面 ──────────────────────────────────────────────────────

/// <summary>
/// 交易系統組態持久化介面 — 提供 Load / Save 操作（類似資料庫）。
/// </summary>
public interface IConfigurationStore
{
    /// <summary>從持久化儲存載入組態</summary>
    Task<TradingConfiguration> LoadAsync();

    /// <summary>將組態寫入持久化儲存</summary>
    Task SaveAsync(TradingConfiguration config);
}

// ── 交易日誌持久化介面 ──────────────────────────────────────────────────

/// <summary>
/// 每日實際成交紀錄持久化介面 — 提供 CRUD 操作。
/// </summary>
public interface ITradeJournalStore
{
    /// <summary>取得所有交易紀錄</summary>
    Task<List<TradeRecord>> GetAllAsync();

    /// <summary>取得指定日期的交易紀錄</summary>
    Task<List<TradeRecord>> GetByDateAsync(DateTime date);

    /// <summary>新增一筆交易紀錄</summary>
    Task AddAsync(TradeRecord record);

    /// <summary>更新一筆交易紀錄</summary>
    Task UpdateAsync(TradeRecord record);

    /// <summary>刪除一筆交易紀錄</summary>
    Task DeleteAsync(string id);

    /// <summary>匯出交易紀錄為 CSV 字串</summary>
    Task<string> ExportCsvAsync(DateTime? fromDate = null, DateTime? toDate = null);
}
