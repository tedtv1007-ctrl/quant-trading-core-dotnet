using QuantTrading.Core.Interfaces;
using QuantTrading.Core.Models;
using QuantTrading.Core.Services;

namespace QuantTrading.Web.Services;

/// <summary>
/// Minimal API endpoints — 提供 REST API 供前端或外部測試使用。
/// </summary>
public static class TradingApiEndpoints
{
    public static void MapTradingApi(this WebApplication app)
    {
        var group = app.MapGroup("/api/trading");

        // ── Watchlist ───────────────────────────────────────────

        // Helper to bypass PipeWriter.UnflushedBytes issue in some test environments
        IResult JsonOk(object value) => Results.Content(
            System.Text.Json.JsonSerializer.Serialize(value, new System.Text.Json.JsonSerializerOptions 
            { 
                PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.CamelCase,
                Converters = { new System.Text.Json.Serialization.JsonStringEnumConverter() }
            }), "application/json");

        // GET /api/trading/watchlist — 取得監控清單
        group.MapGet("/watchlist", (TradingStateService state) => JsonOk(state.GetWatchlist()));

        // POST /api/trading/watchlist — 新增監控股票
        group.MapPost("/watchlist", (WatchlistRequest request, TradingStateService state) =>
        {
            if (string.IsNullOrWhiteSpace(request.Ticker))
                return Results.BadRequest(new { error = "Ticker is required." });
            if (request.RefPrice <= 0)
                return Results.BadRequest(new { error = "RefPrice must be greater than 0." });

            state.AddToWatchlist(request.Ticker, request.RefPrice);
            return JsonOk(new { message = $"Added {request.Ticker}" });
        });

        // DELETE /api/trading/watchlist/{ticker} — 移除監控股票
        group.MapDelete("/watchlist/{ticker}", (string ticker, TradingStateService state) =>
        {
            state.RemoveFromWatchlist(ticker);
            return JsonOk(new { message = $"Removed {ticker}" });
        });

        // ── Signals ─────────────────────────────────────────────

        // GET /api/trading/signals?ticker=XXX — 取得訊號 (可依 ticker 過濾)
        group.MapGet("/signals", (TradingStateService state, string? ticker) => JsonOk(state.GetSignals(ticker)));

        // GET /api/trading/rejections?ticker=XXX — 取得被拒絕的訊號
        group.MapGet("/rejections", (TradingStateService state, string? ticker) => JsonOk(state.GetRejections(ticker)));

        // ── Status ──────────────────────────────────────────────

        // GET /api/trading/status — 取得風控狀態（原子快照）
        group.MapGet("/status", (TradingStateService state) => JsonOk(state.GetStatusSnapshot()));

        // ── Simulation ──────────────────────────────────────────

        // POST /api/trading/simulate — 啟動多股模擬
        group.MapPost("/simulate", (SimulateRequest request, SimulationBackgroundService sim, TradingStateService state) =>
        {
            if (request.TickDelayMs <= 0)
                return Results.BadRequest(new { error = "TickDelayMs must be greater than 0." });
            if (!string.IsNullOrWhiteSpace(request.Ticker) && request.RefPrice <= 0)
                return Results.BadRequest(new { error = "RefPrice must be greater than 0." });

            // 向下相容：若 request 帶有 Ticker，自動加入 watchlist
            if (!string.IsNullOrWhiteSpace(request.Ticker))
            {
                state.AddToWatchlist(request.Ticker, request.RefPrice);
            }

            sim.StartSimulation(
                request.SimulationDate == default ? DateTime.Today : request.SimulationDate,
                request.TickDelayMs);

            return Results.Content(
                System.Text.Json.JsonSerializer.Serialize(new { message = "Multi-stock simulation started" }, new System.Text.Json.JsonSerializerOptions 
                { 
                    PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.CamelCase 
                }), "application/json", statusCode: 202);
        });

        // POST /api/trading/stop — 停止模擬
        group.MapPost("/stop", async (SimulationBackgroundService sim) =>
        {
            await sim.StopSimulationAsync();
            return JsonOk(new { message = "Stop completed" });
        });

        // ── Market Data ─────────────────────────────────────────

        // GET /api/trading/ticks?ticker=XXX — 取得最近 Ticks
        group.MapGet("/ticks", (TradingStateService state, string? ticker) => JsonOk(state.GetRecentTicks(ticker)));

        // GET /api/trading/bars?ticker=XXX — 取得最近 Bars
        group.MapGet("/bars", (TradingStateService state, string? ticker) => JsonOk(state.GetRecentBars(ticker)));

        // ── Trade Journal ───────────────────────────────────────

        // GET /api/trading/journal/export?from=2026-01-01&to=2026-03-01 — 匯出 CSV
        group.MapGet("/journal/export", async (ITradeJournalStore store, DateTime? from, DateTime? to) =>
        {
            var csv = await store.ExportCsvAsync(from, to);
            var fileName = $"trade-journal-{DateTime.UtcNow:yyyyMMdd-HHmmss}.csv";
            return Results.File(
                System.Text.Encoding.UTF8.GetPreamble().Concat(System.Text.Encoding.UTF8.GetBytes(csv)).ToArray(),
                "text/csv",
                fileName);
        });

        // ── Performance Analytics (NoFx-inspired) ───────────────

        // GET /api/trading/analytics?from=2026-01-01&to=2026-03-31 — 取得績效分析
        group.MapGet("/analytics", async (TradeStatisticsService stats, DateTime? from, DateTime? to) =>
        {
            var summary = await stats.GetPerformanceSummaryAsync(from, to);
            return JsonOk(summary);
        });

        // ── System Event Log ────────────────────────────────────

        // GET /api/trading/events?count=100&level=Warning&category=Risk — 取得系統事件
        group.MapGet("/events", (SystemEventLogService eventLog, int? count, string? level, string? category) =>
        {
            SystemEventLevel? levelFilter = level switch
            {
                "Info" => SystemEventLevel.Info,
                "Warning" => SystemEventLevel.Warning,
                "Error" => SystemEventLevel.Error,
                _ => null
            };
            var events = eventLog.GetEvents(count ?? 100, levelFilter, category);
            return JsonOk(events);
        });
    }
}

public record WatchlistRequest(string Ticker, decimal RefPrice);

public record SimulateRequest(
    string Ticker = "2330",
    decimal RefPrice = 600m,
    DateTime SimulationDate = default,
    int TickDelayMs = 100
);
