using System.Text;
using System.Text.Json;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using QuantTrading.Core.Models;
using QuantTrading.Infrastructure.Configuration;
using QuantTrading.Infrastructure.Fugle;

namespace QuantTrading.Core.Tests;

/// <summary>
/// FugleMarketDataFeed 單元測試 — 聚焦於:
///   1. Fugle JSON 訊息解析 (Trades / Candles / Books / Aggregates → TickData / BarData)
///   2. Channel dispatch (Producer-Consumer 正確觸發事件)
///   3. Unix 微秒時間戳轉換 (μs, 16 位數)
///   4. FugleMessages JSON 序列化 / 反序列化
///   5. FugleOptions 預設值驗證
///
/// 資料格式對齊 https://developer.fugle.tw/llms-full.txt (2026-03)
/// </summary>
public class FugleMarketDataFeedTests
{
    // ═════════════════════════════════════════════════════════════════
    //  Helper
    // ═════════════════════════════════════════════════════════════════

    private static FugleMarketDataFeed CreateTestFeed()
    {
        var options = Options.Create(new FugleOptions
        {
            ApiToken = "test-token-for-unit-tests",
            WebSocketUrl = "wss://test.example.com/ws",
            TickChannelCapacity = 100,
            BarChannelCapacity = 50
        });
        var logger = NullLoggerFactory.Instance.CreateLogger<FugleMarketDataFeed>();
        return new FugleMarketDataFeed(options, logger);
    }

    // ═════════════════════════════════════════════════════════════════
    //  1. Unix 微秒時間戳轉換 (μs → DateTime)
    // ═════════════════════════════════════════════════════════════════

    [Fact]
    public void UnixUsToDateTime_ShouldConvertCorrectly()
    {
        // 1707955200000000 μs = 1707955200000 ms = 2024-02-15 01:00:00 UTC
        long unixUs = 1707955200000000;
        var result = FugleMarketDataFeed.UnixUsToDateTime(unixUs);

        var expected = DateTimeOffset.FromUnixTimeMilliseconds(unixUs / 1000).LocalDateTime;
        result.Should().Be(expected);
    }

    [Fact]
    public void UnixUsToDateTime_Zero_ShouldReturnEpoch()
    {
        var result = FugleMarketDataFeed.UnixUsToDateTime(0);
        var expected = DateTimeOffset.FromUnixTimeMilliseconds(0).LocalDateTime;
        result.Should().Be(expected);
    }

    // ═════════════════════════════════════════════════════════════════
    //  2. ProcessMessage — Trades (flat) → TickData
    // ═════════════════════════════════════════════════════════════════

    [Fact]
    public void ProcessMessage_TradeData_ShouldWriteToTickChannel()
    {
        var feed = CreateTestFeed();

        // 官方 API 每筆 trade 為獨立 flat message，非陣列
        var json1 = """
        {
            "event": "data",
            "data": {
                "symbol": "2330", "type": "trades", "exchange": "TWSE", "market": "TSE",
                "price": 600.0, "size": 234, "time": 1707955200000000, "serial": 1,
                "bid": 599.0, "ask": 601.0, "volume": 5000
            },
            "channel": "trades"
        }
        """;
        var json2 = """
        {
            "event": "data",
            "data": {
                "symbol": "2330", "type": "trades", "exchange": "TWSE", "market": "TSE",
                "price": 601.5, "size": 100, "time": 1707955201000000, "serial": 2,
                "bid": 601.0, "ask": 602.0, "volume": 5100
            },
            "channel": "trades"
        }
        """;

        feed.ProcessMessage(Encoding.UTF8.GetBytes(json1));
        feed.ProcessMessage(Encoding.UTF8.GetBytes(json2));

        feed.TotalTicksReceived.Should().Be(2);
    }

    [Fact]
    public void ProcessMessage_SingleTrade_ShouldIncrementCounter()
    {
        var feed = CreateTestFeed();
        var json = """
        {
            "event": "data",
            "data": {
                "symbol": "2330", "type": "trades",
                "price": 598.0, "size": 500, "time": 1707955200000000, "serial": 1,
                "bid": 597.0, "ask": 599.0, "volume": 3000
            },
            "channel": "trades"
        }
        """;

        feed.ProcessMessage(Encoding.UTF8.GetBytes(json));

        feed.TotalTicksReceived.Should().Be(1);
    }

    // ═════════════════════════════════════════════════════════════════
    //  3. ProcessMessage — Candles (flat, ISO 8601 date) → BarData
    // ═════════════════════════════════════════════════════════════════

    [Fact]
    public void ProcessMessage_CandleData_ShouldWriteToBarChannel()
    {
        var feed = CreateTestFeed();
        var json = """
        {
            "event": "data",
            "data": {
                "symbol": "2330", "type": "candles", "exchange": "TWSE", "market": "TSE",
                "date": "2024-02-15T09:00:00.000+08:00",
                "open": 600.0, "high": 605.0, "low": 598.0,
                "close": 602.0, "volume": 12345, "average": 601.0
            },
            "channel": "candles"
        }
        """;

        feed.ProcessMessage(Encoding.UTF8.GetBytes(json));

        feed.TotalBarsReceived.Should().Be(1);
    }

    [Fact]
    public void ProcessMessage_MultipleCandles_ShouldCountAll()
    {
        var feed = CreateTestFeed();

        // 官方 API: 每筆 candle 為獨立 flat message → 3 次 ProcessMessage
        var json1 = """
        {
            "event": "data",
            "data": {
                "symbol": "2454", "type": "candles",
                "date": "2024-02-15T09:00:00.000+08:00",
                "open": 1000, "high": 1010, "low": 995, "close": 1005, "volume": 100, "average": 1002
            },
            "channel": "candles"
        }
        """;
        var json2 = """
        {
            "event": "data",
            "data": {
                "symbol": "2454", "type": "candles",
                "date": "2024-02-15T09:01:00.000+08:00",
                "open": 1005, "high": 1015, "low": 1000, "close": 1012, "volume": 200, "average": 1006
            },
            "channel": "candles"
        }
        """;
        var json3 = """
        {
            "event": "data",
            "data": {
                "symbol": "2454", "type": "candles",
                "date": "2024-02-15T09:02:00.000+08:00",
                "open": 1012, "high": 1020, "low": 1008, "close": 1018, "volume": 150, "average": 1012
            },
            "channel": "candles"
        }
        """;

        feed.ProcessMessage(Encoding.UTF8.GetBytes(json1));
        feed.ProcessMessage(Encoding.UTF8.GetBytes(json2));
        feed.ProcessMessage(Encoding.UTF8.GetBytes(json3));

        feed.TotalBarsReceived.Should().Be(3);
    }

    // ═════════════════════════════════════════════════════════════════
    //  4. ProcessMessage — Books (五檔) → TickData (mid-price)
    // ═════════════════════════════════════════════════════════════════

    [Fact]
    public void ProcessMessage_BooksData_ShouldWriteToTickChannel()
    {
        var feed = CreateTestFeed();
        var json = """
        {
            "event": "data",
            "data": {
                "symbol": "2330", "type": "books", "exchange": "TWSE", "market": "TSE",
                "bids": [
                    { "price": 604.0, "size": 500 },
                    { "price": 603.0, "size": 300 }
                ],
                "asks": [
                    { "price": 606.0, "size": 400 },
                    { "price": 607.0, "size": 200 }
                ],
                "time": 1707955200000000
            },
            "channel": "books"
        }
        """;

        feed.ProcessMessage(Encoding.UTF8.GetBytes(json));

        // books → 取中間價作 TickData
        feed.TotalTicksReceived.Should().Be(1);
    }

    [Fact]
    public void ProcessMessage_BooksData_EmptyBids_ShouldNotWrite()
    {
        var feed = CreateTestFeed();
        var json = """
        {
            "event": "data",
            "data": {
                "symbol": "2330", "type": "books",
                "bids": [],
                "asks": [{ "price": 606.0, "size": 400 }],
                "time": 1707955200000000
            },
            "channel": "books"
        }
        """;

        feed.ProcessMessage(Encoding.UTF8.GetBytes(json));
        feed.TotalTicksReceived.Should().Be(0);
    }

    // ═════════════════════════════════════════════════════════════════
    //  4b. ProcessMessage — Aggregates → TickData
    // ═════════════════════════════════════════════════════════════════

    [Fact]
    public void ProcessMessage_AggregatesData_ShouldWriteToTickChannel()
    {
        var feed = CreateTestFeed();
        var json = """
        {
            "event": "data",
            "data": {
                "symbol": "2330", "type": "aggregates", "exchange": "TWSE", "market": "TSE",
                "date": "2024-02-15",
                "name": "台積電",
                "referencePrice": 600.0,
                "previousClose": 598.0,
                "openPrice": 601.0, "openTime": 1707955200000000,
                "highPrice": 610.0, "highTime": 1707955260000000,
                "lowPrice": 599.0, "lowTime": 1707955320000000,
                "closePrice": 605.0, "closeTime": 1707955380000000,
                "avgPrice": 604.5,
                "change": 7.0, "changePercent": 1.17, "amplitude": 1.84,
                "lastPrice": 605.0, "lastSize": 1000,
                "bids": [{ "price": 604.0, "size": 500 }],
                "asks": [{ "price": 606.0, "size": 400 }],
                "total": { "tradeValue": 5000000, "tradeVolume": 8500, "tradeVolumeAtBid": 4000, "tradeVolumeAtAsk": 4500, "transaction": 200, "time": 1707955380000000 },
                "lastTrade": { "bid": 604.0, "ask": 606.0, "price": 605.0, "size": 100, "time": 1707955380000000, "serial": 999 },
                "serial": 999,
                "lastUpdated": 1707955380000000
            },
            "channel": "aggregates"
        }
        """;

        feed.ProcessMessage(Encoding.UTF8.GetBytes(json));
        feed.TotalTicksReceived.Should().Be(1);
    }

    [Fact]
    public void ProcessMessage_AggregatesData_ZeroPrice_ShouldNotWrite()
    {
        var feed = CreateTestFeed();
        var json = """
        {
            "event": "data",
            "data": {
                "symbol": "2330", "type": "aggregates",
                "lastPrice": 0, "lastSize": 0, "lastUpdated": 1707955200000000
            },
            "channel": "aggregates"
        }
        """;

        feed.ProcessMessage(Encoding.UTF8.GetBytes(json));
        feed.TotalTicksReceived.Should().Be(0);
    }

    // ═════════════════════════════════════════════════════════════════
    //  5. ProcessMessage — Non-data events (應不拋錯)
    // ═════════════════════════════════════════════════════════════════

    [Fact]
    public void ProcessMessage_PongEvent_ShouldNotThrow()
    {
        var feed = CreateTestFeed();
        var json = """{"event":"pong"}""";
        var act = () => feed.ProcessMessage(Encoding.UTF8.GetBytes(json));
        act.Should().NotThrow();
    }

    [Fact]
    public void ProcessMessage_HeartbeatEvent_ShouldNotThrow()
    {
        var feed = CreateTestFeed();
        var json = """{"event":"heartbeat","data":{"time":1707955200000000}}""";
        var act = () => feed.ProcessMessage(Encoding.UTF8.GetBytes(json));
        act.Should().NotThrow();
    }

    [Fact]
    public void ProcessMessage_AuthenticatedEvent_ShouldNotThrow()
    {
        var feed = CreateTestFeed();
        var json = """{"event":"authenticated","data":{"message":"Authenticated successfully."}}""";
        var act = () => feed.ProcessMessage(Encoding.UTF8.GetBytes(json));
        act.Should().NotThrow();
    }

    [Fact]
    public void ProcessMessage_SubscribedEvent_ShouldNotThrow()
    {
        var feed = CreateTestFeed();
        var json = """
        {
            "event": "subscribed",
            "data": {
                "id": "ch-abc123",
                "channel": "trades",
                "symbol": "2330"
            }
        }
        """;
        var act = () => feed.ProcessMessage(Encoding.UTF8.GetBytes(json));
        act.Should().NotThrow();
    }

    [Fact]
    public void ProcessMessage_UnsubscribedEvent_ShouldNotThrow()
    {
        var feed = CreateTestFeed();
        var json = """{"event":"unsubscribed","data":{"id":"ch-abc123"}}""";
        var act = () => feed.ProcessMessage(Encoding.UTF8.GetBytes(json));
        act.Should().NotThrow();
    }

    [Fact]
    public void ProcessMessage_ErrorEvent_ShouldNotThrow()
    {
        var feed = CreateTestFeed();
        var json = """{"event":"error","data":{"message":"Invalid symbol"}}""";
        var act = () => feed.ProcessMessage(Encoding.UTF8.GetBytes(json));
        act.Should().NotThrow();
    }

    [Fact]
    public void ProcessMessage_InvalidJson_ShouldNotThrow()
    {
        var feed = CreateTestFeed();
        var act = () => feed.ProcessMessage(Encoding.UTF8.GetBytes("not valid json{{{"));
        act.Should().NotThrow();
    }

    [Fact]
    public void ProcessMessage_NullJson_ShouldNotThrow()
    {
        var feed = CreateTestFeed();
        var act = () => feed.ProcessMessage(Encoding.UTF8.GetBytes("null"));
        act.Should().NotThrow();
    }

    // ═════════════════════════════════════════════════════════════════
    //  6. 混合 Trades + Candles
    // ═════════════════════════════════════════════════════════════════

    [Fact]
    public void ProcessMessage_MixedTradesAndCandles_ShouldCountBoth()
    {
        var feed = CreateTestFeed();

        var tradesJson = """
        {
            "event": "data",
            "data": {
                "symbol": "2330", "type": "trades",
                "price": 600.0, "size": 100, "time": 1707955200000000, "serial": 1,
                "bid": 599.0, "ask": 601.0, "volume": 3000
            },
            "channel": "trades"
        }
        """;

        var candlesJson = """
        {
            "event": "data",
            "data": {
                "symbol": "2330", "type": "candles",
                "date": "2024-02-15T09:00:00.000+08:00",
                "open": 600, "high": 605, "low": 598, "close": 602, "volume": 500, "average": 601
            },
            "channel": "candles"
        }
        """;

        feed.ProcessMessage(Encoding.UTF8.GetBytes(tradesJson));
        feed.ProcessMessage(Encoding.UTF8.GetBytes(candlesJson));

        feed.TotalTicksReceived.Should().Be(1);
        feed.TotalBarsReceived.Should().Be(1);
    }

    // ═════════════════════════════════════════════════════════════════
    //  7. FugleMessages — JSON 序列化 / 反序列化
    // ═════════════════════════════════════════════════════════════════

    private static readonly JsonSerializerOptions _jsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
    };

    [Fact]
    public void FugleRequest_Auth_ShouldSerializeCorrectly()
    {
        var request = new FugleRequest
        {
            Event = FugleEvents.Auth,
            Data = new FugleAuthData { ApiKey = "my-api-key" }
        };

        var json = JsonSerializer.Serialize(request, _jsonOptions);

        json.Should().Contain("\"event\":\"auth\"");
        json.Should().Contain("\"apikey\":\"my-api-key\"");
    }

    [Fact]
    public void FugleRequest_Subscribe_ShouldSerializeCorrectly()
    {
        var request = new FugleRequest
        {
            Event = FugleEvents.Subscribe,
            Data = new FugleSubscribeData { Channel = FugleChannels.Trades, Symbol = "2330" }
        };

        var json = JsonSerializer.Serialize(request, _jsonOptions);

        json.Should().Contain("\"event\":\"subscribe\"");
        json.Should().Contain("\"channel\":\"trades\"");
        json.Should().Contain("\"symbol\":\"2330\"");
    }

    [Fact]
    public void FugleRequest_Unsubscribe_ShouldSerializeWithChannelId()
    {
        var request = new FugleRequest
        {
            Event = FugleEvents.Unsubscribe,
            Data = new FugleUnsubscribeData { Id = "ch-abc123" }
        };

        var json = JsonSerializer.Serialize(request, _jsonOptions);

        json.Should().Contain("\"event\":\"unsubscribe\"");
        json.Should().Contain("\"id\":\"ch-abc123\"");
    }

    [Fact]
    public void FugleRequest_Ping_ShouldOmitNullData()
    {
        var request = new FugleRequest { Event = FugleEvents.Ping };

        var json = JsonSerializer.Serialize(request, _jsonOptions);

        json.Should().Contain("\"event\":\"ping\"");
        json.Should().NotContain("\"data\"");
    }

    [Fact]
    public void FugleResponse_TradeData_ShouldDeserializeCorrectly()
    {
        var json = """
        {
            "event": "data",
            "data": {
                "symbol": "2330", "type": "trades", "exchange": "TWSE", "market": "TSE",
                "price": 600.0, "size": 234, "time": 1707955200000000, "serial": 1,
                "bid": 599.0, "ask": 601.0, "volume": 5000
            },
            "channel": "trades",
            "id": "ch-trade-001"
        }
        """;

        var response = JsonSerializer.Deserialize<FugleResponse>(json, _jsonOptions);

        response.Should().NotBeNull();
        response!.Event.Should().Be("data");
        response.Channel.Should().Be("trades");
        response.ChannelId.Should().Be("ch-trade-001");
        response.Data.ValueKind.Should().Be(JsonValueKind.Object);

        // data 為 JsonElement，需手動反序列化
        var trade = response.Data.Deserialize<FugleTradeData>(_jsonOptions);
        trade.Should().NotBeNull();
        trade!.Symbol.Should().Be("2330");
        trade.Price.Should().Be(600.0m);
        trade.Size.Should().Be(234);
        trade.Time.Should().Be(1707955200000000);
    }

    [Fact]
    public void FugleResponse_CandleData_ShouldDeserializeCorrectly()
    {
        var json = """
        {
            "event": "data",
            "data": {
                "symbol": "2454", "type": "candles", "exchange": "TWSE", "market": "TSE",
                "date": "2024-02-15T09:00:00.000+08:00",
                "open": 1000.0, "high": 1010.0, "low": 995.0, "close": 1005.0,
                "volume": 12345, "average": 1002.5
            },
            "channel": "candles"
        }
        """;

        var response = JsonSerializer.Deserialize<FugleResponse>(json, _jsonOptions);

        response.Should().NotBeNull();
        response!.Channel.Should().Be("candles");

        var candle = response.Data.Deserialize<FugleCandleData>(_jsonOptions);
        candle.Should().NotBeNull();
        candle!.Open.Should().Be(1000.0m);
        candle.High.Should().Be(1010.0m);
        candle.Low.Should().Be(995.0m);
        candle.Close.Should().Be(1005.0m);
        candle.Volume.Should().Be(12345);
        candle.Date.Should().Contain("2024-02-15");
        candle.Average.Should().Be(1002.5m);
    }

    [Fact]
    public void FugleResponse_BooksData_ShouldDeserializeWithBidsAsks()
    {
        var json = """
        {
            "event": "data",
            "data": {
                "symbol": "2330", "type": "books", "exchange": "TWSE", "market": "TSE",
                "bids": [
                    { "price": 604.0, "size": 500 },
                    { "price": 603.0, "size": 300 }
                ],
                "asks": [
                    { "price": 606.0, "size": 400 }
                ],
                "time": 1707955200000000
            },
            "channel": "books"
        }
        """;

        var response = JsonSerializer.Deserialize<FugleResponse>(json, _jsonOptions);

        response.Should().NotBeNull();
        response!.Channel.Should().Be("books");

        var books = response.Data.Deserialize<FugleBooksData>(_jsonOptions);
        books.Should().NotBeNull();
        books!.Symbol.Should().Be("2330");
        books.Bids.Should().HaveCount(2);
        books.Asks.Should().HaveCount(1);
        books.Bids![0].Price.Should().Be(604.0m);
    }

    // ═════════════════════════════════════════════════════════════════
    //  8. FugleOptions 預設值驗證
    // ═════════════════════════════════════════════════════════════════

    [Fact]
    public void FugleOptions_Defaults_ShouldBeReasonable()
    {
        var options = new FugleOptions();

        options.WebSocketUrl.Should().Contain("fugle.tw");
        options.PingIntervalSeconds.Should().BeGreaterThanOrEqualTo(10);
        options.TickChannelCapacity.Should().BeGreaterThanOrEqualTo(1000);
        options.BarChannelCapacity.Should().BeGreaterThanOrEqualTo(100);
        options.ReconnectBaseDelayMs.Should().BeGreaterThanOrEqualTo(500);
        options.ReconnectMaxDelayMs.Should().BeGreaterThanOrEqualTo(options.ReconnectBaseDelayMs);
    }

    [Fact]
    public void Constructor_WithoutApiToken_ShouldThrow()
    {
        var options = Options.Create(new FugleOptions { ApiToken = "" });
        var logger = NullLoggerFactory.Instance.CreateLogger<FugleMarketDataFeed>();

        var act = () => new FugleMarketDataFeed(options, logger);
        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*API Token*");
    }

    [Fact]
    public void Constructor_WithApiToken_ShouldNotThrow()
    {
        var options = Options.Create(new FugleOptions { ApiToken = "valid-token" });
        var logger = NullLoggerFactory.Instance.CreateLogger<FugleMarketDataFeed>();

        var act = () => new FugleMarketDataFeed(options, logger);
        act.Should().NotThrow();
    }

    // ═════════════════════════════════════════════════════════════════
    //  9. Channel Dispatch — 驗證 Channel → 可讀取資料
    // ═════════════════════════════════════════════════════════════════

    [Fact]
    public void Channel_TickData_ShouldBeReadableAfterProcessMessage()
    {
        var feed = CreateTestFeed();

        var json = """
        {
            "event": "data",
            "data": {
                "symbol": "2330", "type": "trades",
                "price": 600.0, "size": 100, "time": 1707955200000000, "serial": 1,
                "bid": 599.0, "ask": 601.0, "volume": 3000
            },
            "channel": "trades"
        }
        """;

        feed.ProcessMessage(Encoding.UTF8.GetBytes(json));

        // 直接從 channel 讀取驗證 (透過反射取得 private channel)
        var tickChannel = GetTickChannel(feed);
        var canRead = tickChannel.Reader.TryRead(out var tick);

        canRead.Should().BeTrue();
        tick!.Ticker.Should().Be("2330");
        tick.Price.Should().Be(600.0m);
        tick.Volume.Should().Be(100);
    }

    [Fact]
    public void Channel_BarData_ShouldBeReadableAfterProcessMessage()
    {
        var feed = CreateTestFeed();

        var json = """
        {
            "event": "data",
            "data": {
                "symbol": "2454", "type": "candles",
                "date": "2024-02-15T09:00:00.000+08:00",
                "open": 1000, "high": 1010, "low": 995, "close": 1005, "volume": 500, "average": 1002
            },
            "channel": "candles"
        }
        """;

        feed.ProcessMessage(Encoding.UTF8.GetBytes(json));

        var barChannel = GetBarChannel(feed);
        var canRead = barChannel.Reader.TryRead(out var bar);

        canRead.Should().BeTrue();
        bar!.Ticker.Should().Be("2454");
        bar.Open.Should().Be(1000m);
        bar.High.Should().Be(1010m);
        bar.Low.Should().Be(995m);
        bar.Close.Should().Be(1005m);
        bar.Volume.Should().Be(500);
    }

    [Fact]
    public void Channel_BooksMidPrice_ShouldBeReadableAfterProcessMessage()
    {
        var feed = CreateTestFeed();

        var json = """
        {
            "event": "data",
            "data": {
                "symbol": "2330", "type": "books",
                "bids": [{ "price": 604.0, "size": 500 }],
                "asks": [{ "price": 606.0, "size": 400 }],
                "time": 1707955200000000
            },
            "channel": "books"
        }
        """;

        feed.ProcessMessage(Encoding.UTF8.GetBytes(json));

        var tickChannel = GetTickChannel(feed);
        var canRead = tickChannel.Reader.TryRead(out var tick);

        canRead.Should().BeTrue();
        tick!.Ticker.Should().Be("2330");
        tick.Price.Should().Be(605.0m); // mid = (604 + 606) / 2
    }

    // ═════════════════════════════════════════════════════════════════
    //  10. FugleChannels / FugleEvents 常數
    // ═════════════════════════════════════════════════════════════════

    [Fact]
    public void FugleChannels_ShouldHaveExpectedValues()
    {
        FugleChannels.Trades.Should().Be("trades");
        FugleChannels.Candles.Should().Be("candles");
        FugleChannels.Books.Should().Be("books");
        FugleChannels.Aggregates.Should().Be("aggregates");
        FugleChannels.Indices.Should().Be("indices");
    }

    [Fact]
    public void FugleEvents_ShouldHaveExpectedValues()
    {
        FugleEvents.Auth.Should().Be("auth");
        FugleEvents.Authenticated.Should().Be("authenticated");
        FugleEvents.Subscribe.Should().Be("subscribe");
        FugleEvents.Unsubscribe.Should().Be("unsubscribe");
        FugleEvents.Subscribed.Should().Be("subscribed");
        FugleEvents.Unsubscribed.Should().Be("unsubscribed");
        FugleEvents.Subscriptions.Should().Be("subscriptions");
        FugleEvents.Data.Should().Be("data");
        FugleEvents.Ping.Should().Be("ping");
        FugleEvents.Pong.Should().Be("pong");
        FugleEvents.Heartbeat.Should().Be("heartbeat");
        FugleEvents.Error.Should().Be("error");
    }

    // ═════════════════════════════════════════════════════════════════
    //  11. 多筆高頻 Tick — Channel DropOldest 驗證
    // ═════════════════════════════════════════════════════════════════

    [Fact]
    public void ProcessMessage_HighFrequencyTrades_ShouldHandleWithDropOldest()
    {
        var feed = CreateTestFeed(); // capacity = 100

        // 送 200 筆 trades (每筆為獨立 flat message，超過 channel 容量 100 → DropOldest)
        for (int i = 0; i < 200; i++)
        {
            var json = $$"""
            {
                "event": "data",
                "data": {
                    "symbol": "2330", "type": "trades",
                    "price": {{600 + i * 0.1m}}, "size": 100,
                    "time": {{1707955200000000 + i * 1000000}}, "serial": {{i + 1}},
                    "bid": 599.0, "ask": 601.0, "volume": {{3000 + i * 100}}
                },
                "channel": "trades"
            }
            """;
            feed.ProcessMessage(Encoding.UTF8.GetBytes(json));
        }

        // 全部 200 筆都應被計數（DropOldest 不阻塞寫入）
        feed.TotalTicksReceived.Should().Be(200);
    }

    // ═════════════════════════════════════════════════════════════════
    //  12. IsConnected 初始狀態
    // ═════════════════════════════════════════════════════════════════

    [Fact]
    public void IsConnected_BeforeStart_ShouldBeFalse()
    {
        var feed = CreateTestFeed();
        feed.IsConnected.Should().BeFalse();
    }

    // ═════════════════════════════════════════════════════════════════
    //  13. Fugle Free Tier Limits (5 Subscriptions & PreferTradesOnly)
    // ═════════════════════════════════════════════════════════════════

    [Fact]
    public void Subscribe_ExceedingLimit_ShouldNotExceedFiveSubscriptions()
    {
        var feed = CreateTestFeed();

        // 訂閱 3 支股票，每支 2 個頻道 (Realtime: trades + candles) = 6 個訂閱
        // 第 6 個訂閱應被擋掉
        feed.Subscribe("2330", MarketDataType.Realtime); // 2 subs: 2330/trades, 2330/candles
        feed.Subscribe("2454", MarketDataType.Realtime); // 2 subs: 2454/trades, 2454/candles
        feed.Subscribe("2303", MarketDataType.Realtime); // 2 subs: 2303/trades (ok), 2303/candles (skip)

        // 模擬伺服器回傳 subscribed 事件 (TrackChannelId 會增加 _channelIdMap.Count)
        feed.ProcessMessage(Encoding.UTF8.GetBytes("""{"event":"subscribed","data":{"id":"ch1","channel":"trades","symbol":"2330"}}"""));
        feed.ProcessMessage(Encoding.UTF8.GetBytes("""{"event":"subscribed","data":{"id":"ch2","channel":"candles","symbol":"2330"}}"""));
        feed.ProcessMessage(Encoding.UTF8.GetBytes("""{"event":"subscribed","data":{"id":"ch3","channel":"trades","symbol":"2454"}}"""));
        feed.ProcessMessage(Encoding.UTF8.GetBytes("""{"event":"subscribed","data":{"id":"ch4","channel":"candles","symbol":"2454"}}"""));
        feed.ProcessMessage(Encoding.UTF8.GetBytes("""{"event":"subscribed","data":{"id":"ch5","channel":"trades","symbol":"2303"}}"""));

        // 再訂閱一次 2303/candles 或是其他股票
        feed.Subscribe("2317", MarketDataType.Realtime);

        // 驗證 _subscriptions 內部的 entry 數量
        // 注意: _subscriptions 紀錄的是「意圖」，而 _channelIdMap 紀錄的是「已確認連線」或是「佔位」
        // 在我的實作中，Subscribe() 內會檢查 _channelIdMap.Count
        
        // 由於測試中手動觸發 ProcessMessage 模擬確認，我們檢查 _channelIdMap
        var channelIdMap = GetChannelIdMap(feed);
        channelIdMap.Count.Should().Be(5);
    }

    [Fact]
    public void GetChannelsForDataType_PreferTradesOnly_ShouldReturnOnlyTrades()
    {
        var options = Options.Create(new FugleOptions
        {
            ApiToken = "test",
            PreferTradesOnly = true
        });
        var feed = new FugleMarketDataFeed(options, NullLoggerFactory.Instance.CreateLogger<FugleMarketDataFeed>());

        var channels = GetChannelsForDataType(feed, MarketDataType.Realtime);
        channels.Should().HaveCount(1);
        channels.Should().Contain(FugleChannels.Trades);

        channels = GetChannelsForDataType(feed, MarketDataType.Simulate);
        channels.Should().HaveCount(1);
        channels.Should().Contain(FugleChannels.Trades);
    }

    // ═════════════════════════════════════════════════════════════════
    //  Helpers — 反射取得 internal channels
    // ═════════════════════════════════════════════════════════════════

    private static System.Threading.Channels.Channel<TickData> GetTickChannel(FugleMarketDataFeed feed)
    {
        var field = typeof(FugleMarketDataFeed).GetField("_tickChannel",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        return (System.Threading.Channels.Channel<TickData>)field!.GetValue(feed)!;
    }

    private static System.Threading.Channels.Channel<BarData> GetBarChannel(FugleMarketDataFeed feed)
    {
        var field = typeof(FugleMarketDataFeed).GetField("_barChannel",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        return (System.Threading.Channels.Channel<BarData>)field!.GetValue(feed)!;
    }

    private Dictionary<(string Ticker, string Channel), string> GetChannelIdMap(FugleMarketDataFeed feed)
    {
        return (Dictionary<(string Ticker, string Channel), string>)feed.GetType()
            .GetField("_channelIdMap", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!
            .GetValue(feed)!;
    }

    private string[] GetChannelsForDataType(FugleMarketDataFeed feed, MarketDataType dataType)
    {
        return (string[])feed.GetType()
            .GetMethod("GetChannelsForDataType", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!
            .Invoke(feed, new object[] { dataType })!;
    }
}
