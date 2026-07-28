using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using MiniMart.Application.Interfaces;
using MiniMart.Application.Models;
using MiniMart.Domain.Entities;
using MiniMart.Infrastructure.Data;
using Xunit.Abstractions;

namespace MiniMart.Tests;

/// <summary>
/// SQL THẬT mà <c>BulkUpdatePriceStockAsync</c> sinh ra cho 5 sản phẩm.
///
/// <para>
/// <b><c>ToQueryString()</c> KHÔNG dùng được ở đây</b> - nó là extension trên
/// <c>IQueryable</c> nên chỉ in ra được đường ĐỌC. Đường ghi đi qua <c>SaveChanges</c>,
/// không có <c>IQueryable</c> nào tồn tại để in. Cách duy nhất thấy được câu lệnh là
/// bắt log của category <c>Microsoft.EntityFrameworkCore.Database.Command</c>.
/// </para>
/// <para>
/// Bắt log ở tầng <b>host</b> (qua <c>ILoggerProvider</c>) chứ không dựng một
/// <c>DbContext</c> riêng có <c>LogTo</c>: như vậy đo đúng đường mà ứng dụng thật chạy -
/// service resolve từ DI, repository thật, <c>UnitOfWork</c> thật. Dựng context riêng là
/// đo một đường mà không ai dùng.
/// </para>
/// </summary>
public class ProductBulkUpdateSqlTests : IAsyncLifetime
{
    private const int SoSanPham = 5;

    private readonly ITestOutputHelper _output;
    private readonly BatLenhSql _bat = new();
    private readonly WebApplicationFactory<Program> _factory;

    private int _categoryId;
    private List<Product> _products = [];

    public ProductBulkUpdateSqlTests(ITestOutputHelper output)
    {
        _output = output;

        _factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
            builder.ConfigureServices(services => services.AddLogging(logging =>
            {
                logging.AddProvider(_bat);

                // EF Core ghi câu lệnh ở mức Information. Không mở filter thì cấu hình
                // logging của môi trường test có thể chặn mất và danh sách rỗng - test
                // sẽ đỏ ở một assertion nói về SQL, không nói gì về logging.
                logging.AddFilter(DbLoggerCategory.Database.Command.Name, LogLevel.Information);
            })));
    }

    public async Task InitializeAsync()
    {
        using var scope = _factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<MiniMartDbContext>();

        var category = new Category { Name = $"SQ_{Guid.NewGuid():N}"[..14] };

        _products = Enumerable.Range(0, SoSanPham)
            .Select(i => new Product
            {
                Name = $"SQL {i}",
                Price = 100_000m + i,
                Stock = 10 + i,
                Category = category
            })
            .ToList();

        context.Products.AddRange(_products);
        await context.SaveChangesAsync();

        _categoryId = category.Id;
    }

    [Fact]
    public async Task Bulk_update_5_san_pham_KHONG_bi_N_cong_1()
    {
        var items = _products
            .Select(p => new ProductBulkUpdateItem(p.Id, p.Price + 1, p.Stock + 1, p.RowVersion))
            .ToList();

        // Xoá log của phần seed - chỉ quan tâm câu lệnh của chính lần bulk update.
        _bat.Lenh.Clear();

        using (var scope = _factory.Services.CreateScope())
        {
            var service = scope.ServiceProvider.GetRequiredService<IProductService>();
            var ketQua = await service.BulkUpdatePriceStockAsync(items);

            Assert.Equal(SoSanPham, ketQua.SoDongDaLuu);
        }

        foreach (var lenh in _bat.Lenh)
        {
            _output.WriteLine(lenh);
            _output.WriteLine(new string('-', 70));
        }

        // ★ Toàn bộ nghiệp vụ = ĐÚNG 2 lệnh, bất kể có bao nhiêu sản phẩm.
        //
        // N+1 sẽ trông thế này: 1 SELECT + 5 UPDATE = 6 lệnh (hoặc 5 SELECT + 5 UPDATE
        // = 10 nếu đọc lẻ từng dòng). Con số 2 là thứ phải giữ khi số sản phẩm tăng.
        Assert.Equal(2, _bat.Lenh.Count);

        var doc = _bat.Lenh[0];
        var ghi = _bat.Lenh[1];

        // Lệnh 1: MỘT câu SELECT với IN (...), không phải 5 câu FirstOrDefault.
        Assert.Contains("SELECT", doc, StringComparison.Ordinal);
        Assert.Contains(" IN (", doc, StringComparison.Ordinal);

        // Lệnh 2: 5 câu UPDATE nằm trong CÙNG MỘT command (một round-trip).
        Assert.Equal(SoSanPham, Regex.Matches(ghi, @"UPDATE \[Products\]").Count);

        // ★★ Và mỗi câu UPDATE phải mang RowVersion trong WHERE. EF Core tự thêm vì
        // cột này là concurrency token - đây chính là thứ ExecuteUpdate KHÔNG có, vì nó
        // không đi qua Change Tracker.
        Assert.Equal(SoSanPham, Regex.Matches(ghi, @"WHERE \[Id\] = @\w+ AND \[RowVersion\] = @\w+").Count);
    }

    [Fact]
    public async Task So_lenh_KHONG_tang_theo_so_san_pham()
    {
        // Cùng một nghiệp vụ, chỉ 2 dòng thay vì 5. N+1 thì số lệnh phải khác đi.
        var items = _products
            .Take(2)
            .Select(p => new ProductBulkUpdateItem(p.Id, p.Price + 5, p.Stock + 5, p.RowVersion))
            .ToList();

        _bat.Lenh.Clear();

        using var scope = _factory.Services.CreateScope();
        await scope.ServiceProvider.GetRequiredService<IProductService>()
            .BulkUpdatePriceStockAsync(items);

        // Khẳng định theo TÍNH CHẤT (hằng số) chứ không theo con số của một lần chạy:
        // đây mới là định nghĩa của "không N+1".
        Assert.Equal(2, _bat.Lenh.Count);
    }

    /// <summary>
    /// <c>ILoggerProvider</c> chỉ giữ lại log của category câu lệnh DB.
    /// </summary>
    private sealed class BatLenhSql : ILoggerProvider
    {
        public List<string> Lenh { get; } = [];

        public ILogger CreateLogger(string categoryName) =>
            categoryName == DbLoggerCategory.Database.Command.Name
                ? new Logger(Lenh)
                : new KhongGhi();

        public void Dispose() { }

        private sealed class Logger(List<string> lenh) : ILogger
        {
            public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

            public bool IsEnabled(LogLevel logLevel) => logLevel >= LogLevel.Information;

            public void Log<TState>(
                LogLevel logLevel,
                EventId eventId,
                TState state,
                Exception? exception,
                Func<TState, Exception?, string> formatter)
            {
                if (!IsEnabled(logLevel))
                {
                    return;
                }

                // Danh sách dùng chung giữa nhiều DbContext/scope nên phải khoá: EF ghi
                // log từ thread nào chạy truy vấn, không đảm bảo là thread của test.
                lock (lenh)
                {
                    lenh.Add(formatter(state, exception));
                }
            }
        }

        private sealed class KhongGhi : ILogger
        {
            public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

            public bool IsEnabled(LogLevel logLevel) => false;

            public void Log<TState>(
                LogLevel logLevel, EventId eventId, TState state, Exception? exception,
                Func<TState, Exception?, string> formatter)
            { }
        }
    }

    public async Task DisposeAsync()
    {
        using var scope = _factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<MiniMartDbContext>();

        await context.Products.Where(p => p.CategoryId == _categoryId).ExecuteDeleteAsync();
        await context.Categories.Where(c => c.Id == _categoryId).ExecuteDeleteAsync();

        _factory.Dispose();
    }
}
