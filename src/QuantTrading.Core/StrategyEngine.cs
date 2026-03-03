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
    private readonly decimal _refPrice;
    private readonly decimal _dipThresholdPercent = 0.02m; // 2% below VWAP
    private readonly double _volumeSpikeMultiplier = 3.0; // 3x avg volume
    
    private readonly ConcurrentDictionary<string, IntradayState> _states = new();
    private readonly ConcurrentDictionary<string, Queue<BarData>> _barHistory = new();

    public event Action<SignalContext>? OnSignalGenerated;

    public StrategyEngine(IRiskManager riskManager, decimal refPrice)
    {
        _riskManager = riskManager;
        _refPrice = refPrice;
    }

    public async Task ProcessTickAsync(TickData tick)
    {
        var state = _states.GetOrAdd(tick.Ticker, _ => new IntradayState());
        
        lock (state)
        {
            var time = tick.Timestamp.ToLocalTime().TimeOfDay;

            // Strategy A: Pre-Market Gap (08:30 ~ 08:59:55)
            if (time >= new TimeSpan(8, 30, 0) && time <= new TimeSpan(8, 59, 55))
            {
                if (tick.Price > _refPrice * 1.01m)
                {
                    state.IsStrongGap = true;
                }
                
                // Check for sharp pullbacks (Fakeout Filter)
                if (state.PreMarketHigh > 0 && (state.PreMarketHigh - tick.Price) / state.PreMarketHigh > 0.005m)
                {
                    state.IsFakeout = true;
                }
                state.PreMarketHigh = Math.Max(state.PreMarketHigh, tick.Price);

                if (time >= new TimeSpan(8, 59, 55) && state.IsStrongGap && !state.IsFakeout && !state.OpenGapTriggered)
                {
                    state.OpenGapTriggered = true;
                    GenerateSignal(StrategyType.OpenGap, tick, tick.Price * 0.99m, 1.0);
                }
            }

            // Strategy B Confirmation: Next Tick Up (Price > Last Price)
            if (state.PotentialDipSignalReady && tick.Price > state.LastTickPrice)
            {
                GenerateSignal(StrategyType.IntradayDip, tick, state.LastBarLow, state.LastVolumeRatio);
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

            // Update VWAP (Simplified calculation)
            state.UpdateVWAP(bar);

            // Strategy B: Intraday Dip & Volume Surge
            if (bars.Count >= 6) // Need 5 for average + 1 current
            {
                var history = bars.Take(bars.Count - 1).ToList();
                double avgVol = history.TakeLast(5).Average(b => (double)b.Volume);
                
                bool isDip = bar.Close < (state.Vwap * (1m - _dipThresholdPercent));
                bool isVolumeSpike = (double)bar.Volume > (avgVol * _volumeSpikeMultiplier);

                if (isDip && isVolumeSpike)
                {
                    state.PotentialDipSignalReady = true;
                    state.LastBarLow = bar.Low;
                    state.LastVolumeRatio = (double)bar.Volume / avgVol;
                }
            }
        }
    }

    private void GenerateSignal(StrategyType type, TickData tick, decimal stopLoss, double volumeRatio)
    {
        var signal = _riskManager.CalculateAndValidate(type, tick, stopLoss, volumeRatio);
        if (signal != null) OnSignalGenerated?.Invoke(signal);
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
