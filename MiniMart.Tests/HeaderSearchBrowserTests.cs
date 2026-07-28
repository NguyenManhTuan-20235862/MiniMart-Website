using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Playwright;
using MiniMart.Domain.Entities;
using MiniMart.Infrastructure.Data;

namespace MiniMart.Tests;

/// <summary>
/// <c>header-search.js</c> chạy trong trình duyệt thật: gõ, chờ, thấy gợi ý.
///
/// <para>
/// Phần "gợi ý hiện ra khi đang gõ" không có cách nào kiểm bằng <c>HttpClient</c>:
/// nó là chuỗi sự kiện <c>input</c> → hết thời gian chờ → <c>fetch</c> → đổ HTML vào
/// panel. <c>ProductSuggestTests</c> chứng minh endpoint trả về ĐÚNG dữ liệu và đúng
/// thứ tự; bộ này chứng minh dữ liệu đó thật sự tới được màn hình.
/// </para>
/// </summary>
public class HeaderSearchBrowserTests : PlaywrightTestBase
{
    private int _categoryId;

    protected override async Task SeedAsync()
    {
        using var scope = Factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<MiniMartDbContext>();

        var category = new Category { Name = $"HS_{Guid.NewGuid():N}"[..14] };

        // ⚠ Từ khoá ở đây là "kudo", KHÁC "zumo" của ProductSuggestTests — và điều đó
        // là bắt buộc, không phải ngẫu nhiên.
        //
        // xUnit chạy các test class SONG SONG. Hai class cùng dùng một từ khoá thì sản
        // phẩm của class kia cũng khớp, chiếm mất chỗ trong `take` và đẩy dòng hạng thấp
        // nhất ra ngoài — assertion về THỨ TỰ đỏ theo kiểu ngẫu nhiên, chỉ xảy ra khi
        // chạy cả bộ chứ không khi chạy riêng. Đã gặp thật: `Khop_DAU_TEN...` đỏ ở MỌI
        // mutation, kể cả những mutation chỉ sửa JavaScript — dấu hiệu rõ ràng rằng nó
        // đỏ vì nhiễu chứ không vì mutation.
        //
        // Quy tắc rút ra: test khẳng định thứ tự trên một truy vấn có `take` thì từ
        // khoá phải là DUY NHẤT cho class đó.

        // ★ "Ab Kudo" tồn tại để dữ liệu PHÂN BIỆT ĐƯỢC xếp hạng với độ dài.
        //
        // Nó hạng 1 (một TỪ bắt đầu bằng từ khoá) nhưng lại NGẮN hơn "Kudo Bcd" hạng 0.
        // Nhờ vậy hai quy tắc cho ra hai thứ tự khác nhau:
        //     đúng (hạng trước, dài sau) -> Kudo Aa, Kudo Bcd, Ab Kudo, Chuột Kudo Zz
        //     nếu chỉ theo độ dài        -> Ab Kudo, Kudo Aa, Kudo Bcd, Chuột Kudo Zz
        //
        // Bản đầu của bộ test này KHÔNG có nó, và mutation "bỏ xếp hạng" đã LỌT: dữ
        // liệu cũ tình cờ cho cùng một thứ tự theo cả hai quy tắc, nên test xanh mà
        // không chứng minh được điều nó nói.
        category.Products.Add(new Product { Name = "Kudo Aa", Price = 111_000m, Stock = 5 });
        category.Products.Add(new Product { Name = "Kudo Bcd", Price = 222_000m, Stock = 5 });
        category.Products.Add(new Product { Name = "Ab Kudo", Price = 333_000m, Stock = 5 });
        category.Products.Add(new Product { Name = "Chuột Kudo Zz", Price = 444_000m, Stock = 5 });

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

    private ILocator O => Page.Locator("[data-o-tim-kiem]");

    private ILocator Panel => Page.Locator("[data-bang-goi-y]");

    private ILocator Dong => Page.Locator("[data-bang-goi-y] [data-goi-y]");

    private async Task GoAsync(string tuKhoa)
    {
        await O.FillAsync(string.Empty);

        // TypeAsync gõ từng phím thật, nên nó đi qua đúng đường mà người dùng đi:
        // mỗi phím là một sự kiện `input`, và bộ đếm chờ được đặt lại mỗi lần.
        // FillAsync gán thẳng value và chỉ phát MỘT sự kiện — không kiểm được debounce.
        await O.PressSequentiallyAsync(tuKhoa, new LocatorPressSequentiallyOptions { Delay = 30 });
    }

    [Fact]
    public async Task Go_hai_ky_tu_thi_goi_y_HIEN_RA()
    {
        await Page.GotoAsync("/");

        // Panel luôn có trong DOM, chỉ ẩn — server render sẵn để JS không phải tạo thẻ.
        await Assertions.Expect(Panel).ToHaveCountAsync(1);
        await Assertions.Expect(Panel).ToBeHiddenAsync();

        await GoAsync("kudo");

        await Assertions.Expect(Panel).ToBeVisibleAsync();
        await Assertions.Expect(Dong).ToHaveCountAsync(4);
    }

    [Fact]
    public async Task MOT_ky_tu_thi_KHONG_goi_y()
    {
        await Page.GotoAsync("/");

        await GoAsync("z");

        // Ngưỡng 2 ký tự ở client phải khớp ngưỡng ở ProductRepository. Một ký tự
        // khớp gần như mọi thứ nên dropdown lúc đó là nhiễu, không phải trợ giúp.
        await Assertions.Expect(Panel).ToBeHiddenAsync();
    }

    [Fact]
    public async Task Cang_go_NHIEU_thi_danh_sach_cang_HEP()
    {
        await Page.GotoAsync("/");

        await GoAsync("kudo");
        await Assertions.Expect(Dong).ToHaveCountAsync(4);

        await GoAsync("kudo b");
        await Assertions.Expect(Dong).ToHaveCountAsync(1);

        // Đúng yêu cầu "càng nhập nhiều thông tin thì càng thu hẹp".
        await Assertions.Expect(Dong.First).ToContainTextAsync("Kudo Bcd");
    }

    [Fact]
    public async Task Goi_y_giong_nhat_len_DAU_danh_sach()
    {
        await Page.GotoAsync("/");

        await GoAsync("kudo");

        // Khẳng định ĐỦ BỐN vị trí, không chỉ hai. Chỉ kiểm hai dòng đầu là bỏ sót
        // đúng chỗ mà xếp hạng và độ dài mâu thuẫn nhau — xem ghi chú ở SeedAsync.
        //
        //   hạng 0: "Kudo Aa" (7), "Kudo Bcd" (8)   <- bắt đầu bằng từ khoá
        //   hạng 1: "Ab Kudo" (7), "Chuột Kudo Zz"  <- một TỪ bắt đầu bằng từ khoá
        //
        // "Ab Kudo" ngắn hơn "Kudo Bcd" nhưng vẫn phải đứng SAU, vì hạng thắng độ dài.
        await Assertions.Expect(Dong.Nth(0)).ToContainTextAsync("Kudo Aa");
        await Assertions.Expect(Dong.Nth(1)).ToContainTextAsync("Kudo Bcd");
        await Assertions.Expect(Dong.Nth(2)).ToContainTextAsync("Ab Kudo");
        await Assertions.Expect(Dong.Nth(3)).ToContainTextAsync("Chuột Kudo Zz");
    }

    [Fact]
    public async Task Go_KHONG_DAU_van_ra_ten_co_dau()
    {
        await Page.GotoAsync("/");

        await GoAsync("chuot kudo");

        await Assertions.Expect(Dong).ToHaveCountAsync(1);
        await Assertions.Expect(Dong.First).ToContainTextAsync("Chuột Kudo Zz");
    }

    [Fact]
    public async Task Bam_vao_goi_y_thi_sang_trang_chi_tiet()
    {
        await Page.GotoAsync("/");

        await GoAsync("kudo b");
        await Assertions.Expect(Dong).ToHaveCountAsync(1);

        await Dong.First.ClickAsync();

        Assert.Contains("/Product/Details/", Page.Url, StringComparison.OrdinalIgnoreCase);
        await Assertions.Expect(Page.Locator("[data-ten-san-pham]")).ToHaveTextAsync("Kudo Bcd");
    }

    [Fact]
    public async Task Phim_mui_ten_va_Enter_chon_duoc_goi_y()
    {
        await Page.GotoAsync("/");

        await GoAsync("kudo");
        await Assertions.Expect(Dong).ToHaveCountAsync(4);

        // Xuống 2 lần -> dòng thứ hai ("Kudo Bcd"), rồi Enter.
        await O.PressAsync("ArrowDown");
        await O.PressAsync("ArrowDown");
        await O.PressAsync("Enter");

        // Enter khi ĐANG chọn phải mở gợi ý, không phải submit form tìm kiếm.
        Assert.Contains("/Product/Details/", Page.Url, StringComparison.OrdinalIgnoreCase);
        await Assertions.Expect(Page.Locator("[data-ten-san-pham]")).ToHaveTextAsync("Kudo Bcd");
    }

    [Fact]
    public async Task Enter_khi_CHUA_chon_gi_van_submit_form_tim_kiem()
    {
        await Page.GotoAsync("/");

        await GoAsync("kudo");
        await Assertions.Expect(Dong).ToHaveCountAsync(4);

        // ★ Không được cướp phím Enter. Đường tìm kiếm đầy đủ vốn đã chạy được từ
        // trước; một tính năng gợi ý làm hỏng nó là đánh đổi tệ.
        await O.PressAsync("Enter");

        Assert.Contains("search=kudo", Page.Url, StringComparison.OrdinalIgnoreCase);
        await Assertions.Expect(Page.Locator("#productGrid > .col")).ToHaveCountAsync(4);
    }

    [Fact]
    public async Task Escape_dong_bang_goi_y()
    {
        await Page.GotoAsync("/");

        await GoAsync("kudo");
        await Assertions.Expect(Panel).ToBeVisibleAsync();

        await O.PressAsync("Escape");

        await Assertions.Expect(Panel).ToBeHiddenAsync();
    }

    [Fact]
    public async Task Bam_ra_ngoai_thi_dong_bang_goi_y()
    {
        await Page.GotoAsync("/");

        await GoAsync("kudo");
        await Assertions.Expect(Panel).ToBeVisibleAsync();

        await Page.Locator("h1, h2").First.ClickAsync();

        await Assertions.Expect(Panel).ToBeHiddenAsync();
    }

    [Fact]
    public async Task KHONG_lo_con_so_ton_kho_ra_goi_y()
    {
        const int tonKhoDacBiet = 8_675_309;

        using (var scope = Factory.Services.CreateScope())
        {
            var context = scope.ServiceProvider.GetRequiredService<MiniMartDbContext>();
            context.Products.Add(new Product
            {
                Name = "Kudo Ton Kho",
                Price = 555_000m,
                Stock = tonKhoDacBiet,
                CategoryId = _categoryId
            });
            await context.SaveChangesAsync();
        }

        await Page.GotoAsync("/");
        await GoAsync("kudo ton");

        await Assertions.Expect(Dong).ToHaveCountAsync(1);

        var html = await Panel.InnerHTMLAsync();
        Assert.DoesNotContain(tonKhoDacBiet.ToString(), html);
    }

    [Fact]
    public async Task Server_loi_thi_dong_bang_goi_y_chu_khong_lam_hong_o_tim_kiem()
    {
        await Page.GotoAsync("/");

        // ★ Thân response phải là HTML TẠO RA PHẦN TỬ, không phải chuỗi trơn.
        //
        // Bản đầu của test này trả `Body = "loi"`, và mutation "bỏ lệnh kiểm
        // response.ok" đã LỌT: chuỗi trơn gán vào innerHTML chỉ tạo một text node nên
        // `panel.children.length` vẫn là 0, JS vẫn gọi an(), và assertion "panel bị
        // ẩn" vẫn đúng — qua một con đường hoàn toàn khác. Test khẳng định KẾT CỤC mà
        // kết cục lại trùng nhau thì nó không phân biệt được gì.
        //
        // Với một thẻ <a> thì thiếu response.ok là panel HIỆN RA kèm rác của trang lỗi.
        await Page.RouteAsync("**/Product/Suggest*", route =>
            route.FulfillAsync(new RouteFulfillOptions
            {
                Status = 500,
                ContentType = "text/html",
                Body = "<a data-goi-y>RAC TU TRANG LOI</a>"
            }));

        await GoAsync("kudo");

        // ★ PHẢI chờ request xong rồi mới khẳng định, và đây là điểm tinh tế thứ hai
        // mà mutation đã phơi ra.
        //
        // `Expect(Panel).ToBeHiddenAsync()` tự chờ cho tới khi điều kiện ĐÚNG — mà
        // panel vốn đang ẩn sẵn từ đầu, nên nó đúng NGAY LẬP TỨC, trước khi fetch kịp
        // trả về. Assertion "xanh" mà chưa từng quan sát trạng thái cần quan sát. Đó
        // là lý do mutation "bỏ response.ok" lọt lưới ở bản đầu.
        //
        // NetworkIdle chờ tới khi không còn request nào trong 500ms, đủ để fetch trả
        // về VÀ JavaScript xử lý xong.
        await Page.WaitForLoadStateAsync(LoadState.NetworkIdle);

        // fetch KHÔNG reject khi server trả 5xx, nên nhánh này chỉ đúng nhờ lệnh kiểm
        // `response.ok`. Bỏ nó đi là dán HTML trang lỗi vào panel gợi ý.
        Assert.DoesNotContain("RAC TU TRANG LOI", await Panel.InnerHTMLAsync());
        await Assertions.Expect(Panel).ToBeHiddenAsync();

        // Và đường tìm kiếm đầy đủ vẫn phải chạy.
        await O.PressAsync("Enter");
        Assert.Contains("search=kudo", Page.Url, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Go_lien_mach_bon_ky_tu_chi_goi_server_MOT_lan()
    {
        await Page.GotoAsync("/");

        var soLanGoi = 0;

        // Đếm ở tầng mạng của trình duyệt — cách duy nhất quan sát được bộ đếm chờ.
        await Page.RouteAsync("**/Product/Suggest*", async route =>
        {
            Interlocked.Increment(ref soLanGoi);
            await route.ContinueAsync();
        });

        // Gõ 4 ký tự cách nhau 30ms (tổng ~120ms) trong khi ngưỡng chờ là 200ms.
        await GoAsync("kudo");
        await Assertions.Expect(Dong).ToHaveCountAsync(4);

        // ★ Đây là test DUY NHẤT chạm tới bộ đếm chờ, và nó phải đếm REQUEST chứ không
        // nhìn kết quả cuối: bỏ debounce đi thì màn hình vẫn hiện đúng 4 dòng, chỉ là
        // đã bắn 3 request thay vì 1. Mutation "bỏ debounce" từng LỌT vì bản đầu của bộ
        // test này chỉ khẳng định kết quả hiển thị.
        Assert.Equal(1, soLanGoi);
    }
}
