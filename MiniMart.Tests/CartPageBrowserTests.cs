using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Playwright;
using MiniMart.Domain.Entities;
using MiniMart.Infrastructure.Data;

namespace MiniMart.Tests;

/// <summary>
/// Trang <c>/Cart</c> trong trình duyệt thật, đi qua đường <b>không có JavaScript</b>.
///
/// <para>
/// Chọn tắt JavaScript có chủ đích: bảng giỏ hàng ở trang này KHÔNG được
/// <c>cart-dropdown.js</c> can thiệp (file đó chỉ lo dropdown trên navbar và nút "Thêm
/// vào giỏ" ở thẻ sản phẩm), nên đây đúng là đường mà mọi người dùng đi. Tắt JS cũng
/// loại bỏ mọi nghi ngờ rằng kết quả đến từ một lớp tăng cường nào đó.
/// </para>
/// <para>
/// ★ Bộ test này sinh ra sau khi một lượt rà soát tìm thấy nút <b>"+" không làm gì cả</b>.
/// Xem <see cref="Bam_cong_thi_so_luong_TANG"/>.
/// </para>
/// </summary>
public class CartPageBrowserTests : PlaywrightTestBase
{
    private int _categoryId;
    private IBrowserContext _khongJs = null!;
    private IPage _trang = null!;

    protected override async Task SeedAsync()
    {
        using var scope = Factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<MiniMartDbContext>();

        var category = new Category { Name = $"GH_{Guid.NewGuid():N}"[..14] };
        category.Products.Add(new Product { Name = "Ghsp Mot", Price = 100_000m, Stock = 50 });
        category.Products.Add(new Product { Name = "Ghsp Hai", Price = 250_000m, Stock = 50 });

        context.Categories.Add(category);
        await context.SaveChangesAsync();
        _categoryId = category.Id;
    }

    protected override async Task DonDepAsync()
    {
        // Đóng context phụ ở ĐÂY chứ không bằng `new DisposeAsync()`: xUnit gọi
        // DisposeAsync qua interface IAsyncLifetime, nên một method đánh dấu `new` chỉ
        // CHE method của lớp cơ sở và không bao giờ được gọi. DonDepAsync là hook mà
        // lớp cơ sở cam kết sẽ chạy.
        if (_khongJs is not null)
        {
            await _khongJs.CloseAsync();
        }

        using var scope = Factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<MiniMartDbContext>();

        await context.CartItems
            .Where(i => i.Product.CategoryId == _categoryId).ExecuteDeleteAsync();
        await context.Products.Where(p => p.CategoryId == _categoryId).ExecuteDeleteAsync();
        await context.Categories.Where(c => c.Id == _categoryId).ExecuteDeleteAsync();
    }

    /// <summary>Trang riêng, TẮT JavaScript. Tạo lười để mỗi test có phiên giỏ hàng sạch.</summary>
    private async Task<IPage> TrangAsync()
    {
        _khongJs = await Page.Context.Browser!.NewContextAsync(new BrowserNewContextOptions
        {
            BaseURL = Factory.DiaChiGoc,
            JavaScriptEnabled = false
        });

        _trang = await _khongJs.NewPageAsync();

        return _trang;
    }

    /// <summary>
    /// Thêm một món vào giỏ: MỘT lần điều hướng.
    ///
    /// <para>
    /// ⚠ Bản trước lặp lại cả chu trình "mở trang chủ → bấm thêm" <c>soLan</c> lần để
    /// đạt số lượng mong muốn, và chờ <c>NetworkIdle</c> (im lặng 500ms) sau mỗi lần.
    /// Một test cần số lượng 3 vì vậy tốn 6 lần điều hướng — cả class mất gần 3 phút,
    /// và khi bốn class Playwright chạy SONG SONG thì có test vượt ngưỡng 30s rồi đỏ
    /// ngẫu nhiên. Test chậm không chỉ tốn thời gian; nó tự biến thành test flaky.
    /// </para>
    /// <para>
    /// Muốn số lượng khác 1 thì dùng <see cref="DatSoLuongAsync"/> — gõ thẳng vào ô,
    /// một lần điều hướng, và cũng gần với thao tác thật của người dùng hơn.
    /// </para>
    /// </summary>
    private async Task ThemVaoGioAsync(IPage trang, string ten)
    {
        await trang.GotoAsync($"/?categoryId={_categoryId}");
        await trang.Locator(".col", new PageLocatorOptions { HasTextString = ten })
            .Locator("form[data-cart-add] button").ClickAsync();

        // LoadState.Load là đủ cho một lần POST -> 302 -> GET. NetworkIdle còn phải
        // chờ thêm 500ms im lặng mà không cho biết thêm điều gì ở trang không có JS.
        await trang.WaitForLoadStateAsync();
    }

    private static async Task DatSoLuongAsync(IPage trang, string ten, int soLuong)
    {
        var o = Dong(trang, ten).Locator("input[name=Quantity]");

        await o.FillAsync(soLuong.ToString());
        await o.PressAsync("Enter");
        await trang.WaitForLoadStateAsync();
    }

    /// <summary>
    /// Badge trên navbar do SERVER render lại mỗi lần tải trang, nên nó là nguồn sự
    /// thật. Ô nhập số lượng thì không: nó có thể còn mang đúng chữ vừa gõ vào.
    /// </summary>
    private static ILocator Badge(IPage trang) => trang.Locator("[data-cart-count]");

    private static ILocator Dong(IPage trang, string ten) =>
        trang.Locator("tr", new PageLocatorOptions { HasTextString = ten });

    private static async Task BamAsync(ILocator trong, string title)
    {
        await trong.Locator($"button[title='{title}']").ClickAsync();
        await trong.Page.WaitForLoadStateAsync();
    }

    // ───────────── Lỗi đã tìm ra và đã sửa ─────────────

    /// <summary>
    /// ★ Đây là lỗi mà lượt rà soát tìm ra: nút "+" KHÔNG tăng số lượng.
    ///
    /// <para>
    /// Nguyên nhân: một form duy nhất chứa BA phần tử cùng tên <c>Quantity</c> — nút −,
    /// ô nhập, nút +. Trình duyệt gửi lên cả ô nhập lẫn nút vừa bấm, còn model binder
    /// cho một tham số <c>int</c> lấy giá trị ĐẦU TIÊN theo thứ tự DOM:
    /// </para>
    /// <code>
    /// bấm −  ->  Quantity=1, Quantity=2  -> lấy 1  (đúng)
    /// bấm +  ->  Quantity=2, Quantity=3  -> lấy 2  (KHÔNG ĐỔI)
    /// </code>
    /// <para>
    /// Nút "−" chạy đúng chỉ nhờ TÌNH CỜ nó nằm trước ô nhập. Không có exception nào,
    /// HTTP 302 như thường, và chuỗi HTML server gửi đi hoàn toàn hợp lệ.
    /// </para>
    /// </summary>
    [Fact]
    public async Task Bam_cong_thi_so_luong_TANG()
    {
        var trang = await TrangAsync();
        await ThemVaoGioAsync(trang, "Ghsp Mot");
        await DatSoLuongAsync(trang, "Ghsp Mot", 2);

        await Assertions.Expect(Badge(trang)).ToHaveTextAsync("2");

        await BamAsync(Dong(trang, "Ghsp Mot"), "Tăng số lượng");

        await Assertions.Expect(Badge(trang)).ToHaveTextAsync("3");
    }

    [Fact]
    public async Task Moi_form_so_luong_chi_duoc_chua_DUNG_MOT_o_ten_Quantity()
    {
        var trang = await TrangAsync();
        await ThemVaoGioAsync(trang, "Ghsp Mot");

        // Test CẤU TRÚC, canh giữ chính lý do lỗi trên không quay lại. Test hành vi ở
        // trên chứng minh hôm nay đúng; test này tố giác ngay khi ai đó gộp lại thành
        // một form "cho gọn".
        var soNhieuNhat = await trang.EvalOnSelectorAllAsync<int>(
            "form[action*='UpdateQuantity']",
            "fs => Math.max(...fs.map(f => f.querySelectorAll('[name=Quantity]').length))");

        Assert.Equal(1, soNhieuNhat);
    }

    [Fact]
    public async Task Bam_tru_thi_so_luong_GIAM()
    {
        var trang = await TrangAsync();
        await ThemVaoGioAsync(trang, "Ghsp Mot");
        await DatSoLuongAsync(trang, "Ghsp Mot", 3);

        await BamAsync(Dong(trang, "Ghsp Mot"), "Giảm số lượng");

        await Assertions.Expect(Badge(trang)).ToHaveTextAsync("2");
    }

    [Fact]
    public async Task Bam_tru_o_so_luong_MOT_thi_bo_mon_do_khoi_gio()
    {
        var trang = await TrangAsync();
        await ThemVaoGioAsync(trang, "Ghsp Mot");

        // Quantity = 0 được CartService dịch thành "xoá dòng". Bản trước khoá nút này
        // (`disabled` ở số lượng 1) nên không có đường bỏ món bằng nút −, dù đó đúng
        // là điều người dùng muốn khi bấm xuống dưới 1.
        await BamAsync(Dong(trang, "Ghsp Mot"), "Giảm số lượng");

        await Assertions.Expect(trang.Locator("text=Ghsp Mot")).ToHaveCountAsync(0);
    }

    // ───────────── Hành vi còn lại của trang ─────────────

    [Fact]
    public async Task Sua_so_luong_bang_cach_go_thang_vao_o()
    {
        var trang = await TrangAsync();
        await ThemVaoGioAsync(trang, "Ghsp Mot");

        await Dong(trang, "Ghsp Mot").Locator("input[name=Quantity]").FillAsync("5");
        await Dong(trang, "Ghsp Mot").Locator("input[name=Quantity]").PressAsync("Enter");
        await trang.WaitForLoadStateAsync();

        await Assertions.Expect(Badge(trang)).ToHaveTextAsync("5");
    }

    [Fact]
    public async Task Thao_tac_tren_MOT_dong_khong_dung_toi_dong_kia()
    {
        var trang = await TrangAsync();
        await ThemVaoGioAsync(trang, "Ghsp Mot");
        await DatSoLuongAsync(trang, "Ghsp Mot", 2);
        await ThemVaoGioAsync(trang, "Ghsp Hai");

        await Assertions.Expect(Badge(trang)).ToHaveTextAsync("3");

        await BamAsync(Dong(trang, "Ghsp Hai"), "Tăng số lượng");

        // Ghsp Hai: 1 -> 2, Ghsp Mot phải vẫn là 2. Tổng 4.
        await Assertions.Expect(Badge(trang)).ToHaveTextAsync("4");
        await Assertions.Expect(
            Dong(trang, "Ghsp Mot").Locator("input[name=Quantity]")).ToHaveValueAsync("2");
    }

    [Fact]
    public async Task Nut_xoa_bo_dung_dong_duoc_bam()
    {
        var trang = await TrangAsync();
        await ThemVaoGioAsync(trang, "Ghsp Mot");
        await ThemVaoGioAsync(trang, "Ghsp Hai");

        await Dong(trang, "Ghsp Mot").Locator("button[title='Xoá khỏi giỏ']").ClickAsync();
        await trang.WaitForLoadStateAsync();

        await Assertions.Expect(trang.Locator("text=Ghsp Mot")).ToHaveCountAsync(0);
        await Assertions.Expect(trang.Locator(".es-main-wrapper >> text=Ghsp Hai").First)
            .ToBeVisibleAsync();
        await Assertions.Expect(Badge(trang)).ToHaveTextAsync("1");
    }

    [Fact]
    public async Task Tong_tien_do_SERVER_tinh_va_dinh_dang()
    {
        var trang = await TrangAsync();
        await ThemVaoGioAsync(trang, "Ghsp Mot");
        await DatSoLuongAsync(trang, "Ghsp Mot", 2);   // 2 x 100.000
        await ThemVaoGioAsync(trang, "Ghsp Hai");             // 1 x 250.000

        // 450.000, in theo MoneyFormat (InvariantCulture) là "450,000". Khẳng định
        // chuỗi chứ không chỉ con số: cách in tiền phải KHÓA vào InvariantCulture, nếu
        // không cùng một số sẽ ra hai chuỗi khác nhau trên hai máy khác locale.
        await Assertions.Expect(
            trang.Locator(".es-main-wrapper >> text=450,000").First).ToBeVisibleAsync();
    }

    [Fact]
    public async Task Gio_RONG_thi_noi_ro_va_co_duong_di_tiep()
    {
        var trang = await TrangAsync();

        await trang.GotoAsync("/Cart");

        // Giỏ rỗng là kết cục bình thường, không phải lỗi: phải có chữ giải thích VÀ
        // một đường quay lại mua sắm, không phải một trang trắng.
        await Assertions.Expect(Badge(trang)).ToBeHiddenAsync();
        await Assertions.Expect(
            trang.Locator(".es-main-wrapper a[href='/']").First).ToBeVisibleAsync();
    }

    [Fact]
    public async Task KHONG_lo_con_so_ton_kho_ra_trang_gio_hang()
    {
        const int tonKhoDacBiet = 8_675_309;

        using (var scope = Factory.Services.CreateScope())
        {
            var context = scope.ServiceProvider.GetRequiredService<MiniMartDbContext>();
            context.Products.Add(new Product
            {
                Name = "Ghsp Ton Kho",
                Price = 10_000m,
                Stock = tonKhoDacBiet,
                CategoryId = _categoryId
            });
            await context.SaveChangesAsync();
        }

        var trang = await TrangAsync();
        await ThemVaoGioAsync(trang, "Ghsp Ton Kho");

        Assert.DoesNotContain(tonKhoDacBiet.ToString(), await trang.ContentAsync());
    }
}
