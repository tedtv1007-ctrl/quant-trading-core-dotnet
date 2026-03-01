using FluentAssertions;
using QuantTrading.Core;
using QuantTrading.Core.Models;

namespace QuantTrading.Core.Tests;

/// <summary>
/// Strategy A — Pre-Market Gap (開盤試搓策略) 單元測試
/// </summary>
public class StrategyA_PreMarketGapTests
{
    private readonly DateTime _baseDate = new(2026, 2, 28);

    private TickData MakeTick(string ticker, decimal price, TimeSpan time) =>
        new(ticker, price, 100, _baseDate.Add(time));

    // ── 正常觸發 ────────────────────────────────────────────────

    [Fact]
    public async Task StrongGap_NoFakeout_Should_Generate_MarketBuy_Signal()
    {
        // Arrange
        var riskManager = new RiskManager();
        var engine = new StrategyEngine(riskManager);
        engine.SetReferencePrice("2330", 600m);

        SignalContext? capturedSignal = null;
        engine.OnSignalGenerated += s => capturedSignal = s;

        // Act: 在試搓期間送入價格 > 600 * 1.01 = 606
        await engine.ProcessTickAsync(MakeTick("2330", 610m, new TimeSpan(8, 30, 0)));
        await engine.ProcessTickAsync(MakeTick("2330", 611m, new TimeSpan(8, 45, 0)));
        await engine.ProcessTickAsync(MakeTick("2330", 610.5m, new TimeSpan(8, 55, 0)));
        // 判定時刻
        await engine.ProcessTickAsync(MakeTick("2330", 610m, new TimeSpan(8, 59, 55)));

        // Assert
        capturedSignal.Should().NotBeNull();
        capturedSignal!.Strategy.Should().Be(StrategyType.OpenGap);
        capturedSignal.OrderType.Should().Be(OrderType.MarketBuy);
        capturedSignal.Ticker.Should().Be("2330");
        capturedSignal.EntryPrice.Should().Be(610m);
    }

    // ── Gap 不足 → 不觸發 ──────────────────────────────────────

    [Fact]
    public async Task WeakGap_Should_Not_Generate_Signal()
    {
        var riskManager = new RiskManager();
        var engine = new StrategyEngine(riskManager);
        engine.SetReferencePrice("2330", 600m);

        SignalContext? capturedSignal = null;
        engine.OnSignalGenerated += s => capturedSignal = s;

        // 價格 = 603，漲幅 0.5% < 1% 門檻
        await engine.ProcessTickAsync(MakeTick("2330", 603m, new TimeSpan(8, 30, 0)));
        await engine.ProcessTickAsync(MakeTick("2330", 603m, new TimeSpan(8, 59, 55)));

        capturedSignal.Should().BeNull();
    }

    // ── Fakeout 偵測 → 不觸發 ──────────────────────────────────

    [Fact]
    public async Task Fakeout_Pullback_Should_Not_Generate_Signal()
    {
        var riskManager = new RiskManager();
        var engine = new StrategyEngine(riskManager);
        engine.SetReferencePrice("2330", 600m);

        SignalContext? capturedSignal = null;
        engine.OnSignalGenerated += s => capturedSignal = s;

        // 先衝高到 615 (>1%)
        await engine.ProcessTickAsync(MakeTick("2330", 615m, new TimeSpan(8, 30, 0)));
        await engine.ProcessTickAsync(MakeTick("2330", 616m, new TimeSpan(8, 40, 0)));
        // 主力抽單回檔：616 → 610，回檔 0.97% > 0.5% 門檻
        await engine.ProcessTickAsync(MakeTick("2330", 610m, new TimeSpan(8, 50, 0)));
        // 判定時刻的價格仍 > 1%，但有 fakeout
        await engine.ProcessTickAsync(MakeTick("2330", 610m, new TimeSpan(8, 59, 55)));

        capturedSignal.Should().BeNull();
    }

    // ── 未設定 RefPrice → 不觸發 ───────────────────────────────

    [Fact]
    public async Task No_RefPrice_Should_Not_Generate_Signal()
    {
        var riskManager = new RiskManager();
        var engine = new StrategyEngine(riskManager);
        // 不呼叫 SetReferencePrice

        SignalContext? capturedSignal = null;
        engine.OnSignalGenerated += s => capturedSignal = s;

        await engine.ProcessTickAsync(MakeTick("2330", 610m, new TimeSpan(8, 59, 55)));

        capturedSignal.Should().BeNull();
    }

    // ── 只觸發一次 (SignalEmitted flag) ────────────────────────

    [Fact]
    public async Task Should_Emit_Signal_Only_Once_Per_Ticker()
    {
        var riskManager = new RiskManager();
        var engine = new StrategyEngine(riskManager);
        engine.SetReferencePrice("2330", 600m);

        int signalCount = 0;
        engine.OnSignalGenerated += _ => signalCount++;

        await engine.ProcessTickAsync(MakeTick("2330", 610m, new TimeSpan(8, 59, 55)));
        await engine.ProcessTickAsync(MakeTick("2330", 611m, new TimeSpan(8, 59, 56)));
        await engine.ProcessTickAsync(MakeTick("2330", 612m, new TimeSpan(8, 59, 57)));

        signalCount.Should().Be(1);
    }

    // ── 自訂 Config ────────────────────────────────────────────

    [Fact]
    public async Task Custom_GapStrength_Threshold_Should_Work()
    {
        var riskManager = new RiskManager();
        var config = new PreMarketGapConfig { GapStrengthPercent = 0.02m }; // 2% 門檻
        var engine = new StrategyEngine(riskManager, gapConfig: config);
        engine.SetReferencePrice("2330", 600m);

        SignalContext? capturedSignal = null;
        engine.OnSignalGenerated += s => capturedSignal = s;

        // 漲 1.5%: 609 < 612 (2% 門檻)
        await engine.ProcessTickAsync(MakeTick("2330", 609m, new TimeSpan(8, 59, 55)));

        capturedSignal.Should().BeNull();
    }

    // ── 風控拒絕 → OnSignalRejected ────────────────────────────

    [Fact]
    public async Task Risk_Rejection_Should_Fire_OnSignalRejected()
    {
        var riskManager = new RiskManager(new RiskConfig { MaxDailyTrades = 0 }); // 無配額
        var engine = new StrategyEngine(riskManager);
        engine.SetReferencePrice("2330", 600m);

        RejectedSignal? rejection = null;
        engine.OnSignalRejected += r => rejection = r;

        await engine.ProcessTickAsync(MakeTick("2330", 610m, new TimeSpan(8, 59, 55)));

        rejection.Should().NotBeNull();
        rejection!.Strategy.Should().Be(StrategyType.OpenGap);
    }
}
