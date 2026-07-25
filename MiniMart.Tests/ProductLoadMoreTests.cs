using System.Net;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using MiniMart.Domain.Entities;
using MiniMart.Infrastructure.Data;

namespace MiniMart.Tests;

public class ProductLoadMoreTests : IAsyncLifetime
{
    private readonly WebApplicationFactory<Program> _factory = new();
    private int _categoryId;

    public async Task InitializeAsync()
    {
        using var scope = _factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<MiniMartDbContext>();

        var category = new Category { Name = $"LM_{Guid.NewGuid():N}"[..14] };

        // 15 sản phẩm > pageSize 12 -> có trang 2 để kiểm tra HasNextPage.
        for (var i = 1; i <= 15; i++)
        {
            category.Products.Add(new Product
            {
                Name = $"SP{i:D2}",
                Price = i * 10_000m,
                Stock = 99,
                ImageUrl = i % 2 == 0 ? null : $"/images/products/{i}.jpg"
            });
        }

        context.Categories.Add(category);
        await context.SaveChangesAsync();
        _categoryId = category.Id;
    }

    private HttpClient CreateClient() =>
        _factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });

    private async Task<JsonElement> GetJsonAsync(string url)
    {
        using var client = CreateClient();
        var json = await client.GetStringAsync(url);

        return JsonDocument.Parse(json).RootElement.Clone();
    }

    [Fact]
    public async Task Nac_danh_goi_duoc_va_tra_ve_content_type_json()
    {
        using var client = CreateClient();

        var response = await client.GetAsync($"/Product/LoadMore?categoryId={_categoryId}");

        // Endpoint công khai: không [Authorize] nên không bị đá về trang Login.
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("application/json", response.Content.Headers.ContentType?.MediaType);
    }

    [Fact]
    public async Task Tra_ve_dung_bon_truong_cua_DTO_theo_camelCase()
    {
        var root = await GetJsonAsync($"/Product/LoadMore?categoryId={_categoryId}");
        var item = root.GetProperty("items")[0];

        Assert.True(item.TryGetProperty("id", out _));
        Assert.True(item.TryGetProperty("name", out _));
        Assert.True(item.TryGetProperty("price", out _));
        Assert.True(item.TryGetProperty("imageUrl", out _));

        // Đúng 4 trường, không hơn.
        Assert.Equal(4, item.EnumerateObject().Count());
    }

    [Fact]
    public async Task KHONG_duoc_lo_du_lieu_noi_bo_ra_client()
    {
        using var client = CreateClient();

        var json = await client.GetStringAsync($"/Product/LoadMore?categoryId={_categoryId}");

        // Trả thẳng entity Product sẽ lộ hết những thứ này. rowVersion vô nghĩa
        // với client, còn stock là thông tin kinh doanh.
        Assert.DoesNotContain("rowVersion", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("stock", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("categoryId", json, StringComparison.OrdinalIgnoreCase);
        // Vòng lặp Product -> Category -> Products sẽ làm serializer ném exception.
        Assert.DoesNotContain("\"category\"", json, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ImageUrl_null_van_duoc_giu_trong_JSON()
    {
        var root = await GetJsonAsync($"/Product/LoadMore?categoryId={_categoryId}");

        var coNull = root.GetProperty("items").EnumerateArray()
            .Any(i => i.GetProperty("imageUrl").ValueKind == JsonValueKind.Null);

        // Bỏ hẳn trường khi null sẽ khiến client phải kiểm tra sự tồn tại của
        // key thay vì chỉ kiểm tra giá trị.
        Assert.True(coNull);
    }

    [Fact]
    public async Task Trang_1_con_trang_sau_trang_2_thi_het()
    {
        var trang1 = await GetJsonAsync($"/Product/LoadMore?categoryId={_categoryId}&page=1");
        var trang2 = await GetJsonAsync($"/Product/LoadMore?categoryId={_categoryId}&page=2");

        Assert.Equal(12, trang1.GetProperty("items").GetArrayLength());
        Assert.Equal(1, trang1.GetProperty("page").GetInt32());
        Assert.True(trang1.GetProperty("hasNextPage").GetBoolean());

        Assert.Equal(3, trang2.GetProperty("items").GetArrayLength());  // 15 - 12
        Assert.Equal(2, trang2.GetProperty("page").GetInt32());
        // Thiếu cờ này thì client cuộn vô tận, gọi mãi không biết dừng.
        Assert.False(trang2.GetProperty("hasNextPage").GetBoolean());
    }

    [Fact]
    public async Task Hai_trang_khong_tra_ve_trung_san_pham()
    {
        var trang1 = await GetJsonAsync($"/Product/LoadMore?categoryId={_categoryId}&page=1");
        var trang2 = await GetJsonAsync($"/Product/LoadMore?categoryId={_categoryId}&page=2");

        int[] Ids(JsonElement r) =>
            r.GetProperty("items").EnumerateArray().Select(i => i.GetProperty("id").GetInt32()).ToArray();

        Assert.Empty(Ids(trang1).Intersect(Ids(trang2)));
    }

    [Fact]
    public async Task Tham_so_page_bay_khong_lam_vo_endpoint()
    {
        using var client = CreateClient();

        var response = await client.GetAsync($"/Product/LoadMore?categoryId={_categoryId}&page=-5");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
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
