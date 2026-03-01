using FluentAssertions;
using QuantTrading.Core;
using QuantTrading.Core.Models;

namespace QuantTrading.Core.Tests;

/// <summary>
/// RiskManager 單元測試
/// </summary>
public class RiskManagerTests
{
    private static TickData MakeTick(decimal price) =>
        new("2330", price, 100, DateTime.UtcNow);

    // ── 基本部位計算 ────────────────────────────────────────────

    [Fact]
    public void EvaluateSignal_Should_Calculate_PositionSize_Correctly()
    {
        // Arrange: RiskPerTrade=1100, Entry=600, StopLoss=594 → risk=6 → size=183
        var rm = new RiskManager();
        var tick = MakeTick(600m);

        // Act
        var (signal, result) = rm.EvaluateSignal(StrategyType.OpenGap, OrderType.MarketBuy, tick, 594m, 1.0);

        // Assert
        result.Should().Be(SignalResult.Accept);
        signal.Should().NotBeNull();
        signal!.PositionSize.Should().Be(183); // 1100/6 = 183.33 → floor = 183
        signal.EntryPrice.Should().Be(600m);
        signal.StopLossPrice.Should().Be(594m);
    }

    [Fact]
    public void EvaluateSignal_Should_Return_Null_When_StopLoss_Equals_Entry()
    {
        var rm = new RiskManager();
        var tick = MakeTick(600m);

        var (signal, result) = rm.EvaluateSignal(StrategyType.OpenGap, OrderType.MarketBuy, tick, 600m, 1.0);

        signal.Should().BeNull();
        result.Should().Be(SignalResult.RejectRisk);
    }

    // ── 交易次數上限 ────────────────────────────────────────────

    [Fact]
    public void EvaluateSignal_Should_Reject_After_MaxDailyTrades()
    {
        var rm = new RiskManager(new RiskConfig { MaxDailyTrades = 3 });

        // 消耗 3 次交易配額
        for (int i = 0; i < 3; i++)
        {
            rm.EvaluateSignal(StrategyType.OpenGap, OrderType.MarketBuy, MakeTick(600m), 594m, 1.0)
                .Signal.Should().NotBeNull();
        }

        // 第 4 次應該被拒絕
        var (rejected, reason) = rm.EvaluateSignal(StrategyType.OpenGap, OrderType.MarketBuy, MakeTick(600m), 594m, 1.0);
        rejected.Should().BeNull();
        reason.Should().Be(SignalResult.RejectMaxTrades);
        rm.DailyTradeCount.Should().Be(3);
    }

    [Fact]
    public void Default_MaxDailyTrades_Should_Be_5()
    {
        var rm = new RiskManager();

        for (int i = 0; i < 5; i++)
        {
            rm.EvaluateSignal(StrategyType.IntradayDip, OrderType.LimitBuy, MakeTick(100m), 99m, 3.0)
                .Signal.Should().NotBeNull();
        }

        rm.EvaluateSignal(StrategyType.IntradayDip, OrderType.LimitBuy, MakeTick(100m), 99m, 3.0)
            .Signal.Should().BeNull();
    }

    // ── 當日虧損上限 ────────────────────────────────────────────

    [Fact]
    public void EvaluateSignal_Should_Reject_When_DailyLoss_Exceeds_Max()
    {
        var rm = new RiskManager(new RiskConfig { MaxDailyLoss = 3000m });

        rm.RecordRealizedLoss(3000m);
        rm.DailyRealizedLoss.Should().Be(3000m);

        var (signal, result) = rm.EvaluateSignal(StrategyType.OpenGap, OrderType.MarketBuy, MakeTick(600m), 594m, 1.0);
        signal.Should().BeNull();
        result.Should().Be(SignalResult.RejectDailyLoss);
    }

    [Fact]
    public void RecordRealizedLoss_Should_Ignore_Negative_Values()
    {
        var rm = new RiskManager();
        rm.RecordRealizedLoss(-500m);
        rm.DailyRealizedLoss.Should().Be(0);
    }

    // ── 每日重置 ────────────────────────────────────────────────

    [Fact]
    public void ResetDaily_Should_Clear_TradeCount_And_Loss()
    {
        var rm = new RiskManager();

        rm.EvaluateSignal(StrategyType.OpenGap, OrderType.MarketBuy, MakeTick(600m), 594m, 1.0);
        rm.RecordRealizedLoss(1000m);

        rm.DailyTradeCount.Should().Be(1);
        rm.DailyRealizedLoss.Should().Be(1000m);

        rm.ResetDaily();

        rm.DailyTradeCount.Should().Be(0);
        rm.DailyRealizedLoss.Should().Be(0);
    }

    // ── GetCurrentStatus ────────────────────────────────────────

    [Fact]
    public void GetCurrentStatus_Should_Return_Accept_Initially()
    {
        var rm = new RiskManager();
        rm.GetCurrentStatus().Should().Be(SignalResult.Accept);
    }

    [Fact]
    public void GetCurrentStatus_Should_Return_RejectMaxTrades_When_Full()
    {
        var rm = new RiskManager(new RiskConfig { MaxDailyTrades = 1 });
        rm.EvaluateSignal(StrategyType.OpenGap, OrderType.MarketBuy, MakeTick(600m), 594m, 1.0);

        rm.GetCurrentStatus().Should().Be(SignalResult.RejectMaxTrades);
    }

    [Fact]
    public void GetCurrentStatus_Should_Return_RejectDailyLoss_When_Exceeded()
    {
        var rm = new RiskManager(new RiskConfig { MaxDailyLoss = 1000m });
        rm.RecordRealizedLoss(1500m);

        rm.GetCurrentStatus().Should().Be(SignalResult.RejectDailyLoss);
    }

    // ── Signal 欄位正確性 ───────────────────────────────────────

    [Fact]
    public void EvaluateSignal_Should_Populate_All_Fields()
    {
        var rm = new RiskManager();
        var tick = new TickData("AAPL", 150m, 500, DateTime.UtcNow);

        var (signal, result) = rm.EvaluateSignal(StrategyType.IntradayDip, OrderType.LimitBuy, tick, 147m, 4.2);

        result.Should().Be(SignalResult.Accept);
        signal.Should().NotBeNull();
        signal!.Strategy.Should().Be(StrategyType.IntradayDip);
        signal.OrderType.Should().Be(OrderType.LimitBuy);
        signal.Ticker.Should().Be("AAPL");
        signal.EntryPrice.Should().Be(150m);
        signal.StopLossPrice.Should().Be(147m);
        signal.VolumeRatio.Should().Be(4.2);
        signal.PositionSize.Should().Be(366); // 1100/3 = 366.67 → floor = 366
    }

    // ── 新增硬化測試 (v17 Professional Hardening) ───────────

    [Fact]
    public void EvaluateSignal_Should_Reject_When_StopLoss_Above_Entry()
    {
        // Verify that we explicitly reject when StopLoss >= Entry (no Math.Abs masking)
        var rm = new RiskManager();
        var tick = MakeTick(100m);

        // Act: StopLoss at 105 (above entry at 100) should be rejected
        var (signal, result) = rm.EvaluateSignal(StrategyType.OpenGap, OrderType.MarketBuy, tick, 105m, 1.0);

        // Assert
        signal.Should().BeNull();
        result.Should().Be(SignalResult.RejectRisk);
    }

    [Fact]
    public void EvaluateSignal_Should_Cap_PositionSize_At_MaxLimit()
    {
        // Verify position size is capped at MaxPositionSize (999,000) when risk per share is very small
        var rm = new RiskManager();
        var tick = MakeTick(1000m);

        // Act: Entry=1000, StopLoss=999.999 → risk per share = 0.001 → would calculate > 999k
        var (signal, result) = rm.EvaluateSignal(StrategyType.OpenGap, OrderType.MarketBuy, tick, 999.999m, 1.0);

        // Assert
        result.Should().Be(SignalResult.Accept);
        signal.Should().NotBeNull();
        signal!.PositionSize.Should().BeLessThanOrEqualTo(999_000);
        signal.PositionSize.Should().BeGreaterThan(0);
    }
}
