using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using QuantTrading.Core.Interfaces;
using QuantTrading.Core.Models;
using QuantTrading.Core.Services;
using System.Threading.Channels;

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

    // ── Channels for Decoupling ─────────────────────────────────
    private readonly Channel<TickData> _tickChannel = Channel.CreateUnbounded<TickData>();
    private readonly Channel<BarData> _barChannel = Channel.CreateUnbounded<BarData>();

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

        // 啟動 Consumer Tasks
        _ = ConsumeTicksAsync(stoppingToken);
        _ = ConsumeBarsAsync(stoppingToken);

        // 註冊事件 log — 用 Producer 將行情推入 Channel
        _feed.OnTickReceived += tick =>
        {
            _state.AddTick(tick);
            _tickChannel.Writer.TryWrite(tick);
        };

        _feed.OnBarClosed += bar =>
        {
            _state.AddBar(bar);
            _barChannel.Writer.TryWrite(bar);
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

    private async Task ConsumeTicksAsync(CancellationToken ct)
    {
        try
        {
            await foreach (var tick in _tickChannel.Reader.ReadAllAsync(ct))
            {
                if (_engine != null) await _engine.ProcessTickAsync(tick);
            }
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error consuming ticks.");
        }
    }

    private async Task ConsumeBarsAsync(CancellationToken ct)
    {
        try
        {
            await foreach (var bar in _barChannel.Reader.ReadAllAsync(ct))
            {
                if (_engine != null) await _engine.ProcessBarAsync(bar);
            }
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error consuming bars.");
        }
    }

    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("FugleMarketDataHostedService stopping...");
        _tickChannel.Writer.TryComplete();
        _barChannel.Writer.TryComplete();
        await _feed.StopAsync(cancellationToken);
        await base.StopAsync(cancellationToken);
    }
}
