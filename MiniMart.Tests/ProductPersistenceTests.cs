using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using MiniMart.Domain.Entities;
using MiniMart.Infrastructure.Data;

namespace MiniMart.Tests;

/// <summary>
/// Test chạy trên SQL Server THẬT. Bắt buộc phải vậy: rowversion và check
/// constraint là hành vi của database engine, provider InMemory không có.
/// Dữ liệu tạo ra được dọn sạch ở DisposeAsync.
/// </summary>
public class ProductPersistenceTests : IAsyncLifetime
{
    private readonly WebApplicationFactory<Program> _factory = new();
    private readonly List<int> _categoryIdsToCleanUp = [];

    public Task InitializeAsync() => Task.CompletedTask;

    private IServiceScope CreateScope() => _factory.Services.CreateScope();

    private static MiniMartDbContext GetContext(IServiceScope scope) =>
        scope.ServiceProvider.GetRequiredService<MiniMartDbContext>();

    private async Task<(int CategoryId, int ProductId)> TaoDuLieuMauAsync(int stock = 10)
    {
        using var scope = CreateScope();
        var context = GetContext(scope);

        var category = new Category { Name = $"DanhMuc_{Guid.NewGuid():N}"[..20] };
        var product = new Product
        {
            Name = "San pham test",
            Price = 1_000_000m,
            Stock = stock,
            Category = category
        };

        context.Products.Add(product);
        await context.SaveChangesAsync();

        _categoryIdsToCleanUp.Add(category.Id);
        return (category.Id, product.Id);
    }

    [Fact]
    public async Task RowVersion_phai_thay_doi_sau_moi_lan_update()
    {
        var (_, productId) = await TaoDuLieuMauAsync();

        using var scope = CreateScope();
        var context = GetContext(scope);

        var product = await context.Products.SingleAsync(p => p.Id == productId);
        var rowVersionBanDau = product.RowVersion.ToArray();

        product.Price = 2_000_000m;
        await context.SaveChangesAsync();

        // EF Core đọc ngược giá trị mới về sau khi UPDATE, nếu không thì lần
        // lưu kế tiếp sẽ báo xung đột oan.
        Assert.NotEqual(rowVersionBanDau, product.RowVersion);
    }

    [Fact]
    public async Task Ghi_de_dong_thoi_phai_nem_DbUpdateConcurrencyException()
    {
        var (_, productId) = await TaoDuLieuMauAsync();

        // Hai scope = hai DbContext = mô phỏng hai HTTP request khác nhau.
        using var scopeA = CreateScope();
        using var scopeB = CreateScope();
        var contextA = GetContext(scopeA);
        var contextB = GetContext(scopeB);

        var productA = await contextA.Products.SingleAsync(p => p.Id == productId);
        var productB = await contextB.Products.SingleAsync(p => p.Id == productId);

        // A ghi trước và thành công -> DB sinh RowVersion mới.
        productA.Price = 111m;
        await contextA.SaveChangesAsync();

        // B vẫn giữ RowVersion cũ -> WHERE không khớp -> 0 dòng bị ảnh hưởng.
        productB.Price = 222m;

        await Assert.ThrowsAsync<DbUpdateConcurrencyException>(
            () => contextB.SaveChangesAsync());
    }

    [Fact]
    public async Task Check_constraint_phai_chan_ton_kho_am()
    {
        var (_, productId) = await TaoDuLieuMauAsync(stock: 5);

        using var scope = CreateScope();
        var context = GetContext(scope);

        var product = await context.Products.SingleAsync(p => p.Id == productId);
        product.Stock = -1;

        // Chốt chặn ở tầng DB cho nghiệp vụ trừ tồn kho.
        await Assert.ThrowsAsync<DbUpdateException>(() => context.SaveChangesAsync());
    }

    [Fact]
    public async Task Khong_duoc_xoa_Category_khi_con_san_pham()
    {
        var (categoryId, _) = await TaoDuLieuMauAsync();

        using var scope = CreateScope();
        var context = GetContext(scope);

        var category = await context.Categories.SingleAsync(c => c.Id == categoryId);
        context.Categories.Remove(category);

        // DeleteBehavior.Restrict: nếu là Cascade thì lệnh này sẽ âm thầm
        // xoá sạch sản phẩm trong danh mục.
        await Assert.ThrowsAsync<DbUpdateException>(() => context.SaveChangesAsync());
    }

    public async Task DisposeAsync()
    {
        if (_categoryIdsToCleanUp.Count > 0)
        {
            using var scope = CreateScope();
            var context = GetContext(scope);

            // Xoá Product trước rồi mới tới Category - đúng thứ tự mà
            // Restrict bắt buộc.
            await context.Products
                .Where(p => _categoryIdsToCleanUp.Contains(p.CategoryId))
                .ExecuteDeleteAsync();

            await context.Categories
                .Where(c => _categoryIdsToCleanUp.Contains(c.Id))
                .ExecuteDeleteAsync();
        }

        _factory.Dispose();
    }
}
