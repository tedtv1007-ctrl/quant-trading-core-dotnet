using FluentAssertions;
using QuantTrading.Core.Models;
using QuantTrading.Infrastructure.Configuration;

namespace QuantTrading.E2E.Tests;

/// <summary>
/// E2E 測試 — Trade Journal 完整端對端流程驗證。
/// 測試「新增 → 查詢 → 修改 → 刪除」全生命週期。
/// </summary>
public class TradeJournalE2ETests : IDisposable
{
    private readonly string _tempDir;

    public TradeJournalE2ETests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"e2e-journal-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
            Directory.Delete(_tempDir, true);
    }

    private JsonTradeJournalStore CreateStore() =>
        new(Path.Combine(_tempDir, "e2e-journal.json"));

    // ═══════════════════════════════════════════════════════════════
    //  Scenario 1: 完整一日交易記錄流程
    //  新增多筆 → 按日查詢 → 驗證摘要
    // ═══════════════════════════════════════════════════════════════

    [Fact]
    public async Task FullDay_Record_Workflow_Add_Query_Summarize()
    {
        var store = CreateStore();
        var today = new DateTime(2026, 3, 1);

        // 早盤: 買進 2330 (Strategy A — 盤前Gap)
        await store.AddAsync(new TradeRecord
        {
            TradeDate = today,
            Ticker = "2330",
            Direction = TradeDirection.Buy,
            Price = 612m,
            Quantity = 1000,
            Strategy = "Strategy A",
            Note = "盤前跳空買進"
        });

        // 盤中: 買進 2317 (Strategy B — 低接)
        await store.AddAsync(new TradeRecord
        {
            TradeDate = today,
            Ticker = "2317",
            Direction = TradeDirection.Buy,
            Price = 98m,
            Quantity = 3000,
            Strategy = "Strategy B",
            Note = "量能放大低接"
        });

        // 尾盤: 賣出 2330 (獲利了結)
        await store.AddAsync(new TradeRecord
        {
            TradeDate = today,
            Ticker = "2330",
            Direction = TradeDirection.Sell,
            Price = 620m,
            Quantity = 1000,
            Strategy = "Strategy A",
            Note = "漲幅達停利點"
        });

        // 查詢今日紀錄
        var todayRecords = await store.GetByDateAsync(today);

        todayRecords.Should().HaveCount(3);

        // 摘要統計
        var buyTotal = todayRecords
            .Where(r => r.Direction == TradeDirection.Buy)
            .Sum(r => r.Amount);
        var sellTotal = todayRecords
            .Where(r => r.Direction == TradeDirection.Sell)
            .Sum(r => r.Amount);

        buyTotal.Should().Be(612m * 1000 + 98m * 3000);    // 612,000 + 294,000 = 906,000
        sellTotal.Should().Be(620m * 1000);                  // 620,000
        (sellTotal - buyTotal).Should().Be(-286_000m);       // Net
    }

    // ═══════════════════════════════════════════════════════════════
    //  Scenario 2: 跨日紀錄 → 各日獨立查詢
    // ═══════════════════════════════════════════════════════════════

    [Fact]
    public async Task MultiDay_Records_Should_Filter_Correctly()
    {
        var store = CreateStore();
        var mon = new DateTime(2026, 3, 2);
        var tue = new DateTime(2026, 3, 3);
        var wed = new DateTime(2026, 3, 4);

        // Monday: 2 trades
        await store.AddAsync(new TradeRecord { TradeDate = mon, Ticker = "2330", Direction = TradeDirection.Buy, Price = 600m, Quantity = 1000 });
        await store.AddAsync(new TradeRecord { TradeDate = mon, Ticker = "2330", Direction = TradeDirection.Sell, Price = 610m, Quantity = 1000 });

        // Tuesday: 1 trade
        await store.AddAsync(new TradeRecord { TradeDate = tue, Ticker = "2454", Direction = TradeDirection.Buy, Price = 950m, Quantity = 500 });

        // Wednesday: 3 trades
        await store.AddAsync(new TradeRecord { TradeDate = wed, Ticker = "2317", Direction = TradeDirection.Buy, Price = 100m, Quantity = 2000 });
        await store.AddAsync(new TradeRecord { TradeDate = wed, Ticker = "3008", Direction = TradeDirection.Buy, Price = 220m, Quantity = 1000 });
        await store.AddAsync(new TradeRecord { TradeDate = wed, Ticker = "2317", Direction = TradeDirection.Sell, Price = 103m, Quantity = 2000 });

        // Verify each day
        (await store.GetByDateAsync(mon)).Should().HaveCount(2);
        (await store.GetByDateAsync(tue)).Should().HaveCount(1);
        (await store.GetByDateAsync(wed)).Should().HaveCount(3);

        // Total
        (await store.GetAllAsync()).Should().HaveCount(6);
    }

    // ═══════════════════════════════════════════════════════════════
    //  Scenario 3: 修改交易 → 驗證更新後金額
    // ═══════════════════════════════════════════════════════════════

    [Fact]
    public async Task Edit_Trade_Should_Update_Price_And_Amount()
    {
        var store = CreateStore();

        var record = new TradeRecord
        {
            TradeDate = new DateTime(2026, 3, 1),
            Ticker = "2330",
            Direction = TradeDirection.Buy,
            Price = 600m,  // 原始輸入
            Quantity = 1000
        };
        await store.AddAsync(record);

        // 發現輸入錯誤, 實際成交價 605
        record.Price = 605m;
        record.Note = "修正: 實際成交 605";
        await store.UpdateAsync(record);

        var updated = (await store.GetAllAsync()).Single();
        updated.Price.Should().Be(605m);
        updated.Amount.Should().Be(605_000m);
        updated.Note.Should().Contain("修正");
    }

    // ═══════════════════════════════════════════════════════════════
    //  Scenario 4: 刪除錯誤交易 → 驗證清除
    // ═══════════════════════════════════════════════════════════════

    [Fact]
    public async Task Delete_Trade_Should_Remove_From_Store()
    {
        var store = CreateStore();
        var today = new DateTime(2026, 3, 1);

        var r1 = new TradeRecord { TradeDate = today, Ticker = "2330", Direction = TradeDirection.Buy, Price = 600m, Quantity = 1000 };
        var r2 = new TradeRecord { TradeDate = today, Ticker = "2317", Direction = TradeDirection.Buy, Price = 100m, Quantity = 2000 };
        var r3 = new TradeRecord { TradeDate = today, Ticker = "WRONG", Direction = TradeDirection.Buy, Price = 999m, Quantity = 1 };

        await store.AddAsync(r1);
        await store.AddAsync(r2);
        await store.AddAsync(r3);

        // 刪除誤建紀錄
        await store.DeleteAsync(r3.Id);

        var remaining = await store.GetAllAsync();
        remaining.Should().HaveCount(2);
        remaining.Should().NotContain(r => r.Ticker == "WRONG");
    }

    // ═══════════════════════════════════════════════════════════════
    //  Scenario 5: 完整 CRUD 生命週期
    //  Create → Read → Update → Delete → 驗證空
    // ═══════════════════════════════════════════════════════════════

    [Fact]
    public async Task Full_CRUD_Lifecycle()
    {
        var store = CreateStore();

        // CREATE
        var record = new TradeRecord
        {
            TradeDate = new DateTime(2026, 3, 1),
            Ticker = "2330",
            Direction = TradeDirection.Buy,
            Price = 600m,
            Quantity = 1000,
            Strategy = "Strategy A"
        };
        await store.AddAsync(record);

        // READ
        var all = await store.GetAllAsync();
        all.Should().ContainSingle();
        all[0].Ticker.Should().Be("2330");

        // UPDATE
        record.Direction = TradeDirection.Sell;
        record.Price = 615m;
        record.Note = "改為賣出";
        await store.UpdateAsync(record);

        var updated = (await store.GetAllAsync()).Single();
        updated.Direction.Should().Be(TradeDirection.Sell);
        updated.Price.Should().Be(615m);

        // DELETE
        await store.DeleteAsync(record.Id);

        (await store.GetAllAsync()).Should().BeEmpty();
    }

    // ═══════════════════════════════════════════════════════════════
    //  Scenario 6: 大量交易紀錄 — 效能與正確性
    // ═══════════════════════════════════════════════════════════════

    [Fact]
    public async Task BulkRecords_Should_Persist_Correctly()
    {
        var store = CreateStore();
        const int totalDays = 5;
        const int tradesPerDay = 10;

        // 模擬 5 天每天 10 筆交易
        for (int day = 0; day < totalDays; day++)
        {
            var date = new DateTime(2026, 3, 1).AddDays(day);
            for (int t = 0; t < tradesPerDay; t++)
            {
                await store.AddAsync(new TradeRecord
                {
                    TradeDate = date,
                    Ticker = $"{2330 + t}",
                    Direction = t % 2 == 0 ? TradeDirection.Buy : TradeDirection.Sell,
                    Price = 500m + t * 10,
                    Quantity = 1000 + t * 100
                });
            }
        }

        var all = await store.GetAllAsync();
        all.Should().HaveCount(totalDays * tradesPerDay); // 50

        // 每天應有 10 筆
        for (int day = 0; day < totalDays; day++)
        {
            var date = new DateTime(2026, 3, 1).AddDays(day);
            (await store.GetByDateAsync(date)).Should().HaveCount(tradesPerDay);
        }
    }

    // ═══════════════════════════════════════════════════════════════
    //  Scenario 7: 持久化重啟驗證
    //  Store A 寫入 → 關閉 → Store B 讀取 → 修改 → Store C 驗證
    // ═══════════════════════════════════════════════════════════════

    [Fact]
    public async Task Persistence_Across_Multiple_Store_Instances()
    {
        var filePath = Path.Combine(_tempDir, "persist-e2e.json");

        // Phase 1: Write
        var storeA = new JsonTradeJournalStore(filePath);
        await storeA.AddAsync(new TradeRecord
        {
            TradeDate = new DateTime(2026, 3, 1),
            Ticker = "2330",
            Direction = TradeDirection.Buy,
            Price = 600m,
            Quantity = 1000
        });

        // Phase 2: Read + Modify from new instance
        var storeB = new JsonTradeJournalStore(filePath);
        var records = await storeB.GetAllAsync();
        records.Should().ContainSingle();

        records[0].Price = 605m;
        await storeB.UpdateAsync(records[0]);

        // Phase 3: Verify from yet another instance
        var storeC = new JsonTradeJournalStore(filePath);
        var final = await storeC.GetAllAsync();
        final.Should().ContainSingle();
        final[0].Price.Should().Be(605m);
    }

    // ═══════════════════════════════════════════════════════════════
    //  Scenario 8: Buy/Sell 方向正確性
    // ═══════════════════════════════════════════════════════════════

    [Fact]
    public async Task TradeDirection_Should_Persist_Correctly()
    {
        var store = CreateStore();
        var today = new DateTime(2026, 3, 1);

        await store.AddAsync(new TradeRecord { TradeDate = today, Ticker = "2330", Direction = TradeDirection.Buy, Price = 600m, Quantity = 1000 });
        await store.AddAsync(new TradeRecord { TradeDate = today, Ticker = "2330", Direction = TradeDirection.Sell, Price = 615m, Quantity = 1000 });

        var records = await store.GetByDateAsync(today);

        records.Should().HaveCount(2);
        records.Should().ContainSingle(r => r.Direction == TradeDirection.Buy);
        records.Should().ContainSingle(r => r.Direction == TradeDirection.Sell);

        // P&L 驗算: sell − buy = (615 − 600) × 1000 = +15,000
        var buyAmt = records.Where(r => r.Direction == TradeDirection.Buy).Sum(r => r.Amount);
        var sellAmt = records.Where(r => r.Direction == TradeDirection.Sell).Sum(r => r.Amount);
        (sellAmt - buyAmt).Should().Be(15_000m);
    }

    // ═══════════════════════════════════════════════════════════════
    //  Scenario 9: CSV Export E2E — 新增 → 匯出 → 驗證 CSV 內容
    // ═══════════════════════════════════════════════════════════════

    [Fact]
    public async Task CsvExport_E2E_Should_Produce_Valid_Csv_With_Correct_Content()
    {
        var store = CreateStore();

        await store.AddAsync(new TradeRecord
        {
            TradeDate = new DateTime(2026, 3, 1),
            Ticker = "2330",
            Direction = TradeDirection.Buy,
            Price = 600m,
            Quantity = 1000,
            Strategy = "Strategy A",
            Note = "test note"
        });
        await store.AddAsync(new TradeRecord
        {
            TradeDate = new DateTime(2026, 3, 2),
            Ticker = "2317",
            Direction = TradeDirection.Sell,
            Price = 103m,
            Quantity = 2000,
            Note = "note, with comma"
        });
        await store.AddAsync(new TradeRecord
        {
            TradeDate = new DateTime(2026, 2, 28),
            Ticker = "2454",
            Direction = TradeDirection.Buy,
            Price = 950m,
            Quantity = 500
        });

        // Export all
        var csvAll = await store.ExportCsvAsync();
        var allLines = csvAll.Trim().Split('\n');
        allLines.Should().HaveCount(4); // header + 3 records

        // First data line should be 2/28 (sorted by date ascending)
        allLines[1].Should().Contain("2454").And.Contain("2026-02-28");

        // Comma in note should be quoted
        csvAll.Should().Contain("\"note, with comma\"");

        // Export with date range filter
        var csvMarch = await store.ExportCsvAsync(
            fromDate: new DateTime(2026, 3, 1),
            toDate: new DateTime(2026, 3, 31));
        var marchLines = csvMarch.Trim().Split('\n');
        marchLines.Should().HaveCount(3); // header + 2 March records
        marchLines[1].Should().Contain("2330");
        marchLines[2].Should().Contain("2317");

        // Amount should be correct
        csvAll.Should().Contain("600000"); // 600 × 1000
        csvAll.Should().Contain("206000"); // 103 × 2000
    }
}
