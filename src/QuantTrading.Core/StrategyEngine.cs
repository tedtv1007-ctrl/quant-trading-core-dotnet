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
            var time = tick.Timestamp.TimeOfDay;

            // Strategy A: Pre-Market Gap
            if (time >= _gapConfig.MonitorStart && time <= _gapConfig.MonitorEnd)
            {
                if (_referencePrices.TryGetValue(tick.Ticker, out decimal refPrice))
                {
                    if (tick.Price > refPrice * (1 + _gapConfig.GapStrengthPercent))
                    {
                        state.IsStrongGap = true;
                    }
                }
                
                if (state.PreMarketHigh > 0 && 
                    (state.PreMarketHigh - tick.Price) / state.PreMarketHigh > _gapConfig.FakeoutPullbackPercent)
                {
                    state.IsFakeout = true;
                }
                state.PreMarketHigh = Math.Max(state.PreMarketHigh, tick.Price);
            }

            // Strategy A Trigger
            if (time >= _gapConfig.MonitorEnd && time < _gapConfig.MonitorEnd.Add(TimeSpan.FromMinutes(5)) && 
                state.IsStrongGap && !state.IsFakeout && !state.OpenGapTriggered)
            {
                state.OpenGapTriggered = true;
                GenerateSignal(StrategyType.OpenGap, OrderType.MarketBuy, tick, tick.Price * (1 - _gapConfig.StopLossOffsetPercent), 1.0);
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
                    decimal stopLoss = tick.Price * (1 - _dipConfig.StopLossOffsetPercent);
                    GenerateSignal(StrategyType.IntradayDip, OrderType.LimitBuy, tick, stopLoss, state.LastVolumeRatio);
                    
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
        public bool IsStrongGap { get; set; }
        public bool IsFakeout { get; set; }
        public bool OpenGapTriggered { get; set; }
        public bool OpenBaseTriggered { get; set; }

        public decimal Vwap { get; private set; }
        private decimal _cumPriceVol;
        private long _cumVol;

        public bool IsVolumeSpiking { get; set; }
        public bool HasDipped { get; set; }
        public decimal LastTickPrice { get; set; }
        public double LastVolumeRatio { get; set; }

        public void UpdateVWAP(BarData bar)
        {
            _cumPriceVol += bar.Close * bar.Volume;
            _cumVol += bar.Volume;
            Vwap = _cumPriceVol / _cumVol;
        }
    }
}
