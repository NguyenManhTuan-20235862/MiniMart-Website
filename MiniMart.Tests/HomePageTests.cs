using System.Net;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using MiniMart.Domain.Entities;
using MiniMart.Infrastructure.Data;

namespace MiniMart.Tests;

public class HomePageTests : IAsyncLifetime
{
    private const int TonKhoDacBiet = 8_675_309;

    private readonly WebApplicationFactory<Program> _factory = new();
    private readonly List<int> _categoryIds = [];

    private string _tenDanhMucA = "";
    private string _tenDanhMucB = "";
    private int _danhMucA;
    private int _danhMucB;

    public async Task InitializeAsync()
    {
        using var scope = _factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<MiniMartDbContext>();

        _tenDanhMucA = $"HA_{Guid.NewGuid():N}"[..14];
        _tenDanhMucB = $"HB_{Guid.NewGuid():N}"[..14];

        var a = new Category { Name = _tenDanhMucA };
        var b = new Category { Name = _tenDanhMucB };

        // Tồn kho là số 7 chữ số đủ đặc biệt để không trùng với Id hay giá tiền
        // trong HTML. Dùng số nhỏ như 77 sẽ khiến assertion đỏ ngẫu nhiên khi
        // GUID trong tên danh mục tình cờ chứa "77".
        a.Products.Add(new Product { Name = "ConHang", Price = 111_000m, Stock = TonKhoDacBiet });
        a.Products.Add(new Product { Name = "HetHang", Price = 222_000m, Stock = 0 });

        // Danh mục B: 15 sản phẩm > pageSize 12 -> phải hiện nút Xem thêm.
        for (var i = 1; i <= 15; i++)
        {
            b.Products.Add(new Product { Name = $"B{i:D2}", Price = i * 1000m, Stock = 5 });
        }

        context.Categories.AddRange(a, b);
        await context.SaveChangesAsync();

        _danhMucA = a.Id;
        _danhMucB = b.Id;
        _categoryIds.AddRange([a.Id, b.Id]);
    }

    private HttpClient CreateClient() => _factory.CreateClient();

    private static int DemThe(string html) =>
        html.Split("class=\"card h-100 shadow-sm\"").Length - 1;

    [Fact]
    public async Task Trang_chu_hien_the_san_pham_qua_partial()
    {
        using var client = CreateClient();

        var html = await client.GetStringAsync($"/?categoryId={_danhMucA}");

        Assert.Equal(2, DemThe(html));
        Assert.Contains("ConHang", html);
        Assert.Contains("HetHang", html);
    }

    [Fact]
    public async Task Loc_theo_danh_muc_chi_hien_san_pham_cua_danh_muc_do()
    {
        using var client = CreateClient();

        var html = await client.GetStringAsync($"/?categoryId={_danhMucA}");

        Assert.Contains("ConHang", html);
        Assert.DoesNotContain("B01", html);   // sản phẩm của danh mục B
    }

    [Fact]
    public async Task Lua_chon_danh_muc_duoc_giu_lai_sau_khi_loc()
    {
        using var client = CreateClient();

        var html = await client.GetStringAsync($"/?categoryId={_danhMucA}");

        // Mất selected thì mỗi lần lọc xong dropdown lại nhảy về "Tất cả",
        // người dùng không biết mình đang xem danh mục nào.
        Assert.Contains($"value=\"{_danhMucA}\" selected", html);
        Assert.Contains("Xoá lọc", html);
    }

    [Fact]
    public async Task Dropdown_phai_liet_ke_cac_danh_muc()
    {
        using var client = CreateClient();

        var html = await client.GetStringAsync("/");

        Assert.Contains(_tenDanhMucA, html);
        Assert.Contains(_tenDanhMucB, html);
        Assert.Contains("-- Tất cả --", html);
    }

    [Fact]
    public async Task Danh_muc_khong_co_san_pham_hien_thong_bao_rong()
    {
        using var client = CreateClient();

        var html = await client.GetStringAsync("/?categoryId=999999");

        Assert.Equal(0, DemThe(html));
        Assert.Contains("Không tìm thấy sản phẩm nào", html);
    }

    [Fact]
    public async Task Chi_hien_trang_thai_con_hang_KHONG_hien_so_ton_kho()
    {
        using var client = CreateClient();

        var html = await client.GetStringAsync($"/?categoryId={_danhMucA}");

        Assert.Contains("Còn hàng", html);
        Assert.Contains("Hết hàng", html);
        // Số lượng tồn kho là thông tin kinh doanh, không được lộ ra HTML.
        Assert.DoesNotContain(TonKhoDacBiet.ToString(), html);
        Assert.DoesNotContain("RowVersion", html);
    }

    [Fact]
    public async Task Nut_Xem_them_chi_hien_khi_con_trang_sau()
    {
        using var client = CreateClient();

        // Danh mục A chỉ có 2 sản phẩm < pageSize 12.
        var htmlA = await client.GetStringAsync($"/?categoryId={_danhMucA}");
        Assert.DoesNotContain("btnLoadMore", htmlA);

        // Danh mục B có 15 sản phẩm > pageSize 12.
        var htmlB = await client.GetStringAsync($"/?categoryId={_danhMucB}");
        Assert.Contains("btnLoadMore", htmlB);
        Assert.Contains("data-next-page=\"2\"", htmlB);
    }

    [Fact]
    public async Task Trang_chu_khong_can_dang_nhap()
    {
        using var client = _factory.CreateClient(
            new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });

        var response = await client.GetAsync("/");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    public async Task DisposeAsync()
    {
        using var scope = _factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<MiniMartDbContext>();

        await context.Products.Where(p => _categoryIds.Contains(p.CategoryId)).ExecuteDeleteAsync();
        await context.Categories.Where(c => _categoryIds.Contains(c.Id)).ExecuteDeleteAsync();

        _factory.Dispose();
    }
}
