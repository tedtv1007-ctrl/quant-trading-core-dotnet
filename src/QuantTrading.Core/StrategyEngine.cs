using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using QuantTrading.Core.Interfaces;
using QuantTrading.Core.Models;

namespace QuantTrading.Core;

/// <summary>
/// 策略引擎 — 實作 Strategy A (開盤試搓) 與 Strategy B (盤中低接反彈)。
/// 
/// ┌───────────────────────────────────────────────────────────────────┐
/// │  08:30        08:59:55  09:00:00        09:01 ─── 13:25         │
/// │  ├── Strategy A 試搓監控 ──┤  │  ├── Strategy B 盤中運行 ───┤   │
/// └───────────────────────────────────────────────────────────────────┘
/// </summary>
public class StrategyEngine : IStrategyEngine
{
    // ── Dependencies ────────────────────────────────────────────────
    private readonly IRiskManager _riskManager;
    private readonly PreMarketGapConfig _gapConfig;
    private readonly IntradayDipConfig _dipConfig;

    // ── Events ──────────────────────────────────────────────────────
    public event Action<SignalContext>? OnSignalGenerated;
    public event Action<RejectedSignal>? OnSignalRejected;

    // ── Shared State ────────────────────────────────────────────────
    private readonly ConcurrentDictionary<string, decimal> _refPrices = new();

    // ── Strategy A State (per ticker) ───────────────────────────────
    private readonly ConcurrentDictionary<string, PreMarketState> _preMarketStates = new();

    // ── Strategy B State (per ticker) ───────────────────────────────
    private readonly ConcurrentDictionary<string, IntradayState> _intradayStates = new();

    // ── Bar History (per ticker, Queue for O(1) sliding window) ────
    private readonly ConcurrentDictionary<string, Queue<BarData>> _barHistory = new();

    // ─────────────────────────────────────────────────────────────────

    public StrategyEngine(
        IRiskManager riskManager,
        PreMarketGapConfig? gapConfig = null,
        IntradayDipConfig? dipConfig = null)
    {
        _riskManager = riskManager ?? throw new ArgumentNullException(nameof(riskManager));
        _gapConfig = gapConfig ?? new PreMarketGapConfig();
        _dipConfig = dipConfig ?? new IntradayDipConfig();
    }

    /// <inheritdoc />
    public void SetReferencePrice(string ticker, decimal refPrice)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(ticker, nameof(ticker));
        _refPrices[ticker] = refPrice;
    }

    // ═════════════════════════════════════════════════════════════════
    //  Tick 處理入口
    // ═════════════════════════════════════════════════════════════════

    public Task ProcessTickAsync(TickData tick)
    {
        if (string.IsNullOrWhiteSpace(tick.Ticker)) return Task.CompletedTask;

        var time = tick.Timestamp.TimeOfDay;

        // ── Strategy A: 試搓時段 (08:30 ~ 09:00) ──────────────
        if (time >= _gapConfig.MonitorStart && time <= _gapConfig.ExecutionTime)
        {
            EvaluatePreMarketGap(tick, time);
        }

        // ── Strategy B: 盤中時段 ──────────────────────────────
        if (time >= _dipConfig.ActiveStart && time <= _dipConfig.ActiveEnd)
        {
            EvaluateIntradayDipTick(tick);
        }

        return Task.CompletedTask;
    }

    // ═════════════════════════════════════════════════════════════════
    //  Bar 處理入口
    // ═════════════════════════════════════════════════════════════════

    public Task ProcessBarAsync(BarData bar)
    {
        if (string.IsNullOrWhiteSpace(bar.Ticker)) return Task.CompletedTask;

        // ── 維護 K 棒歷史 ──────────────────────────────────────
        var bars = _barHistory.GetOrAdd(bar.Ticker, _ => new Queue<BarData>());
        lock (bars)
        {
            bars.Enqueue(bar);
            // 只保留最近 100 根用於計算
            if (bars.Count > 100)
                bars.Dequeue();
        }

        // ── 更新 VWAP ─────────────────────────────────────────
        var state = _intradayStates.GetOrAdd(bar.Ticker, _ => new IntradayState());
        lock (state)
        {
            // VWAP = Σ(TypicalPrice × Volume) / Σ(Volume)
            decimal typicalPrice = (bar.High + bar.Low + bar.Close) / 3m;
            state.CumulativePriceVolume += typicalPrice * bar.Volume;
            state.CumulativeVolume += bar.Volume;
            state.Vwap = state.CumulativeVolume > 0
                ? state.CumulativePriceVolume / state.CumulativeVolume
                : bar.Close;
        }

        // ── Strategy B: 檢查 Volume Spike ─────────────────────
        EvaluateIntradayDipBar(bar);

        return Task.CompletedTask;
    }

    // ═════════════════════════════════════════════════════════════════
    //  Strategy A — Pre-Market Gap (開盤試搓策略)
    // ═════════════════════════════════════════════════════════════════

    private void EvaluatePreMarketGap(TickData tick, TimeSpan time)
    {
        if (!_refPrices.TryGetValue(tick.Ticker, out var refPrice) || refPrice <= 0)
            return;

        var state = _preMarketStates.GetOrAdd(tick.Ticker, _ => new PreMarketState());

        lock (state)
        {
            // ── 追蹤試搓期間的最高價與最新價 ────────────────────
            if (tick.Price > state.SimHighPrice)
                state.SimHighPrice = tick.Price;

            state.LatestSimPrice = tick.Price;

            // ── Condition 1: Gap Strength ───────────────────────
            //    SimPrice > RefPrice * (1 + GapStrengthPercent)
            bool isStrongGap = tick.Price > refPrice * (1m + _gapConfig.GapStrengthPercent);

            // ── Condition 2: Fakeout Filter ─────────────────────
            //    從試搓最高價回落超過 FakeoutPullbackPercent → 判定為假突破
            bool isFakeout = state.SimHighPrice > 0 &&
                             (state.SimHighPrice - tick.Price) / state.SimHighPrice
                                 > _gapConfig.FakeoutPullbackPercent;

            if (isFakeout)
                state.FakeoutDetected = true;

            // ── 判定時刻: 08:59:55 ─ 確認所有條件 ──────────────
            if (time >= _gapConfig.MonitorEnd && !state.SignalEmitted)
            {
                state.SignalEmitted = true; // 每檔只發一次

                if (isStrongGap && !state.FakeoutDetected)
                {
                    // 停損價 = 進場價 × (1 - StopLossOffsetPercent)
                    decimal stopLoss = tick.Price * (1m - _gapConfig.StopLossOffsetPercent);

                    EmitSignal(
                        StrategyType.OpenGap,
                        OrderType.MarketBuy,
                        tick,
                        stopLoss,
                        volumeRatio: 1.0);
                }
            }
        }
    }

    // ═════════════════════════════════════════════════════════════════
    //  Strategy B — Intraday Dip & Volume Surge (盤中低接反彈)
    // ═════════════════════════════════════════════════════════════════

    /// <summary>
    /// Bar 層級：檢測量能爆量條件 (Volume Spike)
    /// </summary>
    private void EvaluateIntradayDipBar(BarData bar)
    {
        if (!_barHistory.TryGetValue(bar.Ticker, out var bars))
            return;

        double volumeRatio;
        lock (bars)
        {
            int lookback = _dipConfig.VolumeLookbackBars;
            if (bars.Count < lookback + 1)
                return; // 資料不足

            // 取最近 N 根 (不含當前這根) 的平均量
            var recentBars = bars
                .Skip(Math.Max(0, bars.Count - lookback - 1))
                .Take(lookback)
                .ToList();

            double avgVolume = recentBars.Average(b => (double)b.Volume);
            if (avgVolume <= 0) return;

            volumeRatio = bar.Volume / avgVolume;
        }

        // ── 取得 Intraday State ────────────────────────────────
        var state = _intradayStates.GetOrAdd(bar.Ticker, _ => new IntradayState());
        lock (state)
        {
            // Condition 2: Volume Spike — 當前量 > 平均量 × 倍數
            state.IsVolumeSpikeActive = volumeRatio >= _dipConfig.VolumeSpikeMultiplier;
            state.LastVolumeRatio = volumeRatio;
        }
    }

    /// <summary>
    /// Tick 層級：檢測低接條件 + 止跌確認
    /// </summary>
    private void EvaluateIntradayDipTick(TickData tick)
    {
        var state = _intradayStates.GetOrAdd(tick.Ticker, _ => new IntradayState());

        lock (state)
        {
            decimal vwap = state.Vwap;
            if (vwap <= 0)
            {
                state.PreviousTickPrice = tick.Price;
                return;
            }

            // ── Condition 1: Dip Detection ─────────────────────
            //    當前價 < VWAP × (1 - DipThresholdPercent)
            decimal dipThreshold = vwap * (1m - _dipConfig.DipThresholdPercent);
            bool isDip = tick.Price < dipThreshold;

            // ── 複合條件：Dip + Volume Spike ───────────────────
            if (isDip && state.IsVolumeSpikeActive)
            {
                state.DipVolumeConditionMet = true;
            }

            // ── 止跌確認：下一筆 Tick 上漲 ─────────────────────
            //    前一筆 Tick 已滿足 Dip+Volume 且本筆 > 前一筆 = 止跌
            if (state.DipVolumeConditionMet
                && state.PreviousTickPrice > 0
                && tick.Price > state.PreviousTickPrice)
            {
                // 確認反彈，發送 Limit Buy
                decimal stopLoss = tick.Price * (1m - _dipConfig.StopLossOffsetPercent);

                EmitSignal(
                    StrategyType.IntradayDip,
                    OrderType.LimitBuy,
                    tick,
                    stopLoss,
                    state.LastVolumeRatio);

                // 重置，避免同一波連續觸發
                state.DipVolumeConditionMet = false;
                state.IsVolumeSpikeActive = false;
            }

            state.PreviousTickPrice = tick.Price;
        }
    }

    // ═════════════════════════════════════════════════════════════════
    //  Signal Emission (透過 RiskManager 驗證後發送)
    // ═════════════════════════════════════════════════════════════════

    private void EmitSignal(
        StrategyType strategy,
        OrderType orderType,
        TickData tick,
        decimal stopLoss,
        double volumeRatio)
    {
        var (signal, result) = _riskManager.EvaluateSignal(strategy, orderType, tick, stopLoss, volumeRatio);

        if (signal is not null)
        {
            try
            {
                var handler = OnSignalGenerated;
                handler?.Invoke(signal);
            }
            catch
            {
                // 不讓訂閱者例外向上傳播破壞策略狀態
            }
        }
        else
        {
            try
            {
                // 風控拒絕 — result 攜帶具體拒絕原因
                var rejHandler = OnSignalRejected;
                rejHandler?.Invoke(new RejectedSignal(
                    Reason: result,
                    Strategy: strategy,
                    Ticker: tick.Ticker,
                    Timestamp: DateTime.UtcNow
                ));
            }
            catch
            {
                // 不讓訂閱者例外向上傳播
            }
        }
    }

    // ═════════════════════════════════════════════════════════════════
    //  Inner State Classes
    // ═════════════════════════════════════════════════════════════════

    /// <summary>Strategy A 每檔標的的試搓狀態</summary>
    private class PreMarketState
    {
        public decimal SimHighPrice;
        public decimal LatestSimPrice;
        public bool FakeoutDetected;
        public bool SignalEmitted;
    }

    /// <summary>Strategy B 每檔標的的盤中狀態</summary>
    private class IntradayState
    {
        // VWAP 計算
        public decimal CumulativePriceVolume;
        public long CumulativeVolume;
        public decimal Vwap;

        // Volume Spike
        public bool IsVolumeSpikeActive;
        public double LastVolumeRatio;

        // Dip + Reversal
        public bool DipVolumeConditionMet;
        public decimal PreviousTickPrice;
    }
}
