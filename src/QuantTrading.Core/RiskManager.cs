using System;
using QuantTrading.Core.Interfaces;
using QuantTrading.Core.Models;

namespace QuantTrading.Core;

/// <summary>
/// 風控管理器實作
/// ─ 單筆停損 1,100 TWD
/// ─ 單日最多 5 筆交易
/// ─ 單日最大虧損上限
/// ─ 部位上限：單筆最大 999,000 股（安全閥）
/// </summary>
public class RiskManager : IRiskManager
{
    /// <summary>單筆最大部位上限（安全閥，避免極小風險值產生天量部位）。</summary>
    internal const int MaxPositionSize = 999_000;

    private readonly RiskConfig _config;
    private readonly object _lock = new();

    private int _dailyTradeCount;
    private decimal _dailyRealizedLoss;

    public int DailyTradeCount
    {
        get { lock (_lock) return _dailyTradeCount; }
    }

    public decimal DailyRealizedLoss
    {
        get { lock (_lock) return _dailyRealizedLoss; }
    }

    public RiskManager(RiskConfig? config = null)
    {
        _config = config ?? new RiskConfig();
    }

    /// <inheritdoc />
    public (SignalContext? Signal, SignalResult Result) EvaluateSignal(
        StrategyType strategy,
        OrderType orderType,
        TickData tick,
        decimal stopLossPrice,
        double volumeRatio)
    {
        lock (_lock)
        {
            // ── 風控閘門 ────────────────────────────────────────
            if (_dailyTradeCount >= _config.MaxDailyTrades)
                return (null, SignalResult.RejectMaxTrades);

            if (_dailyRealizedLoss >= _config.MaxDailyLoss)
                return (null, SignalResult.RejectDailyLoss);

            // ── 停損方向驗證：買單的 StopLoss 必須低於進場價 ────
            if (stopLossPrice >= tick.Price)
                return (null, SignalResult.RejectRisk);

            // ── 計算部位大小 ────────────────────────────────────
            decimal riskPerShare = tick.Price - stopLossPrice;

            // PositionSize = 1100 / (EntryPrice - StopLossPrice)
            int positionSize = (int)Math.Floor(_config.RiskPerTrade / riskPerShare);
            if (positionSize <= 0)
                return (null, SignalResult.RejectRisk);

            // ── 安全閥：限制最大部位，避免極小風險值產生天量 ────
            if (positionSize > MaxPositionSize)
                positionSize = MaxPositionSize;

            // ── 通過風控，遞增交易計數 ─────────────────────────
            _dailyTradeCount++;

            return (new SignalContext(
                Strategy: strategy,
                OrderType: orderType,
                Ticker: tick.Ticker,
                EntryPrice: tick.Price,
                StopLossPrice: stopLossPrice,
                VolumeRatio: volumeRatio,
                PositionSize: positionSize,
                Timestamp: DateTime.UtcNow
            ), SignalResult.Accept);
        }
    }

    /// <inheritdoc />
    public void RecordRealizedLoss(decimal lossAmount)
    {
        if (lossAmount <= 0) return;
        lock (_lock)
        {
            _dailyRealizedLoss += lossAmount;
        }
    }

    /// <inheritdoc />
    public void ResetDaily()
    {
        lock (_lock)
        {
            _dailyTradeCount = 0;
            _dailyRealizedLoss = 0;
        }
    }

    /// <inheritdoc />
    public SignalResult GetCurrentStatus()
    {
        lock (_lock)
        {
            if (_dailyRealizedLoss >= _config.MaxDailyLoss)
                return SignalResult.RejectDailyLoss;
            if (_dailyTradeCount >= _config.MaxDailyTrades)
                return SignalResult.RejectMaxTrades;
            return SignalResult.Accept;
        }
    }
}
