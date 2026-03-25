using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using QuantTrading.Core.Interfaces;
using QuantTrading.Core.Models;

namespace QuantTrading.Core;

public class StrategyEngine : IStrategyEngine
{
    private readonly IRiskManager _riskManager;
    private readonly PreMarketGapConfig _gapConfig;
    private readonly OpenBaseStrategyConfig _dipConfig;
    private readonly LiquidityRiskEvaluator _liquidityRiskEvaluator = new();
    
    // 儲存每檔股票的參考價 (Strategy A 使用)
    private readonly ConcurrentDictionary<string, decimal> _referencePrices = new();
    
    // 儲存每檔股票的運行狀態
    private readonly ConcurrentDictionary<string, IntradayState> _states = new();
    
    // 儲存每檔股票的成交歷史 (Strategy B 使用)
    private readonly ConcurrentDictionary<string, Queue<BarData>> _barHistory = new();

    public event Action<SignalContext>? OnSignalGenerated;
    public event Action<RejectedSignal>? OnSignalRejected;

    public StrategyEngine(
        IRiskManager riskManager, 
        PreMarketGapConfig? gapConfig = null, 
        OpenBaseStrategyConfig? dipConfig = null)
    {
        _riskManager = riskManager;
        _gapConfig = gapConfig ?? new PreMarketGapConfig();
        _dipConfig = dipConfig ?? new OpenBaseStrategyConfig();
    }

    public void SetReferencePrice(string ticker, decimal refPrice)
    {
        _referencePrices[ticker] = refPrice;
    }

    public async Task ProcessTickAsync(TickData tick)
    {
        var state = _states.GetOrAdd(tick.Ticker, _ => new IntradayState());
        
        lock (state)
        {
            // ── 流動性風險評估 (Limit-Down Emergency Sell) ──
            if (_referencePrices.TryGetValue(tick.Ticker, out decimal refPriceForRisk))
            {
                bool hasLongPosition = state.OpenGapTriggered || state.OpenBaseTriggered;
                if (_liquidityRiskEvaluator.TryEvaluateEmergencySell(tick, refPriceForRisk, hasLongPosition, out var emergencySignal))
                {
                    OnSignalGenerated?.Invoke(emergencySignal!);
                }
            }

            // 若因流動性崩跌被暫停進場，直接捨棄後續邏輯
            if (_liquidityRiskEvaluator.IsSuspended(tick.Ticker)) return;

            var time = tick.Timestamp.TimeOfDay;

            // Strategy A: Pre-Market Gap
            if (time >= _gapConfig.MonitorStart && time <= _gapConfig.MonitorEnd)
            {
                if (_referencePrices.TryGetValue(tick.Ticker, out decimal refPrice))
                {
                    // 在 08:54 前追蹤是否呈現漲停試搓 (Limit Up ~ 9.5%)
                    if (time < new TimeSpan(8, 54, 0))
                    {
                        if (tick.Price >= refPrice * 1.095m)
                        {
                            state.HitLimitUpBefore0854 = true;
                        }
                    }

                    // 僅接受 08:55:00 至 08:59:59 之間的 Tick 作為有效跳空訊號
                    if (time >= new TimeSpan(8, 55, 0) && time <= new TimeSpan(8, 59, 59))
                    {
                        if (tick.Price > refPrice * (1 + _gapConfig.GapStrengthPercent))
                        {
                            state.IsStrongGap = true;
                        }
                        
                        // Fakeout (HighRisk) 判斷
                        if (state.HitLimitUpBefore0854 && state.PreMarketHigh > 0 && 
                            (state.PreMarketHigh - tick.Price) / state.PreMarketHigh > _gapConfig.FakeoutPullbackPercent)
                        {
                            state.IsHighRisk = true;
                        }
                    }
                }
                
                state.PreMarketHigh = Math.Max(state.PreMarketHigh, tick.Price);
            }

            // Strategy A Trigger
            if (time >= _gapConfig.MonitorEnd && time < _gapConfig.MonitorEnd.Add(TimeSpan.FromMinutes(5)) && !state.OpenGapTriggered)
            {
                if (state.IsHighRisk)
                {
                    state.OpenGapTriggered = true;
                    OnSignalRejected?.Invoke(new RejectedSignal(
                        Reason: SignalResult.RejectRisk,
                        Strategy: StrategyType.OpenGap,
                        Ticker: tick.Ticker,
                        Timestamp: tick.Timestamp
                    ));
                }
                else if (state.IsStrongGap)
                {
                    state.OpenGapTriggered = true;
                    GenerateSignal(StrategyType.OpenGap, OrderType.MarketBuy, tick, tick.Price * (1 - _gapConfig.StopLossOffsetPercent), 1.0);
                }
            }

            // Track Order Flow Volumes for Strategy B
            if (time >= _dipConfig.ActiveStart && time <= _dipConfig.ActiveEnd)
            {
                if (tick.Type == TickType.Up) state.OuterVolume += tick.Volume;
                else if (tick.Type == TickType.Down) state.InnerVolume += tick.Volume;
            }

            // Strategy B (Intraday Dip): Check for dip below VWAP and then reversal
            if (time >= _dipConfig.ActiveStart && time <= _dipConfig.ActiveEnd && !state.OpenBaseTriggered)
            {
                bool volumeCondition = !_dipConfig.RequireVolumeSpike || state.IsVolumeSpiking;
                
                // 1. Check for Dip below VWAP threshold (If not yet dipped)
                if (!state.HasDipped)
                {
                    if (state.Vwap > 0 && tick.Price <= state.Vwap * (1 - _dipConfig.DipThresholdPercent))
                    {
                        state.HasDipped = true;
                    }
                }
                // 2. If already dipped, check for reversal (Trend up) + Volume condition
                else if (tick.Price > state.LastTickPrice && volumeCondition)
                {
                    state.OpenBaseTriggered = true;

                    // 弱勢反彈檢測: 價格是否直接突破 VWAP 或 賣盤(內)/買盤(外) > 70%
                    bool isWeakVwap = tick.Price >= state.Vwap;
                    bool isWeakVolume = (state.OuterVolume > 0 && (double)state.InnerVolume / state.OuterVolume > 0.70) 
                                        || (state.OuterVolume == 0 && state.InnerVolume > 0);

                    if (isWeakVwap || isWeakVolume)
                    {
                        OnSignalRejected?.Invoke(new RejectedSignal(
                            Reason: SignalResult.RejectRisk,
                            Strategy: StrategyType.IntradayDip,
                            Ticker: tick.Ticker,
                            Timestamp: tick.Timestamp
                        ));
                    }
                    else
                    {
                        decimal stopLoss = tick.Price * (1 - _dipConfig.StopLossOffsetPercent);
                        GenerateSignal(StrategyType.IntradayDip, OrderType.LimitBuy, tick, stopLoss, state.LastVolumeRatio);
                    }
                    
                    // Reset spike to prevent immediate re-trigger
                    state.IsVolumeSpiking = false; 
                }
            }

            state.LastTickPrice = tick.Price;
        }
    }

    public async Task ProcessBarAsync(BarData bar)
    {
        var bars = _barHistory.GetOrAdd(bar.Ticker, _ => new Queue<BarData>());
        var state = _states.GetOrAdd(bar.Ticker, _ => new IntradayState());

        lock (bars)
        {
            // 第一根 K 棒決定開盤價
            if (state.DailyOpen == 0) state.DailyOpen = bar.Open;

            bars.Enqueue(bar);
            if (bars.Count > 100) bars.Dequeue();

            state.UpdateVWAP(bar);

            var time = bar.Timestamp.TimeOfDay;
            if (time >= _dipConfig.ActiveStart && time <= _dipConfig.ActiveEnd)
            {
                if (bars.Count >= 6) // Lookback for volume
                {
                    var history = bars.Take(bars.Count - 1).ToList();
                    double avgVol = history.TakeLast(5).Average(b => (double)b.Volume);
                    
                    state.IsVolumeSpiking = (double)bar.Volume > (avgVol * _dipConfig.VolumeSpikeMultiplier);
                    state.LastVolumeRatio = (double)bar.Volume / avgVol;
                }
            }
        }
    }

    private void GenerateSignal(StrategyType type, OrderType orderType, TickData tick, decimal stopLoss, double volumeRatio)
    {
        var (signal, result) = _riskManager.EvaluateSignal(type, orderType, tick, stopLoss, volumeRatio);
        
        if (signal != null)
        {
            OnSignalGenerated?.Invoke(signal);
        }
        else if (result != SignalResult.Accept)
        {
            OnSignalRejected?.Invoke(new RejectedSignal(
                Reason: result,
                Strategy: type,
                Ticker: tick.Ticker,
                Timestamp: DateTime.UtcNow
            ));
        }
    }

    private class IntradayState
    {
        public decimal DailyOpen { get; set; }
        public decimal PreMarketHigh { get; set; }
        public bool HitLimitUpBefore0854 { get; set; }
        public bool IsStrongGap { get; set; }
        public bool IsHighRisk { get; set; }
        public bool OpenGapTriggered { get; set; }
        public bool OpenBaseTriggered { get; set; }

        public decimal Vwap { get; private set; }
        private decimal _cumPriceVol;
        private long _cumVol;

        public bool IsVolumeSpiking { get; set; }
        public bool HasDipped { get; set; }
        public decimal LastTickPrice { get; set; }
        public double LastVolumeRatio { get; set; }
        public long InnerVolume { get; set; }
        public long OuterVolume { get; set; }

        public void UpdateVWAP(BarData bar)
        {
            _cumPriceVol += bar.Close * bar.Volume;
            _cumVol += bar.Volume;
            Vwap = _cumPriceVol / _cumVol;
        }
    }
}
