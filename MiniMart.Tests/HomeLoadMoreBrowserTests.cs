using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Playwright;
using MiniMart.Domain.Entities;
using MiniMart.Infrastructure.Data;

namespace MiniMart.Tests;

/// <summary>
/// <c>home-load-more.js</c> chạy trong trình duyệt thật.
///
/// <para>
/// Trước bộ test này, file JS đó chưa từng được thực thi trong bất kỳ test nào — chỉ
/// cú pháp của nó được kiểm (<c>node --check</c>) và HTML mà endpoint trả về được kiểm
/// (<c>ProductLoadMoreTests</c>). Mọi thứ ở giữa hai đầu đó là vùng trắng.
/// </para>
/// </summary>
public class HomeLoadMoreBrowserTests : PlaywrightTestBase
{
    private const int SoSanPham = 15;      // > PageSize 12 nên có đúng 2 trang
    private const int TrangDau = 12;

    private int _categoryId;
    private string _tenDanhMuc = "";

    protected override async Task SeedAsync()
    {
        using var scope = Factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<MiniMartDbContext>();

        _tenDanhMuc = $"PW_{Guid.NewGuid():N}"[..14];
        var category = new Category { Name = _tenDanhMuc };

        for (var i = 1; i <= SoSanPham; i++)
        {
            category.Products.Add(new Product
            {
                Name = $"PWSP{i:D2}",
                Price = i * 10_000m,
                Stock = 20
            });
        }

        context.Categories.Add(category);
        await context.SaveChangesAsync();
        _categoryId = category.Id;
    }

    protected override async Task DonDepAsync()
    {
        using var scope = Factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<MiniMartDbContext>();

        await context.CartItems
            .Where(i => i.Product.CategoryId == _categoryId).ExecuteDeleteAsync();
        await context.Products.Where(p => p.CategoryId == _categoryId).ExecuteDeleteAsync();
        await context.Categories.Where(c => c.Id == _categoryId).ExecuteDeleteAsync();
    }

    /// <summary>Chỉ lọc theo danh mục của test này, để không đếm nhầm hàng của người khác.</summary>
    private Task MoTrangChuAsync() => Page.GotoAsync($"/?categoryId={_categoryId}");

    /// <summary>
    /// <c>&gt; .col</c> chứ không phải <c>.col</c>: dấu <c>&gt;</c> đòi CON TRỰC TIẾP.
    ///
    /// <para>
    /// Đây không phải chi tiết thẩm mỹ — nó là thứ bắt được lỗi dán nhầm vị trí.
    /// <c>insertAdjacentHTML('afterend')</c> đặt thẻ thành ANH EM của lưới thay vì con
    /// của nó: trang vẫn hiện đủ sản phẩm, chỉ là grid Bootstrap vỡ. Đếm bằng
    /// <c>.col</c> trần thì vẫn ra 24 và test xanh vô nghĩa.
    /// </para>
    /// </summary>
    private ILocator TheSanPham => Page.Locator("#productGrid > .col");

    [Fact]
    public async Task Bam_Xem_them_thi_NOI_THEM_the_vao_luoi_chu_khong_thay_the_cu()
    {
        await MoTrangChuAsync();

        await Assertions.Expect(TheSanPham).ToHaveCountAsync(TrangDau);

        await Page.ClickAsync("#btnLoadMore");

        // 15 = 12 + 3. Nếu JS THAY danh sách thay vì nối thêm thì con số này là 3.
        await Assertions.Expect(TheSanPham).ToHaveCountAsync(SoSanPham);

        // Và thẻ của trang 1 phải còn nguyên.
        await Assertions.Expect(Page.Locator("#productGrid >> text=PWSP01")).ToBeVisibleAsync();
        await Assertions.Expect(Page.Locator("#productGrid >> text=PWSP15")).ToBeVisibleAsync();
    }

    [Fact]
    public async Task So_dem_hien_thi_duoc_cap_nhat_theo_so_the_THAT_trong_DOM()
    {
        await MoTrangChuAsync();

        await Assertions.Expect(Page.Locator("#shownCount")).ToHaveTextAsync(TrangDau.ToString());

        await Page.ClickAsync("#btnLoadMore");

        await Assertions.Expect(Page.Locator("#shownCount")).ToHaveTextAsync(SoSanPham.ToString());
    }

    [Fact]
    public async Task Het_du_lieu_thi_nut_Xem_them_BIEN_MAT()
    {
        await MoTrangChuAsync();
        await Page.ClickAsync("#btnLoadMore");

        // Server là nơi DUY NHẤT biết còn trang sau hay không (header X-Next-Page rỗng).
        // Client tự suy ra từ số item nhận được sẽ sai đúng lúc trang cuối vừa đủ đầy.
        await Assertions.Expect(Page.Locator("#btnLoadMore")).ToHaveCountAsync(0);
    }

    [Fact]
    public async Task Bam_HAI_LAN_that_nhanh_KHONG_dan_trung_mot_trang()
    {
        await MoTrangChuAsync();

        // ★ Test này chỉ có ý nghĩa trong trình duyệt thật: nó kiểm một cuộc đua về
        // THỜI GIAN. `fetch` là bất đồng bộ, nên hai cú bấm liên tiếp trước khi request
        // đầu trả về sẽ gửi HAI request cùng page và dán cùng một trang vào lưới hai
        // lần. Thứ chặn nó là cờ `dangTai` trong JS — không có gì ở phía server biết
        // đến sự tồn tại của nó.
        //
        // Dùng dispatchEvent thay vì ClickAsync: Playwright tự đợi nút hết `disabled`
        // trước khi click, mà `disabled = true` chính là nửa còn lại của cơ chế chặn —
        // đợi như vậy là vô hiệu hoá đúng thứ đang muốn kiểm.
        await Page.EvalOnSelectorAsync("#btnLoadMore", @"nut => {
            nut.dispatchEvent(new MouseEvent('click', { bubbles: true }));
            nut.dispatchEvent(new MouseEvent('click', { bubbles: true }));
        }");

        // Chờ mạng lặng rồi mới đếm, thay vì đếm ngay.
        await Page.WaitForLoadStateAsync(LoadState.NetworkIdle);

        // 15 chứ không phải 18 (12 + 3 + 3).
        await Assertions.Expect(TheSanPham).ToHaveCountAsync(SoSanPham);
    }

    [Fact]
    public async Task Bo_loc_dang_hien_thi_duoc_giu_qua_cac_trang()
    {
        // Lọc giá để trang 1 KHÔNG chứa hết danh mục: 15 sản phẩm giá 10k..150k,
        // minPrice=20000 -> còn 14, vẫn nhiều hơn 12 nên có trang 2.
        await Page.GotoAsync($"/?categoryId={_categoryId}&minPrice=20000");

        await Assertions.Expect(TheSanPham).ToHaveCountAsync(TrangDau);

        await Page.ClickAsync("#btnLoadMore");
        await Assertions.Expect(TheSanPham).ToHaveCountAsync(14);

        // PWSP01 giá 10.000 đã bị bộ lọc loại ở trang 1. Nếu JS quên gửi kèm minPrice
        // thì trang 2 sẽ kéo nó về — bug kinh điển của phân trang có lọc.
        await Assertions.Expect(Page.Locator("#productGrid >> text=PWSP01")).ToHaveCountAsync(0);
    }

    [Fact]
    public async Task Nguoi_dung_sua_o_loc_ma_CHUA_bam_Loc_thi_trang_2_van_theo_bo_loc_cu()
    {
        await MoTrangChuAsync();

        // Gõ vào ô lọc nhưng KHÔNG submit. Lúc này ô nhập và danh sách đang hiển thị đã
        // lệch nhau, và JS phải phân trang theo thứ ĐANG HIỂN THỊ.
        await Page.FillAsync("#minPrice", "140000");

        await Page.ClickAsync("#btnLoadMore");

        // Vẫn đủ 15 — nếu JS đọc lại giá trị từ ô input thay vì từ data-* trên nút thì
        // trang 2 chỉ trả về sản phẩm giá >= 140.000 và tổng sẽ không phải 15.
        await Assertions.Expect(TheSanPham).ToHaveCountAsync(SoSanPham);
    }

    [Fact]
    public async Task Server_loi_thi_hien_thong_bao_va_nut_van_bam_lai_duoc()
    {
        await MoTrangChuAsync();

        // Chặn request ở tầng mạng của trình duyệt để giả lập lỗi 500. `fetch` KHÔNG
        // reject khi server trả 5xx (chỉ reject khi lỗi mạng), nên nhánh này chỉ chạy
        // đúng nhờ lệnh kiểm `response.ok` trong JS.
        await Page.RouteAsync("**/Product/LoadMore*", route =>
            route.FulfillAsync(new RouteFulfillOptions { Status = 500, Body = "loi" }));

        await Page.ClickAsync("#btnLoadMore");

        await Assertions.Expect(Page.Locator("#loadMoreError")).ToBeVisibleAsync();

        // Lỗi tạm thời không được làm nút chết vĩnh viễn — người dùng phải thử lại được.
        await Assertions.Expect(Page.Locator("#btnLoadMore")).Not.ToBeDisabledAsync();

        // Và TUYỆT ĐỐI không dán HTML của trang lỗi vào lưới.
        await Assertions.Expect(TheSanPham).ToHaveCountAsync(TrangDau);
    }
}
