using Microsoft.Extensions.Logging;
using QuantTrading.Core.Interfaces;
using QuantTrading.Core.Models;

namespace QuantTrading.Web.Services;

/// <summary>
/// 模擬執行服務 — 將模擬行情注入 StrategyEngine，結果寫入 TradingStateService。
/// 支援同時對多支股票執行模擬。
/// 透過 ITradingEngineFactory 建立引擎實例（遵循 DIP）。
/// </summary>
public class SimulationBackgroundService : IDisposable
{
    private readonly ITradingEngineFactory _engineFactory;
    private readonly TradingStateService _state;
    private readonly TradingConfiguration _config;
    private readonly ILogger<SimulationBackgroundService> _logger;
    private readonly object _lock = new();
    private CancellationTokenSource? _cts;
    private Task? _runningTask;

    public SimulationBackgroundService(
        ITradingEngineFactory engineFactory,
        TradingStateService state,
        TradingConfiguration config,
        ILogger<SimulationBackgroundService> logger)
    {
        _engineFactory = engineFactory;
        _state = state;
        _config = config;
        _logger = logger;
    }

    /// <summary>
    /// 啟動多股模擬（非阻塞）。Task 內部例外會被觀察並記錄。
    /// </summary>
    public void StartSimulation(DateTime simulationDate, int tickDelayMs = 100)
    {
        lock (_lock)
        {
            if (_state.IsSimulationRunning) return;
            _state.IsSimulationRunning = true;

            // Dispose previous CTS if any (defensive)
            _cts?.Dispose();
            _cts = new CancellationTokenSource();
            var ct = _cts.Token;
            _runningTask = RunAndObserveAsync(simulationDate, tickDelayMs, ct);
        }
    }

    private async Task RunAndObserveAsync(DateTime simulationDate, int tickDelayMs, CancellationToken ct)
    {
        try
        {
            await RunMultiStockSimulationAsync(simulationDate, tickDelayMs, ct);
        }
        catch (OperationCanceledException)
        {
            // Expected on cancellation
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Simulation task failed unexpectedly.");
        }
    }

    /// <summary>
    /// 依據 watchlist 中所有股票同時啟動模擬。
    /// 共用同一組 RiskManager，各股票平行跑模擬資料。
    /// </summary>
    private async Task RunMultiStockSimulationAsync(
        DateTime simulationDate,
        int tickDelayMs,
        CancellationToken ct)
    {
        var watchlist = _state.GetWatchlist();
        if (watchlist.Count == 0)
        {
            _state.IsSimulationRunning = false;
            return;
        }

        _state.ClearAll();
        // ClearAll resets statuses and clears data queues but preserves watchlist entries
        _state.SimulationStatus = $"Running {watchlist.Count} stocks...";
        _state.NotifyStateChanged();

        try
        {
            // 透過工廠建立共用 RiskManager + StrategyEngine（DIP）
            // 使用當前儲存的組態參數
            var (engine, riskManager) = _engineFactory.Create(
                _config.RiskConfig, _config.GapConfig, _config.DipConfig);

            // 為每支股票設定參考價
            foreach (var w in watchlist)
            {
                engine.SetReferencePrice(w.Ticker, w.RefPrice);
            }

            // 掛載事件
            engine.OnSignalGenerated += signal =>
            {
                _state.AddSignal(signal);
                _state.DailyTradeCount = riskManager.DailyTradeCount;
                _state.DailyRealizedLoss = riskManager.DailyRealizedLoss;
            };

            engine.OnSignalRejected += rejection =>
            {
                _state.AddRejection(rejection);
            };

            // 為每支股票產生模擬資料
            var simulator = new MarketDataSimulator();
            var scenarios = watchlist.Select(w =>
                simulator.GenerateFullDayScenario(w.Ticker, w.RefPrice, simulationDate)).ToList();

            // ── Phase 1: 所有股票的試搓階段 (平行) ─────────────
            _state.SimulationStatus = "Phase 1: Pre-Market Gap 試搓中...";
            _state.NotifyStateChanged();

            var preMarketTasks = scenarios.Select(scenario =>
                RunPreMarketPhaseAsync(engine, scenario, tickDelayMs, ct));
            await Task.WhenAll(preMarketTasks);

            if (ct.IsCancellationRequested) return;

            // ── Phase 2: 所有股票的盤中 K 棒 + Tick (平行) ──────
            _state.SimulationStatus = "Phase 2: Intraday Dip 盤中分析...";
            _state.NotifyStateChanged();

            var intradayTasks = scenarios.Select(scenario =>
                RunIntradayPhaseAsync(engine, scenario, tickDelayMs, ct));
            await Task.WhenAll(intradayTasks);

            _state.SimulationStatus = $"Completed ✓ ({watchlist.Count} stocks)";
            foreach (var w in watchlist)
                _state.UpdateWatchlistStatus(w.Ticker, "Completed ✓", false);
        }
        catch (TaskCanceledException)
        {
            _state.SimulationStatus = "Cancelled";
        }
        catch (Exception ex)
        {
            _state.SimulationStatus = $"Error: {ex.Message}";
            _logger.LogError(ex, "Simulation error.");
        }
        finally
        {
            _state.IsSimulationRunning = false;
            _state.NotifyStateChanged();
        }
    }

    /// <summary>單股試搓階段。</summary>
    private async Task RunPreMarketPhaseAsync(
        IStrategyEngine engine, SimulationScenario scenario, int delayMs, CancellationToken ct)
    {
        _state.UpdateWatchlistStatus(scenario.Ticker, "Pre-Market...", true);

        foreach (var tick in scenario.PreMarketTicks)
        {
            if (ct.IsCancellationRequested) break;
            _state.AddTick(tick);
            await engine.ProcessTickAsync(tick);
            await Task.Delay(delayMs, ct);
        }

        _state.UpdateWatchlistStatus(scenario.Ticker, "Pre-Market Done", true);
    }

    /// <summary>單股盤中階段。</summary>
    private async Task RunIntradayPhaseAsync(
        IStrategyEngine engine, SimulationScenario scenario, int delayMs, CancellationToken ct)
    {
        _state.UpdateWatchlistStatus(scenario.Ticker, "Intraday...", true);

        foreach (var bar in scenario.IntradayBars)
        {
            if (ct.IsCancellationRequested) break;
            _state.AddBar(bar);
            await engine.ProcessBarAsync(bar);
            await Task.Delay(delayMs, ct);
        }

        foreach (var tick in scenario.IntradayTicks)
        {
            if (ct.IsCancellationRequested) break;
            _state.AddTick(tick);
            await engine.ProcessTickAsync(tick);
            await Task.Delay(delayMs, ct);
        }

        _state.UpdateWatchlistStatus(scenario.Ticker, "Completed ✓", false);
    }

    public async Task StopSimulationAsync()
    {
        Task? taskToWait;
        lock (_lock)
        {
            _cts?.Cancel();
            taskToWait = _runningTask;
        }

        // 等待模擬任務完成（已在 RunAndObserveAsync 中 catch 所有例外）
        if (taskToWait is not null)
        {
            try { await taskToWait; }
            catch { /* already handled in RunAndObserveAsync */ }
        }

        lock (_lock)
        {
            _cts?.Dispose();
            _cts = null;
            _runningTask = null;
        }
    }

    /// <summary>同步版本，僅發送取消訊號，不等待完成。CTS 由下次 StartSimulation 或 Dispose 負責回收。</summary>
    public void StopSimulation()
    {
        lock (_lock)
        {
            _cts?.Cancel();
        }
    }

    public void Dispose()
    {
        lock (_lock)
        {
            _cts?.Cancel();
            _cts?.Dispose();
            _cts = null;
        }
    }
}
