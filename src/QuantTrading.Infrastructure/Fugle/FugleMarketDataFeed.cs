using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using System.Threading.Channels;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Polly;
using Polly.Retry;
using QuantTrading.Core.Interfaces;
using QuantTrading.Core.Models;
using QuantTrading.Infrastructure.Configuration;

namespace QuantTrading.Infrastructure.Fugle;

/// <summary>
/// 富果 (Fugle) MarketData WebSocket 即時行情接收服務。
///
/// ┌──────────────┐  JSON   ┌──────────────┐ Channel  ┌───────────────┐
/// │ Fugle WSS    │ ──────> │ ReceiveLoop  │ ───────> │ DispatchLoop  │
/// │  Server      │         │ (Parser)     │          │ (Events)      │
/// └──────────────┘         └──────────────┘          └───────────────┘
///                                                       │
///                                                       ├─ OnTickReceived
///                                                       └─ OnBarClosed
///
/// 架構重點:
///   1. ClientWebSocket + Polly Exponential Backoff 斷線重連
///   2. Channel&lt;TickData&gt; / Channel&lt;BarData&gt; 緩衝 (Producer-Consumer)
///   3. 心跳 Ping 維持連線活躍
///   4. Lock-protected Dictionary 管理多股票訂閱
/// </summary>
public sealed class FugleMarketDataFeed : IMarketDataFeed, IAsyncDisposable
{
    // ── Dependencies ────────────────────────────────────────────────
    private readonly FugleOptions _options;
    private readonly ILogger<FugleMarketDataFeed> _logger;

    // ── WebSocket ───────────────────────────────────────────────────
    private ClientWebSocket? _ws;
    private readonly SemaphoreSlim _wsSendLock = new(1, 1);

    // ── Channels (Producer-Consumer 緩衝區) ─────────────────────────
    private Channel<TickData> _tickChannel;
    private Channel<BarData> _barChannel;

    // ── Polly 重連策略 ──────────────────────────────────────────────
    private readonly ResiliencePipeline _reconnectPipeline;

    // ── 訂閱追蹤 (lock-protected) ──────────────────────────────────────
    private readonly object _subscriptionLock = new();
    private readonly Dictionary<string, HashSet<string>> _subscriptions = new();
    private readonly Dictionary<(string Ticker, string Channel), string> _channelIdMap = new();

    // ── Internal Tasks ──────────────────────────────────────────────
    private CancellationTokenSource? _cts;
    private Task? _receiveTask;
    private Task? _tickDispatchTask;
    private Task? _barDispatchTask;
    private Task? _pingTask;

    // ── IMarketDataFeed ─────────────────────────────────────────────
    public event Action<TickData>? OnTickReceived;
    public event Action<BarData>? OnBarClosed;
    public bool IsConnected => _ws?.State == WebSocketState.Open;

    // ── 統計 ─────────────────────────────────────────────────────────
    private long _totalTicksReceived;
    private long _totalBarsReceived;
    private long _reconnectCount;
    public long TotalTicksReceived => Interlocked.Read(ref _totalTicksReceived);
    public long TotalBarsReceived => Interlocked.Read(ref _totalBarsReceived);
    public long ReconnectCount => Interlocked.Read(ref _reconnectCount);

    // ── JSON ─────────────────────────────────────────────────────────
    private static readonly JsonSerializerOptions _jsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
    };

    // ═════════════════════════════════════════════════════════════════
    //  Constructor
    // ═════════════════════════════════════════════════════════════════

    public FugleMarketDataFeed(
        IOptions<FugleOptions> options,
        ILogger<FugleMarketDataFeed> logger)
    {
        _options = options?.Value ?? throw new ArgumentNullException(nameof(options));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));

        if (string.IsNullOrWhiteSpace(_options.ApiToken))
            throw new InvalidOperationException("Fugle API Token is required. Set Fugle:ApiToken in configuration.");

        // ── 建立 Bounded Channels ───────────────────────────────
        _tickChannel = Channel.CreateBounded<TickData>(new BoundedChannelOptions(_options.TickChannelCapacity)
        {
            FullMode = BoundedChannelFullMode.DropOldest,
            SingleWriter = true,    // 只有 ReceiveLoop 寫入
            SingleReader = true     // 只有 DispatchLoop 讀取
        });

        _barChannel = Channel.CreateBounded<BarData>(new BoundedChannelOptions(_options.BarChannelCapacity)
        {
            FullMode = BoundedChannelFullMode.DropOldest,
            SingleWriter = true,
            SingleReader = true
        });

        // ── 建立 Polly Exponential Backoff Pipeline ─────────────
        var retryOptions = new RetryStrategyOptions
        {
            MaxRetryAttempts = _options.MaxReconnectAttempts == 0
                ? int.MaxValue
                : _options.MaxReconnectAttempts,
            BackoffType = DelayBackoffType.Exponential,
            Delay = TimeSpan.FromMilliseconds(_options.ReconnectBaseDelayMs),
            MaxDelay = TimeSpan.FromMilliseconds(_options.ReconnectMaxDelayMs),
            UseJitter = true, // 加入隨機抖動避免 thundering herd
            OnRetry = args =>
            {
                Interlocked.Increment(ref _reconnectCount);
                _logger.LogWarning(
                    "WebSocket reconnect attempt #{Attempt} after {Delay}ms (total reconnects: {Count})",
                    args.AttemptNumber + 1,
                    args.RetryDelay.TotalMilliseconds,
                    ReconnectCount);
                return ValueTask.CompletedTask;
            }
        };

        _reconnectPipeline = new ResiliencePipelineBuilder()
            .AddRetry(retryOptions)
            .Build();
    }

    // ═════════════════════════════════════════════════════════════════
    //  IMarketDataFeed — StartAsync / StopAsync
    // ═════════════════════════════════════════════════════════════════

    public async Task StartAsync(CancellationToken cancellationToken = default)
    {
        if (_cts != null)
        {
            _logger.LogWarning("FugleMarketDataFeed is already running.");
            return;
        }

        // 重建 Channels（StopAsync 會 Complete 舊的 channel，無法重用）
        _tickChannel = Channel.CreateBounded<TickData>(new BoundedChannelOptions(_options.TickChannelCapacity)
        {
            FullMode = BoundedChannelFullMode.DropOldest,
            SingleWriter = true,
            SingleReader = true
        });
        _barChannel = Channel.CreateBounded<BarData>(new BoundedChannelOptions(_options.BarChannelCapacity)
        {
            FullMode = BoundedChannelFullMode.DropOldest,
            SingleWriter = true,
            SingleReader = true
        });

        _cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);

        _logger.LogInformation("Starting FugleMarketDataFeed → {Url}", _options.WebSocketUrl);

        // 首次連線 (透過 Polly 重試)
        await ConnectWithRetryAsync(_cts.Token);

        // 啟動背景工作
        _receiveTask = Task.Run(() => ReceiveLoopAsync(_cts.Token), _cts.Token);
        _tickDispatchTask = Task.Run(() => DispatchTickLoopAsync(_cts.Token), _cts.Token);
        _barDispatchTask = Task.Run(() => DispatchBarLoopAsync(_cts.Token), _cts.Token);
        _pingTask = Task.Run(() => PingLoopAsync(_cts.Token), _cts.Token);

        _logger.LogInformation("FugleMarketDataFeed started. Channels: tick={TickCap}, bar={BarCap}",
            _options.TickChannelCapacity, _options.BarChannelCapacity);
    }

    public async Task StopAsync(CancellationToken cancellationToken = default)
    {
        if (_cts == null) return;

        _logger.LogInformation("Stopping FugleMarketDataFeed...");

        _cts.Cancel();

        // 關閉 channel writers 觸發 readers 結束
        _tickChannel.Writer.TryComplete();
        _barChannel.Writer.TryComplete();

        // 等待背景工作結束
        var tasks = new[] { _receiveTask, _tickDispatchTask, _barDispatchTask, _pingTask }
            .Where(t => t != null)
            .Cast<Task>()
            .ToArray();

        try
        {
            await Task.WhenAll(tasks).WaitAsync(TimeSpan.FromSeconds(5), cancellationToken);
        }
        catch (Exception ex) when (ex is TimeoutException or OperationCanceledException)
        {
            _logger.LogWarning("Graceful shutdown timed out, forcing close.");
        }

        // 關閉 WebSocket
        await CloseWebSocketAsync();

        _cts.Dispose();
        _cts = null;

        _logger.LogInformation(
            "FugleMarketDataFeed stopped. Stats: ticks={Ticks}, bars={Bars}, reconnects={Reconnects}",
            TotalTicksReceived, TotalBarsReceived, ReconnectCount);
    }

    // ═════════════════════════════════════════════════════════════════
    //  IMarketDataFeed — Subscribe / Unsubscribe
    // ═════════════════════════════════════════════════════════════════

    public void Subscribe(string ticker, MarketDataType dataType)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(ticker);

        var tickerUpper = ticker.ToUpper();
        var channelsForType = GetChannelsForDataType(dataType);
        var newChannels = new List<string>();

        lock (_subscriptionLock)
        {
            if (!_subscriptions.TryGetValue(tickerUpper, out var entry))
            {
                entry = new HashSet<string>();
                _subscriptions[tickerUpper] = entry;
            }

            foreach (var channel in channelsForType)
            {
                if (entry.Add(channel))
                    newChannels.Add(channel);
            }
        }

        foreach (var channel in newChannels)
        {
            _ = SendSubscribeAsync(tickerUpper, channel);
            _logger.LogInformation("Subscribing: {Ticker}/{Channel} (mode={DataType})", ticker, channel, dataType);
        }
    }

    public void Unsubscribe(string ticker)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(ticker);

        var tickerUpper = ticker.ToUpper();
        var idsToUnsub = new List<string>();
        lock (_subscriptionLock)
        {
            if (_subscriptions.Remove(tickerUpper, out var channels))
            {
                foreach (var channel in channels)
                {
                    var key = (tickerUpper, channel);
                    if (_channelIdMap.Remove(key, out var id))
                        idsToUnsub.Add(id);
                }
            }
        }

        foreach (var id in idsToUnsub)
        {
            _ = SendUnsubscribeByIdAsync(id);
        }

        if (idsToUnsub.Count > 0)
            _logger.LogInformation("Unsubscribed: {Ticker} (ids={Ids})", ticker, string.Join(",", idsToUnsub));
    }

    // ═════════════════════════════════════════════════════════════════
    //  WebSocket Connection Management
    // ═════════════════════════════════════════════════════════════════

    private async Task ConnectWithRetryAsync(CancellationToken ct)
    {
        await _reconnectPipeline.ExecuteAsync(async token =>
        {
            await ConnectOnceAsync(token);
        }, ct);
    }

    private async Task ConnectOnceAsync(CancellationToken ct)
    {
        // 先關閉舊的
        await CloseWebSocketAsync();

        _ws = new ClientWebSocket();
        _ws.Options.KeepAliveInterval = TimeSpan.FromSeconds(_options.PingIntervalSeconds);

        // 連線不帶 apiToken，改用 event-based 驗證
        var uri = new Uri(_options.WebSocketUrl);

        _logger.LogDebug("Connecting to {Uri}...", _options.WebSocketUrl);
        await _ws.ConnectAsync(uri, ct);
        _logger.LogInformation("WebSocket connected (state={State})", _ws.State);

        // 發送 auth 事件並等待 authenticated 回應
        await AuthenticateAsync(ct);
    }

    /// <summary>發送 auth 事件，同步讀取驗證回應 (authenticated / error)。</summary>
    private async Task AuthenticateAsync(CancellationToken ct)
    {
        var authRequest = new FugleRequest
        {
            Event = FugleEvents.Auth,
            Data = new FugleAuthData { ApiKey = _options.ApiToken }
        };
        await SendJsonAsync(authRequest, ct);
        _logger.LogDebug("Auth request sent, waiting for response...");

        // 讀取驗證回應 (最多等 10 秒)
        var buffer = new byte[4096];
        using var authCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        authCts.CancelAfter(TimeSpan.FromSeconds(10));

        using var ms = new MemoryStream();
        WebSocketReceiveResult result;
        do
        {
            result = await _ws!.ReceiveAsync(new ArraySegment<byte>(buffer), authCts.Token);
            ms.Write(buffer, 0, result.Count);
        } while (!result.EndOfMessage);

        var json = Encoding.UTF8.GetString(ms.GetBuffer(), 0, (int)ms.Length);
        _logger.LogDebug("Auth response: {Json}", json);

        var response = JsonSerializer.Deserialize<FugleResponse>(json, _jsonOptions);
        if (response?.Event == FugleEvents.Error)
        {
            throw new InvalidOperationException($"Fugle auth failed: {json}");
        }
        if (response?.Event == FugleEvents.Authenticated)
        {
            _logger.LogInformation("Fugle authenticated successfully.");
        }
        else
        {
            _logger.LogWarning("Expected 'authenticated' but got '{Event}': {Json}", response?.Event, json);
        }
    }

    private async Task CloseWebSocketAsync()
    {
        if (_ws == null) return;

        try
        {
            if (_ws.State == WebSocketState.Open || _ws.State == WebSocketState.CloseReceived)
            {
                using var closeCts = new CancellationTokenSource(TimeSpan.FromSeconds(3));
                await _ws.CloseAsync(WebSocketCloseStatus.NormalClosure, "Bye", closeCts.Token);
            }
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "WebSocket close error (ignored).");
        }
        finally
        {
            _ws.Dispose();
            _ws = null;
        }
    }

    /// <summary>重新訂閱所有先前的股票（斷線重連後呼叫）。</summary>
    private async Task ResubscribeAllAsync()
    {
        List<(string Ticker, string Channel)> toResub;
        lock (_subscriptionLock)
        {
            _channelIdMap.Clear(); // 舊的 channel ID 已失效
            toResub = _subscriptions
                .SelectMany(kvp => kvp.Value.Select(ch => (kvp.Key, ch)))
                .ToList();
        }

        foreach (var (ticker, channel) in toResub)
        {
            await SendSubscribeAsync(ticker, channel);
        }
        _logger.LogInformation("Resubscribed {Count} channel(s) after reconnect.", toResub.Count);
    }

    // ═════════════════════════════════════════════════════════════════
    //  Receive Loop — 從 WebSocket 讀取 JSON → 寫入 Channel
    // ═════════════════════════════════════════════════════════════════

    private async Task ReceiveLoopAsync(CancellationToken ct)
    {
        var buffer = new byte[8192];

        while (!ct.IsCancellationRequested)
        {
            try
            {
                if (_ws == null || _ws.State != WebSocketState.Open)
                {
                    _logger.LogWarning("WebSocket not open, attempting reconnect...");
                    await ConnectWithRetryAsync(ct);
                    await ResubscribeAllAsync();
                    continue;
                }

                // 讀取完整訊息 (可能跨多個 frame)
                using var ms = new MemoryStream();
                WebSocketReceiveResult result;
                bool closeReceived = false;
                do
                {
                    result = await _ws.ReceiveAsync(new ArraySegment<byte>(buffer), ct);
                    if (result.MessageType == WebSocketMessageType.Close)
                    {
                        _logger.LogWarning("Server sent Close frame. Reconnecting...");
                        await ConnectWithRetryAsync(ct);
                        await ResubscribeAllAsync();
                        closeReceived = true;
                        break;
                    }
                    ms.Write(buffer, 0, result.Count);
                } while (!result.EndOfMessage);

                if (closeReceived || ms.Length == 0) continue;

                // 解析 JSON
                ms.Position = 0;
                ProcessMessage(ms.GetBuffer().AsSpan(0, (int)ms.Length));
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                break;
            }
            catch (WebSocketException ex)
            {
                _logger.LogError(ex, "WebSocket error in ReceiveLoop. Reconnecting...");

                try
                {
                    await ConnectWithRetryAsync(ct);
                    await ResubscribeAllAsync();
                }
                catch (OperationCanceledException) when (ct.IsCancellationRequested)
                {
                    break;
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Unexpected error in ReceiveLoop.");
            }
        }
    }

    // ═════════════════════════════════════════════════════════════════
    //  Message Parser — JSON → TickData / BarData → Channel
    // ═════════════════════════════════════════════════════════════════

    /// <summary>解析 Fugle JSON 並寫入對應 Channel。內部方法，可單元測試。</summary>
    internal void ProcessMessage(ReadOnlySpan<byte> utf8Json)
    {
        // ── 原始訊息 log (Debug 等級，用於除錯) ─────────────────
        var rawText = Encoding.UTF8.GetString(utf8Json);
        _logger.LogDebug("WS ← {RawMessage}", rawText);

        FugleResponse? msg;
        try
        {
            msg = JsonSerializer.Deserialize<FugleResponse>(utf8Json, _jsonOptions);
        }
        catch (JsonException ex)
        {
            _logger.LogWarning(ex, "Failed to deserialize Fugle message. Raw: {Raw}", rawText);
            return;
        }

        if (msg == null) return;

        switch (msg.Event)
        {
            case FugleEvents.Data:
                ProcessDataEvent(msg.Data, msg.Channel);
                break;

            case FugleEvents.Authenticated:
                _logger.LogInformation("Authenticated event received.");
                break;

            case FugleEvents.Subscribed:
                HandleSubscribedEvent(msg.Data);
                break;

            case FugleEvents.Unsubscribed:
                _logger.LogInformation("Unsubscribed: {Data}", msg.Data);
                break;

            case FugleEvents.Pong:
                _logger.LogDebug("Pong received.");
                break;

            case FugleEvents.Heartbeat:
                _logger.LogDebug("Heartbeat received.");
                break;

            case FugleEvents.Error:
                _logger.LogError("Fugle error (raw): {Raw}", rawText);
                break;

            default:
                _logger.LogInformation("Unknown/Other event: {Event}, raw: {Raw}", msg.Event, rawText);
                break;
        }
    }

    /// <summary>處理 subscribed 事件，記錄 channel ID 以供 unsubscribe 使用。</summary>
    private void HandleSubscribedEvent(JsonElement data)
    {
        try
        {
            if (data.ValueKind == JsonValueKind.Array)
            {
                var items = data.Deserialize<List<FugleSubscribedInfo>>(_jsonOptions);
                if (items != null)
                    foreach (var item in items) TrackChannelId(item);
            }
            else if (data.ValueKind == JsonValueKind.Object)
            {
                var item = data.Deserialize<FugleSubscribedInfo>(_jsonOptions);
                if (item != null) TrackChannelId(item);
            }
        }
        catch (JsonException ex)
        {
            _logger.LogWarning(ex, "Failed to parse subscribed event data.");
        }
    }

    private void TrackChannelId(FugleSubscribedInfo info)
    {
        lock (_subscriptionLock)
        {
            _channelIdMap[(info.Symbol.ToUpper(), info.Channel)] = info.Id;
        }
        _logger.LogInformation("Subscription confirmed: {Symbol}/{Channel} → id={Id}",
            info.Symbol, info.Channel, info.Id);
    }

    /// <summary>依據 channel 類型分派 data event。</summary>
    private void ProcessDataEvent(JsonElement data, string? channel)
    {
        if (data.ValueKind != JsonValueKind.Object) return;

        switch (channel)
        {
            case FugleChannels.Trades:
                ProcessTradesData(data);
                break;

            case FugleChannels.Candles:
                ProcessCandlesData(data);
                break;

            case FugleChannels.Books:
                ProcessBooksData(data);
                break;

            case FugleChannels.Aggregates:
                ProcessAggregatesData(data);
                break;

            case FugleChannels.Indices:
                _logger.LogDebug("Index data received: {Data}", data);
                break;

            default:
                _logger.LogWarning("Unknown channel '{Channel}' in data event.", channel);
                break;
        }
    }

    private void ProcessTradesData(JsonElement data)
    {
        FugleTradeData? trade;
        try { trade = data.Deserialize<FugleTradeData>(_jsonOptions); }
        catch (JsonException ex) { _logger.LogWarning(ex, "Failed to parse trades data."); return; }

        if (trade == null || trade.Price <= 0) return;

        var tick = new TickData(
            Ticker: trade.Symbol,
            Price: trade.Price,
            Volume: trade.Size,
            Timestamp: UnixUsToDateTime(trade.Time)
        );

        _tickChannel.Writer.TryWrite(tick);
        Interlocked.Increment(ref _totalTicksReceived);
    }

    private void ProcessCandlesData(JsonElement data)
    {
        FugleCandleData? candle;
        try { candle = data.Deserialize<FugleCandleData>(_jsonOptions); }
        catch (JsonException ex) { _logger.LogWarning(ex, "Failed to parse candles data."); return; }

        if (candle == null) return;

        // candles 頻道的時間為 ISO 8601 字串，例如 "2023-05-29T13:30:00.000+08:00"
        var timestamp = DateTimeOffset.TryParse(candle.Date, out var dto)
            ? dto.LocalDateTime
            : DateTime.UtcNow;

        var bar = new BarData(
            Ticker: candle.Symbol,
            Open: candle.Open,
            High: candle.High,
            Low: candle.Low,
            Close: candle.Close,
            Volume: candle.Volume,
            Timestamp: timestamp
        );

        _barChannel.Writer.TryWrite(bar);
        Interlocked.Increment(ref _totalBarsReceived);
    }

    private void ProcessBooksData(JsonElement data)
    {
        FugleBooksData? books;
        try { books = data.Deserialize<FugleBooksData>(_jsonOptions); }
        catch (JsonException ex) { _logger.LogWarning(ex, "Failed to parse books data."); return; }

        if (books == null) return;

        // 最佳五檔 → 取中間價作為 TickData
        if (books.Bids is { Count: > 0 } && books.Asks is { Count: > 0 })
        {
            var midPrice = (books.Bids[0].Price + books.Asks[0].Price) / 2m;

            var tick = new TickData(
                Ticker: books.Symbol,
                Price: midPrice,
                Volume: 0,
                Timestamp: UnixUsToDateTime(books.Time)
            );

            _tickChannel.Writer.TryWrite(tick);
            Interlocked.Increment(ref _totalTicksReceived);
        }
    }

    private void ProcessAggregatesData(JsonElement data)
    {
        FugleAggregateData? agg;
        try { agg = data.Deserialize<FugleAggregateData>(_jsonOptions); }
        catch (JsonException ex) { _logger.LogWarning(ex, "Failed to parse aggregates data."); return; }

        if (agg == null || agg.LastPrice <= 0) return;

        var tick = new TickData(
            Ticker: agg.Symbol,
            Price: agg.LastPrice,
            Volume: agg.LastSize,
            Timestamp: UnixUsToDateTime(agg.LastUpdated)
        );

        _tickChannel.Writer.TryWrite(tick);
        Interlocked.Increment(ref _totalTicksReceived);
    }

    // ═════════════════════════════════════════════════════════════════
    //  Dispatch Loops — 從 Channel 讀取 → 觸發事件
    // ═════════════════════════════════════════════════════════════════

    private async Task DispatchTickLoopAsync(CancellationToken ct)
    {
        _logger.LogDebug("TickDispatch loop started.");

        await foreach (var tick in _tickChannel.Reader.ReadAllAsync(ct))
        {
            try
            {
                OnTickReceived?.Invoke(tick);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error dispatching tick for {Ticker}", tick.Ticker);
            }
        }

        _logger.LogDebug("TickDispatch loop ended.");
    }

    private async Task DispatchBarLoopAsync(CancellationToken ct)
    {
        _logger.LogDebug("BarDispatch loop started.");

        await foreach (var bar in _barChannel.Reader.ReadAllAsync(ct))
        {
            try
            {
                OnBarClosed?.Invoke(bar);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error dispatching bar for {Ticker}", bar.Ticker);
            }
        }

        _logger.LogDebug("BarDispatch loop ended.");
    }

    // ═════════════════════════════════════════════════════════════════
    //  Ping Loop — 定期送出心跳維持連線
    // ═════════════════════════════════════════════════════════════════

    private async Task PingLoopAsync(CancellationToken ct)
    {
        var interval = TimeSpan.FromSeconds(_options.PingIntervalSeconds);
        _logger.LogDebug("Ping loop started (interval={Interval}s).", _options.PingIntervalSeconds);

        while (!ct.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(interval, ct);

                if (IsConnected)
                {
                    var ping = new FugleRequest { Event = FugleEvents.Ping };
                    await SendJsonAsync(ping, ct);
                    _logger.LogDebug("Ping sent.");
                }
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Ping error (will retry next cycle).");
            }
        }
    }

    // ═════════════════════════════════════════════════════════════════
    //  WebSocket Send Helpers
    // ═════════════════════════════════════════════════════════════════

    private async Task SendSubscribeAsync(string symbol, string channel)
    {
        var msg = new FugleRequest
        {
            Event = FugleEvents.Subscribe,
            Data = new FugleSubscribeData { Channel = channel, Symbol = symbol }
        };
        await SendJsonAsync(msg, _cts?.Token ?? CancellationToken.None);
    }

    private async Task SendUnsubscribeByIdAsync(string channelId)
    {
        var msg = new FugleRequest
        {
            Event = FugleEvents.Unsubscribe,
            Data = new FugleUnsubscribeData { Id = channelId }
        };
        await SendJsonAsync(msg, _cts?.Token ?? CancellationToken.None);
    }

    private async Task SendJsonAsync<T>(T message, CancellationToken ct)
    {
        if (_ws == null || _ws.State != WebSocketState.Open) return;

        var json = JsonSerializer.SerializeToUtf8Bytes(message, _jsonOptions);

        await _wsSendLock.WaitAsync(ct);
        try
        {
            await _ws.SendAsync(
                new ArraySegment<byte>(json),
                WebSocketMessageType.Text,
                endOfMessage: true,
                ct);
        }
        finally
        {
            _wsSendLock.Release();
        }
    }

    // ═════════════════════════════════════════════════════════════════
    //  Helpers
    // ═════════════════════════════════════════════════════════════════

    private static string[] GetChannelsForDataType(MarketDataType dataType) => dataType switch
    {
        // Simulate (試搓) → aggregates (含最佳五檔/試搓價) + trades
        MarketDataType.Simulate => new[] { FugleChannels.Aggregates, FugleChannels.Trades },
        // Realtime → trades + candles
        MarketDataType.Realtime => new[] { FugleChannels.Trades, FugleChannels.Candles },
        _ => new[] { FugleChannels.Trades }
    };

    /// <summary>
    /// 將 Fugle 微秒時間戳 (μs, 16 位數) 轉換為本地 DateTime。
    /// </summary>
    internal static DateTime UnixUsToDateTime(long unixUs)
    {
        return DateTimeOffset.FromUnixTimeMilliseconds(unixUs / 1000).LocalDateTime;
    }

    // ═════════════════════════════════════════════════════════════════
    //  IAsyncDisposable
    // ═════════════════════════════════════════════════════════════════

    public async ValueTask DisposeAsync()
    {
        try
        {
            await StopAsync();
        }
        finally
        {
            _wsSendLock.Dispose();
        }
    }
}
