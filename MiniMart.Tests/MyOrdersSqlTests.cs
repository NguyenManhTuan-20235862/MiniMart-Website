using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using MiniMart.Application.Interfaces;
using MiniMart.Domain.Entities;
using MiniMart.Domain.Enums;
using MiniMart.Infrastructure.Data;
using Xunit.Abstractions;

namespace MiniMart.Tests;

/// <summary>
/// SQL THẬT của trang "Đơn hàng của tôi".
///
/// <para>
/// Tồn tại vì claim trung tâm của Phase 9 - "chiếu xuống <c>OrderSummary</c> nên không
/// dòng <c>OrderDetail</c> nào rời khỏi database" - <b>không thể kiểm bằng test hành
/// vi</b>. Đổi sang <c>Include(o =&gt; o.Items)</c> rồi <c>Sum</c> trong C# cho ra ĐÚNG
/// cùng con số trên màn hình; chỉ số lệnh và hình dạng SQL mới phân biệt được.
/// </para>
/// <para>
/// Cùng kỹ thuật với <see cref="ProductBulkUpdateSqlTests"/>. Đây là bản chép thứ HAI
/// của lớp bắt log - ngưỡng gộp của dự án là bản thứ ba, nên để nguyên còn dễ đọc hơn.
/// </para>
/// </summary>
public class MyOrdersSqlTests : IAsyncLifetime
{
    private const int SoDon = 5;
    private const int SoDongMoiDon = 4;

    private readonly ITestOutputHelper _output;
    private readonly BatLenhSql _bat = new();
    private readonly WebApplicationFactory<Program> _factory;

    private int _categoryId;
    private int _userId;

    public MyOrdersSqlTests(ITestOutputHelper output)
    {
        _output = output;

        _factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
            builder.ConfigureServices(services => services.AddLogging(logging =>
            {
                logging.AddProvider(_bat);
                logging.AddFilter(DbLoggerCategory.Database.Command.Name, LogLevel.Information);
            })));
    }

    public async Task InitializeAsync()
    {
        using var scope = _factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<MiniMartDbContext>();

        var category = new Category { Name = $"OS_{Guid.NewGuid():N}"[..14] };
        var products = Enumerable.Range(0, SoDongMoiDon)
            .Select(i => new Product
            {
                Name = $"OS{i}_{Guid.NewGuid():N}"[..20],
                Price = 100_000m + i,
                Stock = 500,
                Category = category
            })
            .ToList();

        var user = new User
        {
            Username = $"os_{Guid.NewGuid():N}"[..16],
            PasswordHash = "khong-dung-de-dang-nhap",
            Role = UserRole.Customer
        };

        context.Products.AddRange(products);
        context.Users.Add(user);
        await context.SaveChangesAsync();

        _categoryId = category.Id;
        _userId = user.Id;

        for (var i = 0; i < SoDon; i++)
        {
            var order = new Order
            {
                UserId = _userId,
                CreatedAt = DateTime.UtcNow.AddMinutes(-i),
                Status = OrderStatus.Paid,
                RecipientName = "Nguoi Nhan",
                RecipientPhone = "0900000000",
                ShippingAddress = "So 1",
                TotalAmount = 1_000_000m
            };

            foreach (var p in products)
            {
                order.Items.Add(new OrderDetail
                {
                    ProductId = p.Id,
                    ProductName = p.Name,
                    UnitPrice = p.Price,
                    Quantity = 2
                });
            }

            context.Orders.Add(order);
        }

        await context.SaveChangesAsync();
    }

    [Fact]
    public async Task Danh_sach_don_KHONG_keo_ve_dong_OrderDetail_nao()
    {
        _bat.Lenh.Clear();

        using (var scope = _factory.Services.CreateScope())
        {
            var orderService = scope.ServiceProvider.GetRequiredService<IOrderService>();
            var trang = await orderService.GetMyOrdersAsync(_userId);

            Assert.Equal(SoDon, trang.TotalCount);
            Assert.All(trang.Items, d => Assert.Equal(SoDongMoiDon * 2, d.TongSoLuong));
        }

        foreach (var lenh in _bat.Lenh)
        {
            _output.WriteLine(lenh);
            _output.WriteLine(new string('-', 70));
        }

        // Đúng 2 lệnh bất kể có bao nhiêu đơn và bao nhiêu dòng: một COUNT cho tổng số
        // bản ghi, một SELECT lấy đúng một trang.
        Assert.Equal(2, _bat.Lenh.Count);

        var doc = _bat.Lenh[1];

        // ★ Bằng chứng cho claim: SUM chạy DƯỚI database, và không cột nào của
        // OrderDetails (ProductName, UnitPrice) được chọn về.
        Assert.Contains("SUM(", doc, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("[ProductName]", doc, StringComparison.Ordinal);
        Assert.DoesNotContain("[UnitPrice]", doc, StringComparison.Ordinal);

        // Phân trang phải là OFFSET/FETCH dưới DB, không phải Skip/Take trong bộ nhớ.
        Assert.Contains("OFFSET", doc, StringComparison.Ordinal);

        // Tie-breaker phải có mặt trong ORDER BY, nếu không bản ghi nhảy giữa hai trang.
        Assert.Matches(@"ORDER BY .*\[CreatedAt\] DESC.*\[Id\] DESC", doc);
    }

    [Fact]
    public async Task So_lenh_KHONG_tang_theo_so_don()
    {
        _bat.Lenh.Clear();

        using var scope = _factory.Services.CreateScope();
        var orderService = scope.ServiceProvider.GetRequiredService<IOrderService>();

        // Chỉ 2 đơn thay vì 5. N+1 thì số lệnh phải khác đi - đây mới là định nghĩa
        // của "không N+1", chứ không phải một con số của một lần chạy.
        await orderService.GetMyOrdersAsync(_userId, pageSize: 2);

        Assert.Equal(2, _bat.Lenh.Count);
    }

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
                LogLevel logLevel, EventId eventId, TState state, Exception? exception,
                Func<TState, Exception?, string> formatter)
            {
                if (!IsEnabled(logLevel))
                {
                    return;
                }

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

        var orderIds = await context.Orders
            .Where(o => o.UserId == _userId)
            .Select(o => o.Id)
            .ToListAsync();

        await context.OrderDetails.Where(d => orderIds.Contains(d.OrderId)).ExecuteDeleteAsync();
        await context.Orders.Where(o => orderIds.Contains(o.Id)).ExecuteDeleteAsync();
        await context.Products.Where(p => p.CategoryId == _categoryId).ExecuteDeleteAsync();
        await context.Categories.Where(c => c.Id == _categoryId).ExecuteDeleteAsync();
        await context.Users.Where(u => u.Id == _userId).ExecuteDeleteAsync();

        _factory.Dispose();
    }
}
