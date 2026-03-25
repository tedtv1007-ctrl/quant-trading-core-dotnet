using FluentAssertions;
using QuantTrading.Core;
using QuantTrading.Core.Models;

namespace QuantTrading.Core.Tests;

public class LiquidityRiskEvaluatorTests
{
    [Fact]
    public void Should_Trigger_MarketSell_When_Within_2_Percent_Of_LimitDown_And_Long()
    {
        // Arrange
        var evaluator = new LiquidityRiskEvaluator();
        decimal refPrice = 100m; // Limit down = 90
        
        // 90 * 1.02 = 91.8
        var tick = new TickData("2330", 91.5m, 100, DateTime.UtcNow);

        // Act
        bool result = evaluator.TryEvaluateEmergencySell(tick, refPrice, hasLongPosition: true, out var signal);

        // Assert
        result.Should().BeTrue();
        signal.Should().NotBeNull();
        signal!.OrderType.Should().Be(OrderType.MarketSell);
        evaluator.IsSuspended("2330").Should().BeTrue();
    }

    [Fact]
    public void Should_Suspend_But_Not_Sell_If_No_Long_Position()
    {
        // Arrange
        var evaluator = new LiquidityRiskEvaluator();
        decimal refPrice = 100m;
        var tick = new TickData("2330", 91m, 100, DateTime.UtcNow);

        // Act
        bool result = evaluator.TryEvaluateEmergencySell(tick, refPrice, hasLongPosition: false, out var signal);

        // Assert
        result.Should().BeFalse();
        signal.Should().BeNull();
        evaluator.IsSuspended("2330").Should().BeTrue(); // 仍然要暫停進場
    }

    [Fact]
    public void Should_Not_Trigger_When_Above_Threshold()
    {
        // Arrange
        var evaluator = new LiquidityRiskEvaluator();
        decimal refPrice = 100m;
        var tick = new TickData("2330", 92m, 100, DateTime.UtcNow); // Limit down=90, threshold=91.8

        // Act
        bool result = evaluator.TryEvaluateEmergencySell(tick, refPrice, hasLongPosition: true, out var signal);

        // Assert
        result.Should().BeFalse();
        signal.Should().BeNull();
        evaluator.IsSuspended("2330").Should().BeFalse();
    }
}
