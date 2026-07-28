using System.Net;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using MiniMart.Domain.Entities;
using MiniMart.Infrastructure.Data;

namespace MiniMart.Tests;

/// <summary>
/// <c>GET /Admin/Product/BulkEdit</c> - bảng sửa giá / tồn kho hàng loạt.
///
/// <para>
/// Đây là bộ test khoá <b>hợp đồng đặt tên input</b> mà
/// <see cref="ProductBulkUpdateModelTests"/> chưa kiểm được: lúc đó chưa có endpoint
/// nào render ra HTML. Ba thứ được canh ở đây đều là loại hỏng KHÔNG có lỗi build và
/// KHÔNG có lỗi runtime - chỉ mất dữ liệu trong im lặng.
/// </para>
/// </summary>
public class ProductBulkEditPageTests : IAsyncLifetime
{
    private const int SoSanPham = 25;   // > 20 = một trang, để có trang thứ hai

    private readonly WebApplicationFactory<Program> _factory = new();
    private readonly List<string> _usernames = [];

    private int _categoryId;

    public async Task InitializeAsync()
    {
        using var scope = _factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<MiniMartDbContext>();

        var category = new Category { Name = $"BE_{Guid.NewGuid():N}"[..14] };

        // Tên có tiền tố sắp xếp được để biết chắc dòng nào rơi vào trang nào:
        // repository OrderBy(Name).ThenBy(Id).
        for (var i = 0; i < SoSanPham; i++)
        {
            context.Products.Add(new Product
            {
                Name = $"ZBulk_{i:D3}_{Guid.NewGuid():N}"[..24],
                Price = 100_000m + i,
                Stock = 10 + i,
                Category = category
            });
        }

        await context.SaveChangesAsync();

        _categoryId = category.Id;
    }

    // ───────────── Hợp đồng đặt tên input ─────────────

    [Fact]
    public async Task Chi_so_input_phai_LIEN_TUC_bat_dau_tu_0()
    {
        var html = await MoBangAsync();

        var chiSo = Regex.Matches(html, @"name=""Items\[(\d+)\]\.Price""")
            .Select(m => int.Parse(m.Groups[1].Value))
            .Distinct()
            .Order()
            .ToArray();

        // ★ Ràng buộc quan trọng nhất của cả màn hình.
        //
        // Model binder đọc Items[0], Items[1]... và DỪNG ở chỉ số đầu tiên bị thiếu.
        // Dùng ProductId làm chỉ số (Items[@p.Id]) thì id không liên tục từ 0, binder
        // dừng ngay dòng đầu và ÂM THẦM bỏ toàn bộ phần còn lại: người dùng sửa 20
        // dòng, bấm Lưu, hệ thống báo thành công, 19 dòng không được ghi.
        Assert.Equal(Enumerable.Range(0, 20).ToArray(), chiSo);
    }

    [Fact]
    public async Task Moi_dong_co_du_bon_input_dung_ten()
    {
        var html = await MoBangAsync();

        // Tiền tố "Items" chính là TÊN property trên ViewModel. Đổi tên property mà
        // quên đổi trong Razor thì binder không tìm thấy gì, Items về rỗng, và
        // Controller lưu 0 dòng - không lỗi nào.
        foreach (var ten in new[] { "Id", "Price", "Stock", "RowVersion" })
        {
            Assert.Contains($"name=\"Items[0].{ten}\"", html, StringComparison.Ordinal);
            Assert.Contains($"name=\"Items[19].{ten}\"", html, StringComparison.Ordinal);
        }
    }

    [Fact]
    public async Task KHONG_duoc_co_input_thieu_chi_so()
    {
        var html = await MoBangAsync();

        // Đây là dấu vết của `foreach` thay vì `for`: asp-for="dong.Price" sinh ra
        // name="Price" trần, mọi dòng trùng tên nhau và binder chỉ nhận một giá trị.
        Assert.DoesNotContain("name=\"Price\"", html, StringComparison.Ordinal);
        Assert.DoesNotContain("name=\"Stock\"", html, StringComparison.Ordinal);
        Assert.DoesNotContain("name=\"RowVersion\"", html, StringComparison.Ordinal);
    }

    // ───────────── RowVersion ─────────────

    [Fact]
    public async Task RowVersion_render_dang_Base64_khong_phai_System_Byte()
    {
        var html = await MoBangAsync();

        // asp-for trên byte[] gọi ToString() và cho ra đúng chuỗi này. Form vẫn
        // submit, binder không giải mã được, Optimistic Concurrency biến mất im lặng.
        Assert.DoesNotContain("System.Byte[]", html, StringComparison.Ordinal);

        var giaTri = LayGiaTri(html, @"Items\[\d+\]\.RowVersion");

        Assert.Equal(20, giaTri.Length);

        foreach (var v in giaTri)
        {
            // rowversion của SQL Server là 8 byte -> đúng 12 ký tự Base64.
            Assert.True(Convert.TryFromBase64String(v, new byte[16], out var soByte),
                $"Không phải Base64 hợp lệ: '{v}'");
            Assert.Equal(8, soByte);
        }
    }

    [Fact]
    public async Task Moi_dong_co_RowVersion_RIENG()
    {
        var html = await MoBangAsync();

        var giaTri = LayGiaTri(html, @"Items\[\d+\]\.RowVersion");

        // Một lần POST mang theo N phiên bản ĐỘC LẬP - đó là điểm khiến sửa hàng loạt
        // khác sửa lẻ. Render chung một giá trị cho mọi dòng thì 19 dòng sẽ so với
        // phiên bản của dòng thứ 20.
        Assert.Equal(giaTri.Length, giaTri.Distinct().Count());
    }

    // ───────────── Phân trang (tái dùng Phase 3) ─────────────

    [Fact]
    public async Task Phan_trang_20_dong_moi_trang()
    {
        var trang1 = await MoBangAsync();
        var trang2 = await MoBangAsync(page: 2);

        Assert.Equal(20, DemDong(trang1));

        // 25 sản phẩm của test này + những sản phẩm sẵn có trong DB, nên trang 2 chỉ
        // khẳng định "có dòng" chứ không khẳng định con số cụ thể.
        Assert.True(DemDong(trang2) > 0);
    }

    // ⚠ ĐÃ BỎ: một test "hai trang không lặp lại cùng một sản phẩm".
    //
    // Nó xanh khi chạy riêng và ĐỎ khi chạy toàn bộ, vì nó mở trang 1 và trang 2 bằng
    // HAI request và giả định tập sản phẩm toàn cục đứng yên ở giữa - trong khi các
    // test class khác chạy song song vẫn đang thêm/xoá sản phẩm. Đó không phải lỗi của
    // code: phân trang bằng OFFSET vốn KHÔNG bảo đảm được điều đó khi dữ liệu đổi.
    //
    // Tính chất tie-breaker đã được khoá đúng chỗ và không flaky, trên một tập dữ liệu
    // do chính test kiểm soát: ProductQueryTests và ProductLoadMoreTests. Giữ thêm một
    // bản đỏ ngẫu nhiên ở đây chỉ làm cả bộ test mất uy tín.

    [Fact]
    public async Task Page_bay_bi_kep_ve_trang_1()
    {
        var html = await MoBangAsync(page: -5);

        // Repository đã kẹp; Controller đọc lại Page từ KẾT QUẢ chứ không từ tham số,
        // nên hidden field Page mang số trang THẬT sự đang xem.
        Assert.Equal(["1"], LayGiaTri(html, "Page"));
    }

    // ───────────── Bảo mật ─────────────

    [Fact]
    public async Task Nguoi_khong_phai_Admin_khong_vao_duoc()
    {
        using var client = _factory.CreateClient(
            new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });

        var response = await client.GetAsync("/Admin/Product/BulkEdit");

        // [Authorize(Roles = "Admin")] ở cấp class nên action thêm sau tự được bảo vệ.
        Assert.Contains(
            response.StatusCode,
            new[] { System.Net.HttpStatusCode.Found, System.Net.HttpStatusCode.Redirect });
    }

    [Fact]
    public async Task Bang_KHONG_co_o_nhap_ten_san_pham()
    {
        var html = await MoBangAsync();

        // Name có [BindNever] và chỉ được hiện ra dưới dạng chữ. Có ô nhập tên ở đây
        // nghĩa là màn hình vừa mở thêm một quyền mà nó không định cho.
        Assert.DoesNotContain("Items[0].Name", html, StringComparison.Ordinal);
    }

    [Fact]
    public void Helper_doc_value_phai_giai_ma_thuc_the_HTML()
    {
        // Chuỗi dưới đây là HTML THẬT đã làm test đỏ: Base64 của rowversion chứa '+',
        // HtmlEncoder mã hoá thành "&#x2B;".
        //
        // Test này TẤT ĐỊNH, khác hẳn các test còn lại của lớp: chúng chỉ chạm vào bug
        // khi giá trị rowversion ngẫu nhiên tình cờ có dấu '+' (đã đo: khoảng 1 trong
        // vài lần chạy toàn bộ), nên chúng canh được rất kém. Chỗ đúng để khoá một bài
        // học phụ thuộc dữ liệu là một test tự cấp dữ liệu.
        const string html =
            """<input type="hidden" name="Items[0].RowVersion" value="AAAAAAAd0&#x2B;s=" />""";

        var giaTri = LayGiaTri(html, @"Items\[\d+\]\.RowVersion");

        Assert.Equal("AAAAAAAd0+s=", giaTri[0]);
        Assert.True(Convert.TryFromBase64String(giaTri[0], new byte[16], out var soByte));
        Assert.Equal(8, soByte);
    }

    // ───────────── Helper ─────────────

    private static int DemDong(string html) =>
        Regex.Matches(html, @"name=""Items\[\d+\]\.Id""").Count;

    /// <summary>
    /// Bóc <c>value</c> của mọi <c>&lt;input&gt;</c> có <c>name</c> khớp mẫu.
    ///
    /// <para>
    /// ★ Tìm THẺ trước rồi mới bóc thuộc tính, TUYỆT ĐỐI không viết một regex kiểu
    /// <c>name="..." value="..."</c>. Hai bản đầu của bộ test này làm đúng thế và đỏ
    /// oan: Razor xuống dòng giữa <c>name</c> và <c>value</c>, còn <c>asp-for</c> thì
    /// chèn cả loạt <c>data-val-*</c> vào giữa. Đây chính là bài học đã ghi ở
    /// <c>rules/testing.md</c> mà tôi vừa mắc lại.
    /// </para>
    /// <para>
    /// Kèm <c>Assert</c> cho chính helper để nó tự tố giác khi đọc ra rỗng, thay vì
    /// lặng lẽ trả mảng trống và làm test đỏ ở một assertion chẳng liên quan.
    /// </para>
    /// </summary>
    private static string[] LayGiaTri(string html, string mauTen)
    {
        var the = Regex.Matches(html, @"<input\b[^>]*>", RegexOptions.Singleline)
            .Select(m => m.Value)
            .Where(t => Regex.IsMatch(t, $@"name=""{mauTen}"""))
            .ToArray();

        Assert.NotEmpty(the);

        return the
            // ★ HtmlDecode: Base64 của rowversion đôi khi chứa '+', mà HtmlEncoder mã
            // hoá thành "&#x2B;". Bỏ bước này thì test đỏ NGẪU NHIÊN - chỉ khi giá trị
            // rowversion tình cờ sinh ra dấu '+' - và trông hệt như flaky hạ tầng.
            // Trình duyệt tự giải mã lúc parse thuộc tính nên HTML là ĐÚNG; chỗ sai là
            // ở đây. Đã gặp thật: 'AAAAAAAd0&#x2B;s=' không phải Base64 hợp lệ.
            .Select(t => WebUtility.HtmlDecode(
                Regex.Match(t, @"value=""([^""]*)""").Groups[1].Value))
            .ToArray();
    }

    private async Task<string> MoBangAsync(int page = 1)
    {
        using var client = await TaoClientAdminAsync();

        return await client.GetStringAsync($"/Admin/Product/BulkEdit?page={page}");
    }

    private async Task<HttpClient> TaoClientAdminAsync()
    {
        var (client, username) = await _factory.TaoClientAdminAsync("be");

        _usernames.Add(username);

        return client;
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
