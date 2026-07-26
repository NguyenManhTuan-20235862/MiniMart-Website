using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using MiniMart.Application.Interfaces;
using MiniMart.Common.Exceptions;
using MiniMart.Domain.Entities;
using MiniMart.Infrastructure.Data;

namespace MiniMart.Tests;

/// <summary>
/// Chặn xoá sản phẩm đã có đơn hàng - hệ quả cố ý của
/// <c>OrderDetails.ProductId = DeleteBehavior.Restrict</c>.
///
/// <para>
/// Cùng khuôn với <c>CategoryHasProductsException</c>: Service kiểm TRƯỚC để có thông
/// báo tử tế, khoá ngoại dưới DB là bảo đảm cuối cùng. Khác ở chỗ giỏ hàng thì ngược
/// lại - <c>CartItems.ProductId</c> là Cascade, xoá sản phẩm là nó tự biến khỏi mọi giỏ.
/// </para>
/// </summary>
public class ProductDeleteGuardTests : IAsyncLifetime
{
    private readonly WebApplicationFactory<Program> _factory = new();
    private readonly List<int> _userIds = [];

    private int _categoryId;
    private int _productCoDon;
    private int _productKhongDon;

    public async Task InitializeAsync()
    {
        using var scope = _factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<MiniMartDbContext>();

        var category = new Category { Name = $"DG_{Guid.NewGuid():N}"[..14] };
        var coDon = new Product { Name = "DaBan", Price = 100_000m, Stock = 10, Category = category };
        var khongDon = new Product { Name = "ChuaBan", Price = 100_000m, Stock = 10, Category = category };

        context.Products.AddRange(coDon, khongDon);
        await context.SaveChangesAsync();

        _categoryId = category.Id;
        _productCoDon = coDon.Id;
        _productKhongDon = khongDon.Id;

        await TaoDonHangChoAsync(_productCoDon);
    }

    [Fact]
    public async Task Xoa_san_pham_da_co_don_thi_bi_chan_kem_huong_xu_ly()
    {
        using var scope = _factory.Services.CreateScope();
        var productService = scope.ServiceProvider.GetRequiredService<IProductService>();

        var ex = await Assert.ThrowsAsync<ProductHasOrdersException>(
            () => productService.DeleteAsync(_productCoDon));

        // Thông báo phải nói được VIỆC CẦN LÀM, không chỉ nói "không được".
        Assert.Contains("đã có đơn hàng", ex.Message);
        Assert.Contains("đặt tồn kho về 0", ex.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(_productCoDon, ex.ProductId);
    }

    [Fact]
    public async Task San_pham_bi_chan_van_con_nguyen_trong_DB()
    {
        using var scope = _factory.Services.CreateScope();
        var productService = scope.ServiceProvider.GetRequiredService<IProductService>();

        await Assert.ThrowsAsync<ProductHasOrdersException>(
            () => productService.DeleteAsync(_productCoDon));

        var context = scope.ServiceProvider.GetRequiredService<MiniMartDbContext>();

        Assert.True(await context.Products.AnyAsync(p => p.Id == _productCoDon));
    }

    [Fact]
    public async Task Don_hang_cu_KHONG_bi_anh_huong()
    {
        using var scope = _factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<MiniMartDbContext>();

        // Đây là lý do dùng Restrict thay vì Cascade: Cascade sẽ làm mất dòng đơn khi
        // admin xoá sản phẩm, tức lịch sử bán hàng tự sửa lại chính nó và tổng tiền
        // đơn không còn khớp tổng các dòng.
        var dong = await context.OrderDetails
            .AsNoTracking()
            .Where(d => d.ProductId == _productCoDon)
            .ToListAsync();

        Assert.NotEmpty(dong);
        Assert.All(dong, d => Assert.Equal("DaBan", d.ProductName));
    }

    [Fact]
    public async Task San_pham_CHUA_tung_ban_thi_van_xoa_binh_thuong()
    {
        using var scope = _factory.Services.CreateScope();
        var productService = scope.ServiceProvider.GetRequiredService<IProductService>();

        // Ràng buộc mới không được chặn oan: chỉ sản phẩm ĐÃ có đơn mới bị giữ lại.
        await productService.DeleteAsync(_productKhongDon);

        var context = scope.ServiceProvider.GetRequiredService<MiniMartDbContext>();

        Assert.False(await context.Products.AnyAsync(p => p.Id == _productKhongDon));
    }

    [Fact]
    public async Task Vi_pham_khoa_ngoai_duoc_dich_thanh_ReferenceConstraintException()
    {
        using var scope = _factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<MiniMartDbContext>();
        var unitOfWork = scope.ServiceProvider.GetRequiredService<Domain.Interfaces.IUnitOfWork>();

        // Xoá THẲNG qua DbContext, bỏ qua lớp kiểm tra của Service - mô phỏng khe
        // TOCTOU: có người vừa đặt hàng giữa lúc Service kiểm và lúc lưu.
        var product = await context.Products.SingleAsync(p => p.Id == _productCoDon);
        context.Products.Remove(product);

        // Không có bước dịch này thì trường hợp hiếm đó cho ra HTTP 500 kèm thông báo
        // của EF Core. Đây là nơi DUY NHẤT biết mã lỗi 547 của SQL Server.
        await Assert.ThrowsAsync<ReferenceConstraintException>(
            () => unitOfWork.SaveChangesAsync());
    }

    // ───────────── Helper ─────────────

    private async Task TaoDonHangChoAsync(int productId)
    {
        using var scope = _factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<MiniMartDbContext>();

        var user = new User
        {
            Username = $"dg_{Guid.NewGuid():N}"[..16],
            PasswordHash = "khong-dung-de-dang-nhap",
            Role = UserRole.Customer
        };

        context.Users.Add(user);
        await context.SaveChangesAsync();

        _userIds.Add(user.Id);

        context.Orders.Add(new Order
        {
            UserId = user.Id,
            CreatedAt = DateTime.UtcNow,
            TotalAmount = 100_000m,
            Items =
            [
                new OrderDetail
                {
                    ProductId = productId,
                    ProductName = "DaBan",
                    UnitPrice = 100_000m,
                    Quantity = 1
                }
            ]
        });

        await context.SaveChangesAsync();
    }

    public async Task DisposeAsync()
    {
        using var scope = _factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<MiniMartDbContext>();

        await context.OrderDetails.Where(d => _userIds.Contains(d.Order.UserId)).ExecuteDeleteAsync();
        await context.Orders.Where(o => _userIds.Contains(o.UserId)).ExecuteDeleteAsync();
        await context.Products.Where(p => p.CategoryId == _categoryId).ExecuteDeleteAsync();
        await context.Categories.Where(c => c.Id == _categoryId).ExecuteDeleteAsync();
        await context.Users.Where(u => _userIds.Contains(u.Id)).ExecuteDeleteAsync();

        _factory.Dispose();
    }
}
