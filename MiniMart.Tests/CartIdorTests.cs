using System.Net;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using MiniMart.Domain.Entities;
using MiniMart.Infrastructure.Data;
using MiniMart.Web.Models;

namespace MiniMart.Tests;

/// <summary>
/// IDOR (Insecure Direct Object Reference) trên giỏ hàng.
///
/// <para>
/// Thiết kế cố ý nhận <c>productId</c> chứ không phải <c>cartItemId</c>, nên về
/// nguyên tắc không có tham chiếu trực tiếp nào để lạm dụng: <c>productId</c> luôn
/// được tra trong giỏ của CHÍNH người gửi request. Nhưng "về nguyên tắc" không phải
/// bằng chứng - lập luận đúng mà code sai vẫn là code sai, và một lần refactor thêm
/// lại <c>cartItemId</c> cho tiện sẽ mở lỗ hổng mà không ai nhận ra.
/// </para>
/// <para>
/// Bộ test này khoá tính chất đó lại: kể cả khi kẻ tấn công BIẾT id thật dưới DB,
/// giỏ của người khác vẫn không đổi một dòng nào.
/// </para>
/// </summary>
public class CartIdorTests : IAsyncLifetime
{
    private const string MatKhau = "MatKhau123";

    private readonly WebApplicationFactory<Program> _factory = new();
    private readonly List<string> _usernames = [];

    private int _categoryId;
    private int _productA;
    private int _productB;

    public async Task InitializeAsync()
    {
        using var scope = _factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<MiniMartDbContext>();

        var category = new Category { Name = $"ID_{Guid.NewGuid():N}"[..14] };
        var a = new Product { Name = "SanPhamA", Price = 100_000m, Stock = 50, Category = category };
        var b = new Product { Name = "SanPhamB", Price = 200_000m, Stock = 50, Category = category };

        context.Products.AddRange(a, b);
        await context.SaveChangesAsync();

        _categoryId = category.Id;
        _productA = a.Id;
        _productB = b.Id;
    }

    // ───────────── Ghi: sửa và xoá ─────────────

    [Fact]
    public async Task Nguoi_B_sua_so_luong_cua_CUNG_san_pham_khong_dong_toi_gio_nguoi_A()
    {
        var (clientA, userA) = await TaoNguoiDungCoGioAsync(_productA, 5);
        var (clientB, userB) = await TaoNguoiDungCoGioAsync(_productA, 1);

        using (clientA)
        using (clientB)
        {
            // Cùng một productId, hai giỏ khác nhau. Đây là điểm mấu chốt: productId
            // KHÔNG định danh dòng giỏ hàng, nó chỉ là toạ độ trong giỏ của người gửi.
            var response = await clientB.PostFormAsync("/Cart/UpdateQuantity", new()
            {
                ["ProductId"] = _productA.ToString(),
                ["Quantity"] = "42"
            });

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);

            Assert.Equal(new[] { (_productA, 5) }, await DocGioAsync(userA));
            Assert.Equal(new[] { (_productA, 42) }, await DocGioAsync(userB));
        }
    }

    [Fact]
    public async Task Nguoi_B_xoa_san_pham_chi_xoa_dong_cua_CHINH_minh()
    {
        var (clientA, userA) = await TaoNguoiDungCoGioAsync(_productA, 3);
        var (clientB, userB) = await TaoNguoiDungCoGioAsync(_productA, 3);

        using (clientA)
        using (clientB)
        {
            await clientB.PostFormAsync("/Cart/Remove", new()
            {
                ["ProductId"] = _productA.ToString()
            });

            Assert.Equal(new[] { (_productA, 3) }, await DocGioAsync(userA));
            Assert.Empty(await DocGioAsync(userB));
        }
    }

    [Fact]
    public async Task Biet_id_THAT_cua_dong_CartItem_nguoi_khac_cung_khong_dung_duoc()
    {
        var (clientA, userA) = await TaoNguoiDungCoGioAsync(_productA, 7);
        var (clientB, userB) = await TaoNguoiDungCoGioAsync(_productB, 1);

        using (clientA)
        using (clientB)
        {
            // Kịch bản tệ nhất: kẻ tấn công có sẵn khoá chính của dòng giỏ hàng
            // người khác (rò qua log, qua backup, qua một endpoint khác sau này).
            var cartItemIdCuaA = await LayCartItemIdAsync(userA);

            // Gửi nó vào MỌI ô có thể: ProductId (chỗ duy nhất endpoint đọc), và
            // CartItemId / Id / CartId / UserId (những tên mà một API dùng
            // cartItemId sẽ nhận). Không ô nào chạm được tới giỏ của A.
            await clientB.PostFormAsync("/Cart/UpdateQuantity", new()
            {
                ["ProductId"] = cartItemIdCuaA.ToString(),
                ["CartItemId"] = cartItemIdCuaA.ToString(),
                ["Id"] = cartItemIdCuaA.ToString(),
                ["Quantity"] = "99"
            });

            await clientB.PostFormAsync("/Cart/Remove", new()
            {
                ["ProductId"] = cartItemIdCuaA.ToString(),
                ["CartItemId"] = cartItemIdCuaA.ToString(),
                ["Id"] = cartItemIdCuaA.ToString()
            });

            Assert.Equal(new[] { (_productA, 7) }, await DocGioAsync(userA));
        }
    }

    [Fact]
    public async Task Gui_kem_UserId_va_CartId_cua_nguoi_khac_khong_co_tac_dung()
    {
        var (clientA, userA) = await TaoNguoiDungCoGioAsync(_productA, 4);
        var (clientB, userB) = await TaoNguoiDungCoGioAsync(_productB, 1);

        using (clientA)
        using (clientB)
        {
            var (cartIdCuaA, userIdCuaA) = await LayIdGioVaNguoiDungAsync(userA);

            // Over-posting: cố ghi đè chủ sở hữu của thao tác. Chặn được là nhờ
            // ViewModel riêng (AddToCartRequest chỉ có ProductId + Quantity) - bind
            // thẳng entity CartItem thì hai trường này sẽ được model binder nhận.
            await clientB.PostFormAsync("/Cart/Add", new()
            {
                ["ProductId"] = _productA.ToString(),
                ["Quantity"] = "1",
                ["CartId"] = cartIdCuaA.ToString(),
                ["UserId"] = userIdCuaA.ToString()
            });

            Assert.Equal(new[] { (_productA, 4) }, await DocGioAsync(userA));

            // Và thao tác vẫn đúng với giỏ của CHÍNH B (không phải bị chặn hẳn).
            Assert.Equal(
                new[] { (_productA, 1), (_productB, 1) }.Order().ToArray(),
                (await DocGioAsync(userB)).Order().ToArray());
        }
    }

    // ───────────── Hợp đồng: không có tham chiếu trực tiếp nào để lạm dụng ─────────────

    [Fact]
    public void Request_model_KHONG_he_co_truong_dinh_danh_dong_gio_hang()
    {
        Type[] requests =
        [
            typeof(AddToCartRequest),
            typeof(UpdateCartQuantityRequest),
            typeof(RemoveFromCartRequest)
        ];

        var choPhep = new[] { "ProductId", "Quantity" };

        foreach (var request in requests)
        {
            var ten = request.GetProperties().Select(p => p.Name).ToArray();

            // Đây là test CẤU TRÚC, không phải hành vi: nó tố giác ngay lúc có ai
            // thêm CartItemId/CartId/UserId vào hợp đồng, thay vì chờ tới lúc lỗ
            // hổng bị khai thác. Ba test ở trên chứng minh hôm nay an toàn; test này
            // chứng minh cách thiết kế đang giữ cho nó an toàn.
            Assert.Equal(choPhep.Intersect(ten).Order(), ten.Order());
        }
    }

    // ───────────── Đọc ─────────────

    [Fact]
    public async Task Trang_gio_hang_chi_hien_hang_cua_CHINH_minh()
    {
        var (clientA, _) = await TaoNguoiDungCoGioAsync(_productA, 1);
        var (clientB, _) = await TaoNguoiDungCoGioAsync(_productB, 1);

        using (clientA)
        using (clientB)
        {
            var htmlB = await clientB.GetStringAsync("/Cart");

            // Không có route nào nhận id giỏ hàng, nên không có gì để đoán. Test này
            // khoá điều đó: thêm /Cart/{id} sau này sẽ làm nó đỏ nếu id không được
            // kiểm chủ sở hữu.
            Assert.Contains("SanPhamB", htmlB);
            Assert.DoesNotContain("SanPhamA", htmlB);
        }
    }

    [Fact]
    public async Task Khach_vang_lai_khong_cham_duoc_gio_duoi_DB()
    {
        var (clientA, userA) = await TaoNguoiDungCoGioAsync(_productA, 6);

        using var _ = clientA;
        using var khach = CreateClient();

        await khach.PostFormAsync("/Cart/Remove", new()
        {
            ["ProductId"] = _productA.ToString()
        });

        await khach.PostFormAsync("/Cart/UpdateQuantity", new()
        {
            ["ProductId"] = _productA.ToString(),
            ["Quantity"] = "1"
        });

        // Khách vãng lai được factory định tuyến sang SessionCartStore, nên mọi
        // thao tác của họ nằm gọn trong Session của chính họ - không có đường nào
        // đi tới bảng CartItems.
        Assert.Equal(new[] { (_productA, 6) }, await DocGioAsync(userA));
    }

    // ───────────── Helper ─────────────

    private HttpClient CreateClient() =>
        _factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = true });

    /// <summary>Tạo một người dùng mới đã đăng nhập, giỏ có sẵn một sản phẩm.</summary>
    private async Task<(HttpClient Client, string Username)> TaoNguoiDungCoGioAsync(
        int productId, int quantity)
    {
        var client = CreateClient();
        var username = $"id_{Guid.NewGuid():N}"[..16];

        _usernames.Add(username);

        await client.PostFormAsync("/Account/Register", new()
        {
            ["Username"] = username,
            ["Password"] = MatKhau,
            ["ConfirmPassword"] = MatKhau
        });

        await client.PostFormAsync("/Cart/Add", new()
        {
            ["ProductId"] = productId.ToString(),
            ["Quantity"] = quantity.ToString()
        });

        return (client, username);
    }

    private async Task<(int ProductId, int Quantity)[]> DocGioAsync(string username)
    {
        using var scope = _factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<MiniMartDbContext>();

        var dong = await context.CartItems
            .AsNoTracking()
            .Where(i => i.Cart.User.Username == username)
            .OrderBy(i => i.ProductId)
            .Select(i => new { i.ProductId, i.Quantity })
            .ToListAsync();

        return dong.Select(d => (d.ProductId, d.Quantity)).ToArray();
    }

    private async Task<int> LayCartItemIdAsync(string username)
    {
        using var scope = _factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<MiniMartDbContext>();

        return await context.CartItems
            .AsNoTracking()
            .Where(i => i.Cart.User.Username == username)
            .Select(i => i.Id)
            .SingleAsync();
    }

    private async Task<(int CartId, int UserId)> LayIdGioVaNguoiDungAsync(string username)
    {
        using var scope = _factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<MiniMartDbContext>();

        var gio = await context.Carts
            .AsNoTracking()
            .Where(c => c.User.Username == username)
            .Select(c => new { c.Id, c.UserId })
            .SingleAsync();

        return (gio.Id, gio.UserId);
    }

    public async Task DisposeAsync()
    {
        using var scope = _factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<MiniMartDbContext>();

        await context.CartItems.Where(i => _usernames.Contains(i.Cart.User.Username)).ExecuteDeleteAsync();
        await context.Carts.Where(c => _usernames.Contains(c.User.Username)).ExecuteDeleteAsync();
        await context.Products.Where(p => p.CategoryId == _categoryId).ExecuteDeleteAsync();
        await context.Categories.Where(c => c.Id == _categoryId).ExecuteDeleteAsync();
        await context.Users.Where(u => _usernames.Contains(u.Username)).ExecuteDeleteAsync();

        _factory.Dispose();
    }
}
