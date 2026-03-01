namespace QuantTrading.Infrastructure.Configuration;

/// <summary>
/// 富果 (Fugle) MarketData WebSocket 連線設定。
/// 對應 appsettings.json 的 "Fugle" 區段。
/// </summary>
public class FugleOptions
{
    public const string SectionName = "Fugle";

    // ── 連線 ────────────────────────────────────────────────────────

    /// <summary>WebSocket 端點 (wss://)</summary>
    public string WebSocketUrl { get; set; } = "wss://api.fugle.tw/marketdata/v1.0/stock/streaming";

    /// <summary>Fugle API Token</summary>
    public string ApiToken { get; set; } = "";

    // ── 韌性 (Resilience) ───────────────────────────────────────────

    /// <summary>重連最大嘗試次數 (0 = 無限)</summary>
    public int MaxReconnectAttempts { get; set; } = 0;

    /// <summary>重連初始等待 (ms)</summary>
    public int ReconnectBaseDelayMs { get; set; } = 1_000;

    /// <summary>重連最大等待 (ms)</summary>
    public int ReconnectMaxDelayMs { get; set; } = 30_000;

    /// <summary>心跳 (Ping) 間隔秒數</summary>
    public int PingIntervalSeconds { get; set; } = 30;

    // ── Channel 緩衝區 ──────────────────────────────────────────────

    /// <summary>Tick Channel 容量</summary>
    public int TickChannelCapacity { get; set; } = 10_000;

    /// <summary>Bar Channel 容量</summary>
    public int BarChannelCapacity { get; set; } = 1_000;
}
