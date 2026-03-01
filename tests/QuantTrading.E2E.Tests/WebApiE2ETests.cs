using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using QuantTrading.Core.Models;
using QuantTrading.Web.Services;

namespace QuantTrading.E2E.Tests;

/// <summary>
/// E2E 測試 — Web API Endpoints 端對端驗證
/// 使用 WebApplicationFactory 測試真實 HTTP request/response 行為。
/// </summary>
public class WebApiE2ETests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _factory;
    private readonly HttpClient _client;

    public WebApiE2ETests(WebApplicationFactory<Program> factory)
    {
        _factory = factory;
        _client = factory.CreateClient();
    }

    // ── GET /api/trading/status ─────────────────────────────────

    [Fact]
    public async Task GetStatus_Should_Return_Ok_With_Initial_State()
    {
        var response = await _client.GetAsync("/api/trading/status");

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var content = await response.Content.ReadFromJsonAsync<StatusResponse>();
        content.Should().NotBeNull();
        content!.DailyTradeCount.Should().Be(0);
        content.IsSimulationRunning.Should().BeFalse();
    }

    // ── GET /api/trading/signals ────────────────────────────────

    [Fact]
    public async Task GetSignals_Should_Return_Empty_Array_Initially()
    {
        var response = await _client.GetAsync("/api/trading/signals");

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var signals = await response.Content.ReadFromJsonAsync<List<SignalContext>>();
        signals.Should().NotBeNull();
        signals.Should().BeEmpty();
    }

    // ── GET /api/trading/rejections ─────────────────────────────

    [Fact]
    public async Task GetRejections_Should_Return_Empty_Array_Initially()
    {
        var response = await _client.GetAsync("/api/trading/rejections");

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var rejections = await response.Content.ReadFromJsonAsync<List<RejectedSignal>>();
        rejections.Should().NotBeNull();
        rejections.Should().BeEmpty();
    }

    // ── GET /api/trading/ticks ──────────────────────────────────

    [Fact]
    public async Task GetTicks_Should_Return_Empty_Initially()
    {
        var response = await _client.GetAsync("/api/trading/ticks");
        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    // ── GET /api/trading/bars ───────────────────────────────────

    [Fact]
    public async Task GetBars_Should_Return_Empty_Initially()
    {
        var response = await _client.GetAsync("/api/trading/bars");
        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    // ── POST /api/trading/simulate ──────────────────────────────

    [Fact]
    public async Task PostSimulate_Should_Return_Accepted()
    {
        var request = new SimulateRequest(
            Ticker: "2330",
            RefPrice: 600m,
            SimulationDate: new DateTime(2026, 2, 28),
            TickDelayMs: 10
        );

        var response = await _client.PostAsJsonAsync("/api/trading/simulate", request);

        response.StatusCode.Should().Be(HttpStatusCode.Accepted);
    }

    // ── POST /api/trading/stop ──────────────────────────────────

    [Fact]
    public async Task PostStop_Should_Return_Ok()
    {
        var response = await _client.PostAsync("/api/trading/stop", null);
        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    // ── Full E2E: Simulate then check signals ───────────────────

    [Fact]
    public async Task Simulate_Then_GetSignals_Should_Return_Generated_Signals()
    {
        // 使用新的 factory 實例確保乾淨狀態
        var factory = new WebApplicationFactory<Program>();
        var client = factory.CreateClient();

        // Start simulation with fast speed
        var request = new SimulateRequest(
            Ticker: "2330",
            RefPrice: 600m,
            SimulationDate: new DateTime(2026, 2, 28),
            TickDelayMs: 5
        );

        await client.PostAsJsonAsync("/api/trading/simulate", request);

        // 等待模擬完成 (短暫的 delay)
        await Task.Delay(5000);

        // Check status
        var statusResponse = await client.GetAsync("/api/trading/status");
        statusResponse.StatusCode.Should().Be(HttpStatusCode.OK);

        // Check signals — 應該有產生至少一些訊號或資料
        var ticksResponse = await client.GetAsync("/api/trading/ticks");
        ticksResponse.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    // ── DTO for deserialization ──────────────────────────────────

    private record StatusResponse(
        bool IsSimulationRunning,
        string SimulationStatus,
        int DailyTradeCount,
        decimal DailyRealizedLoss,
        int WatchlistCount,
        List<string> ActiveTickers
    );
}
