using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.Extensions.DependencyInjection;
using MiniMart.Domain.Entities;
using MiniMart.Infrastructure.Data;

namespace MiniMart.Tests;

/// <summary>
/// Ràng buộc DB của Cart/CartItem.
///
/// <para>
/// BẮT BUỘC chạy trên SQL Server thật: unique index, check constraint và
/// delete behavior là hành vi của database engine. EF Core InMemory không thực
/// thi chúng nên mọi test ở đây sẽ xanh kể cả khi migration quên hết ràng buộc.
/// </para>
/// <para>
/// Đây là nửa "sự thật" của quy ước validate ở HAI nơi. Nửa "thông báo tử tế"
/// nằm ở CartService, sẽ làm ở bước 4.
/// </para>
/// </summary>
public class CartSchemaTests : IAsyncLifetime
{
    private readonly WebApplicationFactory<Program> _factory = new();

    private int _userId;
    private int _categoryId;
    private int _productA;
    private int _productB;

    public async Task InitializeAsync()
    {
        using var scope = _factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<MiniMartDbContext>();

        var user = new User
        {
            Username = $"cart_{Guid.NewGuid():N}"[..16],
            PasswordHash = "khong-dung-de-dang-nhap",
            Role = UserRole.Customer
        };

        var category = new Category { Name = $"CT_{Guid.NewGuid():N}"[..14] };
        var a = new Product { Name = "SP A", Price = 10_000m, Stock = 5, Category = category };
        var b = new Product { Name = "SP B", Price = 20_000m, Stock = 5, Category = category };

        context.Users.Add(user);
        context.Products.AddRange(a, b);
        await context.SaveChangesAsync();

        _userId = user.Id;
        _categoryId = category.Id;
        _productA = a.Id;
        _productB = b.Id;
    }

    private async Task<int> TaoGioAsync(int userId)
    {
        using var scope = _factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<MiniMartDbContext>();

        var cart = new Cart { UserId = userId, CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow };
        context.Carts.Add(cart);
        await context.SaveChangesAsync();

        return cart.Id;
    }

    // ───────────── UNIQUE(CartId, ProductId) ─────────────

    [Fact]
    public async Task Cung_san_pham_hai_dong_trong_mot_gio_thi_DB_tu_choi()
    {
        var cartId = await TaoGioAsync(_userId);

        using var scope = _factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<MiniMartDbContext>();

        context.CartItems.Add(new CartItem { CartId = cartId, ProductId = _productA, Quantity = 1 });
        await context.SaveChangesAsync();

        context.CartItems.Add(new CartItem { CartId = cartId, ProductId = _productA, Quantity = 3 });

        // Đây là ràng buộc TRUNG TÂM của thiết kế: nhờ nó mà productId định danh
        // được một dòng, nên API giỏ hàng không cần cartItemId - và do đó không
        // có Id nào của người khác để đoán.
        await Assert.ThrowsAsync<DbUpdateException>(() => context.SaveChangesAsync());
    }

    [Fact]
    public async Task Cung_san_pham_o_HAI_gio_khac_nhau_thi_hop_le()
    {
        var gioMot = await TaoGioAsync(_userId);

        using var scope = _factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<MiniMartDbContext>();

        var userKhac = new User
        {
            Username = $"cart_{Guid.NewGuid():N}"[..16],
            PasswordHash = "x",
            Role = UserRole.Customer
        };
        context.Users.Add(userKhac);
        await context.SaveChangesAsync();

        var gioHai = await TaoGioAsync(userKhac.Id);

        // Unique index là (CartId, ProductId) chứ không phải (ProductId): index
        // trên riêng ProductId sẽ khiến chỉ một người trong toàn hệ thống được
        // mua một sản phẩm - test này khoá điều đó lại.
        context.CartItems.Add(new CartItem { CartId = gioMot, ProductId = _productA, Quantity = 1 });
        context.CartItems.Add(new CartItem { CartId = gioHai, ProductId = _productA, Quantity = 1 });

        await context.SaveChangesAsync();

        Assert.Equal(2, await context.CartItems.CountAsync(i => i.ProductId == _productA));
    }

    [Fact]
    public async Task Hai_san_pham_khac_nhau_trong_cung_mot_gio_thi_hop_le()
    {
        var cartId = await TaoGioAsync(_userId);

        using var scope = _factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<MiniMartDbContext>();

        context.CartItems.Add(new CartItem { CartId = cartId, ProductId = _productA, Quantity = 1 });
        context.CartItems.Add(new CartItem { CartId = cartId, ProductId = _productB, Quantity = 2 });

        await context.SaveChangesAsync();

        Assert.Equal(2, await context.CartItems.CountAsync(i => i.CartId == cartId));
    }

    // ───────────── UNIQUE(UserId) trên Carts ─────────────

    [Fact]
    public async Task Mot_nguoi_dung_khong_the_co_hai_gio()
    {
        await TaoGioAsync(_userId);

        using var scope = _factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<MiniMartDbContext>();

        context.Carts.Add(new Cart
        {
            UserId = _userId,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        });

        // Không có ràng buộc này thì hai request đồng thời của cùng một người sẽ
        // tạo ra hai giỏ, và họ thấy hàng lúc có lúc không tuỳ giỏ nào đọc trước.
        await Assert.ThrowsAsync<DbUpdateException>(() => context.SaveChangesAsync());
    }

    // ───────────── CHECK Quantity > 0 ─────────────

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public async Task So_luong_khong_duong_thi_DB_tu_choi(int soLuong)
    {
        var cartId = await TaoGioAsync(_userId);

        using var scope = _factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<MiniMartDbContext>();

        context.CartItems.Add(new CartItem { CartId = cartId, ProductId = _productA, Quantity = soLuong });

        // Số lượng 0 nghĩa là dòng phải bị XOÁ hẳn, không phải nằm lại với số 0.
        await Assert.ThrowsAsync<DbUpdateException>(() => context.SaveChangesAsync());
    }

    [Fact]
    public async Task Check_constraint_ton_tai_trong_model()
    {
        using var scope = _factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<MiniMartDbContext>();

        // Check constraint bị lược khỏi model runtime nên phải đọc từ design-time model.
        var model = context.GetService<IDesignTimeModel>().Model;
        var rangBuoc = model.FindEntityType(typeof(CartItem))!.GetCheckConstraints();

        Assert.Contains(rangBuoc, c => c.Name == "CK_CartItems_Quantity_Positive");
    }

    // ───────────── Delete behavior ─────────────

    [Fact]
    public async Task Xoa_gio_thi_cac_dong_trong_gio_di_theo()
    {
        var cartId = await TaoGioAsync(_userId);

        using (var scope = _factory.Services.CreateScope())
        {
            var context = scope.ServiceProvider.GetRequiredService<MiniMartDbContext>();
            context.CartItems.Add(new CartItem { CartId = cartId, ProductId = _productA, Quantity = 1 });
            await context.SaveChangesAsync();
        }

        using (var scope = _factory.Services.CreateScope())
        {
            var context = scope.ServiceProvider.GetRequiredService<MiniMartDbContext>();
            await context.Carts.Where(c => c.Id == cartId).ExecuteDeleteAsync();
        }

        using (var scope = _factory.Services.CreateScope())
        {
            var context = scope.ServiceProvider.GetRequiredService<MiniMartDbContext>();
            Assert.Equal(0, await context.CartItems.CountAsync(i => i.CartId == cartId));
        }
    }

    [Fact]
    public async Task Xoa_san_pham_thi_no_bien_khoi_moi_gio_hang()
    {
        var cartId = await TaoGioAsync(_userId);

        using (var scope = _factory.Services.CreateScope())
        {
            var context = scope.ServiceProvider.GetRequiredService<MiniMartDbContext>();
            context.CartItems.Add(new CartItem { CartId = cartId, ProductId = _productB, Quantity = 1 });
            await context.SaveChangesAsync();
        }

        using (var scope = _factory.Services.CreateScope())
        {
            var context = scope.ServiceProvider.GetRequiredService<MiniMartDbContext>();

            // Cascade chứ KHÔNG Restrict, cố ý khác Category -> Product. Nếu là
            // Restrict thì chỉ cần MỘT khách còn sản phẩm trong giỏ là admin
            // không xoá được sản phẩm, mà admin không nhìn thấy giỏ của ai nên
            // sẽ không hiểu vì sao bị chặn.
            await context.Products.Where(p => p.Id == _productB).ExecuteDeleteAsync();
        }

        using (var scope = _factory.Services.CreateScope())
        {
            var context = scope.ServiceProvider.GetRequiredService<MiniMartDbContext>();
            Assert.Equal(0, await context.CartItems.CountAsync(i => i.ProductId == _productB));
        }
    }

    [Fact]
    public async Task Xoa_san_pham_van_bi_chan_boi_Category_Restrict()
    {
        using var scope = _factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<MiniMartDbContext>();

        // Cascade từ CartItem KHÔNG được làm suy yếu Restrict của Category:
        // vẫn không xoá được danh mục còn sản phẩm.
        await Assert.ThrowsAsync<DbUpdateException>(async () =>
        {
            var category = await context.Categories.SingleAsync(c => c.Id == _categoryId);
            context.Categories.Remove(category);
            await context.SaveChangesAsync();
        });
    }

    public async Task DisposeAsync()
    {
        using var scope = _factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<MiniMartDbContext>();

        // Cascade dọn CartItems/Carts theo, nhưng xoá tường minh cho chắc chắn
        // - test không nên phụ thuộc vào chính thứ nó đang kiểm tra.
        await context.CartItems.Where(i => i.Cart.User.Username.StartsWith("cart_")).ExecuteDeleteAsync();
        await context.Carts.Where(c => c.User.Username.StartsWith("cart_")).ExecuteDeleteAsync();
        await context.Products.Where(p => p.CategoryId == _categoryId).ExecuteDeleteAsync();
        await context.Categories.Where(c => c.Id == _categoryId).ExecuteDeleteAsync();
        await context.Users.Where(u => u.Username.StartsWith("cart_")).ExecuteDeleteAsync();

        _factory.Dispose();
    }
}
