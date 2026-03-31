using FluentAssertions;
using QuantTrading.Core.Models;
using QuantTrading.Core.Services;
using QuantTrading.Infrastructure.Configuration;

namespace QuantTrading.E2E.Tests;

/// <summary>
/// E2E 測試 — Performance Analytics 端對端驗證。
/// 測試 TradeStatisticsService 的 P/L 計算、勝率、回撤等指標。
/// </summary>
public class PerformanceAnalyticsE2ETests : IDisposable
{
    private readonly string _tempDir;

    public PerformanceAnalyticsE2ETests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"e2e-analytics-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
            Directory.Delete(_tempDir, true);
    }

    private JsonTradeJournalStore CreateStore() =>
        new(Path.Combine(_tempDir, "analytics-journal.json"));

    // ═══════════════════════════════════════════════════════════════
    //  Scenario 1: 無交易紀錄 → 空績效
    // ═══════════════════════════════════════════════════════════════

    [Fact]
    public async Task Empty_Journal_Returns_Empty_Summary()
    {
        var store = CreateStore();
        var service = new TradeStatisticsService(store);

        var summary = await service.GetPerformanceSummaryAsync();

        summary.TotalTrades.Should().Be(0);
        summary.CompletedPairs.Should().Be(0);
        summary.WinRate.Should().Be(0);
        summary.TotalPnL.Should().Be(0);
        summary.MaxDrawdown.Should().Be(0);
    }

    // ═══════════════════════════════════════════════════════════════
    //  Scenario 2: 完整買賣配對 → 計算正確 P/L
    // ═══════════════════════════════════════════════════════════════

    [Fact]
    public async Task Single_BuySell_Pair_Calculates_Correct_PnL()
    {
        var store = CreateStore();
        var service = new TradeStatisticsService(store);
        var today = new DateTime(2026, 3, 1);

        await store.AddAsync(new TradeRecord
        {
            TradeDate = today,
            Ticker = "2330",
            Direction = TradeDirection.Buy,
            Price = 600m,
            Quantity = 1000
        });

        await store.AddAsync(new TradeRecord
        {
            TradeDate = today,
            Ticker = "2330",
            Direction = TradeDirection.Sell,
            Price = 610m,
            Quantity = 1000
        });

        var summary = await service.GetPerformanceSummaryAsync();

        summary.TotalTrades.Should().Be(2);
        summary.CompletedPairs.Should().Be(1);
        summary.WinCount.Should().Be(1);
        summary.LossCount.Should().Be(0);
        summary.WinRate.Should().Be(100);
        summary.TotalPnL.Should().Be(10_000m); // (610 - 600) * 1000
        summary.BuyTotal.Should().Be(600_000m);
        summary.SellTotal.Should().Be(610_000m);
    }

    // ═══════════════════════════════════════════════════════════════
    //  Scenario 3: 多筆交易混合盈虧 → 正確勝率與損益
    // ═══════════════════════════════════════════════════════════════

    [Fact]
    public async Task Mixed_WinLoss_Calculates_Correct_WinRate_And_ProfitFactor()
    {
        var store = CreateStore();
        var service = new TradeStatisticsService(store);
        var today = new DateTime(2026, 3, 1);

        // Trade 1: Win — Buy 600, Sell 615 (P/L = +15,000)
        await store.AddAsync(new TradeRecord { TradeDate = today, Ticker = "2330", Direction = TradeDirection.Buy, Price = 600m, Quantity = 1000 });
        await store.AddAsync(new TradeRecord { TradeDate = today, Ticker = "2330", Direction = TradeDirection.Sell, Price = 615m, Quantity = 1000 });

        // Trade 2: Loss — Buy 950, Sell 940 (P/L = -5,000)
        await store.AddAsync(new TradeRecord { TradeDate = today, Ticker = "2454", Direction = TradeDirection.Buy, Price = 950m, Quantity = 500 });
        await store.AddAsync(new TradeRecord { TradeDate = today, Ticker = "2454", Direction = TradeDirection.Sell, Price = 940m, Quantity = 500 });

        // Trade 3: Win — Buy 100, Sell 108 (P/L = +16,000)
        await store.AddAsync(new TradeRecord { TradeDate = today, Ticker = "2317", Direction = TradeDirection.Buy, Price = 100m, Quantity = 2000 });
        await store.AddAsync(new TradeRecord { TradeDate = today, Ticker = "2317", Direction = TradeDirection.Sell, Price = 108m, Quantity = 2000 });

        var summary = await service.GetPerformanceSummaryAsync();

        summary.TotalTrades.Should().Be(6);
        summary.CompletedPairs.Should().Be(3);
        summary.WinCount.Should().Be(2);
        summary.LossCount.Should().Be(1);
        summary.WinRate.Should().BeApproximately(66.7m, 0.1m);
        summary.TotalPnL.Should().Be(26_000m); // 15000 - 5000 + 16000
        summary.ProfitFactor.Should().BeGreaterThan(1); // avgWin/avgLoss > 1
    }

    // ═══════════════════════════════════════════════════════════════
    //  Scenario 4: 最大回撤計算
    // ═══════════════════════════════════════════════════════════════

    [Fact]
    public async Task Drawdown_Calculated_Correctly()
    {
        var store = CreateStore();
        var service = new TradeStatisticsService(store);
        var d1 = new DateTime(2026, 3, 1);
        var d2 = new DateTime(2026, 3, 2);
        var d3 = new DateTime(2026, 3, 3);

        // Day 1: Win +10,000
        await store.AddAsync(new TradeRecord { TradeDate = d1, Ticker = "2330", Direction = TradeDirection.Buy, Price = 600m, Quantity = 1000 });
        await store.AddAsync(new TradeRecord { TradeDate = d1, Ticker = "2330", Direction = TradeDirection.Sell, Price = 610m, Quantity = 1000 });

        // Day 2: Loss -8,000
        await store.AddAsync(new TradeRecord { TradeDate = d2, Ticker = "2330", Direction = TradeDirection.Buy, Price = 610m, Quantity = 1000 });
        await store.AddAsync(new TradeRecord { TradeDate = d2, Ticker = "2330", Direction = TradeDirection.Sell, Price = 602m, Quantity = 1000 });

        // Day 3: Loss -5,000
        await store.AddAsync(new TradeRecord { TradeDate = d3, Ticker = "2330", Direction = TradeDirection.Buy, Price = 605m, Quantity = 1000 });
        await store.AddAsync(new TradeRecord { TradeDate = d3, Ticker = "2330", Direction = TradeDirection.Sell, Price = 600m, Quantity = 1000 });

        var summary = await service.GetPerformanceSummaryAsync();

        // Cumulative: +10000, +2000, -3000
        // Peak = 10000, MDD = 10000 - (-3000) = 13000
        summary.MaxDrawdown.Should().Be(13_000m);
    }

    // ═══════════════════════════════════════════════════════════════
    //  Scenario 5: 日期範圍過濾
    // ═══════════════════════════════════════════════════════════════

    [Fact]
    public async Task Date_Filter_Returns_Only_Matching_Period()
    {
        var store = CreateStore();
        var service = new TradeStatisticsService(store);

        // March trades
        await store.AddAsync(new TradeRecord { TradeDate = new DateTime(2026, 3, 1), Ticker = "2330", Direction = TradeDirection.Buy, Price = 600m, Quantity = 1000 });
        await store.AddAsync(new TradeRecord { TradeDate = new DateTime(2026, 3, 1), Ticker = "2330", Direction = TradeDirection.Sell, Price = 610m, Quantity = 1000 });

        // February trades
        await store.AddAsync(new TradeRecord { TradeDate = new DateTime(2026, 2, 15), Ticker = "2317", Direction = TradeDirection.Buy, Price = 100m, Quantity = 2000 });
        await store.AddAsync(new TradeRecord { TradeDate = new DateTime(2026, 2, 15), Ticker = "2317", Direction = TradeDirection.Sell, Price = 95m, Quantity = 2000 });

        // Filter: March only
        var marchSummary = await service.GetPerformanceSummaryAsync(new DateTime(2026, 3, 1), new DateTime(2026, 3, 31));
        marchSummary.TotalTrades.Should().Be(2);
        marchSummary.TotalPnL.Should().Be(10_000m);

        // Filter: All time
        var allSummary = await service.GetPerformanceSummaryAsync();
        allSummary.TotalTrades.Should().Be(4);
    }

    // ═══════════════════════════════════════════════════════════════
    //  Scenario 6: 策略分布統計
    // ═══════════════════════════════════════════════════════════════

    [Fact]
    public async Task Strategy_Breakdown_Counts_Correctly()
    {
        var store = CreateStore();
        var service = new TradeStatisticsService(store);
        var today = new DateTime(2026, 3, 1);

        await store.AddAsync(new TradeRecord { TradeDate = today, Ticker = "2330", Direction = TradeDirection.Buy, Price = 600m, Quantity = 1000, Strategy = "Strategy A" });
        await store.AddAsync(new TradeRecord { TradeDate = today, Ticker = "2330", Direction = TradeDirection.Sell, Price = 610m, Quantity = 1000, Strategy = "Strategy A" });
        await store.AddAsync(new TradeRecord { TradeDate = today, Ticker = "2317", Direction = TradeDirection.Buy, Price = 100m, Quantity = 2000, Strategy = "Strategy B" });
        await store.AddAsync(new TradeRecord { TradeDate = today, Ticker = "2454", Direction = TradeDirection.Buy, Price = 950m, Quantity = 500, Strategy = null });

        var summary = await service.GetPerformanceSummaryAsync();

        summary.StrategyBreakdown.Should().ContainKey("Strategy A").WhoseValue.Should().Be(2);
        summary.StrategyBreakdown.Should().ContainKey("Strategy B").WhoseValue.Should().Be(1);
        summary.StrategyBreakdown.Should().ContainKey("Untagged").WhoseValue.Should().Be(1);
    }
}
