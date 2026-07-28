using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using MiniMart.Domain.Entities;
using MiniMart.Domain.Interfaces;
using MiniMart.Infrastructure.Data;

namespace MiniMart.Tests;

/// <summary>
/// Test lọc và phân trang trên SQL Server thật: đây là code sinh SQL, mock
/// DbContext chỉ kiểm tra được LINQ chạy trong bộ nhớ chứ không kiểm tra được
/// câu SQL có đúng không.
/// </summary>
public class ProductQueryTests : IAsyncLifetime
{
    private readonly WebApplicationFactory<Program> _factory = new();
    private readonly List<int> _categoryIds = [];

    private int _categoryA;

    private IServiceScope CreateScope() => _factory.Services.CreateScope();

    /// <summary>
    /// Dựng dữ liệu cố định: danh mục A có 5 sản phẩm giá 100k..500k,
    /// danh mục B có 3 sản phẩm giá 1tr..3tr.
    /// </summary>
    public async Task InitializeAsync()
    {
        using var scope = CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<MiniMartDbContext>();

        var a = new Category { Name = $"A_{Guid.NewGuid():N}"[..14] };
        var b = new Category { Name = $"B_{Guid.NewGuid():N}"[..14] };

        for (var i = 1; i <= 5; i++)
        {
            a.Products.Add(new Product { Name = $"A{i:D2}", Price = i * 100_000m, Stock = 10 });
        }

        for (var i = 1; i <= 3; i++)
        {
            b.Products.Add(new Product { Name = $"B{i:D2}", Price = i * 1_000_000m, Stock = 10 });
        }

        context.Categories.AddRange(a, b);
        await context.SaveChangesAsync();

        _categoryA = a.Id;
        _categoryIds.AddRange([a.Id, b.Id]);
    }

    private async Task<T> DungRepositoryAsync<T>(Func<IProductRepository, Task<T>> thaoTac)
    {
        using var scope = CreateScope();
        return await thaoTac(scope.ServiceProvider.GetRequiredService<IProductRepository>());
    }

    // ───────────── Lọc ─────────────

    [Fact]
    public async Task Loc_theo_danh_muc_chi_tra_san_pham_cua_danh_muc_do()
    {
        var result = await DungRepositoryAsync(r => r.GetProductsAsync(categoryId: _categoryA));

        Assert.Equal(5, result.TotalCount);
        Assert.All(result.Items, p => Assert.Equal(_categoryA, p.CategoryId));
    }

    [Fact]
    public async Task Loc_theo_khoang_gia_lay_dung_hai_dau_mut()
    {
        // 200k..400k trong danh mục A -> A02, A03, A04. Biên PHẢI được tính vào
        // vì dùng >= và <=.
        var result = await DungRepositoryAsync(
            r => r.GetProductsAsync(categoryId: _categoryA, minPrice: 200_000m, maxPrice: 400_000m));

        Assert.Equal(3, result.TotalCount);
        Assert.All(result.Items, p => Assert.InRange(p.Price, 200_000m, 400_000m));
    }

    [Fact]
    public async Task Khong_truyen_bo_loc_nao_thi_khong_ap_dung_dieu_kien()
    {
        var result = await DungRepositoryAsync(r => r.GetProductsAsync(pageSize: 100));

        // Ít nhất 8 sản phẩm của test này; DB có thể còn dữ liệu khác.
        Assert.True(result.TotalCount >= 8);
    }

    [Fact]
    public async Task Chi_loc_gia_thi_lay_ca_hai_danh_muc()
    {
        var result = await DungRepositoryAsync(
            r => r.GetProductsAsync(minPrice: 400_000m, maxPrice: 2_000_000m, pageSize: 100));

        var cuaTest = result.Items.Where(p => _categoryIds.Contains(p.CategoryId)).ToList();

        // A04 (400k), A05 (500k), B01 (1tr), B02 (2tr)
        Assert.Equal(4, cuaTest.Count);
    }

    // ───────────── Phân trang ─────────────

    [Fact]
    public async Task Hai_trang_lien_tiep_khong_duoc_trung_ban_ghi()
    {
        var trang1 = await DungRepositoryAsync(
            r => r.GetProductsAsync(categoryId: _categoryA, page: 1, pageSize: 2));
        var trang2 = await DungRepositoryAsync(
            r => r.GetProductsAsync(categoryId: _categoryA, page: 2, pageSize: 2));

        var idTrang1 = trang1.Items.Select(p => p.Id).ToList();
        var idTrang2 = trang2.Items.Select(p => p.Id).ToList();

        Assert.Equal(2, idTrang1.Count);
        Assert.Equal(2, idTrang2.Count);
        // Thiếu tie-breaker ThenBy(Id) thì hai trang có thể trùng bản ghi.
        Assert.Empty(idTrang1.Intersect(idTrang2));
    }

    [Fact]
    public async Task TotalCount_la_tong_khop_bo_loc_khong_phai_so_item_trong_trang()
    {
        var result = await DungRepositoryAsync(
            r => r.GetProductsAsync(categoryId: _categoryA, page: 1, pageSize: 2));

        Assert.Equal(2, result.Items.Count);   // một trang
        Assert.Equal(5, result.TotalCount);    // toàn bộ khớp bộ lọc
        Assert.Equal(3, result.TotalPages);    // ceil(5 / 2)
        Assert.True(result.HasNextPage);
        Assert.False(result.HasPreviousPage);
    }

    [Fact]
    public async Task Ket_qua_phai_mang_dung_so_trang_da_yeu_cau()
    {
        var result = await DungRepositoryAsync(
            r => r.GetProductsAsync(categoryId: _categoryA, page: 2, pageSize: 2));

        // Trả sai Page thì HasPreviousPage luôn false và nút "Trang trước"
        // không bao giờ hiện trên giao diện.
        Assert.Equal(2, result.Page);
        Assert.True(result.HasPreviousPage);
        Assert.True(result.HasNextPage);   // 5 bản ghi, pageSize 2 -> còn trang 3
    }

    [Fact]
    public async Task Trang_cuoi_thi_khong_con_trang_sau()
    {
        var result = await DungRepositoryAsync(
            r => r.GetProductsAsync(categoryId: _categoryA, page: 3, pageSize: 2));

        Assert.Equal(3, result.Page);
        Assert.Single(result.Items);       // 5 = 2 + 2 + 1
        Assert.True(result.HasPreviousPage);
        Assert.False(result.HasNextPage);
    }

    [Fact]
    public async Task Trang_vuot_qua_pham_vi_roi_ve_TRANG_CUOI_va_TotalCount_van_dung()
    {
        var result = await DungRepositoryAsync(
            r => r.GetProductsAsync(categoryId: _categoryA, page: 99, pageSize: 2));

        // ⚠ Test này TRƯỚC ĐÂY khẳng định `Assert.Empty(result.Items)`, và điều đó đã
        // được đổi có chủ đích. Ghi lại lý do vì đổi một khẳng định đang xanh là việc
        // phải giải trình được:
        //
        // Tên cũ ("trả về rỗng nhưng TotalCount vẫn đúng") mô tả hành vi QUAN SÁT ĐƯỢC
        // chứ không bảo vệ nó — không có một dòng lý do nào kèm theo, khác hẳn
        // `Page_khong_hop_le_bi_dua_ve_1` ngay bên dưới (có nêu rõ "OFFSET âm"). Tức nó
        // ghi lại một tai nạn, không phải một quyết định.
        //
        // Và tai nạn đó có hậu quả thật, tìm ra bằng một lượt quét khu vực Admin:
        // `/Admin/User?page=999` in ra "Hệ thống chưa có tài khoản khách hàng nào" khi
        // đang có 11 tài khoản, còn `/Admin/Product/BulkEdit?page=999` in ra "Chưa có
        // sản phẩm nào" khi kho có 50 — và vì bộ phân trang chỉ hiện khi có nhiều hơn
        // một trang, trang rỗng KHÔNG có nó, nên người dùng rơi vào ngõ cụt.
        //
        // Phần thật sự đáng giá của test cũ là TotalCount, và nó được giữ nguyên.
        Assert.Equal(3, result.Page);      // 5 sản phẩm, 2 mỗi trang -> trang cuối là 3
        Assert.Single(result.Items);       // 5 = 2 + 2 + 1
        Assert.Equal(5, result.TotalCount);
        Assert.False(result.HasNextPage);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-5)]
    public async Task Page_khong_hop_le_bi_dua_ve_1(int page)
    {
        // ?page=-5 từ query string không được làm vỡ câu SQL (OFFSET âm).
        var result = await DungRepositoryAsync(
            r => r.GetProductsAsync(categoryId: _categoryA, page: page, pageSize: 2));

        Assert.Equal(1, result.Page);
        Assert.Equal(2, result.Items.Count);
    }

    [Fact]
    public async Task PageSize_qua_lon_bi_gioi_han()
    {
        // Chặn ?pageSize=999999 kéo sập server.
        var result = await DungRepositoryAsync(
            r => r.GetProductsAsync(categoryId: _categoryA, pageSize: 999_999));

        Assert.Equal(100, result.PageSize);
    }

    // ───────────── Hình dạng SQL ─────────────

    [Fact]
    public void Phan_trang_phai_thuc_hien_duoi_DB_bang_OFFSET_FETCH()
    {
        using var scope = CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<MiniMartDbContext>();

        // BIẾN chứ không phải hằng số: EF Core nhúng thẳng hằng số vào SQL và
        // chỉ tham số hoá giá trị đến từ biến. Repository thật dùng biến
        // (categoryId.Value) nên test phải mô phỏng đúng như vậy.
        var categoryId = _categoryA;

        var sql = context.Products
            .AsNoTracking()
            .Where(p => p.CategoryId == categoryId)
            .OrderBy(p => p.Name).ThenBy(p => p.Id)
            .Skip(12).Take(12)
            .ToQueryString();

        // Nếu phân trang rơi vào bộ nhớ, SQL sẽ không có OFFSET/FETCH và server
        // phải tải toàn bộ bảng về trước khi cắt trang.
        Assert.Contains("OFFSET", sql);
        Assert.Contains("FETCH NEXT", sql);

        // Giá trị lọc phải là THAM SỐ, không nối thẳng vào câu lệnh: chống SQL
        // injection và cho phép SQL Server tái dùng execution plan.
        Assert.DoesNotContain($"[CategoryId] = {categoryId}", sql);
        Assert.Contains("@", sql);

        // Tie-breaker phải có mặt, nếu không phân trang không ổn định.
        Assert.Contains("ORDER BY [p].[Name], [p].[Id]", sql);
    }

    public async Task DisposeAsync()
    {
        using var scope = CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<MiniMartDbContext>();

        await context.Products.Where(p => _categoryIds.Contains(p.CategoryId)).ExecuteDeleteAsync();
        await context.Categories.Where(c => _categoryIds.Contains(c.Id)).ExecuteDeleteAsync();

        _factory.Dispose();
    }
}
