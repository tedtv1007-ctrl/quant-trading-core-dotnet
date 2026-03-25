using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using QuantTrading.Core.Interfaces;
using QuantTrading.Core.Models;
using QuantTrading.Core.Services;

namespace QuantTrading.Infrastructure.Fugle;

/// <summary>
/// BackgroundService wrapper — 管理 FugleMarketDataFeed 的生命週期。
/// ASP.NET Core Host 啟動時 → StartAsync，停止時 → StopAsync。
/// </summary>
public sealed class FugleMarketDataHostedService : BackgroundService
{
    private readonly IMarketDataFeed _feed;
    private readonly TradingStateService _state;
    private readonly ITradingEngineFactory _engineFactory;
    private readonly TradingConfiguration _config;
    private readonly ILogger<FugleMarketDataHostedService> _logger;

    private IStrategyEngine? _engine;
    private IRiskManager? _riskManager;

    /// <summary>啟動後自動訂閱的預設標的。</summary>
    private static readonly string[] DefaultTickers = ["2344"];

    public FugleMarketDataHostedService(
        IMarketDataFeed feed,
        TradingStateService state,
        ITradingEngineFactory engineFactory,
        TradingConfiguration config,
        ILogger<FugleMarketDataHostedService> logger)
    {
        _feed = feed;
        _state = state;
        _engineFactory = engineFactory;
        _config = config;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("FugleMarketDataHostedService starting...");

        // ── 初始化策略引擎 ──────────────────────────────────────────
        var (engine, riskManager) = _engineFactory.Create(
            _config.RiskConfig, _config.GapConfig, _config.DipConfig);
        _engine = engine;
        _riskManager = riskManager;

        // 註冊事件 — 將訊號寫入 StateService
        _engine.OnSignalGenerated += signal =>
        {
            _logger.LogInformation("🚀 SIGNAL GENERATED: {Ticker} {Strategy} @ {Price}", 
                signal.Ticker, signal.Strategy, signal.EntryPrice);
            _state.AddSignal(signal);
            _state.DailyTradeCount = _riskManager.DailyTradeCount;
            _state.DailyRealizedLoss = _riskManager.DailyRealizedLoss;
        };

        _engine.OnSignalRejected += rejection =>
        {
            _logger.LogWarning("❌ SIGNAL REJECTED: {Ticker} Reason={Reason}", 
                rejection.Ticker, rejection.Reason);
            _state.AddRejection(rejection);
        };

        // 註冊事件 log — 用於觀察即時行情
        _feed.OnTickReceived += async tick =>
        {
            _state.AddTick(tick);
            if (_engine != null) await _engine.ProcessTickAsync(tick);
        };

        _feed.OnBarClosed += async bar =>
        {
            _state.AddBar(bar);
            if (_engine != null) await _engine.ProcessBarAsync(bar);
        };

        // 監聽 Watchlist 變更
        _state.OnStateChanged += () =>
        {
            if (_feed.IsConnected)
            {
                var watchlist = _state.GetWatchlist();
                foreach (var entry in watchlist)
                {
                    _feed.Subscribe(entry.Ticker, MarketDataType.Realtime);
                }
            }
        };

        try
        {
            await _feed.StartAsync(stoppingToken);

            // 自動將預設標的加入 Watchlist (若尚未存在)
            foreach (var ticker in DefaultTickers)
            {
                // 注意：這裡假設一個合理的參考價，實務上可從資料庫或外部 API 取得
                _state.AddToWatchlist(ticker, 100m); 
            }

            // 初始訂閱 Watchlist 中的所有股票
            var initialWatchlist = _state.GetWatchlist();
            foreach (var entry in initialWatchlist)
            {
                _feed.Subscribe(entry.Ticker, MarketDataType.Realtime);
                _logger.LogInformation("Initial subscription: {Ticker}", entry.Ticker);
            }

            // 保持運行直到 Host 停止
            await Task.Delay(Timeout.Infinite, stoppingToken);
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // Host 正常停止
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "FugleMarketDataHostedService encountered an error but will remain alive for other services.");
            // Do not re-throw here to prevent crashing the entire Web Application Host in dev/test environments.
        }
    }

    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("FugleMarketDataHostedService stopping...");
        await _feed.StopAsync(cancellationToken);
        await base.StopAsync(cancellationToken);
    }
}
