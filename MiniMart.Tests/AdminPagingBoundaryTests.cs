using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using MiniMart.Domain.Entities;
using MiniMart.Infrastructure.Data;

namespace MiniMart.Tests;

/// <summary>
/// Trang vượt quá trang cuối phải rơi về trang cuối, KHÔNG phải trả về một trang rỗng.
///
/// <para>
/// Lỗi này được tìm ra bằng một lượt quét khu vực Admin, và nó thuộc loại
/// <b>không có exception nào</b>: <c>?page=999</c> trả HTTP 200, SQL hợp lệ,
/// <c>Skip(19960)</c> chạy ngon lành và trả về danh sách rỗng. Cái sai chỉ hiện ra ở
/// hai chỗ, cả hai đều ở tầng giao diện:
/// </para>
/// <list type="number">
/// <item>
/// View không phân biệt được "trang này rỗng" với "chưa có dữ liệu nào", nên nó in ra
/// câu thứ hai — <b>sai sự thật</b> khi trong kho có 50 sản phẩm / 11 tài khoản.
/// </item>
/// <item>
/// Bộ phân trang chỉ được render khi có nhiều hơn một trang, mà trang rỗng thì không
/// có nó → <b>ngõ cụt</b>, người dùng chỉ thoát được bằng cách tự sửa URL.
/// </item>
/// </list>
/// <para>
/// Chữa ở tầng truy vấn chứ không ở view: view không nên biết cách sửa một tham số
/// hỏng, và chữa ở Controller là mỗi Controller phải nhớ làm lại. Đây đúng là nửa còn
/// thiếu của lệnh kẹp <c>page &lt; 1</c> đã có từ trước.
/// </para>
/// </summary>
public class AdminPagingBoundaryTests : IAsyncLifetime
{
    private const int SoSanPham = 7;

    private readonly WebApplicationFactory<Program> _factory = new();
    private int _categoryId;
    private readonly List<string> _usernames = [];

    public async Task InitializeAsync()
    {
        using var scope = _factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<MiniMartDbContext>();

        var category = new Category { Name = $"PB_{Guid.NewGuid():N}"[..14] };

        for (var i = 1; i <= SoSanPham; i++)
        {
            category.Products.Add(new Product
            {
                Name = $"PBSP{i:D2}_{Guid.NewGuid():N}"[..18],
                Price = i * 1000m,
                Stock = 5
            });
        }

        context.Categories.Add(category);
        await context.SaveChangesAsync();
        _categoryId = category.Id;
    }

    // ───────────── Sản phẩm ─────────────

    [Fact]
    public async Task Trang_vuot_qua_trang_cuoi_thi_ve_TRANG_CUOI_chu_khong_rong()
    {
        using var scope = _factory.Services.CreateScope();
        var repository = scope.ServiceProvider.GetRequiredService<
            Domain.Interfaces.IProductRepository>();

        // 7 sản phẩm, 3 mỗi trang -> 3 trang. Hỏi trang 999.
        var ketQua = await repository.GetProductsAsync(
            categoryId: _categoryId, page: 999, pageSize: 3);

        Assert.Equal(3, ketQua.Page);          // rơi về trang cuối
        Assert.Equal(3, ketQua.TotalPages);
        Assert.Single(ketQua.Items);           // trang 3 có đúng 1 món (7 = 3+3+1)
        Assert.Equal(SoSanPham, ketQua.TotalCount);
    }

    [Fact]
    public async Task Trang_am_van_ve_trang_MOT_nhu_truoc()
    {
        using var scope = _factory.Services.CreateScope();
        var repository = scope.ServiceProvider.GetRequiredService<
            Domain.Interfaces.IProductRepository>();

        // Nửa cũ của lệnh kẹp phải còn nguyên - đây là test canh việc "sửa cận trên
        // làm hỏng cận dưới".
        var ketQua = await repository.GetProductsAsync(
            categoryId: _categoryId, page: -5, pageSize: 3);

        Assert.Equal(1, ketQua.Page);
        Assert.Equal(3, ketQua.Items.Count);
    }

    [Fact]
    public async Task Bo_loc_khong_khop_gi_thi_KHONG_ep_trang_ve_0()
    {
        using var scope = _factory.Services.CreateScope();
        var repository = scope.ServiceProvider.GetRequiredService<
            Domain.Interfaces.IProductRepository>();

        // TotalCount = 0 -> TotalPages = 0. Kẹp mà không xét điều kiện này thì Page
        // thành 0, và "trang 0" không phải một trang nào cả: link "Trước/Sau" tính từ
        // đó sẽ ra trang -1 và trang 1.
        var ketQua = await repository.GetProductsAsync(
            categoryId: _categoryId, minPrice: 999_999_999m, page: 999, pageSize: 3);

        Assert.Equal(0, ketQua.TotalCount);
        Assert.Empty(ketQua.Items);
        Assert.True(ketQua.Page >= 1, $"Page = {ketQua.Page}, không được nhỏ hơn 1.");
    }

    [Fact]
    public async Task Trang_BulkEdit_vuot_qua_cuoi_KHONG_noi_doi_la_chua_co_san_pham()
    {
        var (client, username) = await _factory.TaoClientAdminAsync("pb");
        _usernames.Add(username);

        var html = await client.GetStringAsync("/Admin/Product/BulkEdit?page=999");

        // Câu này đúng khi bảng Products rỗng thật, và SAI khi chỉ là trang rỗng.
        Assert.DoesNotContain("Chưa có sản phẩm nào", html);

        // Và phải có bộ phân trang để quay lại - không có nó là ngõ cụt.
        Assert.Contains("← Trước", html);
    }

    // ───────────── Người dùng ─────────────

    [Fact]
    public async Task Trang_nguoi_dung_vuot_qua_cuoi_KHONG_noi_doi_la_he_thong_rong()
    {
        var (client, username) = await _factory.TaoClientAdminAsync("pu");
        _usernames.Add(username);

        var html = await client.GetStringAsync("/Admin/User?page=999");

        // Có ít nhất tài khoản admin vừa tạo ở trên, nên câu này luôn là nói dối.
        Assert.DoesNotContain("chưa có tài khoản khách hàng nào", html);
        Assert.Contains(username, html);
    }

    [Fact]
    public async Task Tim_kiem_khong_ra_gi_van_noi_dung_LY_DO()
    {
        var (client, username) = await _factory.TaoClientAdminAsync("pt");
        _usernames.Add(username);

        var html = await client.GetStringAsync("/Admin/User?search=khongtontai_xyz_9876543");

        // Rỗng vì BỘ LỌC khác hẳn rỗng vì HẾT TRANG. Chỉ trường hợp này mới được
        // nói "không khớp", và nó phải nêu lại từ khoá để người dùng biết đã tìm gì.
        Assert.Contains("khớp với từ khoá", html);
        Assert.DoesNotContain("chưa có tài khoản khách hàng nào", html);
    }

    public async Task DisposeAsync()
    {
        using var scope = _factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<MiniMartDbContext>();

        await context.Products.Where(p => p.CategoryId == _categoryId).ExecuteDeleteAsync();
        await context.Categories.Where(c => c.Id == _categoryId).ExecuteDeleteAsync();
        await context.Users.Where(u => _usernames.Contains(u.Username)).ExecuteDeleteAsync();

        _factory.Dispose();
    }
}
