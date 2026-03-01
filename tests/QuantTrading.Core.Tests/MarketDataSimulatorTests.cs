using FluentAssertions;
using QuantTrading.Core;
using QuantTrading.Core.Models;
using QuantTrading.Web.Services;

namespace QuantTrading.Core.Tests;

/// <summary>
/// MarketDataSimulator 單元測試
/// </summary>
public class MarketDataSimulatorTests
{
    private readonly DateTime _baseDate = new(2026, 2, 28);

    [Fact]
    public void GeneratePreMarketTicks_StrongGap_Should_Produce_Ticks_Above_1Percent()
    {
        var sim = new MarketDataSimulator();
        var ticks = sim.GeneratePreMarketTicks("2330", 600m, _baseDate, strongGap: true, fakeout: false);

        ticks.Should().NotBeEmpty();
        ticks.Should().AllSatisfy(t =>
        {
            t.Ticker.Should().Be("2330");
            t.Timestamp.Date.Should().Be(_baseDate.Date);
            t.Timestamp.TimeOfDay.Should().BeGreaterThanOrEqualTo(new TimeSpan(8, 30, 0));
            t.Timestamp.TimeOfDay.Should().BeLessThanOrEqualTo(new TimeSpan(9, 0, 0));
        });

        // 大部分 tick 應該 > 600 * 1.01 = 606 (strong gap)
        ticks.Count(t => t.Price > 606m).Should().BeGreaterThan(ticks.Count / 2);
    }

    [Fact]
    public void GeneratePreMarketTicks_WeakGap_Should_Produce_Ticks_Below_1Percent()
    {
        var sim = new MarketDataSimulator();
        var ticks = sim.GeneratePreMarketTicks("2330", 600m, _baseDate, strongGap: false, fakeout: false);

        ticks.Should().NotBeEmpty();
        // 弱勢：大部分 tick < 606
        ticks.Count(t => t.Price < 606m).Should().BeGreaterThan(ticks.Count / 2);
    }

    [Fact]
    public void GeneratePreMarketTicks_Fakeout_Should_Pull_Back_After_0855()
    {
        var sim = new MarketDataSimulator();
        var ticks = sim.GeneratePreMarketTicks("2330", 600m, _baseDate, strongGap: true, fakeout: true);

        var lateTicks = ticks.Where(t => t.Timestamp.TimeOfDay >= new TimeSpan(8, 55, 0)).ToList();
        lateTicks.Should().NotBeEmpty();
        // Fakeout 模式下晚期 tick 價格應回落到接近參考價
        lateTicks.Should().AllSatisfy(t => t.Price.Should().BeLessThan(601m));
    }

    [Fact]
    public void GenerateIntradayData_Should_Produce_Bars_And_Ticks()
    {
        var sim = new MarketDataSimulator();
        var (bars, ticks) = sim.GenerateIntradayData("2330", 610m, _baseDate, triggerDipSignal: true);

        bars.Should().HaveCount(20);
        ticks.Should().HaveCountGreaterThan(0);

        bars.Should().AllSatisfy(b =>
        {
            b.Ticker.Should().Be("2330");
            b.Volume.Should().BeGreaterThan(0);
        });

        // 應該有一根爆量 bar
        bars.Max(b => b.Volume).Should().BeGreaterThan(2000);
    }

    [Fact]
    public void GenerateFullDayScenario_Should_Produce_Complete_Scenario()
    {
        var sim = new MarketDataSimulator();
        var scenario = sim.GenerateFullDayScenario("2330", 600m, _baseDate);

        scenario.Ticker.Should().Be("2330");
        scenario.RefPrice.Should().Be(600m);
        scenario.PreMarketTicks.Should().NotBeEmpty();
        scenario.IntradayBars.Should().NotBeEmpty();
        scenario.IntradayTicks.Should().NotBeEmpty();
    }
}
