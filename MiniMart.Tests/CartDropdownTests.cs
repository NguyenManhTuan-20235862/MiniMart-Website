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
/// Dropdown giỏ hàng trên navbar: partial <c>_CartDropdown</c>, endpoint
/// <c>GET /Cart/Dropdown</c>, và hợp đồng JSON mà <c>cart-dropdown.js</c> tiêu thụ.
///
/// <para>
/// Bản thân file JS thì bộ test này KHÔNG chạm tới được (dự án chưa có headless
/// browser). Nhưng ba thứ nó phụ thuộc thì kiểm chứng được, và đó là phần dễ vỡ khi
/// refactor: các hook <c>data-*</c> trong markup, hình dạng JSON, và việc header
/// <c>Accept</c> thật sự chuyển được response sang JSON.
/// </para>
/// </summary>
public class CartDropdownTests : IAsyncLifetime
{
    private readonly WebApplicationFactory<Program> _factory = new();

    private int _categoryId;
    private int _productConHang;
    private int _productItHang;

    public async Task InitializeAsync()
    {
        using var scope = _factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<MiniMartDbContext>();

        var category = new Category { Name = $"DD_{Guid.NewGuid():N}"[..14] };
        var conHang = new Product { Name = "ConHang", Price = 100_000m, Stock = 50, Category = category };
        var itHang = new Product { Name = "ItHang", Price = 200_000m, Stock = 2, Category = category };

        context.Products.AddRange(conHang, itHang);
        await context.SaveChangesAsync();

        _categoryId = category.Id;
        _productConHang = conHang.Id;
        _productItHang = itHang.Id;
    }

    // ───────────── Hợp đồng JSON ─────────────

    [Fact]
    public async Task Accept_application_json_thi_endpoint_ghi_tra_JSON_chu_khong_HTML()
    {
        using var client = CreateClient();

        var response = await PostJsonAsync(client, "/Cart/Add", new()
        {
            ["ProductId"] = _productConHang.ToString(),
            ["Quantity"] = "2"
        });

        // Nhánh JSON phải được xét TRƯỚC nhánh PartialView, vì fetch của dropdown gửi
        // cả hai header. Đảo thứ tự thì client nhận HTML và response.json() sẽ ném.
        Assert.Equal("application/json", response.Content.Headers.ContentType?.MediaType);
        Assert.DoesNotContain("<div", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task JSON_mang_du_thu_de_cap_nhat_UI()
    {
        using var client = CreateClient();

        var root = await PostDocAsync(client, "/Cart/Add", new()
        {
            ["ProductId"] = _productConHang.ToString(),
            ["Quantity"] = "2"
        });

        Assert.Equal(2, root.GetProperty("count").GetInt32());
        Assert.Equal("200,000", root.GetProperty("totalText").GetString());
        Assert.False(root.GetProperty("isEmpty").GetBoolean());

        var line = root.GetProperty("line");
        Assert.Equal(_productConHang, line.GetProperty("productId").GetInt32());
        Assert.Equal(2, line.GetProperty("quantity").GetInt32());
        Assert.Equal("200,000", line.GetProperty("lineTotalText").GetString());
        Assert.False(line.GetProperty("removed").GetBoolean());
    }

    [Fact]
    public async Task Tien_trong_JSON_da_duoc_SERVER_dinh_dang_san()
    {
        using var client = CreateClient();

        var root = await PostDocAsync(client, "/Cart/Add", new()
        {
            ["ProductId"] = _productConHang.ToString(),
            ["Quantity"] = "11"      // 11 x 100.000 = 1.100.000
        });

        // Đây là ràng buộc giữ cho nhánh JSON không vi phạm tinh thần quy ước: nếu
        // totalText biến mất thì JavaScript buộc phải tự định dạng tiền, và cách in
        // tiền sẽ tồn tại ở hai nơi rồi lệch nhau theo locale.
        Assert.Equal("1,100,000", root.GetProperty("totalText").GetString());

        // total thô vẫn có để client tính toán nếu cần, nhưng KHÔNG dùng để hiển thị.
        Assert.Equal(1_100_000m, root.GetProperty("total").GetDecimal());
    }

    [Fact]
    public async Task Xoa_dong_thi_JSON_bao_removed_va_isEmpty()
    {
        using var client = CreateClient();

        await PostJsonAsync(client, "/Cart/Add", new()
        {
            ["ProductId"] = _productConHang.ToString(),
            ["Quantity"] = "1"
        });

        var root = await PostDocAsync(client, "/Cart/Remove", new()
        {
            ["ProductId"] = _productConHang.ToString()
        });

        // removed = tín hiệu để JS gỡ node dòng đó; isEmpty = tín hiệu để JS nhờ Razor
        // dựng lại phần "Giỏ hàng đang trống" thay vì tự tạo thẻ HTML.
        Assert.True(root.GetProperty("line").GetProperty("removed").GetBoolean());
        Assert.True(root.GetProperty("isEmpty").GetBoolean());
        Assert.Equal(0, root.GetProperty("count").GetInt32());
    }

    [Fact]
    public async Task Giam_ve_0_duoc_hieu_la_xoa_dong()
    {
        using var client = CreateClient();

        await PostJsonAsync(client, "/Cart/Add", new()
        {
            ["ProductId"] = _productConHang.ToString(),
            ["Quantity"] = "1"
        });

        // Nút "−" ở số lượng 1 gửi thẳng Quantity = 0; nhờ CartService dịch 0 thành xoá
        // mà JS không cần nhánh riêng cho trường hợp này.
        var root = await PostDocAsync(client, "/Cart/UpdateQuantity", new()
        {
            ["ProductId"] = _productConHang.ToString(),
            ["Quantity"] = "0"
        });

        Assert.True(root.GetProperty("line").GetProperty("removed").GetBoolean());
        Assert.True(root.GetProperty("isEmpty").GetBoolean());
    }

    [Fact]
    public async Task Vuot_ton_kho_thi_JSON_tra_so_da_KEP_kem_notice()
    {
        using var client = CreateClient();

        // ItHang chỉ còn 2. Nút "+" cố ý không bị disable theo tồn kho (client không
        // được biết con số đó), nên server phải kẹp và giải thích.
        var root = await PostDocAsync(client, "/Cart/Add", new()
        {
            ["ProductId"] = _productItHang.ToString(),
            ["Quantity"] = "10"
        });

        Assert.Equal(2, root.GetProperty("line").GetProperty("quantity").GetInt32());
        Assert.Contains("chỉ còn 2", root.GetProperty("notice").GetString());
    }

    [Fact]
    public async Task JSON_KHONG_lo_ton_kho_hay_RowVersion()
    {
        using var client = CreateClient();

        var response = await PostJsonAsync(client, "/Cart/Add", new()
        {
            ["ProductId"] = _productItHang.ToString(),
            ["Quantity"] = "1"
        });

        var json = await response.Content.ReadAsStringAsync();
        var line = JsonDocument.Parse(json).RootElement.GetProperty("line");

        // DTO cũng là một bề mặt lộ dữ liệu như HTML. Tồn kho là thông tin kinh doanh;
        // nút "+" không cần biết trần vì server kẹp hộ.
        Assert.Equal(
            new[] { "lineTotalText", "productId", "quantity", "removed" },
            line.EnumerateObject().Select(p => p.Name).Order().ToArray());

        Assert.DoesNotContain("rowVersion", json, StringComparison.OrdinalIgnoreCase);
    }

    // ───────────── Endpoint trả HTML dropdown ─────────────

    [Fact]
    public async Task Dropdown_tra_PartialView_KHONG_kem_layout()
    {
        using var client = CreateClient();

        var html = await client.GetStringAsync("/Cart/Dropdown");

        Assert.Contains("data-cart-dropdown", html);

        // Endpoint này tồn tại để RAZOR dựng markup thay cho JavaScript. Đổi thành
        // View() là gửi lại cả layout và JS sẽ nhét một trang vào trong dropdown.
        Assert.DoesNotContain("<!DOCTYPE", html);
        Assert.DoesNotContain("navbar", html);
    }

    [Fact]
    public async Task Dropdown_hien_dung_hang_trong_gio()
    {
        using var client = CreateClient();

        await PostJsonAsync(client, "/Cart/Add", new()
        {
            ["ProductId"] = _productConHang.ToString(),
            ["Quantity"] = "3"
        });

        var html = await client.GetStringAsync("/Cart/Dropdown");

        Assert.Contains("ConHang", html);
        Assert.Contains($"data-product-id=\"{_productConHang}\"", html);
        Assert.Contains("300,000", html);
    }

    [Fact]
    public async Task Dropdown_rong_thi_hien_trang_thai_rong()
    {
        using var client = CreateClient();

        var html = await client.GetStringAsync("/Cart/Dropdown");

        Assert.Contains("Giỏ hàng đang trống", html);
        Assert.DoesNotContain("data-cart-line", html);
    }

    // ───────────── Hook data-* mà JavaScript phụ thuộc ─────────────

    [Theory]
    [InlineData("data-cart-dropdown")]
    [InlineData("data-url-update")]
    [InlineData("data-url-remove")]
    [InlineData("data-url-dropdown")]
    [InlineData("data-cart-notice")]
    [InlineData("data-cart-line")]
    [InlineData("data-cart-quantity")]
    [InlineData("data-cart-line-total")]
    [InlineData("data-cart-total")]
    [InlineData("data-cart-action=\"increase\"")]
    [InlineData("data-cart-action=\"decrease\"")]
    [InlineData("data-cart-action=\"remove\"")]
    public async Task Markup_dropdown_giu_du_hook_cho_JavaScript(string hook)
    {
        using var client = CreateClient();

        await PostJsonAsync(client, "/Cart/Add", new()
        {
            ["ProductId"] = _productConHang.ToString(),
            ["Quantity"] = "1"
        });

        var html = await client.GetStringAsync("/Cart/Dropdown");

        // Đây là khoảng trống test lớn nhất của tính năng: cart-dropdown.js không có
        // test chạy thật. Đổi tên một thuộc tính data-* trong Razor sẽ làm JS im lặng
        // không tìm thấy node và ngừng hoạt động - không lỗi build, không lỗi runtime.
        // Bộ Theory này biến việc đó thành test đỏ.
        Assert.Contains(hook, html);
    }

    [Fact]
    public async Task Dropdown_co_antiforgery_token_de_fetch_dung_duoc()
    {
        using var client = CreateClient();

        var html = await client.GetStringAsync("/Cart/Dropdown");

        // Token nằm TRONG partial nên mỗi lần JS thay dropdown bằng HTML mới thì token
        // mới đi kèm. Thiếu nó thì mọi thao tác +/- nhận 400 và JS phải reload trang.
        Assert.Contains("name=\"__RequestVerificationToken\"", html);
    }

    // ───────────── Icon và badge trên navbar ─────────────

    [Fact]
    public async Task Navbar_luon_render_node_badge_ke_ca_khi_gio_rong()
    {
        using var client = CreateClient();

        var html = await client.GetStringAsync("/");

        // Badge phải LUÔN có trong DOM (chỉ ẩn) để JS có node mà gán textContent.
        // Bọc trong @if thì lần đầu thêm hàng vào giỏ rỗng sẽ không có node nào để
        // cập nhật, và JS buộc phải tự tạo thẻ - đúng thứ thiết kế này muốn tránh.
        Assert.Contains("data-cart-count", html);
        Assert.Matches("data-cart-count[^>]*hidden", html);
    }

    [Fact]
    public async Task Badge_hien_va_mang_so_mon_khi_gio_co_hang()
    {
        using var client = CreateClient();

        await PostJsonAsync(client, "/Cart/Add", new()
        {
            ["ProductId"] = _productConHang.ToString(),
            ["Quantity"] = "4"
        });

        var html = await client.GetStringAsync("/");

        var the = Regex.Match(html, "<span[^>]*data-cart-count[^>]*>[^<]*</span>").Value;

        Assert.DoesNotContain("hidden", the);
        Assert.Contains("4", the);
    }

    [Fact]
    public async Task Navbar_nhung_san_dropdown_de_khong_can_goi_them_request()
    {
        using var client = CreateClient();

        await PostJsonAsync(client, "/Cart/Add", new()
        {
            ["ProductId"] = _productConHang.ToString(),
            ["Quantity"] = "1"
        });

        var html = await client.GetStringAsync("/");

        // Dropdown được render sẵn trong layout: mở ra là thấy ngay, JS chỉ gọi
        // /Cart/Dropdown khi cấu trúc giỏ đã đổi.
        Assert.Contains("data-cart-dropdown-container", html);
        Assert.Contains("ConHang", html);
    }

    [Fact]
    public async Task Nut_them_vao_gio_van_la_form_POST_chay_duoc_khi_tat_JavaScript()
    {
        using var client = CreateClient();

        var html = await client.GetStringAsync("/");

        // data-cart-add chỉ là dấu cho JS chặn submit. Bỏ form đi để "làm cho gọn" là
        // biến tính năng thành phụ thuộc JavaScript.
        Assert.Contains("data-cart-add", html);
        Assert.Contains("action=\"/Cart/Add\"", html);
    }

    // ───────────── Đường không-JavaScript vẫn nguyên vẹn ─────────────

    [Fact]
    public async Task Khong_gui_Accept_json_thi_van_la_Post_Redirect_Get()
    {
        using var client = _factory.CreateClient(
            new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });

        var response = await client.PostFormAsync("/Cart/Add", new()
        {
            ["ProductId"] = _productConHang.ToString(),
            ["Quantity"] = "1"
        });

        // Thêm nhánh JSON KHÔNG được phá đường của người tắt JavaScript.
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Equal("/Cart", response.Headers.Location!.ToString());
    }

    [Fact]
    public async Task Header_X_Cart_Count_van_co_tren_ca_response_JSON()
    {
        using var client = CreateClient();

        var response = await PostJsonAsync(client, "/Cart/Add", new()
        {
            ["ProductId"] = _productConHang.ToString(),
            ["Quantity"] = "2"
        });

        Assert.Equal(
            "2",
            response.Headers.TryGetValues(CartController.CartCountHeader, out var v)
                ? v.FirstOrDefault()
                : null);
    }

    // ───────────── Helper ─────────────

    private HttpClient CreateClient() =>
        _factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = true });

    /// <summary>POST giống hệt cách <c>cart-dropdown.js</c> gửi: Accept application/json.</summary>
    private static async Task<HttpResponseMessage> PostJsonAsync(
        HttpClient client, string path, Dictionary<string, string> fields)
    {
        var token = await client.LayAntiforgeryTokenAsync();

        var request = new HttpRequestMessage(HttpMethod.Post, path)
        {
            Content = new FormUrlEncodedContent(fields)
        };

        request.Headers.Add("Accept", "application/json");

        // fetch() gửi token qua HEADER, không qua trường form - đúng như
        // AddAntiforgery(o => o.HeaderName = "RequestVerificationToken").
        request.Headers.Add("RequestVerificationToken", token);

        return await client.SendAsync(request);
    }

    private static async Task<JsonElement> PostDocAsync(
        HttpClient client, string path, Dictionary<string, string> fields)
    {
        var response = await PostJsonAsync(client, path, fields);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        return JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement;
    }

    public async Task DisposeAsync()
    {
        using var scope = _factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<MiniMartDbContext>();

        await context.CartItems.Where(i => i.Product.CategoryId == _categoryId).ExecuteDeleteAsync();
        await context.Products.Where(p => p.CategoryId == _categoryId).ExecuteDeleteAsync();
        await context.Categories.Where(c => c.Id == _categoryId).ExecuteDeleteAsync();

        _factory.Dispose();
    }
}
