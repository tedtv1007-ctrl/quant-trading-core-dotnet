using System.Text.Json;
using System.Text.Json.Serialization;

namespace QuantTrading.Infrastructure.Fugle;

// ═══════════════════════════════════════════════════════════════════════
//  Fugle MarketData WebSocket v1.0 JSON DTOs
//  文件: https://developer.fugle.tw/docs/data/websocket-api/getting-started
//
//  資料格式對齊 https://developer.fugle.tw/llms-full.txt (2026-03)
//  ‧trades / candles / books / aggregates / indices 五種頻道
//  ‧所有 data event 的 payload 為 flat object (非巢狀陣列)
//  ‧時間戳為 microseconds (μs，16 位數)
//  ‧驗證流程: connect → send auth → receive authenticated
// ═══════════════════════════════════════════════════════════════════════

// ══════════════════════════════════════════════════════════════════════
//  Client → Server
// ══════════════════════════════════════════════════════════════════════

/// <summary>通用 Client→Server 訊息信封。data 為 object 以支援不同結構。</summary>
public class FugleRequest
{
    [JsonPropertyName("event")]
    public string Event { get; set; } = "";

    [JsonPropertyName("data")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public object? Data { get; set; }
}

/// <summary>auth 事件的 data。</summary>
public class FugleAuthData
{
    [JsonPropertyName("apikey")]
    public string ApiKey { get; set; } = "";
}

/// <summary>subscribe 事件的 data（單股或多股）。</summary>
public class FugleSubscribeData
{
    [JsonPropertyName("channel")]
    public string Channel { get; set; } = "";

    [JsonPropertyName("symbol")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Symbol { get; set; }

    [JsonPropertyName("symbols")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public List<string>? Symbols { get; set; }

    [JsonPropertyName("intradayOddLot")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public bool IntradayOddLot { get; set; }
}

/// <summary>unsubscribe 事件的 data（依 channel ID）。</summary>
public class FugleUnsubscribeData
{
    [JsonPropertyName("id")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Id { get; set; }

    [JsonPropertyName("ids")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public List<string>? Ids { get; set; }
}

// ══════════════════════════════════════════════════════════════════════
//  Server → Client
// ══════════════════════════════════════════════════════════════════════

/// <summary>
/// Server 回傳訊息的最外層信封。
/// data 為 JsonElement，依 event/channel 再反序列化為具體型別。
/// </summary>
public class FugleResponse
{
    [JsonPropertyName("event")]
    public string Event { get; set; } = "";

    [JsonPropertyName("data")]
    public JsonElement Data { get; set; }

    /// <summary>訂閱頻道 ID（僅 data 事件帶有）。</summary>
    [JsonPropertyName("id")]
    public string? ChannelId { get; set; }

    /// <summary>頻道名稱（僅 data 事件帶有）。</summary>
    [JsonPropertyName("channel")]
    public string? Channel { get; set; }
}

/// <summary>subscribed 事件回傳的單筆訂閱資訊。</summary>
public class FugleSubscribedInfo
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = "";

    [JsonPropertyName("channel")]
    public string Channel { get; set; } = "";

    [JsonPropertyName("symbol")]
    public string Symbol { get; set; } = "";
}

// ══════════════════════════════════════════════════════════════════════
//  Channel Data DTOs — data event 內的具體結構 (Flat)
// ══════════════════════════════════════════════════════════════════════

// ── trades 頻道 ─────────────────────────────────────────────────────

/// <summary>trades 頻道即時成交資料 (flat object，非陣列)。</summary>
public class FugleTradeData
{
    [JsonPropertyName("symbol")]
    public string Symbol { get; set; } = "";

    [JsonPropertyName("type")]
    public string Type { get; set; } = "";

    [JsonPropertyName("exchange")]
    public string Exchange { get; set; } = "";

    [JsonPropertyName("market")]
    public string Market { get; set; } = "";

    [JsonPropertyName("bid")]
    public decimal Bid { get; set; }

    [JsonPropertyName("ask")]
    public decimal Ask { get; set; }

    [JsonPropertyName("price")]
    public decimal Price { get; set; }

    [JsonPropertyName("size")]
    public long Size { get; set; }

    /// <summary>累計成交量。</summary>
    [JsonPropertyName("volume")]
    public long Volume { get; set; }

    /// <summary>Unix timestamp (μs，16 位數)。</summary>
    [JsonPropertyName("time")]
    public long Time { get; set; }

    [JsonPropertyName("serial")]
    public long Serial { get; set; }

    // ── Boolean flags ──────────────────────────────────────────
    [JsonPropertyName("isTrial")]
    public bool IsTrial { get; set; }

    [JsonPropertyName("isOpen")]
    public bool IsOpen { get; set; }

    [JsonPropertyName("isClose")]
    public bool IsClose { get; set; }

    [JsonPropertyName("isContinuous")]
    public bool IsContinuous { get; set; }

    [JsonPropertyName("isLimitUpPrice")]
    public bool IsLimitUpPrice { get; set; }

    [JsonPropertyName("isLimitDownPrice")]
    public bool IsLimitDownPrice { get; set; }

    [JsonPropertyName("isLimitUpBid")]
    public bool IsLimitUpBid { get; set; }

    [JsonPropertyName("isLimitDownBid")]
    public bool IsLimitDownBid { get; set; }

    [JsonPropertyName("isLimitUpAsk")]
    public bool IsLimitUpAsk { get; set; }

    [JsonPropertyName("isLimitDownAsk")]
    public bool IsLimitDownAsk { get; set; }

    [JsonPropertyName("isLimitUpHalt")]
    public bool IsLimitUpHalt { get; set; }

    [JsonPropertyName("isLimitDownHalt")]
    public bool IsLimitDownHalt { get; set; }

    [JsonPropertyName("isDelayedOpen")]
    public bool IsDelayedOpen { get; set; }

    [JsonPropertyName("isDelayedClose")]
    public bool IsDelayedClose { get; set; }
}

// ── candles 頻道 ────────────────────────────────────────────────────

/// <summary>candles 頻道分鐘 K 資料 (flat object)。</summary>
public class FugleCandleData
{
    [JsonPropertyName("symbol")]
    public string Symbol { get; set; } = "";

    [JsonPropertyName("type")]
    public string Type { get; set; } = "";

    [JsonPropertyName("exchange")]
    public string Exchange { get; set; } = "";

    [JsonPropertyName("market")]
    public string Market { get; set; } = "";

    /// <summary>K 線時間 (ISO 8601: "2023-05-29T13:30:00.000+08:00")。</summary>
    [JsonPropertyName("date")]
    public string Date { get; set; } = "";

    [JsonPropertyName("open")]
    public decimal Open { get; set; }

    [JsonPropertyName("high")]
    public decimal High { get; set; }

    [JsonPropertyName("low")]
    public decimal Low { get; set; }

    [JsonPropertyName("close")]
    public decimal Close { get; set; }

    [JsonPropertyName("volume")]
    public long Volume { get; set; }

    [JsonPropertyName("average")]
    public decimal Average { get; set; }
}

// ── books 頻道 ──────────────────────────────────────────────────────

/// <summary>books 頻道最佳五檔資料。</summary>
public class FugleBooksData
{
    [JsonPropertyName("symbol")]
    public string Symbol { get; set; } = "";

    [JsonPropertyName("type")]
    public string Type { get; set; } = "";

    [JsonPropertyName("exchange")]
    public string Exchange { get; set; } = "";

    [JsonPropertyName("market")]
    public string Market { get; set; } = "";

    /// <summary>Unix timestamp (μs)。</summary>
    [JsonPropertyName("time")]
    public long Time { get; set; }

    [JsonPropertyName("bids")]
    public List<FuglePriceLevel>? Bids { get; set; }

    [JsonPropertyName("asks")]
    public List<FuglePriceLevel>? Asks { get; set; }
}

// ── aggregates 頻道 ─────────────────────────────────────────────────

/// <summary>aggregates 頻道聚合行情資料 (完整報價)。</summary>
public class FugleAggregateData
{
    [JsonPropertyName("date")]
    public string Date { get; set; } = "";

    [JsonPropertyName("type")]
    public string Type { get; set; } = "";

    [JsonPropertyName("exchange")]
    public string Exchange { get; set; } = "";

    [JsonPropertyName("market")]
    public string Market { get; set; } = "";

    [JsonPropertyName("symbol")]
    public string Symbol { get; set; } = "";

    [JsonPropertyName("name")]
    public string Name { get; set; } = "";

    [JsonPropertyName("referencePrice")]
    public decimal ReferencePrice { get; set; }

    [JsonPropertyName("previousClose")]
    public decimal PreviousClose { get; set; }

    [JsonPropertyName("openPrice")]
    public decimal OpenPrice { get; set; }

    [JsonPropertyName("openTime")]
    public long OpenTime { get; set; }

    [JsonPropertyName("highPrice")]
    public decimal HighPrice { get; set; }

    [JsonPropertyName("highTime")]
    public long HighTime { get; set; }

    [JsonPropertyName("lowPrice")]
    public decimal LowPrice { get; set; }

    [JsonPropertyName("lowTime")]
    public long LowTime { get; set; }

    [JsonPropertyName("closePrice")]
    public decimal ClosePrice { get; set; }

    [JsonPropertyName("closeTime")]
    public long CloseTime { get; set; }

    [JsonPropertyName("avgPrice")]
    public decimal AvgPrice { get; set; }

    [JsonPropertyName("change")]
    public decimal Change { get; set; }

    [JsonPropertyName("changePercent")]
    public decimal ChangePercent { get; set; }

    [JsonPropertyName("amplitude")]
    public decimal Amplitude { get; set; }

    [JsonPropertyName("lastPrice")]
    public decimal LastPrice { get; set; }

    [JsonPropertyName("lastSize")]
    public long LastSize { get; set; }

    [JsonPropertyName("bids")]
    public List<FuglePriceLevel>? Bids { get; set; }

    [JsonPropertyName("asks")]
    public List<FuglePriceLevel>? Asks { get; set; }

    [JsonPropertyName("total")]
    public FugleTotal? Total { get; set; }

    [JsonPropertyName("lastTrade")]
    public FugleLastTrade? LastTrade { get; set; }

    [JsonPropertyName("lastTrial")]
    public FugleLastTrade? LastTrial { get; set; }

    [JsonPropertyName("serial")]
    public long Serial { get; set; }

    [JsonPropertyName("lastUpdated")]
    public long LastUpdated { get; set; }
}

// ── indices 頻道 ────────────────────────────────────────────────────

/// <summary>indices 頻道指數資料。</summary>
public class FugleIndexData
{
    [JsonPropertyName("symbol")]
    public string Symbol { get; set; } = "";

    [JsonPropertyName("type")]
    public string Type { get; set; } = "";

    [JsonPropertyName("exchange")]
    public string Exchange { get; set; } = "";

    [JsonPropertyName("index")]
    public decimal Index { get; set; }

    /// <summary>Unix timestamp (μs)。</summary>
    [JsonPropertyName("time")]
    public long Time { get; set; }
}

// ══════════════════════════════════════════════════════════════════════
//  Shared Sub-types
// ══════════════════════════════════════════════════════════════════════

public class FuglePriceLevel
{
    [JsonPropertyName("price")]
    public decimal Price { get; set; }

    [JsonPropertyName("size")]
    public long Size { get; set; }
}

public class FugleTotal
{
    [JsonPropertyName("tradeValue")]
    public decimal TradeValue { get; set; }

    [JsonPropertyName("tradeVolume")]
    public long TradeVolume { get; set; }

    [JsonPropertyName("tradeVolumeAtBid")]
    public long TradeVolumeAtBid { get; set; }

    [JsonPropertyName("tradeVolumeAtAsk")]
    public long TradeVolumeAtAsk { get; set; }

    [JsonPropertyName("transaction")]
    public long Transaction { get; set; }

    [JsonPropertyName("time")]
    public long Time { get; set; }
}

public class FugleLastTrade
{
    [JsonPropertyName("bid")]
    public decimal Bid { get; set; }

    [JsonPropertyName("ask")]
    public decimal Ask { get; set; }

    [JsonPropertyName("price")]
    public decimal Price { get; set; }

    [JsonPropertyName("size")]
    public long Size { get; set; }

    [JsonPropertyName("time")]
    public long Time { get; set; }

    [JsonPropertyName("serial")]
    public long Serial { get; set; }
}

// ══════════════════════════════════════════════════════════════════════
//  常數
// ══════════════════════════════════════════════════════════════════════

public static class FugleChannels
{
    public const string Trades     = "trades";
    public const string Candles    = "candles";
    public const string Books      = "books";
    public const string Aggregates = "aggregates";
    public const string Indices    = "indices";
}

public static class FugleEvents
{
    public const string Auth          = "auth";
    public const string Authenticated = "authenticated";
    public const string Subscribe     = "subscribe";
    public const string Unsubscribe   = "unsubscribe";
    public const string Subscribed    = "subscribed";
    public const string Unsubscribed  = "unsubscribed";
    public const string Subscriptions = "subscriptions";
    public const string Data          = "data";
    public const string Ping          = "ping";
    public const string Pong          = "pong";
    public const string Heartbeat     = "heartbeat";
    public const string Error         = "error";
}
