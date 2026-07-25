using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using MiniMart.Domain.Entities;
using MiniMart.Domain.Interfaces;
using MiniMart.Infrastructure.Data;

namespace MiniMart.Tests;

/// <summary>
/// Repository là code truy vấn EF Core, nên phải chạy trên SQL Server thật.
/// Mock DbContext chỉ kiểm tra được chính cái mock, không kiểm tra được câu
/// SQL sinh ra có đúng không.
/// </summary>
public class ProductCategoryRepositoryTests : IAsyncLifetime
{
    private readonly WebApplicationFactory<Program> _factory = new();
    private readonly List<int> _categoryIdsToCleanUp = [];

    public Task InitializeAsync() => Task.CompletedTask;

    private IServiceScope CreateScope() => _factory.Services.CreateScope();

    private async Task<Category> TaoDanhMucCoSanPhamAsync(params string[] productNames)
    {
        using var scope = CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<MiniMartDbContext>();

        var category = new Category { Name = $"DM_{Guid.NewGuid():N}"[..16] };

        foreach (var name in productNames)
        {
            category.Products.Add(new Product
            {
                Name = name,
                Price = 100_000m,
                Stock = 10
            });
        }

        context.Categories.Add(category);
        await context.SaveChangesAsync();

        _categoryIdsToCleanUp.Add(category.Id);
        return category;
    }

    // ───────────── Eager loading & tracking ─────────────

    [Fact]
    public async Task GetAllAsync_phai_nap_kem_Category()
    {
        var category = await TaoDanhMucCoSanPhamAsync("San pham A");

        using var scope = CreateScope();
        var repository = scope.ServiceProvider.GetRequiredService<IProductRepository>();

        var products = await repository.GetAllAsync();
        var product = products.Single(p => p.CategoryId == category.Id);

        // Thiếu Include thì dòng dưới là null và view sẽ ném NullReferenceException.
        Assert.NotNull(product.Category);
        Assert.Equal(category.Name, product.Category.Name);
    }

    [Fact]
    public async Task GetAllAsync_tra_ve_entity_KHONG_duoc_theo_doi()
    {
        var category = await TaoDanhMucCoSanPhamAsync("San pham A");

        using var scope = CreateScope();
        var repository = scope.ServiceProvider.GetRequiredService<IProductRepository>();
        var context = scope.ServiceProvider.GetRequiredService<MiniMartDbContext>();

        var product = (await repository.GetAllAsync()).First(p => p.CategoryId == category.Id);

        // Detached = AsNoTracking đang hoạt động. Sửa entity này rồi gọi
        // SaveChanges sẽ KHÔNG lưu gì - đó là lý do phải có GetForUpdateAsync riêng.
        Assert.Equal(EntityState.Detached, context.Entry(product).State);
    }

    [Fact]
    public async Task GetForUpdateAsync_tra_ve_entity_CO_theo_doi()
    {
        var category = await TaoDanhMucCoSanPhamAsync("San pham A");
        var productId = category.Products.First().Id;

        using var scope = CreateScope();
        var repository = scope.ServiceProvider.GetRequiredService<IProductRepository>();
        var context = scope.ServiceProvider.GetRequiredService<MiniMartDbContext>();

        var product = await repository.GetForUpdateAsync(productId);

        // Unchanged = đang được Change Tracker theo dõi. Bắt buộc phải vậy thì
        // RowVersion gốc mới được giữ để kẹp vào WHERE lúc UPDATE.
        Assert.NotNull(product);
        Assert.Equal(EntityState.Unchanged, context.Entry(product!).State);
    }

    // ───────────── Truy vấn nghiệp vụ ─────────────

    [Fact]
    public async Task GetByCategoryAsync_chi_tra_san_pham_dung_danh_muc()
    {
        var categoryA = await TaoDanhMucCoSanPhamAsync("A1", "A2");
        await TaoDanhMucCoSanPhamAsync("B1");

        using var scope = CreateScope();
        var repository = scope.ServiceProvider.GetRequiredService<IProductRepository>();

        var products = await repository.GetByCategoryAsync(categoryA.Id);

        Assert.Equal(2, products.Count);
        Assert.All(products, p => Assert.Equal(categoryA.Id, p.CategoryId));
    }

    [Fact]
    public async Task HasProductsAsync_phan_biet_dung_danh_muc_rong_va_khong_rong()
    {
        var coSanPham = await TaoDanhMucCoSanPhamAsync("A1");
        var rong = await TaoDanhMucCoSanPhamAsync();

        using var scope = CreateScope();
        var repository = scope.ServiceProvider.GetRequiredService<ICategoryRepository>();

        // Sai chỗ này thì luồng xoá sẽ để DbUpdateException văng thẳng ra người dùng.
        Assert.True(await repository.HasProductsAsync(coSanPham.Id));
        Assert.False(await repository.HasProductsAsync(rong.Id));
    }

    [Fact]
    public async Task ExistsByNameAsync_khi_sua_thi_khong_tinh_chinh_no_la_trung()
    {
        var category = await TaoDanhMucCoSanPhamAsync();

        using var scope = CreateScope();
        var repository = scope.ServiceProvider.GetRequiredService<ICategoryRepository>();

        // Không có excludeId thì sửa danh mục mà giữ nguyên tên sẽ bị báo trùng
        // với chính nó.
        Assert.True(await repository.ExistsByNameAsync(category.Name));
        Assert.False(await repository.ExistsByNameAsync(category.Name, excludeId: category.Id));
    }

    // ───────────── Đăng ký DI ─────────────

    [Theory]
    [InlineData(typeof(IProductRepository))]
    [InlineData(typeof(ICategoryRepository))]
    public void Repository_phai_duoc_dang_ky_voi_lifetime_Scoped(Type serviceType)
    {
        using var scopeA = CreateScope();
        using var scopeB = CreateScope();

        var fromA1 = scopeA.ServiceProvider.GetRequiredService(serviceType);
        var fromA2 = scopeA.ServiceProvider.GetRequiredService(serviceType);
        var fromB = scopeB.ServiceProvider.GetRequiredService(serviceType);

        Assert.Same(fromA1, fromA2);   // cùng request -> cùng instance
        Assert.NotSame(fromA1, fromB); // khác request -> khác instance
    }

    public async Task DisposeAsync()
    {
        if (_categoryIdsToCleanUp.Count > 0)
        {
            using var scope = CreateScope();
            var context = scope.ServiceProvider.GetRequiredService<MiniMartDbContext>();

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
