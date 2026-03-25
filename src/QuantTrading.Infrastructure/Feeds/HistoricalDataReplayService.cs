using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using QuantTrading.Core.Interfaces;
using QuantTrading.Core.Models;

namespace QuantTrading.Infrastructure.Feeds;

/// <summary>
/// 歷史行情回放引擎 (Replay Engine)
/// 實作 IMarketDataFeed，支援從本地 JSON 讀取歷史 Tick 資料，
/// 並依照原始時間戳記間隔 (可依乘數加速) 觸發事件。
/// 可用於週末離線策略驗證與 UI 壓力測試。
/// </summary>
public class HistoricalDataReplayService : IMarketDataFeed
{
    public event Action<TickData>? OnTickReceived;
    public event Action<BarData>? OnBarClosed;

    public bool IsConnected => _isRunning;

    private bool _isRunning;
    private readonly string _filePath;
    private readonly double _speedMultiplier;

    private readonly HashSet<string> _subscriptions = new(StringComparer.OrdinalIgnoreCase);

    public HistoricalDataReplayService(string filePath, double speedMultiplier = 1.0)
    {
        _filePath = filePath;
        _speedMultiplier = speedMultiplier;
    }

    public void Subscribe(string ticker, MarketDataType dataType)
    {
        lock (_subscriptions)
        {
            _subscriptions.Add(ticker);
        }
    }

    public void Unsubscribe(string ticker)
    {
        lock (_subscriptions)
        {
            _subscriptions.Remove(ticker);
        }
    }

    public async Task StartAsync(CancellationToken cancellationToken = default)
    {
        if (_isRunning) return;
        _isRunning = true;

        if (!File.Exists(_filePath))
        {
            _isRunning = false;
            throw new FileNotFoundException("Historical data file not found", _filePath);
        }

        var json = await File.ReadAllTextAsync(_filePath, cancellationToken);
        var ticks = JsonSerializer.Deserialize<List<TickData>>(json, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

        if (ticks == null || !ticks.Any())
        {
            _isRunning = false;
            return;
        }

        // 確保依照時間排序
        ticks = ticks.OrderBy(t => t.Timestamp).ToList();

        // 以背景任務執行回放
        _ = Task.Run(async () => await ReplayLocallyAsync(ticks, cancellationToken), cancellationToken);
    }

    private async Task ReplayLocallyAsync(List<TickData> ticks, CancellationToken ct)
    {
        try
        {
            for (int i = 0; i < ticks.Count; i++)
            {
                if (ct.IsCancellationRequested || !_isRunning) break;

                var tick = ticks[i];

                bool isSubscribed;
                lock (_subscriptions)
                {
                    isSubscribed = _subscriptions.Contains(tick.Ticker);
                }

                if (isSubscribed)
                {
                    OnTickReceived?.Invoke(tick);
                }

                // 若非最後一筆，計算延遲時間並等待
                if (i < ticks.Count - 1)
                {
                    var nextTick = ticks[i + 1];
                    var timeDiff = nextTick.Timestamp - tick.Timestamp;
                    
                    if (timeDiff.TotalMilliseconds > 0)
                    {
                        var delay = timeDiff.TotalMilliseconds / _speedMultiplier;
                        
                        // 若延遲太小(小於 1ms)，直接連續發送，避免 Task.Delay 開銷
                        if (delay >= 1)
                        {
                            await Task.Delay((int)delay, ct);
                        }
                    }
                }
            }
        }
        catch (TaskCanceledException) { }
        finally
        {
            _isRunning = false;
        }
    }

    public Task StopAsync(CancellationToken cancellationToken = default)
    {
        _isRunning = false;
        return Task.CompletedTask;
    }
}
