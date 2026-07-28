using System.Net;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using MiniMart.Domain.Entities;
using MiniMart.Infrastructure.Data;
using MiniMart.Web.Controllers;

namespace MiniMart.Tests;

/// <summary>
/// /Product/LoadMore trả HTML (PartialView) chứ không phải JSON, nên test ở đây
/// kiểm chứng ĐÚNG chuỗi mà JavaScript sẽ dán vào DOM. Đó là lý do chính chọn
/// PartialView thay vì Json: với Json, phần dựng markup nằm trong JavaScript và
/// không có test nào chạm tới được.
/// </summary>
public class ProductLoadMoreTests : IAsyncLifetime
{
    // 7 chữ số đủ đặc biệt để không trùng với Id hay giá tiền trong HTML.
    private const int TonKhoDacBiet = 8_675_309;

    private readonly WebApplicationFactory<Program> _factory = new();
    private int _categoryId;
    private string _tenDanhMuc = "";

    public async Task InitializeAsync()
    {
        using var scope = _factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<MiniMartDbContext>();

        _tenDanhMuc = $"LM_{Guid.NewGuid():N}"[..14];
        var category = new Category { Name = _tenDanhMuc };

        // 15 sản phẩm > pageSize 12 -> có trang 2 để kiểm tra phân trang.
        for (var i = 1; i <= 15; i++)
        {
            category.Products.Add(new Product
            {
                Name = $"SP{i:D2}",
                Price = i * 10_000m,
                Stock = i == 15 ? 0 : TonKhoDacBiet,
                ImageUrl = i % 2 == 0 ? null : $"/images/products/{i}.jpg"
            });
        }

        context.Categories.Add(category);
        await context.SaveChangesAsync();
        _categoryId = category.Id;
    }

    private HttpClient CreateClient() =>
        _factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });

    private static int DemThe(string html) =>
        html.Split("class=\"card h-100 shadow-sm\"").Length - 1;

    private async Task<(string Html, string? NextPage)> GetPartialAsync(string url)
    {
        using var client = CreateClient();
        var response = await client.GetAsync(url);

        response.Headers.TryGetValues(ProductController.NextPageHeader, out var values);

        return (await response.Content.ReadAsStringAsync(), values?.FirstOrDefault());
    }

    [Fact]
    public async Task Nac_danh_goi_duoc_va_tra_ve_HTML()
    {
        using var client = CreateClient();

        var response = await client.GetAsync($"/Product/LoadMore?categoryId={_categoryId}");

        // Endpoint công khai: không [Authorize] nên không bị đá về trang Login.
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("text/html", response.Content.Headers.ContentType?.MediaType);
    }

    [Fact]
    public async Task Tra_ve_dung_markup_cua_ProductCard()
    {
        var (html, _) = await GetPartialAsync($"/Product/LoadMore?categoryId={_categoryId}&page=1");

        Assert.Equal(12, DemThe(html));
        Assert.Contains("SP01", html);
        // Dùng lại đúng một khuôn thẻ với trang 1 do server render.
        Assert.Contains("<div class=\"col\">", html);
    }

    [Fact]
    public async Task KHONG_duoc_kem_layout()
    {
        var (html, _) = await GetPartialAsync($"/Product/LoadMore?categoryId={_categoryId}");

        // Đây là điểm phân biệt PartialView() với View(). Dùng View() thì response
        // kèm cả html/head/navbar/footer - trình duyệt đã có sẵn những thứ đó rồi,
        // dán thêm vào giữa trang sẽ ra một trang lồng trong trang.
        Assert.DoesNotContain("<!DOCTYPE", html);
        Assert.DoesNotContain("<html", html);
        Assert.DoesNotContain("navbar", html);
        Assert.DoesNotContain("</body>", html);
    }

    [Fact]
    public async Task KHONG_duoc_boc_them_the_lam_vo_Bootstrap_grid()
    {
        var (html, _) = await GetPartialAsync($"/Product/LoadMore?categoryId={_categoryId}");

        // Output phải là một dãy .col dán thẳng được vào .row của trang.
        // Bọc thêm một tầng div là .col không còn con trực tiếp của .row -> vỡ layout.
        Assert.StartsWith("<div class=\"col\">", html.TrimStart());
        Assert.DoesNotContain("class=\"row", html);
    }

    [Fact]
    public async Task KHONG_duoc_lo_con_so_ton_kho()
    {
        var (html, _) = await GetPartialAsync($"/Product/LoadMore?categoryId={_categoryId}");

        // _ProductCard chỉ in trạng thái. Vì server render, quy tắc này được áp
        // dụng cho cả trang 1 và trang 2+ mà không phải nhắc lại lần nào.
        Assert.DoesNotContain(TonKhoDacBiet.ToString(), html);
        Assert.DoesNotContain("RowVersion", html);
        Assert.Contains("Còn hàng", html);
    }

    [Fact]
    public async Task Header_X_Next_Page_chi_ro_trang_ke_tiep()
    {
        var (_, trang1) = await GetPartialAsync($"/Product/LoadMore?categoryId={_categoryId}&page=1");

        // HTML không có chỗ đặt metadata phân trang nên nó đi qua header.
        // Thiếu thông tin này thì JS gọi mãi không biết dừng.
        Assert.Equal("2", trang1);
    }

    [Fact]
    public async Task Header_X_Next_Page_rong_khi_het_du_lieu()
    {
        var (html, trang2) = await GetPartialAsync($"/Product/LoadMore?categoryId={_categoryId}&page=2");

        Assert.Equal(3, DemThe(html));   // 15 - 12
        Assert.True(string.IsNullOrEmpty(trang2));
    }

    [Fact]
    public async Task Hai_trang_khong_tra_ve_trung_san_pham()
    {
        var (trang1, _) = await GetPartialAsync($"/Product/LoadMore?categoryId={_categoryId}&page=1");
        var (trang2, _) = await GetPartialAsync($"/Product/LoadMore?categoryId={_categoryId}&page=2");

        // SP01..SP12 ở trang 1, SP13..SP15 ở trang 2. Thiếu tie-breaker trong
        // OrderBy thì một sản phẩm có thể xuất hiện ở cả hai trang.
        Assert.Contains(">SP12</a>", trang1);
        Assert.DoesNotContain(">SP12</a>", trang2);
        Assert.Contains(">SP13</a>", trang2);
        Assert.DoesNotContain(">SP13</a>", trang1);
    }

    [Fact]
    public async Task LoadMore_ap_dung_bo_loc_khoang_gia()
    {
        var (html, trangKe) = await GetPartialAsync(
            $"/Product/LoadMore?categoryId={_categoryId}&minPrice=30000&maxPrice=60000");

        // Dữ liệu: 15 sản phẩm giá 10.000 .. 150.000 -> khớp SP03..SP06.
        Assert.Equal(4, DemThe(html));
        Assert.Contains(">SP03</a>", html);
        Assert.Contains(">SP06</a>", html);
        Assert.DoesNotContain(">SP02</a>", html);
        Assert.DoesNotContain(">SP07</a>", html);
        Assert.True(string.IsNullOrEmpty(trangKe));
    }

    [Fact]
    public async Task Bo_loc_gia_van_duoc_ap_dung_o_trang_2()
    {
        // minPrice=30000 -> còn 13 sản phẩm (SP03..SP15) > pageSize 12.
        var (trang1, trangKe) = await GetPartialAsync(
            $"/Product/LoadMore?categoryId={_categoryId}&minPrice=30000&page=1");
        var (trang2, _) = await GetPartialAsync(
            $"/Product/LoadMore?categoryId={_categoryId}&minPrice=30000&page=2");

        Assert.Equal(12, DemThe(trang1));
        Assert.Equal("2", trangKe);
        Assert.Equal(1, DemThe(trang2));

        // Quên truyền minPrice sang trang 2 là bug kinh điển: trang 2 sẽ chứa
        // SP01/SP02 đã bị loại ở trang 1.
        Assert.Contains(">SP15</a>", trang2);
        Assert.DoesNotContain(">SP01</a>", trang2);
        Assert.DoesNotContain(">SP02</a>", trang2);
    }

    [Fact]
    public async Task Khoang_gia_nguoc_tra_ve_rong_chu_khong_loi()
    {
        using var client = CreateClient();

        var response = await client.GetAsync(
            $"/Product/LoadMore?categoryId={_categoryId}&minPrice=100000&maxPrice=1000");
        var html = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(0, DemThe(html));
    }

    [Fact]
    public async Task Tham_so_page_bay_khong_lam_vo_endpoint()
    {
        using var client = CreateClient();

        var response = await client.GetAsync($"/Product/LoadMore?categoryId={_categoryId}&page=-5");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Ten_san_pham_duoc_Razor_escape()
    {
        using var scope = _factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<MiniMartDbContext>();
        context.Products.Add(new Product
        {
            Name = "<script>alert(1)</script>",
            Price = 1m,
            Stock = 1,
            CategoryId = _categoryId
        });
        await context.SaveChangesAsync();

        var (html, _) = await GetPartialAsync($"/Product/LoadMore?categoryId={_categoryId}&maxPrice=1");

        // Đây là thứ cách trả JSON KHÔNG có: JSON không escape ký tự <, nên tên
        // này về tới client nguyên dạng và việc escape là trách nhiệm của
        // JavaScript. Với PartialView, Razor escape sẵn - không thể quên.
        Assert.Contains("&lt;script&gt;", html);
        Assert.DoesNotContain("<script>alert(1)</script>", html);
    }

    [Fact]
    public async Task Route_Admin_khong_bi_anh_huong_du_trung_ten_class()
    {
        using var client = CreateClient();

        var response = await client.GetAsync("/Admin/Product");

        // Areas/Admin/Controllers/ProductController và Controllers/ProductController
        // trùng tên class nhưng khác Area -> khác route, không nhập nhằng.
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.StartsWith("/Account/Login", response.Headers.Location!.PathAndQuery);
    }

    public async Task DisposeAsync()
    {
        using var scope = _factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<MiniMartDbContext>();

        await context.Products.Where(p => p.CategoryId == _categoryId).ExecuteDeleteAsync();
        await context.Categories.Where(c => c.Id == _categoryId).ExecuteDeleteAsync();

        _factory.Dispose();
    }
}
