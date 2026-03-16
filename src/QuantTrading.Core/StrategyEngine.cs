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
    private readonly IntradayDipConfig _dipConfig;
    
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
        IntradayDipConfig? dipConfig = null)
    {
        _riskManager = riskManager;
        _gapConfig = gapConfig ?? new PreMarketGapConfig();
        _dipConfig = dipConfig ?? new IntradayDipConfig();
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
            var time = tick.Timestamp.ToLocalTime().TimeOfDay;

            // Strategy A: Pre-Market Gap (MonitorStart ~ MonitorEnd)
            if (time >= _gapConfig.MonitorStart && time <= _gapConfig.MonitorEnd)
            {
                if (_referencePrices.TryGetValue(tick.Ticker, out decimal refPrice))
                {
                    if (tick.Price > refPrice * (1 + _gapConfig.GapStrengthPercent))
                    {
                        state.IsStrongGap = true;
                    }
                }
                
                // Check for sharp pullbacks (Fakeout Filter)
                if (state.PreMarketHigh > 0 && 
                    (state.PreMarketHigh - tick.Price) / state.PreMarketHigh > _gapConfig.FakeoutPullbackPercent)
                {
                    state.IsFakeout = true;
                }
                state.PreMarketHigh = Math.Max(state.PreMarketHigh, tick.Price);

                if (time >= _gapConfig.MonitorEnd && state.IsStrongGap && !state.IsFakeout && !state.OpenGapTriggered)
                {
                    state.OpenGapTriggered = true;
                    GenerateSignal(StrategyType.OpenGap, OrderType.MarketBuy, tick, tick.Price * (1 - _gapConfig.StopLossOffsetPercent), 1.0);
                }
            }

            // Strategy B Confirmation: Next Tick Up (Price > Last Price)
            if (state.PotentialDipSignalReady && tick.Price > state.LastTickPrice)
            {
                GenerateSignal(StrategyType.IntradayDip, OrderType.LimitBuy, tick, state.LastBarLow, state.LastVolumeRatio);
                state.PotentialDipSignalReady = false; // Reset after trigger
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
            bars.Enqueue(bar);
            if (bars.Count > 100) bars.Dequeue();

            // Update VWAP
            state.UpdateVWAP(bar);

            // Strategy B: Intraday Dip & Volume Surge (ActiveStart ~ ActiveEnd)
            var time = bar.Timestamp.ToLocalTime().TimeOfDay;
            if (time >= _dipConfig.ActiveStart && time <= _dipConfig.ActiveEnd)
            {
                if (bars.Count >= _dipConfig.VolumeLookbackBars + 1)
                {
                    var history = bars.Take(bars.Count - 1).ToList();
                    double avgVol = history.TakeLast(_dipConfig.VolumeLookbackBars).Average(b => (double)b.Volume);
                    
                    bool isDip = bar.Close < (state.Vwap * (1m - _dipConfig.DipThresholdPercent));
                    bool isVolumeSpike = (double)bar.Volume > (avgVol * _dipConfig.VolumeSpikeMultiplier);

                    if (isDip && isVolumeSpike)
                    {
                        state.PotentialDipSignalReady = true;
                        state.LastBarLow = bar.Low;
                        state.LastVolumeRatio = (double)bar.Volume / avgVol;
                    }
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
        public decimal PreMarketHigh { get; set; }
        public bool IsStrongGap { get; set; }
        public bool IsFakeout { get; set; }
        public bool OpenGapTriggered { get; set; }

        public decimal Vwap { get; private set; }
        private decimal _cumPriceVol;
        private long _cumVol;

        public bool PotentialDipSignalReady { get; set; }
        public decimal LastTickPrice { get; set; }
        public decimal LastBarLow { get; set; }
        public double LastVolumeRatio { get; set; }

        public void UpdateVWAP(BarData bar)
        {
            _cumPriceVol += bar.Close * bar.Volume;
            _cumVol += bar.Volume;
            Vwap = _cumPriceVol / _cumVol;
        }
    }
}
