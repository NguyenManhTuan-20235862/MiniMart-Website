using System.Net;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using MiniMart.Domain.Entities;
using MiniMart.Infrastructure.Data;

namespace MiniMart.Tests;

/// <summary>
/// Trang xem lại giỏ hàng trước khi đặt (<c>GET /Checkout</c>).
///
/// <para>
/// Ba thứ đáng khoá ở bước này: <c>[Authorize]</c> thật sự chặn khách vãng lai, giỏ rỗng
/// không render được trang xác nhận, và bảng ở đây là CHỈ ĐỌC - không có ô sửa số lượng
/// hay nút xoá. Cái thứ ba là loại lỗi không có exception nào báo: form vẫn render, chỉ
/// là bấm vào thì người dùng bị đá khỏi luồng đặt hàng.
/// </para>
/// </summary>
public class CheckoutPageTests : IAsyncLifetime
{
    private const string MatKhau = "MatKhau123";

    private readonly WebApplicationFactory<Program> _factory = new();
    private readonly List<string> _usernames = [];

    private int _categoryId;
    private int _productConHang;
    private int _productItHang;

    public async Task InitializeAsync()
    {
        using var scope = _factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<MiniMartDbContext>();

        var category = new Category { Name = $"CO_{Guid.NewGuid():N}"[..14] };
        var conHang = new Product { Name = "ConHang", Price = 100_000m, Stock = 50, Category = category };
        var itHang = new Product { Name = "ItHang", Price = 200_000m, Stock = 2, Category = category };

        context.Products.AddRange(conHang, itHang);
        await context.SaveChangesAsync();

        _categoryId = category.Id;
        _productConHang = conHang.Id;
        _productItHang = itHang.Id;
    }

    // ───────────── Bảo vệ bằng [Authorize] ─────────────

    [Fact]
    public async Task Khach_vang_lai_bi_day_sang_trang_dang_nhap_kem_ReturnUrl()
    {
        using var client = CreateClient(theoRedirect: false);

        var response = await client.GetAsync("/Checkout");

        // Đặt hàng cần một tài khoản để gắn đơn vào, khác CartController (khách vãng lai
        // vẫn phải mua được hàng). Bỏ [Authorize] thì ICurrentUser.Id là null và bước
        // tạo đơn ở sau sẽ đổ ở khoá ngoại - muộn hơn nhiều so với ở đây.
        Assert.Equal(HttpStatusCode.Found, response.StatusCode);

        var location = response.Headers.Location!.ToString();

        Assert.Contains("/Account/Login", location);

        // ReturnUrl là thứ đưa họ trở lại đúng /Checkout sau khi đăng nhập. Thiếu nó thì
        // người dùng bị bỏ ở trang chủ và phải tự tìm lại đường.
        Assert.Contains("ReturnUrl", location);
        Assert.Contains("Checkout", Uri.UnescapeDataString(location));
    }

    [Fact]
    public async Task Dang_nhap_xong_thi_gio_Session_da_gop_va_Checkout_mo_duoc()
    {
        using var client = CreateClient();

        // Bỏ hàng vào giỏ khi CHƯA có tài khoản - đúng hành vi thật.
        await client.PostFormAsync("/Cart/Add", new()
        {
            ["ProductId"] = _productConHang.ToString(),
            ["Quantity"] = "2"
        });

        await DangKyAsync(client);

        var html = await client.GetStringAsync("/Checkout");

        // Luồng này không cần code mới ở Phase 5: gộp giỏ đã làm ở Phase 4 bước 6.
        // Test ở đây để khoá lại rằng hai phase khớp nhau.
        Assert.Contains("Xác nhận đặt hàng", html);
        Assert.Contains("ConHang", html);
        Assert.Contains("200,000", html);
    }

    // ───────────── Giỏ rỗng ─────────────

    [Fact]
    public async Task Gio_rong_thi_KHONG_render_trang_xac_nhan_ma_ve_lai_gio_hang()
    {
        using var client = CreateClient(theoRedirect: false);
        await DangKyAsync(client);

        var response = await client.GetAsync("/Checkout");

        // Trang "xác nhận đặt hàng" không có gì để xác nhận là trang vô nghĩa, và nó
        // còn mở đường tới một POST đặt đơn rỗng.
        Assert.Equal(HttpStatusCode.Found, response.StatusCode);
        Assert.Equal("/Cart", response.Headers.Location!.ToString());
    }

    [Fact]
    public async Task Gio_rong_thi_giai_thich_ly_do_chu_khong_im_lang_chuyen_trang()
    {
        using var client = CreateClient();
        await DangKyAsync(client);

        // Client tự theo redirect nên đây là HTML của /Cart, đọc TempData ngay lần này.
        var html = await client.GetStringAsync("/Checkout");

        Assert.Contains("Giỏ hàng đang trống nên chưa thể đặt hàng", html);
    }

    // ───────────── Bảng chỉ đọc ─────────────

    [Fact]
    public async Task Bang_o_Checkout_KHONG_co_o_sua_so_luong_hay_nut_xoa()
    {
        using var client = await TaoClientCoGioAsync(_productConHang, 3);

        var html = await client.GetStringAsync("/Checkout");

        // Cho sửa ngay tại đây thì form POST về /Cart/UpdateQuantity rồi redirect về
        // /Cart - người dùng bị đá khỏi luồng đặt hàng mà không hiểu vì sao.
        Assert.DoesNotContain("action=\"/Cart/UpdateQuantity\"", html);
        Assert.DoesNotContain("action=\"/Cart/Remove\"", html);
        Assert.DoesNotContain("name=\"Quantity\"", html);
    }

    [Fact]
    public async Task Bang_o_Checkout_van_hien_du_so_lieu_de_doi_chieu()
    {
        using var client = await TaoClientCoGioAsync(_productConHang, 3);

        var html = await client.GetStringAsync("/Checkout");

        // Ẩn phần sửa được KHÔNG được kéo theo mất số liệu: người dùng phải đối chiếu
        // được tên, đơn giá, số lượng, thành tiền và tổng cộng trước khi xác nhận.
        Assert.Contains("ConHang", html);
        Assert.Contains("100,000", html);     // đơn giá
        Assert.Contains("300,000", html);     // thành tiền = tổng cộng
        Assert.Contains(">3</span>", html);   // số lượng dạng chữ, không phải input
    }

    [Fact]
    public async Task So_cot_cua_hai_hang_khop_nhau_khi_an_cot_thao_tac()
    {
        using var client = await TaoClientCoGioAsync(_productConHang, 1);

        var html = await client.GetStringAsync("/Checkout");

        // Ẩn <td> mà quên ẩn <th> (hoặc quên ô rỗng ở tfoot) làm bảng lệch cột. HTML sai
        // kiểu này KHÔNG gây lỗi nào - trình duyệt vẫn vẽ, chỉ là vẽ lệch.
        var soTh = Regex.Matches(html, "<th\\b").Count;
        var soTdHangDau = Regex.Matches(LayHangDauTienAsync(html), "<td\\b").Count;

        Assert.Equal(4, soTh);
        Assert.Equal(4, soTdHangDau);
    }

    // ───────────── Trang /Cart dẫn được sang đây ─────────────

    [Fact]
    public async Task Trang_gio_hang_co_nut_dan_sang_Checkout()
    {
        using var client = await TaoClientCoGioAsync(_productConHang, 1);

        var html = await client.GetStringAsync("/Cart");

        // Là thẻ <a> (GET) chứ không phải form POST: nó chỉ điều hướng sang trang xem
        // lại, chưa tạo gì cả.
        Assert.Contains("href=\"/Checkout\"", html);
        Assert.Contains("Tiến hành đặt hàng", html);
    }

    [Fact]
    public async Task Gio_rong_thi_trang_gio_hang_KHONG_hien_nut_dat_hang()
    {
        using var client = CreateClient();

        var html = await client.GetStringAsync("/Cart");

        // Nút dẫn tới một trang chắc chắn sẽ đẩy họ quay lại là nút gây bối rối.
        Assert.DoesNotContain("Tiến hành đặt hàng", html);
    }

    [Fact]
    public async Task Trang_Checkout_KHONG_hien_lai_nut_dat_hang()
    {
        using var client = await TaoClientCoGioAsync(_productConHang, 1);

        var html = await client.GetStringAsync("/Checkout");

        // Nút "Tiến hành đặt hàng" nằm trong _CartTable nên phải bị cờ ChoPhepSua ẩn đi,
        // nếu không trang tự trỏ về chính nó.
        Assert.DoesNotContain("Tiến hành đặt hàng", html);
    }

    // ───────────── Cảnh báo, không chặn ─────────────

    [Fact]
    public async Task Khong_du_hang_thi_canh_bao_nhung_trang_van_mo_duoc()
    {
        using var client = await TaoClientCoGioAsync(_productItHang, 2);

        // Tồn kho tụt SAU khi đã vào giỏ - kịch bản thật, giỏ không giữ hàng.
        await DoiTonKhoAsync(_productItHang, 1);

        var response = await client.GetAsync("/Checkout");
        var html = await response.Content.ReadAsStringAsync();

        // Chặn mở trang là người dùng không đọc được vấn đề nằm ở sản phẩm nào.
        // Việc chặn thuộc về POST đặt hàng - đó là lúc dữ liệu thật sự được ghi.
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("không còn đủ hàng", html);
        Assert.Contains("Không đủ hàng", html);      // badge trên đúng dòng đó
    }

    [Fact]
    public async Task Trang_noi_ro_gia_se_duoc_chot_luc_xac_nhan()
    {
        using var client = await TaoClientCoGioAsync(_productConHang, 1);

        var html = await client.GetStringAsync("/Checkout");

        // Giỏ hàng cố ý hiện giá HIỆN TẠI (không snapshot). Người dùng phải được nói
        // trước điều đó, vì giá có thể đổi giữa lúc xem và lúc bấm xác nhận.
        Assert.Contains("chốt lại vào đúng thời điểm bạn xác nhận", html);
    }

    [Fact]
    public async Task Nut_xac_nhan_da_noi_duoc_vao_POST_dat_hang()
    {
        using var client = await TaoClientCoGioAsync(_productConHang, 1);

        var html = await client.GetStringAsync("/Checkout");

        var nut = Regex.Match(html, "<button[^>]*>\\s*Xác nhận đặt hàng\\s*</button>").Value;

        // Bản trước của test này khẳng định nút ĐANG `disabled`, vì action POST chưa
        // tồn tại và một nút trỏ tới action chưa có sẽ cho 404 khi bấm. Nó được viết
        // để CỐ Ý đỏ khi POST xuất hiện - và nó đã đỏ đúng lúc đó, nên được sửa thành
        // khẳng định ngược lại thay vì để một nút chết nằm lại trong giao diện.
        Assert.DoesNotContain("disabled", nut);
        Assert.Contains("action=\"/Checkout/Confirm\"", html);
    }

    // ───────────── Helper ─────────────

    private HttpClient CreateClient(bool theoRedirect = true) =>
        _factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = theoRedirect });

    private async Task<HttpClient> TaoClientCoGioAsync(int productId, int quantity)
    {
        var client = CreateClient();

        await DangKyAsync(client);

        await client.PostFormAsync("/Cart/Add", new()
        {
            ["ProductId"] = productId.ToString(),
            ["Quantity"] = quantity.ToString()
        });

        return client;
    }

    private async Task<string> DangKyAsync(HttpClient client)
    {
        var username = TestAuthExtensions.SinhUsername("co");

        _usernames.Add(username);
        await client.DangKyAsync(username, MatKhau);

        return username;
    }

    private async Task DoiTonKhoAsync(int productId, int stock)
    {
        using var scope = _factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<MiniMartDbContext>();

        await context.Products
            .Where(p => p.Id == productId)
            .ExecuteUpdateAsync(s => s.SetProperty(p => p.Stock, stock));
    }

    /// <summary>
    /// Hàng đầu tiên của <c>tbody</c>. Tách riêng để phần đếm cột không phải đọc cả
    /// bảng - <c>tfoot</c> có số cột khác nên trộn vào là đếm sai.
    /// </summary>
    private static string LayHangDauTienAsync(string html)
    {
        var tbody = Regex.Match(html, "<tbody>(.*?)</tbody>", RegexOptions.Singleline);

        Assert.True(tbody.Success, "Không tìm thấy tbody trong bảng giỏ hàng.");

        var tr = Regex.Match(tbody.Groups[1].Value, "<tr>(.*?)</tr>", RegexOptions.Singleline);

        Assert.True(tr.Success, "Không tìm thấy hàng nào trong tbody.");

        return tr.Groups[1].Value;
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
