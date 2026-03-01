using FluentAssertions;
using QuantTrading.Core;
using QuantTrading.Core.Models;

namespace QuantTrading.E2E.Tests;

/// <summary>
/// E2E 測試 — 完整行情 → 策略引擎 → 風控 → 訊號 的端對端 pipeline 測試
/// </summary>
public class TradingPipelineE2ETests
{
    private readonly DateTime _baseDate = new(2026, 2, 28);

    // ═══════════════════════════════════════════════════════════════
    //  Scenario 1: 完整 Strategy A 試搓流程
    // ═══════════════════════════════════════════════════════════════

    [Fact]
    public async Task FullDay_StrategyA_StrongGap_Should_Produce_MarketBuy_Signal()
    {
        // Arrange — 建立完整系統
        var riskManager = new RiskManager();
        var engine = new StrategyEngine(riskManager);
        engine.SetReferencePrice("2330", 600m);

        var signals = new List<SignalContext>();
        var rejections = new List<RejectedSignal>();
        engine.OnSignalGenerated += s => signals.Add(s);
        engine.OnSignalRejected += r => rejections.Add(r);

        // Act — 模擬完整試搓期間
        // 08:30 ~ 08:59 持續送入強勢報價
        for (int minute = 30; minute <= 59; minute++)
        {
            decimal price = 610m + (minute - 30) * 0.1m; // 緩步上升
            var tick = new TickData("2330", price, 100, _baseDate.Add(new TimeSpan(8, minute, 0)));
            await engine.ProcessTickAsync(tick);
        }

        // 08:59:55 判定 tick
        var decisionTick = new TickData("2330", 613m, 100, _baseDate.Add(new TimeSpan(8, 59, 55)));
        await engine.ProcessTickAsync(decisionTick);

        // Assert
        signals.Should().ContainSingle();
        signals[0].Strategy.Should().Be(StrategyType.OpenGap);
        signals[0].OrderType.Should().Be(OrderType.MarketBuy);
        signals[0].Ticker.Should().Be("2330");
        signals[0].EntryPrice.Should().Be(613m);
        signals[0].PositionSize.Should().BeGreaterThan(0);
        rejections.Should().BeEmpty();

        // 風控驗證
        riskManager.DailyTradeCount.Should().Be(1);
    }

    // ═══════════════════════════════════════════════════════════════
    //  Scenario 2: 完整 Strategy B 盤中低接流程
    // ═══════════════════════════════════════════════════════════════

    [Fact]
    public async Task FullDay_StrategyB_DipVolumeReversal_Should_Produce_LimitBuy_Signal()
    {
        // Arrange
        var riskManager = new RiskManager();
        var engine = new StrategyEngine(riskManager);

        var signals = new List<SignalContext>();
        engine.OnSignalGenerated += s => signals.Add(s);

        string ticker = "2330";

        // 建立 VWAP 基準 (6 根正常 K 棒, close ≈ 600)
        for (int i = 0; i < 6; i++)
        {
            var bar = new BarData(ticker, 599m, 602m, 598m, 600m, 500,
                _baseDate.Add(new TimeSpan(9, 1 + i, 0)));
            await engine.ProcessBarAsync(bar);
        }

        // 注入一根爆量 K 棒 (volume=2000, avg=500 → ratio=4.0)
        var spikeBar = new BarData(ticker, 590m, 592m, 580m, 582m, 2000,
            _baseDate.Add(new TimeSpan(9, 7, 0)));
        await engine.ProcessBarAsync(spikeBar);

        // VWAP ≈ (600×500×6 + 584.67×2000)/(3000+2000) ≈ 593.87
        // Dip threshold = 593.87 × 0.98 ≈ 582.0
        // Tick 1: Dip 確認 (575 < 582)
        await engine.ProcessTickAsync(
            new TickData(ticker, 575m, 200, _baseDate.Add(new TimeSpan(9, 7, 30))));

        // Tick 2: 止跌反彈確認 (576 > 575)
        await engine.ProcessTickAsync(
            new TickData(ticker, 576m, 150, _baseDate.Add(new TimeSpan(9, 7, 31))));

        // Assert
        signals.Should().ContainSingle();
        signals[0].Strategy.Should().Be(StrategyType.IntradayDip);
        signals[0].OrderType.Should().Be(OrderType.LimitBuy);
        signals[0].VolumeRatio.Should().BeGreaterThanOrEqualTo(3.0);
        signals[0].PositionSize.Should().BeGreaterThan(0);
    }

    // ═══════════════════════════════════════════════════════════════
    //  Scenario 3: Strategy A + B 混合 → 風控限制 5 筆
    // ═══════════════════════════════════════════════════════════════

    [Fact]
    public async Task Mixed_Strategies_Should_Respect_MaxDailyTrades()
    {
        var riskManager = new RiskManager(new RiskConfig { MaxDailyTrades = 2 });
        var engine = new StrategyEngine(riskManager);
        engine.SetReferencePrice("2330", 600m);
        engine.SetReferencePrice("2317", 100m);

        var signals = new List<SignalContext>();
        var rejections = new List<RejectedSignal>();
        engine.OnSignalGenerated += s => signals.Add(s);
        engine.OnSignalRejected += r => rejections.Add(r);

        // Strategy A: ticker 2330 — 觸發
        await engine.ProcessTickAsync(new TickData("2330", 610m, 100, _baseDate.Add(new TimeSpan(8, 59, 55))));

        // Strategy A: ticker 2317 — 觸發 (第 2 筆)
        await engine.ProcessTickAsync(new TickData("2317", 103m, 100, _baseDate.Add(new TimeSpan(8, 59, 56))));

        signals.Should().HaveCount(2);
        riskManager.DailyTradeCount.Should().Be(2);

        // 接下來的任何訊號都應該被拒絕
        // 建一個 Strategy B 場景
        for (int i = 0; i < 6; i++)
        {
            await engine.ProcessBarAsync(new BarData("2330", 600m, 602m, 598m, 600m, 500,
                _baseDate.Add(new TimeSpan(9, 1 + i, 0))));
        }
        await engine.ProcessBarAsync(new BarData("2330", 580m, 582m, 578m, 580m, 2000,
            _baseDate.Add(new TimeSpan(9, 7, 0))));

        // VWAP ≈ (600×500×6 + 580×2000)/5000 ≈ 592, threshold ≈ 580.16
        await engine.ProcessTickAsync(new TickData("2330", 575m, 200, _baseDate.Add(new TimeSpan(9, 7, 30))));
        await engine.ProcessTickAsync(new TickData("2330", 576m, 150, _baseDate.Add(new TimeSpan(9, 7, 31))));

        // 第 3 筆被拒絕
        signals.Should().HaveCount(2);
        rejections.Should().HaveCountGreaterThan(0);
    }

    // ═══════════════════════════════════════════════════════════════
    //  Scenario 4: 每日重置後應能再次交易
    // ═══════════════════════════════════════════════════════════════

    [Fact]
    public async Task ResetDaily_Should_Allow_Trading_Again()
    {
        var riskManager = new RiskManager(new RiskConfig { MaxDailyTrades = 1 });
        var engine = new StrategyEngine(riskManager);
        engine.SetReferencePrice("2330", 600m);

        var signals = new List<SignalContext>();
        engine.OnSignalGenerated += s => signals.Add(s);

        // Day 1: 消耗配額
        await engine.ProcessTickAsync(new TickData("2330", 610m, 100, _baseDate.Add(new TimeSpan(8, 59, 55))));
        signals.Should().ContainSingle();

        // Reset
        riskManager.ResetDaily();
        riskManager.DailyTradeCount.Should().Be(0);

        // Day 2: 需要新的 engine 實例 (或至少新的 state) — 這裡用新的 engine
        var engine2 = new StrategyEngine(riskManager);
        engine2.SetReferencePrice("2330", 600m);
        engine2.OnSignalGenerated += s => signals.Add(s);

        await engine2.ProcessTickAsync(new TickData("2330", 612m, 100,
            _baseDate.AddDays(1).Add(new TimeSpan(8, 59, 55))));

        signals.Should().HaveCount(2);
    }

    // ═══════════════════════════════════════════════════════════════
    //  Scenario 5: 累計虧損達上限 → 全面停止
    // ═══════════════════════════════════════════════════════════════

    [Fact]
    public async Task DailyLoss_Exceeding_Max_Should_Block_All_Signals()
    {
        var riskManager = new RiskManager(new RiskConfig { MaxDailyLoss = 2000m });
        var engine = new StrategyEngine(riskManager);
        engine.SetReferencePrice("2330", 600m);

        var signals = new List<SignalContext>();
        var rejections = new List<RejectedSignal>();
        engine.OnSignalGenerated += s => signals.Add(s);
        engine.OnSignalRejected += r => rejections.Add(r);

        // 第一筆正常通過
        await engine.ProcessTickAsync(new TickData("2330", 610m, 100, _baseDate.Add(new TimeSpan(8, 59, 55))));
        signals.Should().ContainSingle();

        // 回報 2500 TWD 虧損 (> 2000 上限)
        riskManager.RecordRealizedLoss(2500m);

        // 用新 engine (模擬另一檔) 嘗試交易
        var engine2 = new StrategyEngine(riskManager);
        engine2.SetReferencePrice("2317", 100m);
        engine2.OnSignalGenerated += s => signals.Add(s);
        engine2.OnSignalRejected += r => rejections.Add(r);

        await engine2.ProcessTickAsync(new TickData("2317", 103m, 100, _baseDate.Add(new TimeSpan(8, 59, 56))));

        // 應被拒絕
        signals.Should().ContainSingle(); // 仍然只有 1 筆
        rejections.Should().HaveCountGreaterThan(0);
        riskManager.GetCurrentStatus().Should().Be(SignalResult.RejectDailyLoss);
    }
}
