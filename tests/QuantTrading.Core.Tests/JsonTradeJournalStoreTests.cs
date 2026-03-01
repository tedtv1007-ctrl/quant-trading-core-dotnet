using FluentAssertions;
using QuantTrading.Core.Models;
using QuantTrading.Infrastructure.Configuration;

namespace QuantTrading.Core.Tests;

/// <summary>
/// JsonTradeJournalStore 單元測試 — 驗證 CRUD 操作、JSON 持久化、執行緒安全。
/// 每個測試使用獨立的暫存檔避免互相干擾。
/// </summary>
public class JsonTradeJournalStoreTests : IDisposable
{
    private readonly string _tempDir;

    public JsonTradeJournalStoreTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"trade-journal-test-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
            Directory.Delete(_tempDir, true);
    }

    private JsonTradeJournalStore CreateStore(string? fileName = null)
    {
        var filePath = Path.Combine(_tempDir, fileName ?? "journal.json");
        return new JsonTradeJournalStore(filePath);
    }

    private static TradeRecord MakeRecord(
        string ticker = "2330",
        TradeDirection direction = TradeDirection.Buy,
        decimal price = 600m,
        int quantity = 1000,
        DateTime? tradeDate = null,
        string? strategy = "StrategyA",
        string? note = null)
    {
        return new TradeRecord
        {
            Ticker = ticker,
            Direction = direction,
            Price = price,
            Quantity = quantity,
            TradeDate = tradeDate ?? new DateTime(2026, 3, 1),
            Strategy = strategy,
            Note = note
        };
    }

    // ═══════════════════════════════════════════════════════════════
    //  GetAllAsync
    // ═══════════════════════════════════════════════════════════════

    [Fact]
    public async Task GetAllAsync_EmptyFile_Should_Return_Empty_List()
    {
        var store = CreateStore();

        var result = await store.GetAllAsync();

        result.Should().NotBeNull();
        result.Should().BeEmpty();
    }

    [Fact]
    public async Task GetAllAsync_After_Adding_Records_Should_Return_All()
    {
        var store = CreateStore();
        await store.AddAsync(MakeRecord("2330"));
        await store.AddAsync(MakeRecord("2317"));
        await store.AddAsync(MakeRecord("2454"));

        var result = await store.GetAllAsync();

        result.Should().HaveCount(3);
        result.Select(r => r.Ticker).Should().Contain(new[] { "2330", "2317", "2454" });
    }

    // ═══════════════════════════════════════════════════════════════
    //  AddAsync
    // ═══════════════════════════════════════════════════════════════

    [Fact]
    public async Task AddAsync_Should_Persist_Record_With_All_Fields()
    {
        var store = CreateStore();
        var record = MakeRecord(
            ticker: "2330",
            direction: TradeDirection.Buy,
            price: 615.5m,
            quantity: 2000,
            strategy: "StrategyA",
            note: "盤前強勢買進");

        await store.AddAsync(record);

        var all = await store.GetAllAsync();
        all.Should().ContainSingle();

        var saved = all[0];
        saved.Id.Should().Be(record.Id);
        saved.Ticker.Should().Be("2330");
        saved.Direction.Should().Be(TradeDirection.Buy);
        saved.Price.Should().Be(615.5m);
        saved.Quantity.Should().Be(2000);
        saved.Strategy.Should().Be("StrategyA");
        saved.Note.Should().Be("盤前強勢買進");
        saved.Amount.Should().Be(615.5m * 2000);
    }

    [Fact]
    public async Task AddAsync_Should_AutoCreate_Directory()
    {
        var deepPath = Path.Combine(_tempDir, "sub", "deep", "journal.json");
        var store = new JsonTradeJournalStore(deepPath);

        await store.AddAsync(MakeRecord());

        File.Exists(deepPath).Should().BeTrue();
        var all = await store.GetAllAsync();
        all.Should().ContainSingle();
    }

    [Fact]
    public async Task AddAsync_Multiple_Should_Accumulate()
    {
        var store = CreateStore();

        for (int i = 0; i < 10; i++)
            await store.AddAsync(MakeRecord($"00{i:D2}"));

        var all = await store.GetAllAsync();
        all.Should().HaveCount(10);
    }

    // ═══════════════════════════════════════════════════════════════
    //  GetByDateAsync
    // ═══════════════════════════════════════════════════════════════

    [Fact]
    public async Task GetByDateAsync_Should_Filter_By_Date()
    {
        var store = CreateStore();
        var day1 = new DateTime(2026, 3, 1);
        var day2 = new DateTime(2026, 3, 2);
        var day3 = new DateTime(2026, 3, 3);

        await store.AddAsync(MakeRecord("2330", tradeDate: day1));
        await store.AddAsync(MakeRecord("2317", tradeDate: day1));
        await store.AddAsync(MakeRecord("2454", tradeDate: day2));
        await store.AddAsync(MakeRecord("3008", tradeDate: day3));

        var result1 = await store.GetByDateAsync(day1);
        var result2 = await store.GetByDateAsync(day2);
        var result3 = await store.GetByDateAsync(day3);

        result1.Should().HaveCount(2);
        result2.Should().ContainSingle();
        result3.Should().ContainSingle();
    }

    [Fact]
    public async Task GetByDateAsync_No_Match_Should_Return_Empty()
    {
        var store = CreateStore();
        await store.AddAsync(MakeRecord(tradeDate: new DateTime(2026, 3, 1)));

        var result = await store.GetByDateAsync(new DateTime(2026, 12, 31));

        result.Should().BeEmpty();
    }

    [Fact]
    public async Task GetByDateAsync_Should_Order_By_CreatedAt_Descending()
    {
        var store = CreateStore();
        var day = new DateTime(2026, 3, 1);

        var r1 = MakeRecord("AAA", tradeDate: day);
        r1.CreatedAt = new DateTime(2026, 3, 1, 9, 0, 0);
        var r2 = MakeRecord("BBB", tradeDate: day);
        r2.CreatedAt = new DateTime(2026, 3, 1, 10, 0, 0);
        var r3 = MakeRecord("CCC", tradeDate: day);
        r3.CreatedAt = new DateTime(2026, 3, 1, 8, 0, 0);

        await store.AddAsync(r1);
        await store.AddAsync(r2);
        await store.AddAsync(r3);

        var result = await store.GetByDateAsync(day);

        result.Should().HaveCount(3);
        result[0].Ticker.Should().Be("BBB"); // 10:00 (最新)
        result[1].Ticker.Should().Be("AAA"); // 09:00
        result[2].Ticker.Should().Be("CCC"); // 08:00 (最舊)
    }

    // ═══════════════════════════════════════════════════════════════
    //  UpdateAsync
    // ═══════════════════════════════════════════════════════════════

    [Fact]
    public async Task UpdateAsync_Should_Modify_Existing_Record()
    {
        var store = CreateStore();
        var record = MakeRecord("2330", price: 600m);
        await store.AddAsync(record);

        // 修改價格和備註
        record.Price = 610m;
        record.Note = "修正成交價";
        await store.UpdateAsync(record);

        var all = await store.GetAllAsync();
        all.Should().ContainSingle();
        all[0].Price.Should().Be(610m);
        all[0].Note.Should().Be("修正成交價");
    }

    [Fact]
    public async Task UpdateAsync_NonExistent_Id_Should_Not_Throw()
    {
        var store = CreateStore();
        await store.AddAsync(MakeRecord());

        var ghost = new TradeRecord { Id = "nonexistent", Ticker = "GHOST" };

        // 不存在的 Id → 不應拋例外，也不應改變既有紀錄
        var act = async () => await store.UpdateAsync(ghost);
        await act.Should().NotThrowAsync();

        var all = await store.GetAllAsync();
        all.Should().ContainSingle();
        all[0].Ticker.Should().Be("2330");
    }

    [Fact]
    public async Task UpdateAsync_Should_Only_Modify_Target_Record()
    {
        var store = CreateStore();
        var r1 = MakeRecord("2330", price: 600m);
        var r2 = MakeRecord("2317", price: 100m);
        await store.AddAsync(r1);
        await store.AddAsync(r2);

        r1.Price = 620m;
        await store.UpdateAsync(r1);

        var all = await store.GetAllAsync();
        all.Should().HaveCount(2);
        all.First(r => r.Id == r1.Id).Price.Should().Be(620m);
        all.First(r => r.Id == r2.Id).Price.Should().Be(100m); // 未被影響
    }

    // ═══════════════════════════════════════════════════════════════
    //  DeleteAsync
    // ═══════════════════════════════════════════════════════════════

    [Fact]
    public async Task DeleteAsync_Should_Remove_Record()
    {
        var store = CreateStore();
        var record = MakeRecord();
        await store.AddAsync(record);

        await store.DeleteAsync(record.Id);

        var all = await store.GetAllAsync();
        all.Should().BeEmpty();
    }

    [Fact]
    public async Task DeleteAsync_NonExistent_Id_Should_Not_Throw()
    {
        var store = CreateStore();
        await store.AddAsync(MakeRecord());

        var act = async () => await store.DeleteAsync("nonexistent!");
        await act.Should().NotThrowAsync();

        var all = await store.GetAllAsync();
        all.Should().ContainSingle(); // 原紀錄仍在
    }

    [Fact]
    public async Task DeleteAsync_Should_Only_Remove_Target()
    {
        var store = CreateStore();
        var r1 = MakeRecord("2330");
        var r2 = MakeRecord("2317");
        var r3 = MakeRecord("2454");
        await store.AddAsync(r1);
        await store.AddAsync(r2);
        await store.AddAsync(r3);

        await store.DeleteAsync(r2.Id);

        var all = await store.GetAllAsync();
        all.Should().HaveCount(2);
        all.Should().NotContain(r => r.Id == r2.Id);
        all.Should().Contain(r => r.Id == r1.Id);
        all.Should().Contain(r => r.Id == r3.Id);
    }

    // ═══════════════════════════════════════════════════════════════
    //  Persistence — 資料持久化驗證
    // ═══════════════════════════════════════════════════════════════

    [Fact]
    public async Task Data_Should_Persist_Across_Store_Instances()
    {
        var filePath = Path.Combine(_tempDir, "persist.json");

        // Instance A: 新增紀錄
        var storeA = new JsonTradeJournalStore(filePath);
        await storeA.AddAsync(MakeRecord("2330", price: 600m));
        await storeA.AddAsync(MakeRecord("2317", price: 105m));

        // Instance B: 用相同路徑建新實例 → 應可讀到資料
        var storeB = new JsonTradeJournalStore(filePath);
        var all = await storeB.GetAllAsync();

        all.Should().HaveCount(2);
        all.Select(r => r.Ticker).Should().Contain(new[] { "2330", "2317" });
    }

    [Fact]
    public async Task TradeDirection_Should_Serialize_As_String()
    {
        var store = CreateStore();
        await store.AddAsync(MakeRecord(direction: TradeDirection.Buy));
        await store.AddAsync(MakeRecord(direction: TradeDirection.Sell));

        // 讀取 JSON 原文驗證 enum 序列化格式
        var filePath = Path.Combine(_tempDir, "journal.json");
        var json = await File.ReadAllTextAsync(filePath);

        json.Should().Contain("\"Buy\"");
        json.Should().Contain("\"Sell\"");
        json.Should().NotContain("\"0\"");
        json.Should().NotContain("\"1\"");
    }

    [Fact]
    public async Task CorruptedFile_Should_Return_Empty_List()
    {
        var filePath = Path.Combine(_tempDir, "corrupt.json");
        await File.WriteAllTextAsync(filePath, "{ this is not valid JSON !!!");

        var store = new JsonTradeJournalStore(filePath);
        var result = await store.GetAllAsync();

        result.Should().NotBeNull();
        result.Should().BeEmpty();
    }

    // ═══════════════════════════════════════════════════════════════
    //  Thread Safety — 並行操作
    // ═══════════════════════════════════════════════════════════════

    [Fact]
    public async Task ConcurrentAdds_Should_Not_Lose_Records()
    {
        var store = CreateStore("concurrent.json");
        const int count = 20;

        var tasks = Enumerable.Range(0, count)
            .Select(i => store.AddAsync(MakeRecord($"T{i:D3}")))
            .ToArray();

        await Task.WhenAll(tasks);

        var all = await store.GetAllAsync();
        all.Should().HaveCount(count);
    }

    // ═══════════════════════════════════════════════════════════════
    //  Amount 計算驗證
    // ═══════════════════════════════════════════════════════════════

    [Theory]
    [InlineData(100, 1000, 100_000)]
    [InlineData(615.5, 2000, 1_231_000)]
    [InlineData(0, 500, 0)]
    [InlineData(50.25, 0, 0)]
    public void TradeRecord_Amount_Should_Equal_Price_Times_Quantity(
        double price, int qty, double expectedAmount)
    {
        var record = new TradeRecord
        {
            Price = (decimal)price,
            Quantity = qty
        };

        record.Amount.Should().Be((decimal)expectedAmount);
    }

    // ═══════════════════════════════════════════════════════════════
    //  CSV Export 測試
    // ═══════════════════════════════════════════════════════════════

    [Fact]
    public async Task ExportCsv_EmptyStore_Should_Return_Header_Only()
    {
        var store = CreateStore("csv-empty.json");
        var csv = await store.ExportCsvAsync();

        csv.Should().StartWith("Id,TradeDate,Ticker,Direction,Price,Quantity,Amount,Strategy,Note,CreatedAt");
        csv.Trim().Split('\n').Should().HaveCount(1); // header only
    }

    [Fact]
    public async Task ExportCsv_Should_Include_All_Records()
    {
        var store = CreateStore("csv-all.json");
        await store.AddAsync(new TradeRecord { TradeDate = new DateTime(2026, 3, 1), Ticker = "2330", Direction = TradeDirection.Buy, Price = 600m, Quantity = 1000 });
        await store.AddAsync(new TradeRecord { TradeDate = new DateTime(2026, 3, 2), Ticker = "2317", Direction = TradeDirection.Sell, Price = 105m, Quantity = 2000 });

        var csv = await store.ExportCsvAsync();
        var lines = csv.Trim().Split('\n');

        lines.Should().HaveCount(3); // header + 2 records
        lines[1].Should().Contain("2330").And.Contain("Buy").And.Contain("600");
        lines[2].Should().Contain("2317").And.Contain("Sell").And.Contain("105");
    }

    [Fact]
    public async Task ExportCsv_WithDateRange_Should_Filter_Records()
    {
        var store = CreateStore("csv-range.json");
        await store.AddAsync(new TradeRecord { TradeDate = new DateTime(2026, 1, 10), Ticker = "AAA", Price = 10m, Quantity = 100 });
        await store.AddAsync(new TradeRecord { TradeDate = new DateTime(2026, 2, 15), Ticker = "BBB", Price = 20m, Quantity = 200 });
        await store.AddAsync(new TradeRecord { TradeDate = new DateTime(2026, 3, 20), Ticker = "CCC", Price = 30m, Quantity = 300 });

        var csv = await store.ExportCsvAsync(
            fromDate: new DateTime(2026, 2, 1),
            toDate: new DateTime(2026, 2, 28));

        var lines = csv.Trim().Split('\n');
        lines.Should().HaveCount(2); // header + 1 record
        lines[1].Should().Contain("BBB");
    }

    [Fact]
    public async Task ExportCsv_Should_Escape_Commas_In_Note()
    {
        var store = CreateStore("csv-escape.json");
        await store.AddAsync(new TradeRecord
        {
            TradeDate = new DateTime(2026, 3, 1),
            Ticker = "2330",
            Price = 600m,
            Quantity = 1000,
            Note = "test, with comma"
        });

        var csv = await store.ExportCsvAsync();
        // Note field should be quoted
        csv.Should().Contain("\"test, with comma\"");
    }

    [Fact]
    public async Task ExportCsv_Should_Sort_By_Date_Ascending()
    {
        var store = CreateStore("csv-sort.json");
        await store.AddAsync(new TradeRecord { TradeDate = new DateTime(2026, 3, 3), Ticker = "CCC", Price = 30m, Quantity = 300 });
        await store.AddAsync(new TradeRecord { TradeDate = new DateTime(2026, 3, 1), Ticker = "AAA", Price = 10m, Quantity = 100 });
        await store.AddAsync(new TradeRecord { TradeDate = new DateTime(2026, 3, 2), Ticker = "BBB", Price = 20m, Quantity = 200 });

        var csv = await store.ExportCsvAsync();
        var lines = csv.Trim().Split('\n');

        lines[1].Should().Contain("AAA");
        lines[2].Should().Contain("BBB");
        lines[3].Should().Contain("CCC");
    }
}
