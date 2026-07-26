using System.Net;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using MiniMart.Domain.Entities;
using MiniMart.Infrastructure.Data;
using MiniMart.Web.Controllers;

namespace MiniMart.Tests;

/// <summary>
/// CartController qua pipeline HTTP thật: gồm Session, antiforgery, model binding
/// và cả việc chọn kho lưu trữ theo trạng thái đăng nhập.
///
/// <para>
/// Đây cũng là chỗ đầu tiên kiểm chứng được VỊ TRÍ của <c>app.UseSession()</c>:
/// đặt sau phần map endpoint thì mọi request giỏ hàng của khách vãng lai sẽ ném
/// "Session has not been configured for this application or request" - test ở
/// bước 3 không bắt được điều này.
/// </para>
/// </summary>
public class CartControllerTests : IAsyncLifetime
{
    private readonly WebApplicationFactory<Program> _factory = new();
    private readonly List<string> _usernames = [];

    private int _categoryId;
    private int _productConHang;
    private int _productItHang;
    private int _productHetHang;

    public async Task InitializeAsync()
    {
        using var scope = _factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<MiniMartDbContext>();

        var category = new Category { Name = $"CB_{Guid.NewGuid():N}"[..14] };
        var conHang = new Product { Name = "ConHang", Price = 100_000m, Stock = 50, Category = category };
        var itHang = new Product { Name = "ItHang", Price = 200_000m, Stock = 2, Category = category };
        var hetHang = new Product { Name = "HetHang", Price = 300_000m, Stock = 0, Category = category };

        context.Products.AddRange(conHang, itHang, hetHang);
        await context.SaveChangesAsync();

        _categoryId = category.Id;
        _productConHang = conHang.Id;
        _productItHang = itHang.Id;
        _productHetHang = hetHang.Id;
    }

    private HttpClient CreateClient(bool theoRedirect = true) =>
        _factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = theoRedirect });

    // ───────────── Khách vãng lai: Session ─────────────

    [Fact]
    public async Task Trang_gio_hang_mo_duoc_khi_chua_dang_nhap()
    {
        using var client = CreateClient();

        var response = await client.GetAsync("/Cart");

        // Giỏ hàng KHÔNG có [Authorize]: khách vãng lai cũng phải mua được hàng.
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("Giỏ hàng đang trống", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Them_vao_gio_bang_form_thuong_thi_Post_Redirect_Get()
    {
        using var client = CreateClient(theoRedirect: false);

        var response = await ThemVaoGioAsync(client, _productConHang, 2);

        // Không có nhánh redirect này thì người dùng không bật JavaScript sẽ nhận
        // về một mảnh HTML rời làm cả trang, và bấm F5 sẽ hỏi gửi lại form.
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        // Location ở đây là URI TƯƠNG ĐỐI ("/Cart"), nên .PathAndQuery ném
        // InvalidOperationException. Dùng ToString() để không phụ thuộc việc
        // framework trả tuyệt đối hay tương đối.
        Assert.Equal("/Cart", response.Headers.Location!.ToString());
    }

    [Fact]
    public async Task Gio_hang_cua_khach_vang_lai_song_qua_nhieu_request()
    {
        using var client = CreateClient();

        await ThemVaoGioAsync(client, _productConHang, 2);

        var html = await client.GetStringAsync("/Cart");

        // Session sống được là nhờ cookie MiniMart.Session; HttpClient của
        // WebApplicationFactory giữ cookie giữa các request giống trình duyệt.
        Assert.Contains("ConHang", html);
        Assert.Contains("value=\"2\"", html);
    }

    [Fact]
    public async Task Them_cung_san_pham_hai_lan_thi_CONG_DON()
    {
        using var client = CreateClient();

        await ThemVaoGioAsync(client, _productConHang, 2);
        await ThemVaoGioAsync(client, _productConHang, 3);

        var html = await client.GetStringAsync("/Cart");

        Assert.Contains("value=\"5\"", html);
        Assert.Equal(1, DemDongGioHang(html));
    }

    [Fact]
    public async Task Khach_vang_lai_KHONG_tao_ban_ghi_nao_duoi_DB()
    {
        using var client = CreateClient();

        await ThemVaoGioAsync(client, _productConHang, 1);

        using var scope = _factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<MiniMartDbContext>();

        // Factory phải định tuyến khách vãng lai sang SessionCartStore. Nếu nó
        // chọn nhầm DatabaseCartStore thì DatabaseCartStore sẽ ném vì
        // ICurrentUser.Id là null - nhưng nếu ai đó "sửa" cho nó không ném nữa
        // thì bảng CartItems sẽ đầy rác của khách chưa đăng nhập.
        Assert.Equal(0, await context.CartItems
            .CountAsync(i => i.ProductId == _productConHang));
    }

    // ───────────── Đã đăng nhập: DB ─────────────

    [Fact]
    public async Task Nguoi_da_dang_nhap_thi_gio_hang_nam_duoi_DB()
    {
        using var client = await TaoClientDaDangNhapAsync();

        await ThemVaoGioAsync(client, _productConHang, 3);

        using var scope = _factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<MiniMartDbContext>();

        var item = await context.CartItems
            .SingleAsync(i => i.ProductId == _productConHang);

        Assert.Equal(3, item.Quantity);
    }

    // ───────────── AJAX ─────────────

    [Fact]
    public async Task Request_AJAX_tra_PartialView_KHONG_kem_layout()
    {
        using var client = CreateClient();

        var response = await ThemVaoGioAsync(client, _productConHang, 1, ajax: true);
        var html = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("ConHang", html);

        // PartialView -> View là lỗi build-được-nhưng-sai: response sẽ kèm cả
        // html/head/navbar và dán vào giữa trang thành một trang lồng trong trang.
        Assert.DoesNotContain("<!DOCTYPE", html);
        Assert.DoesNotContain("navbar", html);
    }

    [Fact]
    public async Task Header_X_Cart_Count_mang_tong_so_MON_khong_phai_so_dong()
    {
        using var client = CreateClient();

        await ThemVaoGioAsync(client, _productConHang, 2, ajax: true);
        var response = await ThemVaoGioAsync(client, _productItHang, 1, ajax: true);

        // 2 + 1 = 3 món trong 2 dòng. Badge trên navbar hiện 3.
        Assert.Equal("3", LayHeader(response, CartController.CartCountHeader));
    }

    // ───────────── Kết cục nghiệp vụ ─────────────

    [Fact]
    public async Task Them_qua_ton_kho_thi_kep_lai_va_hien_thong_bao()
    {
        using var client = CreateClient();

        // ItHang chỉ còn 2. Đọc thông báo từ response của POST (client đã theo
        // redirect nên đây là trang /Cart): TempData chỉ đọc được MỘT lần, gọi
        // GET /Cart thêm lần nữa là thông báo đã bị tiêu thụ và biến mất.
        var html = await (await ThemVaoGioAsync(client, _productItHang, 10))
            .Content.ReadAsStringAsync();

        Assert.Contains("chỉ còn 2", html);
        Assert.Contains("value=\"2\"", html);
    }

    [Fact]
    public async Task Them_hang_da_het_thi_bao_het_hang_chu_khong_phai_HTTP_500()
    {
        using var client = CreateClient();

        // Client tự theo redirect, nên response cuối là trang /Cart kèm TempData.
        var response = await ThemVaoGioAsync(client, _productHetHang, 1);
        var html = await response.Content.ReadAsStringAsync();

        // Hết hàng là KẾT CỤC NGHIỆP VỤ bình thường: người dùng mở trang từ lâu
        // rồi mới bấm. Trả 500 là biến việc bình thường thành sự cố.
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("đã hết hàng", html);
    }

    [Fact]
    public async Task Them_san_pham_khong_ton_tai_thi_khong_lam_vo_trang()
    {
        using var client = CreateClient();

        var html = await (await ThemVaoGioAsync(client, 999_999, 1))
            .Content.ReadAsStringAsync();

        Assert.Contains("không còn tồn tại", html);
    }

    [Fact]
    public async Task So_luong_vuot_tran_100_bi_Data_Annotation_chan()
    {
        using var client = CreateClient();

        var html = await (await ThemVaoGioAsync(client, _productConHang, 999_999))
            .Content.ReadAsStringAsync();

        // Trần 100 tồn tại vì CartService cộng dồn số mới vào số đang có, nên
        // quantity = int.MaxValue sẽ làm phép cộng tràn số và ra số âm.
        Assert.Contains("Số lượng phải từ 1 đến 100", html);
        Assert.Contains("Giỏ hàng đang trống", html);
    }

    // ───────────── Cập nhật và xoá ─────────────

    [Fact]
    public async Task UpdateQuantity_dat_so_TUYET_DOI()
    {
        using var client = CreateClient();
        await ThemVaoGioAsync(client, _productConHang, 5);

        await client.PostFormAsync("/Cart/UpdateQuantity", new()
        {
            ["ProductId"] = _productConHang.ToString(),
            ["Quantity"] = "2"
        });

        Assert.Contains("value=\"2\"", await client.GetStringAsync("/Cart"));
    }

    [Fact]
    public async Task UpdateQuantity_bang_0_thi_xoa_dong()
    {
        using var client = CreateClient();
        await ThemVaoGioAsync(client, _productConHang, 5);

        await client.PostFormAsync("/Cart/UpdateQuantity", new()
        {
            ["ProductId"] = _productConHang.ToString(),
            ["Quantity"] = "0"
        });

        Assert.Contains("Giỏ hàng đang trống", await client.GetStringAsync("/Cart"));
    }

    [Fact]
    public async Task Remove_xoa_dung_dong()
    {
        using var client = CreateClient();
        await ThemVaoGioAsync(client, _productConHang, 1);
        await ThemVaoGioAsync(client, _productItHang, 1);

        await client.PostFormAsync("/Cart/Remove", new()
        {
            ["ProductId"] = _productConHang.ToString()
        });

        var html = await client.GetStringAsync("/Cart");

        Assert.DoesNotContain(">ConHang<", html);
        Assert.Contains(">ItHang<", html);
    }

    // ───────────── Bảo mật ─────────────

    [Fact]
    public async Task POST_thieu_antiforgery_token_thi_bi_tu_choi()
    {
        using var client = CreateClient(theoRedirect: false);

        var response = await client.PostAsync("/Cart/Add", new FormUrlEncodedContent(
        [
            new KeyValuePair<string, string>("ProductId", _productConHang.ToString()),
            new KeyValuePair<string, string>("Quantity", "1")
        ]));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Theory]
    [InlineData("/Cart/Add")]
    [InlineData("/Cart/UpdateQuantity")]
    [InlineData("/Cart/Remove")]
    public async Task Cac_endpoint_ghi_KHONG_nhan_GET(string path)
    {
        using var client = CreateClient(theoRedirect: false);

        var response = await client.GetAsync($"{path}?productId={_productConHang}&quantity=1");

        // Nếu thao tác ghi nhận GET thì chỉ cần nhúng
        // <img src="/Cart/Remove?productId=5"> vào một trang bất kỳ là xoá được
        // giỏ của người khác - antiforgery token cũng không chặn được vì GET
        // không đi qua [ValidateAntiForgeryToken].
        //
        // Với conventional routing, ASP.NET Core trả 404 (không phải 405) khi
        // action tồn tại nhưng không nhận verb này. Mã nào cũng được, miễn không
        // thành công - nên khẳng định điều THỰC SỰ quan trọng là giỏ không đổi.
        Assert.Contains(
            response.StatusCode,
            new[] { HttpStatusCode.NotFound, HttpStatusCode.MethodNotAllowed });

        Assert.Contains("Giỏ hàng đang trống", await client.GetStringAsync("/Cart"));
    }

    // ───────────── Summary (JSON) ─────────────

    [Fact]
    public async Task Summary_tra_JSON_cho_badge()
    {
        using var client = CreateClient();
        await ThemVaoGioAsync(client, _productConHang, 2);

        var response = await client.GetAsync("/Cart/Summary");
        var root = JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement;

        Assert.Equal("application/json", response.Content.Headers.ContentType?.MediaType);
        Assert.Equal(2, root.GetProperty("count").GetInt32());
        Assert.Equal(200_000m, root.GetProperty("total").GetDecimal());

        // totalText do server định dạng sẵn để client không tự làm rồi lệch khỏi
        // cách server in tiền.
        Assert.Equal("200,000", root.GetProperty("totalText").GetString());
    }

    [Fact]
    public async Task Summary_KHONG_lo_danh_sach_san_pham()
    {
        using var client = CreateClient();
        await ThemVaoGioAsync(client, _productConHang, 1);

        var json = await client.GetStringAsync("/Cart/Summary");

        // Trả thẳng CartView sẽ gửi cả tên, giá, tồn kho từng dòng - badge không
        // cần gì trong đó.
        Assert.DoesNotContain("ConHang", json);
        Assert.DoesNotContain("lines", json, StringComparison.OrdinalIgnoreCase);
    }

    // ───────────── Badge trên navbar ─────────────

    [Fact]
    public async Task Badge_tren_navbar_hien_so_mon()
    {
        using var client = CreateClient();
        await ThemVaoGioAsync(client, _productConHang, 3);

        var html = await client.GetStringAsync("/");

        Assert.Matches("""badge rounded-pill bg-danger">\s*3\s*<""", html);
    }

    // ───────────── Helper ─────────────

    /// <summary>
    /// Đếm theo URL, KHÔNG theo mẫu `&lt;form action="..."`: Form tag helper có thể
    /// render method trước action, và regex phụ thuộc thứ tự thuộc tính sẽ đếm ra 0
    /// trong khi form vẫn có đủ.
    /// </summary>
    private static int DemDongGioHang(string html) =>
        Regex.Matches(html, Regex.Escape("/Cart/Remove")).Count;

    private static string? LayHeader(HttpResponseMessage response, string name) =>
        response.Headers.TryGetValues(name, out var values) ? values.FirstOrDefault() : null;

    private static Task<HttpResponseMessage> ThemVaoGioAsync(
        HttpClient client, int productId, int quantity, bool ajax = false) =>
        client.PostFormAsync("/Cart/Add", new()
        {
            ["ProductId"] = productId.ToString(),
            ["Quantity"] = quantity.ToString()
        }, ajax);

    private async Task<HttpClient> TaoClientDaDangNhapAsync()
    {
        var client = CreateClient();
        var username = $"gh_{Guid.NewGuid():N}"[..16];
        const string password = "MatKhau123";

        _usernames.Add(username);

        await client.PostFormAsync("/Account/Register", new()
        {
            ["Username"] = username,
            ["Password"] = password,
            ["ConfirmPassword"] = password
        });

        return client;
    }

    public async Task DisposeAsync()
    {
        using var scope = _factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<MiniMartDbContext>();

        await context.CartItems.Where(i => i.Product.CategoryId == _categoryId).ExecuteDeleteAsync();
        await context.Carts.Where(c => _usernames.Contains(c.User.Username)).ExecuteDeleteAsync();
        await context.Products.Where(p => p.CategoryId == _categoryId).ExecuteDeleteAsync();
        await context.Categories.Where(c => c.Id == _categoryId).ExecuteDeleteAsync();
        await context.Users.Where(u => _usernames.Contains(u.Username)).ExecuteDeleteAsync();

        _factory.Dispose();
    }
}
