using System.Net;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using MiniMart.Domain.Entities;
using MiniMart.Infrastructure.Data;

namespace MiniMart.Tests;

/// <summary>
/// <c>POST /Checkout/Confirm</c> và trang cảm ơn - qua pipeline HTTP thật.
///
/// <para>
/// Điểm cần khoá chặt nhất ở đây là <c>userId</c>: nó đến từ cookie đã ký chứ không
/// từ request, và trang <c>Success</c> chỉ cho xem đơn của chính mình.
/// </para>
/// </summary>
public class CheckoutConfirmTests : IAsyncLifetime
{
    private const string MatKhau = "MatKhau123";

    private readonly WebApplicationFactory<Program> _factory = new();
    private readonly List<string> _usernames = [];

    private int _categoryId;
    private int _productId;
    private int _productItHang;

    public async Task InitializeAsync()
    {
        using var scope = _factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<MiniMartDbContext>();

        var category = new Category { Name = $"CF_{Guid.NewGuid():N}"[..14] };
        var product = new Product { Name = "ConHang", Price = 150_000m, Stock = 20, Category = category };
        var itHang = new Product { Name = "ItHang", Price = 90_000m, Stock = 3, Category = category };

        context.Products.AddRange(product, itHang);
        await context.SaveChangesAsync();

        _categoryId = category.Id;
        _productId = product.Id;
        _productItHang = itHang.Id;
    }

    // ───────────── Đường thành công ─────────────

    [Fact]
    public async Task Dat_hang_thanh_cong_thi_Post_Redirect_Get_sang_trang_cam_on()
    {
        // Một client duy nhất xuyên suốt: mỗi HttpClient của WebApplicationFactory có
        // CookieContainer RIÊNG, nên không chép được phiên sang client thứ hai.
        var (client, _) = await TaoNguoiMuaAsync(_productId, 2, theoRedirect: false);

        var response = await client.PostFormAsync("/Checkout/Confirm", new());

        // PRG bắt buộc: không redirect thì bấm F5 sau khi đặt hàng sẽ hỏi gửi lại
        // form, và lần này gửi lại nghĩa là đặt THÊM một đơn nữa.
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);

        // Dạng đường dẫn `/Checkout/Success/298`, KHÔNG phải `?id=298`: route mặc
        // định là {controller}/{action}/{id?} nên tham số tên `id` được đặt vào
        // segment. Đoán sai chỗ này là test đỏ mà code vẫn đúng.
        Assert.Matches(@"^/Checkout/Success/\d+$", response.Headers.Location!.ToString());
    }

    [Fact]
    public async Task Trang_cam_on_hien_dung_so_lieu_da_snapshot()
    {
        var (client, _) = await TaoNguoiMuaAsync(_productId, 2);

        var html = await (await client.PostFormAsync("/Checkout/Confirm", new()))
            .Content.ReadAsStringAsync();

        Assert.Contains("Đặt hàng thành công", html);
        Assert.Contains("ConHang", html);
        Assert.Contains("150,000", html);   // đơn giá đã snapshot
        Assert.Contains("300,000", html);   // tổng cộng
    }

    [Fact]
    public async Task Dat_hang_xong_thi_gio_rong_va_ton_kho_giam()
    {
        var (client, username) = await TaoNguoiMuaAsync(_productId, 3);

        await client.PostFormAsync("/Checkout/Confirm", new());

        using var scope = _factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<MiniMartDbContext>();

        Assert.False(await context.CartItems.AnyAsync(i => i.Cart.User.Username == username));
        Assert.Equal(17, await context.Products.Where(p => p.Id == _productId)
            .Select(p => p.Stock).SingleAsync());
    }

    [Fact]
    public async Task Gia_trong_don_KHONG_doi_khi_shop_doi_gia_sau_do()
    {
        var (client, username) = await TaoNguoiMuaAsync(_productId, 1);

        await client.PostFormAsync("/Checkout/Confirm", new());

        using (var scope = _factory.Services.CreateScope())
        {
            var context = scope.ServiceProvider.GetRequiredService<MiniMartDbContext>();

            await context.Products.Where(p => p.Id == _productId)
                .ExecuteUpdateAsync(s => s.SetProperty(p => p.Price, 999_000m));
        }

        var orderId = await LayOrderIdAsync(username);
        var html = await client.GetStringAsync($"/Checkout/Success?id={orderId}");

        // Lý do tồn tại của snapshot: hoá đơn phải đọc lại được đúng con số hôm nay.
        Assert.Contains("150,000", html);
        Assert.DoesNotContain("999,000", html);
    }

    // ───────────── Bảo mật ─────────────

    [Fact]
    public async Task Khong_xem_duoc_don_hang_cua_NGUOI_KHAC()
    {
        var (clientA, usernameA) = await TaoNguoiMuaAsync(_productId, 1);
        await clientA.PostFormAsync("/Checkout/Confirm", new());

        var orderIdCuaA = await LayOrderIdAsync(usernameA);

        var (clientB, _) = await TaoNguoiMuaAsync(_productId, 1);

        var response = await clientB.GetAsync($"/Checkout/Success?id={orderIdCuaA}");

        // GetMyOrderAsync lọc theo userId NGAY TRONG truy vấn. 404 cho cả "không tồn
        // tại" lẫn "của người khác" - phân biệt hai cái là để lộ đơn số đó có thật.
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Gui_kem_userId_cua_nguoi_khac_khong_co_tac_dung()
    {
        var (clientA, usernameA) = await TaoNguoiMuaAsync(_productId, 1);
        var (clientB, usernameB) = await TaoNguoiMuaAsync(_productId, 1);

        var userIdCuaA = await LayUserIdAsync(usernameA);

        // Cố ép đơn hàng sang tên người khác qua form.
        await clientB.PostFormAsync("/Checkout/Confirm", new()
        {
            ["userId"] = userIdCuaA.ToString(),
            ["UserId"] = userIdCuaA.ToString()
        });

        using var scope = _factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<MiniMartDbContext>();

        // Confirm KHÔNG nhận tham số nào từ request - userId đến từ cookie đã ký.
        // Nhận nó từ form là đặt đơn và trừ tồn kho dưới tên người khác.
        Assert.False(await context.Orders.AnyAsync(o => o.User.Username == usernameA));
        Assert.True(await context.Orders.AnyAsync(o => o.User.Username == usernameB));
    }

    [Fact]
    public async Task Khach_vang_lai_khong_POST_Confirm_duoc()
    {
        using var client = TaoClient(theoRedirect: false);

        var response = await client.PostFormAsync("/Checkout/Confirm", new());

        // [Authorize] ở cấp class nên action thêm sau tự động được bảo vệ.
        Assert.Equal(HttpStatusCode.Found, response.StatusCode);
        Assert.Contains("/Account/Login", response.Headers.Location!.ToString());
    }

    [Fact]
    public async Task Confirm_KHONG_nhan_GET()
    {
        var (client, username) = await TaoNguoiMuaAsync(_productId, 1);

        var response = await client.GetAsync("/Checkout/Confirm");

        // Nếu đặt hàng nhận GET thì chỉ cần nhúng <img src="/Checkout/Confirm"> vào
        // một trang bất kỳ là đặt đơn hộ người khác - antiforgery không chặn được GET.
        Assert.Contains(
            response.StatusCode,
            new[] { HttpStatusCode.NotFound, HttpStatusCode.MethodNotAllowed });

        using var scope = _factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<MiniMartDbContext>();

        Assert.False(await context.Orders.AnyAsync(o => o.User.Username == username));
    }

    // ───────────── Đường lỗi ─────────────

    [Fact]
    public async Task Khong_du_hang_thi_ve_gio_hang_kem_thong_bao_ro_rang()
    {
        var (client, username) = await TaoNguoiMuaAsync(_productItHang, 3);

        // Tồn kho tụt sau khi đã vào giỏ - giỏ hàng không giữ hàng.
        using (var scope = _factory.Services.CreateScope())
        {
            var context = scope.ServiceProvider.GetRequiredService<MiniMartDbContext>();

            await context.Products.Where(p => p.Id == _productItHang)
                .ExecuteUpdateAsync(s => s.SetProperty(p => p.Stock, 1));
        }

        var html = await (await client.PostFormAsync("/Checkout/Confirm", new()))
            .Content.ReadAsStringAsync();

        // Đẩy về /Cart chứ không render lại /Checkout: người dùng cần SỬA giỏ, mà
        // trang Checkout cố ý không sửa được.
        Assert.Contains("ItHang", html);
        Assert.Contains("chỉ còn 1", html);

        // Và tuyệt đối không được tạo đơn.
        using var scope2 = _factory.Services.CreateScope();
        var context2 = scope2.ServiceProvider.GetRequiredService<MiniMartDbContext>();

        Assert.False(await context2.Orders.AnyAsync(o => o.User.Username == username));
    }

    [Fact]
    public async Task Gio_rong_thi_khong_tao_don_0_dong()
    {
        var (client, username) = await TaoNguoiMuaAsync(_productId, 1);

        await client.PostFormAsync("/Cart/Remove", new()
        {
            ["ProductId"] = _productId.ToString()
        });

        var html = await (await client.PostFormAsync("/Checkout/Confirm", new()))
            .Content.ReadAsStringAsync();

        Assert.Contains("Giỏ hàng đang trống", html);

        using var scope = _factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<MiniMartDbContext>();

        Assert.False(await context.Orders.AnyAsync(o => o.User.Username == username));
    }

    [Fact]
    public async Task POST_thieu_antiforgery_token_thi_bi_tu_choi()
    {
        var (client, username) = await TaoNguoiMuaAsync(_productId, 1);

        var response = await client.PostAsync("/Checkout/Confirm", new FormUrlEncodedContent([]));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

        using var scope = _factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<MiniMartDbContext>();

        Assert.False(await context.Orders.AnyAsync(o => o.User.Username == username));
    }

    // ───────────── Nút trên trang Checkout ─────────────

    [Fact]
    public async Task Trang_Checkout_co_form_POST_toi_Confirm_va_nut_KHONG_con_disabled()
    {
        var (client, _) = await TaoNguoiMuaAsync(_productId, 1);

        var html = await client.GetStringAsync("/Checkout");

        Assert.Contains("action=\"/Checkout/Confirm\"", html);

        var nut = Regex.Match(html, "<button[^>]*>\\s*Xác nhận đặt hàng\\s*</button>").Value;

        // Ở bước trước nút này cố ý disabled vì action chưa tồn tại. Giờ action đã
        // có nên nút phải bấm được - test cũ đã được sửa cùng lúc với thay đổi này.
        Assert.DoesNotContain("disabled", nut);
        Assert.Contains("type=\"submit\"", nut);
    }

    // ───────────── Helper ─────────────

    private HttpClient TaoClient(bool theoRedirect = true) =>
        _factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = theoRedirect });

    private async Task<(HttpClient Client, string Username)> TaoNguoiMuaAsync(
        int productId, int soLuong, bool theoRedirect = true)
    {
        var client = TaoClient(theoRedirect);
        var username = $"cf_{Guid.NewGuid():N}"[..16];

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
            ["Quantity"] = soLuong.ToString()
        });

        return (client, username);
    }

    private async Task<int> LayOrderIdAsync(string username)
    {
        using var scope = _factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<MiniMartDbContext>();

        return await context.Orders
            .AsNoTracking()
            .Where(o => o.User.Username == username)
            .Select(o => o.Id)
            .SingleAsync();
    }

    private async Task<int> LayUserIdAsync(string username)
    {
        using var scope = _factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<MiniMartDbContext>();

        return await context.Users
            .AsNoTracking()
            .Where(u => u.Username == username)
            .Select(u => u.Id)
            .SingleAsync();
    }

    public async Task DisposeAsync()
    {
        using var scope = _factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<MiniMartDbContext>();

        await context.OrderDetails.Where(d => _usernames.Contains(d.Order.User.Username)).ExecuteDeleteAsync();
        await context.Orders.Where(o => _usernames.Contains(o.User.Username)).ExecuteDeleteAsync();
        await context.CartItems.Where(i => _usernames.Contains(i.Cart.User.Username)).ExecuteDeleteAsync();
        await context.Carts.Where(c => _usernames.Contains(c.User.Username)).ExecuteDeleteAsync();
        await context.Products.Where(p => p.CategoryId == _categoryId).ExecuteDeleteAsync();
        await context.Categories.Where(c => c.Id == _categoryId).ExecuteDeleteAsync();
        await context.Users.Where(u => _usernames.Contains(u.Username)).ExecuteDeleteAsync();

        _factory.Dispose();
    }
}
