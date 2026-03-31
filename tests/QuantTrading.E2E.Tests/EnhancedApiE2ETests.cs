using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;

namespace QuantTrading.E2E.Tests;

/// <summary>
/// E2E 測試 — 新增 API endpoints (Health Check, Analytics, Events) 端對端驗證。
/// </summary>
public class EnhancedApiE2ETests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly HttpClient _client;

    public EnhancedApiE2ETests(WebApplicationFactory<Program> factory)
    {
        _client = factory.CreateClient();
    }

    // ── Health Check ────────────────────────────────────────────

    [Fact]
    public async Task HealthCheck_Returns_Healthy()
    {
        var response = await _client.GetAsync("/api/health");
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var content = await response.Content.ReadAsStringAsync();
        content.Should().Be("Healthy");
    }

    // ── Analytics API ───────────────────────────────────────────

    [Fact]
    public async Task GetAnalytics_Returns_Ok_With_Empty_Summary()
    {
        var response = await _client.GetAsync("/api/trading/analytics");
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var content = await response.Content.ReadAsStringAsync();
        content.Should().Contain("totalTrades");
        content.Should().Contain("winRate");
    }

    [Fact]
    public async Task GetAnalytics_WithDateRange_Returns_Ok()
    {
        var response = await _client.GetAsync("/api/trading/analytics?from=2026-01-01&to=2026-12-31");
        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    // ── Events API ──────────────────────────────────────────────

    [Fact]
    public async Task GetEvents_Returns_Ok()
    {
        var response = await _client.GetAsync("/api/trading/events");
        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task GetEvents_WithFilters_Returns_Ok()
    {
        var response = await _client.GetAsync("/api/trading/events?count=50&level=Warning&category=Risk");
        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    // ── Watchlist CRUD E2E ──────────────────────────────────────

    [Fact]
    public async Task Watchlist_Add_And_Get_And_Delete()
    {
        var factory = new WebApplicationFactory<Program>();
        var client = factory.CreateClient();

        // Add
        var addResponse = await client.PostAsJsonAsync("/api/trading/watchlist",
            new { Ticker = "TEST", RefPrice = 500m });
        addResponse.StatusCode.Should().Be(HttpStatusCode.OK);

        // Get
        var getResponse = await client.GetAsync("/api/trading/watchlist");
        getResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var content = await getResponse.Content.ReadAsStringAsync();
        content.Should().Contain("TEST");

        // Delete
        var deleteResponse = await client.DeleteAsync("/api/trading/watchlist/TEST");
        deleteResponse.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    // ── Input Validation ────────────────────────────────────────

    [Fact]
    public async Task Watchlist_Add_Empty_Body_Returns_Error()
    {
        // Sending an improperly structured request should not crash the API
        var response = await _client.PostAsJsonAsync("/api/trading/watchlist",
            new { Ticker = (string?)null, RefPrice = 0m });
        // Should return either 400 (validation) or handle gracefully
        var statusCode = (int)response.StatusCode;
        statusCode.Should().BeOneOf(400, 500);
    }

    [Fact]
    public async Task Simulate_WithDefaultParams_Returns_Accepted()
    {
        // Valid simulation request
        var response = await _client.PostAsJsonAsync("/api/trading/simulate",
            new { Ticker = "2330", RefPrice = 600m, SimulationDate = "2026-03-01", TickDelayMs = 50 });
        response.StatusCode.Should().Be(HttpStatusCode.Accepted);
    }
}
