using FluentAssertions;
using QuantTrading.Core.Services;

namespace QuantTrading.E2E.Tests;

/// <summary>
/// E2E 測試 — System Event Log 端對端驗證。
/// 測試事件紀錄、過濾、容量限制等功能。
/// </summary>
public class SystemEventLogE2ETests
{
    // ═══════════════════════════════════════════════════════════════
    //  Scenario 1: 基本事件紀錄與讀取
    // ═══════════════════════════════════════════════════════════════

    [Fact]
    public void Log_Events_And_Retrieve_Successfully()
    {
        var eventLog = new SystemEventLogService();

        eventLog.LogInfo("System", "Application started");
        eventLog.LogSignal("OpenGap MarketBuy 2330 @613.00");
        eventLog.LogRisk("Daily trade limit reached (5/5)");
        eventLog.LogError("System", "Connection lost to Fugle WebSocket");

        var events = eventLog.GetEvents(10);

        events.Should().HaveCount(4);
        eventLog.TotalCount.Should().Be(4);
    }

    // ═══════════════════════════════════════════════════════════════
    //  Scenario 2: Level 過濾
    // ═══════════════════════════════════════════════════════════════

    [Fact]
    public void Filter_By_Level_Returns_Correct_Events()
    {
        var eventLog = new SystemEventLogService();

        eventLog.LogInfo("System", "Normal event");
        eventLog.LogWarning("Risk", "High risk detected");
        eventLog.LogError("System", "Fatal error");
        eventLog.LogInfo("Signal", "Signal generated");

        var warnings = eventLog.GetEvents(10, SystemEventLevel.Warning);
        warnings.Should().ContainSingle();
        warnings[0].Message.Should().Contain("High risk");

        var errors = eventLog.GetEvents(10, SystemEventLevel.Error);
        errors.Should().ContainSingle();
    }

    // ═══════════════════════════════════════════════════════════════
    //  Scenario 3: Category 過濾
    // ═══════════════════════════════════════════════════════════════

    [Fact]
    public void Filter_By_Category_Returns_Correct_Events()
    {
        var eventLog = new SystemEventLogService();

        eventLog.LogSignal("Buy signal 2330");
        eventLog.LogSignal("Sell signal 2317");
        eventLog.LogRisk("Max trades reached");
        eventLog.LogSystem("Simulation started");

        var signalEvents = eventLog.GetEvents(10, categoryFilter: "Signal");
        signalEvents.Should().HaveCount(2);

        var riskEvents = eventLog.GetEvents(10, categoryFilter: "Risk");
        riskEvents.Should().ContainSingle();
    }

    // ═══════════════════════════════════════════════════════════════
    //  Scenario 4: 容量限制 (不超過 MaxEvents)
    // ═══════════════════════════════════════════════════════════════

    [Fact]
    public void Events_Are_Capped_At_Max_Size()
    {
        var eventLog = new SystemEventLogService();

        // Log more than the max
        for (int i = 0; i < 2100; i++)
        {
            eventLog.LogInfo("Test", $"Event {i}");
        }

        eventLog.TotalCount.Should().BeLessThanOrEqualTo(2000);
    }

    // ═══════════════════════════════════════════════════════════════
    //  Scenario 5: Clear 清除所有事件
    // ═══════════════════════════════════════════════════════════════

    [Fact]
    public void Clear_Removes_All_Events()
    {
        var eventLog = new SystemEventLogService();

        eventLog.LogInfo("System", "Event 1");
        eventLog.LogWarning("Risk", "Event 2");
        eventLog.TotalCount.Should().Be(2);

        eventLog.Clear();
        eventLog.TotalCount.Should().Be(0);
        eventLog.GetEvents(10).Should().BeEmpty();
    }

    // ═══════════════════════════════════════════════════════════════
    //  Scenario 6: 組合過濾 (Level + Category)
    // ═══════════════════════════════════════════════════════════════

    [Fact]
    public void Combined_Filter_Level_And_Category()
    {
        var eventLog = new SystemEventLogService();

        eventLog.LogInfo("Signal", "Normal signal");
        eventLog.LogWarning("Signal", "Weak signal detected");
        eventLog.LogWarning("Risk", "Position too large");
        eventLog.LogError("Signal", "Signal processing error");

        var warningSignals = eventLog.GetEvents(10, SystemEventLevel.Warning, "Signal");
        warningSignals.Should().ContainSingle();
        warningSignals[0].Message.Should().Contain("Weak signal");
    }

    // ═══════════════════════════════════════════════════════════════
    //  Scenario 7: 事件回傳順序 (最新優先)
    // ═══════════════════════════════════════════════════════════════

    [Fact]
    public void Events_Returned_In_Reverse_Chronological_Order()
    {
        var eventLog = new SystemEventLogService();

        eventLog.LogInfo("System", "First");
        eventLog.LogInfo("System", "Second");
        eventLog.LogInfo("System", "Third");

        var events = eventLog.GetEvents(10);

        events[0].Message.Should().Be("Third");
        events[1].Message.Should().Be("Second");
        events[2].Message.Should().Be("First");
    }
}
