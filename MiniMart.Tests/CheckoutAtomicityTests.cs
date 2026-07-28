using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using MiniMart.Application.Interfaces;
using MiniMart.Common.Exceptions;
using MiniMart.Domain.Entities;
using MiniMart.Infrastructure.Data;

namespace MiniMart.Tests;

/// <summary>
/// Giỏ 3 sản phẩm, MỘT sản phẩm thiếu hàng: không được tạo đơn "nửa vời".
///
/// <para>
/// Kịch bản này khác hẳn <see cref="CheckoutConcurrencyTests"/>: ở đó nhiều người
/// tranh nhau MỘT sản phẩm, còn ở đây một người mua NHIỀU sản phẩm và chỉ một món
/// hỏng. Bất biến cần khoá là "tất cả hoặc không gì" theo chiều ngang - hai món còn
/// lại tuyệt đối không được trừ kho.
/// </para>
/// <para>
/// Sản phẩm được xử lý theo thứ tự <c>ProductId</c> tăng dần, nên đặt món thiếu ở
/// GIỮA là cố ý: nó bảo đảm có ít nhất một món đã bị trừ trong Change Tracker TRƯỚC
/// khi exception được ném, và một món chưa hề được chạm tới.
/// </para>
/// </summary>
public class CheckoutAtomicityTests : IAsyncLifetime
{
    private const int TonKhoBanDau = 10;

    private readonly WebApplicationFactory<Program> _factory = new();
    private readonly List<int> _userIds = [];

    private int _categoryId;
    private int _idA;
    private int _idB;
    private int _idC;

    public async Task InitializeAsync()
    {
        using var scope = _factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<MiniMartDbContext>();

        var category = new Category { Name = $"AT_{Guid.NewGuid():N}"[..14] };

        var a = new Product { Name = "SanPhamA", Price = 100_000m, Stock = TonKhoBanDau, Category = category };
        var b = new Product { Name = "SanPhamB", Price = 200_000m, Stock = TonKhoBanDau, Category = category };
        var c = new Product { Name = "SanPhamC", Price = 300_000m, Stock = TonKhoBanDau, Category = category };

        context.Products.AddRange(a, b, c);
        await context.SaveChangesAsync();

        _categoryId = category.Id;

        // Sắp xếp lại theo Id để chắc chắn B nằm GIỮA theo thứ tự xử lý, không phụ
        // thuộc vào thứ tự EF Core gán Id.
        var theoId = new[] { a, b, c }.OrderBy(p => p.Id).ToArray();

        _idA = theoId[0].Id;
        _idB = theoId[1].Id;
        _idC = theoId[2].Id;
    }

    [Fact]
    public async Task Mot_trong_ba_san_pham_thieu_hang_thi_KHONG_mon_nao_bi_tru_kho()
    {
        var userId = await TaoNguoiMuaAsync(soLuongMoiMon: 2);

        // Món GIỮA hết hàng sau khi cả ba đã nằm trong giỏ - giỏ hàng không giữ hàng.
        await DoiTonKhoAsync(_idB, 0);

        await Assert.ThrowsAsync<InsufficientStockException>(() => DatHangAsync(userId));

        // ★ Bất biến trung tâm: A đã bị trừ trong Change Tracker trước khi B ném, C
        // thì chưa được chạm tới. Cả ba phải y nguyên dưới DB.
        Assert.Equal(TonKhoBanDau, await LayTonKhoAsync(_idA));
        Assert.Equal(0, await LayTonKhoAsync(_idB));
        Assert.Equal(TonKhoBanDau, await LayTonKhoAsync(_idC));
    }

    [Fact]
    public async Task Khong_tao_don_hang_nua_voi()
    {
        var userId = await TaoNguoiMuaAsync(soLuongMoiMon: 2);

        await DoiTonKhoAsync(_idB, 0);

        await Assert.ThrowsAsync<InsufficientStockException>(() => DatHangAsync(userId));

        using var scope = _factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<MiniMartDbContext>();

        // Không có Order nào, và cũng không có OrderDetail mồ côi. Một đơn chỉ chứa
        // A (món đã kịp qua vòng lặp) là đơn "nửa vời" - khách bị thu tiền cho thứ
        // họ không hề chọn riêng lẻ.
        Assert.False(await context.Orders.AnyAsync(o => _userIds.Contains(o.UserId)));
        Assert.False(await context.OrderDetails.AnyAsync(
            d => d.ProductId == _idA || d.ProductId == _idB || d.ProductId == _idC));
    }

    [Fact]
    public async Task Gio_hang_con_NGUYEN_ca_ba_mon_de_nguoi_dung_sua()
    {
        var userId = await TaoNguoiMuaAsync(soLuongMoiMon: 2);

        await DoiTonKhoAsync(_idB, 0);

        await Assert.ThrowsAsync<InsufficientStockException>(() => DatHangAsync(userId));

        using var scope = _factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<MiniMartDbContext>();

        var dongGio = await context.CartItems
            .AsNoTracking()
            .Where(i => i.Cart.UserId == userId)
            .ToListAsync();

        // ClearAsync nằm TRONG cùng đơn vị công việc với SaveChanges, nên thất bại
        // thì giỏ còn nguyên. Mất giỏ mà cũng không có đơn là hỏng nặng nhất.
        Assert.Equal(3, dongGio.Count);
        Assert.All(dongGio, i => Assert.Equal(2, i.Quantity));
    }

    [Fact]
    public async Task Thong_bao_chi_dung_TEN_mon_thieu_hang()
    {
        var userId = await TaoNguoiMuaAsync(soLuongMoiMon: 2);

        await DoiTonKhoAsync(_idB, 0);

        var ex = await Assert.ThrowsAsync<InsufficientStockException>(() => DatHangAsync(userId));

        // Nói "có sản phẩm hết hàng" mà không nói món nào là bắt người dùng tự dò
        // trong giỏ 3 món.
        Assert.Contains("SanPhamB", ex.Message);
        Assert.DoesNotContain("SanPhamA", ex.Message);
        Assert.DoesNotContain("SanPhamC", ex.Message);
    }

    [Fact]
    public async Task Sua_giai_giu_lai_thi_dat_duoc_ca_ba_mon()
    {
        var userId = await TaoNguoiMuaAsync(soLuongMoiMon: 2);

        await DoiTonKhoAsync(_idB, 0);

        await Assert.ThrowsAsync<InsufficientStockException>(() => DatHangAsync(userId));

        // Shop nhập hàng lại - đây là đối chứng cho bốn test trên: chúng phải thất bại
        // vì THIẾU HÀNG, không phải vì luồng đặt hàng bị hỏng vĩnh viễn sau một lần lỗi.
        await DoiTonKhoAsync(_idB, TonKhoBanDau);

        var ketQua = await DatHangAsync(userId);

        Assert.Equal(3, ketQua.ItemCount);

        // 2x100.000 + 2x200.000 + 2x300.000
        Assert.Equal(1_200_000m, ketQua.TotalAmount);

        Assert.Equal(TonKhoBanDau - 2, await LayTonKhoAsync(_idA));
        Assert.Equal(TonKhoBanDau - 2, await LayTonKhoAsync(_idB));
        Assert.Equal(TonKhoBanDau - 2, await LayTonKhoAsync(_idC));
    }

    [Fact]
    public async Task San_pham_bi_GO_BAN_thi_tu_bien_khoi_gio_va_dat_duoc_hai_mon_con_lai()
    {
        var userId = await TaoNguoiMuaAsync(soLuongMoiMon: 1);

        // Gỡ bán món giữa. KHÔNG xoá dòng giỏ bằng tay: CartItems -> Products dùng
        // Cascade nên DB tự dọn.
        using (var scope = _factory.Services.CreateScope())
        {
            var context = scope.ServiceProvider.GetRequiredService<MiniMartDbContext>();

            await context.Products.Where(p => p.Id == _idB).ExecuteDeleteAsync();
        }

        // Bản đầu của test này khẳng định NotFoundException - và nó ĐỎ. Lý do: Cascade
        // đã gỡ dòng giỏ nên GetLinesAsync chỉ còn trả về 2 dòng, cả hai đều tra được,
        // và đơn hàng đi tiếp bình thường. Đó mới là hành vi đúng: món đã ngừng bán
        // biến khỏi giỏ, hai món còn lại không việc gì phải bị chặn theo.
        var ketQua = await DatHangAsync(userId);

        Assert.Equal(2, ketQua.ItemCount);
        Assert.Equal(400_000m, ketQua.TotalAmount);      // 1x100.000 + 1x300.000

        Assert.Equal(TonKhoBanDau - 1, await LayTonKhoAsync(_idA));
        Assert.Equal(TonKhoBanDau - 1, await LayTonKhoAsync(_idC));
    }

    // ───────────── Helper ─────────────

    private async Task<Application.Models.CheckoutResult> DatHangAsync(int userId)
    {
        using var scope = _factory.Services.CreateScope();

        GanNguoiDung(scope, userId);

        return await scope.ServiceProvider
            .GetRequiredService<IOrderService>()
            .CheckoutAsync(userId, CheckoutTestData.GiaoHang);
    }

    private static void GanNguoiDung(IServiceScope scope, int userId)
    {
        var accessor = scope.ServiceProvider
            .GetRequiredService<Microsoft.AspNetCore.Http.IHttpContextAccessor>();

        accessor.HttpContext = new Microsoft.AspNetCore.Http.DefaultHttpContext
        {
            User = new System.Security.Claims.ClaimsPrincipal(
                new System.Security.Claims.ClaimsIdentity(
                    [new System.Security.Claims.Claim(
                        System.Security.Claims.ClaimTypes.NameIdentifier, userId.ToString())],
                    "Cookies")),
            RequestServices = scope.ServiceProvider
        };
    }

    private async Task<int> TaoNguoiMuaAsync(int soLuongMoiMon)
    {
        using var scope = _factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<MiniMartDbContext>();

        var user = new User
        {
            Username = $"at_{Guid.NewGuid():N}"[..16],
            PasswordHash = "khong-dung-de-dang-nhap",
            Role = UserRole.Customer
        };

        context.Users.Add(user);
        await context.SaveChangesAsync();

        context.Carts.Add(new Cart
        {
            UserId = user.Id,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow,
            Items =
            [
                new CartItem { ProductId = _idA, Quantity = soLuongMoiMon },
                new CartItem { ProductId = _idB, Quantity = soLuongMoiMon },
                new CartItem { ProductId = _idC, Quantity = soLuongMoiMon }
            ]
        });

        await context.SaveChangesAsync();

        _userIds.Add(user.Id);

        return user.Id;
    }

    private async Task DoiTonKhoAsync(int productId, int stock)
    {
        using var scope = _factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<MiniMartDbContext>();

        await context.Products
            .Where(p => p.Id == productId)
            .ExecuteUpdateAsync(s => s.SetProperty(p => p.Stock, stock));
    }

    private async Task<int> LayTonKhoAsync(int productId)
    {
        using var scope = _factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<MiniMartDbContext>();

        return await context.Products
            .AsNoTracking()
            .Where(p => p.Id == productId)
            .Select(p => p.Stock)
            .SingleAsync();
    }

    public async Task DisposeAsync()
    {
        using var scope = _factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<MiniMartDbContext>();

        await context.OrderDetails.Where(d => _userIds.Contains(d.Order.UserId)).ExecuteDeleteAsync();
        await context.Orders.Where(o => _userIds.Contains(o.UserId)).ExecuteDeleteAsync();
        await context.CartItems.Where(i => _userIds.Contains(i.Cart.UserId)).ExecuteDeleteAsync();
        await context.Carts.Where(c => _userIds.Contains(c.UserId)).ExecuteDeleteAsync();
        await context.Products.Where(p => p.CategoryId == _categoryId).ExecuteDeleteAsync();
        await context.Categories.Where(c => c.Id == _categoryId).ExecuteDeleteAsync();
        await context.Users.Where(u => _userIds.Contains(u.Id)).ExecuteDeleteAsync();

        _factory.Dispose();
    }
}
