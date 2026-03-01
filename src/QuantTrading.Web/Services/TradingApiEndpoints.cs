using QuantTrading.Core.Interfaces;
using QuantTrading.Core.Models;

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

        // GET /api/trading/watchlist — 取得監控清單
        group.MapGet("/watchlist", (TradingStateService state) =>
        {
            return Results.Ok(state.GetWatchlist());
        });

        // POST /api/trading/watchlist — 新增監控股票
        group.MapPost("/watchlist", (WatchlistRequest request, TradingStateService state) =>
        {
            if (string.IsNullOrWhiteSpace(request.Ticker))
                return Results.BadRequest(new { error = "Ticker is required." });
            if (request.RefPrice <= 0)
                return Results.BadRequest(new { error = "RefPrice must be greater than 0." });

            state.AddToWatchlist(request.Ticker, request.RefPrice);
            return Results.Ok(new { message = $"Added {request.Ticker}" });
        });

        // DELETE /api/trading/watchlist/{ticker} — 移除監控股票
        group.MapDelete("/watchlist/{ticker}", (string ticker, TradingStateService state) =>
        {
            state.RemoveFromWatchlist(ticker);
            return Results.Ok(new { message = $"Removed {ticker}" });
        });

        // ── Signals ─────────────────────────────────────────────

        // GET /api/trading/signals?ticker=XXX — 取得訊號 (可依 ticker 過濾)
        group.MapGet("/signals", (TradingStateService state, string? ticker) =>
        {
            return Results.Ok(state.GetSignals(ticker));
        });

        // GET /api/trading/rejections?ticker=XXX — 取得被拒絕的訊號
        group.MapGet("/rejections", (TradingStateService state, string? ticker) =>
        {
            return Results.Ok(state.GetRejections(ticker));
        });

        // ── Status ──────────────────────────────────────────────

        // GET /api/trading/status — 取得風控狀態（原子快照）
        group.MapGet("/status", (TradingStateService state) =>
        {
            return Results.Ok(state.GetStatusSnapshot());
        });

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

            return Results.Accepted(value: new { message = "Multi-stock simulation started" });
        });

        // POST /api/trading/stop — 停止模擬
        group.MapPost("/stop", async (SimulationBackgroundService sim) =>
        {
            await sim.StopSimulationAsync();
            return Results.Ok(new { message = "Stop completed" });
        });

        // ── Market Data ─────────────────────────────────────────

        // GET /api/trading/ticks?ticker=XXX — 取得最近 Ticks
        group.MapGet("/ticks", (TradingStateService state, string? ticker) =>
        {
            return Results.Ok(state.GetRecentTicks(ticker));
        });

        // GET /api/trading/bars?ticker=XXX — 取得最近 Bars
        group.MapGet("/bars", (TradingStateService state, string? ticker) =>
        {
            return Results.Ok(state.GetRecentBars(ticker));
        });

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
    }
}

public record WatchlistRequest(string Ticker, decimal RefPrice);

public record SimulateRequest(
    string Ticker = "2330",
    decimal RefPrice = 600m,
    DateTime SimulationDate = default,
    int TickDelayMs = 100
);
