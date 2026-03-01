using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using QuantTrading.Core.Interfaces;
using QuantTrading.Core.Models;

namespace QuantTrading.Infrastructure.Fugle;

/// <summary>
/// BackgroundService wrapper — 管理 FugleMarketDataFeed 的生命週期。
/// ASP.NET Core Host 啟動時 → StartAsync，停止時 → StopAsync。
/// </summary>
public sealed class FugleMarketDataHostedService : BackgroundService
{
    private readonly IMarketDataFeed _feed;
    private readonly ILogger<FugleMarketDataHostedService> _logger;

    /// <summary>啟動後自動訂閱的預設標的 (可後續改為從設定讀取)。</summary>
    private static readonly string[] DefaultTickers = ["2330"];

    public FugleMarketDataHostedService(
        IMarketDataFeed feed,
        ILogger<FugleMarketDataHostedService> logger)
    {
        _feed = feed;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("FugleMarketDataHostedService starting...");

        // 註冊事件 log — 用於觀察即時行情
        _feed.OnTickReceived += tick =>
            _logger.LogInformation(
                "📈 TICK  {Ticker} | Price={Price} | Vol={Volume} | {Time:HH:mm:ss.fff}",
                tick.Ticker, tick.Price, tick.Volume, tick.Timestamp);

        _feed.OnBarClosed += bar =>
            _logger.LogInformation(
                "📊 BAR   {Ticker} | O={Open} H={High} L={Low} C={Close} | Vol={Volume} | {Time:HH:mm:ss}",
                bar.Ticker, bar.Open, bar.High, bar.Low, bar.Close, bar.Volume, bar.Timestamp);

        try
        {
            await _feed.StartAsync(stoppingToken);

            // 自動訂閱預設標的
            foreach (var ticker in DefaultTickers)
            {
                _feed.Subscribe(ticker, MarketDataType.Realtime);
                _logger.LogInformation("Auto-subscribed to {Ticker}", ticker);
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
            _logger.LogError(ex, "FugleMarketDataHostedService fatal error.");
            throw;
        }
    }

    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("FugleMarketDataHostedService stopping...");
        await _feed.StopAsync(cancellationToken);
        await base.StopAsync(cancellationToken);
    }
}
