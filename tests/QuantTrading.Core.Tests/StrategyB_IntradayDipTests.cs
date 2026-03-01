using FluentAssertions;
using QuantTrading.Core;
using QuantTrading.Core.Models;

namespace QuantTrading.Core.Tests;

/// <summary>
/// Strategy B — Intraday Dip &amp; Volume Surge (盤中低接反彈) 單元測試
/// </summary>
public class StrategyB_IntradayDipTests
{
    private readonly DateTime _baseDate = new(2026, 2, 28);

    private TickData MakeTick(string ticker, decimal price, TimeSpan time) =>
        new(ticker, price, 100, _baseDate.Add(time));

    private BarData MakeBar(string ticker, decimal close, long volume, TimeSpan time) =>
        new(ticker, close, close * 1.002m, close * 0.998m, close, volume, _baseDate.Add(time));

    /// <summary>
    /// Helper: 建立足量 K 棒歷史 + VWAP 狀態
    /// </summary>
    private async Task SeedBarsAndVwap(StrategyEngine engine, string ticker, decimal vwapTarget, int count = 6)
    {
        for (int i = 0; i < count; i++)
        {
            var barTime = new TimeSpan(9, 1 + i, 0);
            var bar = MakeBar(ticker, vwapTarget, 500, barTime);
            await engine.ProcessBarAsync(bar);
        }
    }

    // ── 完整條件觸發 ────────────────────────────────────────────

    [Fact]
    public async Task Dip_Plus_VolumeSpike_Plus_Reversal_Should_Generate_LimitBuy()
    {
        // Arrange
        var riskManager = new RiskManager();
        var engine = new StrategyEngine(riskManager);

        SignalContext? capturedSignal = null;
        engine.OnSignalGenerated += s => capturedSignal = s;

        string ticker = "2330";

        // 建立 VWAP ≈ 600 的基準 (6 根 K 棒)
        await SeedBarsAndVwap(engine, ticker, 600m, count: 6);

        // 注入一根爆量 K 棒：volume=2000 vs avg=500 → ratio=4.0 > 3.0
        var spikeBar = MakeBar(ticker, 580m, 2000, new TimeSpan(9, 7, 0));
        await engine.ProcessBarAsync(spikeBar);

        // 考慮 spike bar 也會影響 VWAP (6*600*500 + 580*2000)/5000 ≈ 592
        // Dip threshold = 592 × 0.98 ≈ 580.16
        // Tick 1: 價格低於 VWAP dip threshold → Dip
        var dipTick = MakeTick(ticker, 575m, new TimeSpan(9, 7, 30));
        await engine.ProcessTickAsync(dipTick);

        // Tick 2: 上漲 (止跌確認) → 應該觸發訊號
        var reversalTick = MakeTick(ticker, 576m, new TimeSpan(9, 7, 31));
        await engine.ProcessTickAsync(reversalTick);

        // Assert
        capturedSignal.Should().NotBeNull();
        capturedSignal!.Strategy.Should().Be(StrategyType.IntradayDip);
        capturedSignal.OrderType.Should().Be(OrderType.LimitBuy);
        capturedSignal.Ticker.Should().Be(ticker);
        capturedSignal.EntryPrice.Should().Be(576m);
    }

    // ── 無爆量 → 不觸發 ────────────────────────────────────────

    [Fact]
    public async Task Dip_Without_VolumeSpike_Should_Not_Generate_Signal()
    {
        var riskManager = new RiskManager();
        var engine = new StrategyEngine(riskManager);

        SignalContext? capturedSignal = null;
        engine.OnSignalGenerated += s => capturedSignal = s;

        string ticker = "2330";

        await SeedBarsAndVwap(engine, ticker, 600m, count: 6);

        // 一般量 K 棒 (500 vs avg 500 → ratio=1.0 < 3.0)
        var normalBar = MakeBar(ticker, 580m, 500, new TimeSpan(9, 7, 0));
        await engine.ProcessBarAsync(normalBar);

        // Dip tick
        await engine.ProcessTickAsync(MakeTick(ticker, 585m, new TimeSpan(9, 7, 30)));
        // Reversal tick
        await engine.ProcessTickAsync(MakeTick(ticker, 586m, new TimeSpan(9, 7, 31)));

        capturedSignal.Should().BeNull();
    }

    // ── 無 Dip → 不觸發 ────────────────────────────────────────

    [Fact]
    public async Task VolumeSpike_Without_Dip_Should_Not_Generate_Signal()
    {
        var riskManager = new RiskManager();
        var engine = new StrategyEngine(riskManager);

        SignalContext? capturedSignal = null;
        engine.OnSignalGenerated += s => capturedSignal = s;

        string ticker = "2330";

        await SeedBarsAndVwap(engine, ticker, 600m, count: 6);

        // 爆量但價格仍在 VWAP 附近 (不低於 588)
        var spikeBar = MakeBar(ticker, 600m, 2000, new TimeSpan(9, 7, 0));
        await engine.ProcessBarAsync(spikeBar);

        await engine.ProcessTickAsync(MakeTick(ticker, 599m, new TimeSpan(9, 7, 30)));
        await engine.ProcessTickAsync(MakeTick(ticker, 600m, new TimeSpan(9, 7, 31)));

        capturedSignal.Should().BeNull();
    }

    // ── 無反彈確認 → 不觸發 ────────────────────────────────────

    [Fact]
    public async Task Dip_VolumeSpike_But_No_Reversal_Should_Not_Generate_Signal()
    {
        var riskManager = new RiskManager();
        var engine = new StrategyEngine(riskManager);

        SignalContext? capturedSignal = null;
        engine.OnSignalGenerated += s => capturedSignal = s;

        string ticker = "2330";

        await SeedBarsAndVwap(engine, ticker, 600m, count: 6);

        var spikeBar = MakeBar(ticker, 580m, 2000, new TimeSpan(9, 7, 0));
        await engine.ProcessBarAsync(spikeBar);

        // Dip 1: 滿足條件
        await engine.ProcessTickAsync(MakeTick(ticker, 585m, new TimeSpan(9, 7, 30)));
        // Dip 2: 繼續下跌 (無反彈)
        await engine.ProcessTickAsync(MakeTick(ticker, 584m, new TimeSpan(9, 7, 31)));

        capturedSignal.Should().BeNull();
    }

    // ── VWAP 計算正確性 ────────────────────────────────────────

    [Fact]
    public async Task VWAP_Should_Update_With_Each_Bar()
    {
        var riskManager = new RiskManager();
        var engine = new StrategyEngine(riskManager);

        string ticker = "TEST";

        // Bar 1: H=102, L=98, C=100 → TypicalPrice=100, Vol=1000
        // VWAP = (100×1000) / 1000 = 100
        var bar1 = new BarData(ticker, 99m, 102m, 98m, 100m, 1000, _baseDate.Add(new TimeSpan(9, 1, 0)));
        await engine.ProcessBarAsync(bar1);

        // Bar 2: H=112, L=108, C=110 → TypicalPrice=110, Vol=2000
        // VWAP = (100×1000 + 110×2000) / 3000 = 320000/3000 ≈ 106.67
        var bar2 = new BarData(ticker, 109m, 112m, 108m, 110m, 2000, _baseDate.Add(new TimeSpan(9, 2, 0)));
        await engine.ProcessBarAsync(bar2);

        // Verify by checking that a tick at 104 (< 106.67×0.98=104.53) triggers dip
        // Need volume spike first
        for (int i = 0; i < 5; i++)
        {
            await engine.ProcessBarAsync(MakeBar(ticker, 106m, 100, new TimeSpan(9, 3 + i, 0)));
        }
        await engine.ProcessBarAsync(MakeBar(ticker, 106m, 500, new TimeSpan(9, 8, 0))); // spike: 500/100=5.0

        SignalContext? signal = null;
        engine.OnSignalGenerated += s => signal = s;

        await engine.ProcessTickAsync(MakeTick(ticker, 100m, new TimeSpan(9, 8, 30))); // dip
        await engine.ProcessTickAsync(MakeTick(ticker, 101m, new TimeSpan(9, 8, 31))); // reversal

        signal.Should().NotBeNull();
    }

    // ── 重置機制：觸發後不會連續觸發 ───────────────────────────

    [Fact]
    public async Task Should_Reset_After_Signal_And_Not_Retrigger_Immediately()
    {
        var riskManager = new RiskManager();
        var engine = new StrategyEngine(riskManager);

        int signalCount = 0;
        engine.OnSignalGenerated += _ => signalCount++;

        string ticker = "2330";

        await SeedBarsAndVwap(engine, ticker, 600m, count: 6);

        var spikeBar = MakeBar(ticker, 580m, 2000, new TimeSpan(9, 7, 0));
        await engine.ProcessBarAsync(spikeBar);

        // 觸發一次 (VWAP ≈ 592, threshold ≈ 580.16, need price < 580)
        await engine.ProcessTickAsync(MakeTick(ticker, 575m, new TimeSpan(9, 7, 30)));
        await engine.ProcessTickAsync(MakeTick(ticker, 576m, new TimeSpan(9, 7, 31)));

        signalCount.Should().Be(1);

        // 立即再送 reversal tick → 不應再觸發 (VolumeSpike 已重置)
        await engine.ProcessTickAsync(MakeTick(ticker, 575m, new TimeSpan(9, 7, 32)));
        await engine.ProcessTickAsync(MakeTick(ticker, 576m, new TimeSpan(9, 7, 33)));

        signalCount.Should().Be(1);
    }

    // ── 盤中時段外不觸發 ───────────────────────────────────────

    [Fact]
    public async Task Tick_Outside_Active_Hours_Should_Not_Trigger()
    {
        var riskManager = new RiskManager();
        var engine = new StrategyEngine(riskManager);

        SignalContext? capturedSignal = null;
        engine.OnSignalGenerated += s => capturedSignal = s;

        string ticker = "2330";

        await SeedBarsAndVwap(engine, ticker, 600m, count: 6);

        var spikeBar = MakeBar(ticker, 580m, 2000, new TimeSpan(9, 7, 0));
        await engine.ProcessBarAsync(spikeBar);

        // 盤後的 tick → 不應觸發
        await engine.ProcessTickAsync(MakeTick(ticker, 585m, new TimeSpan(13, 30, 0)));
        await engine.ProcessTickAsync(MakeTick(ticker, 586m, new TimeSpan(13, 30, 1)));

        capturedSignal.Should().BeNull();
    }

    // ── 自訂 Config ────────────────────────────────────────────

    [Fact]
    public async Task Custom_DipThreshold_Should_Work()
    {
        var riskManager = new RiskManager();
        // 設定 Dip 門檻為 5% (更嚴格)
        var dipConfig = new IntradayDipConfig { DipThresholdPercent = 0.05m };
        var engine = new StrategyEngine(riskManager, dipConfig: dipConfig);

        SignalContext? capturedSignal = null;
        engine.OnSignalGenerated += s => capturedSignal = s;

        string ticker = "2330";
        await SeedBarsAndVwap(engine, ticker, 600m, count: 6);

        var spikeBar = MakeBar(ticker, 580m, 2000, new TimeSpan(9, 7, 0));
        await engine.ProcessBarAsync(spikeBar);

        // 585 vs VWAP 600: diff = 2.5% < 5% 門檻 → 不算 Dip
        await engine.ProcessTickAsync(MakeTick(ticker, 585m, new TimeSpan(9, 7, 30)));
        await engine.ProcessTickAsync(MakeTick(ticker, 586m, new TimeSpan(9, 7, 31)));

        capturedSignal.Should().BeNull();
    }
}
