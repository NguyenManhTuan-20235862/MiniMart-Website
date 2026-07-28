using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Playwright;
using MiniMart.Domain.Entities;
using MiniMart.Infrastructure.Data;

namespace MiniMart.Tests;

/// <summary>
/// <c>cart-dropdown.js</c> chạy trong trình duyệt thật.
///
/// <para>
/// File JS này là chỗ khó kiểm nhất của dự án vì nó móc vào sự kiện của CHÍNH Bootstrap
/// (<c>show.bs.dropdown</c>). Không có trình duyệt thật thì sự kiện đó không bao giờ
/// xảy ra, nên nhánh "dựng lại dropdown khi cấu trúc giỏ đổi" — nhánh phức tạp nhất —
/// là nhánh duy nhất không cách nào chạm tới.
/// </para>
/// <para>
/// Khách VÃNG LAI, không đăng nhập: giỏ nằm ở Session. Cố ý chọn đường này vì nó là
/// đường mà JS phải chạy đúng ngay từ lượt xem đầu tiên, và nó không kéo theo cookie
/// đăng nhập vào phép đo.
/// </para>
/// </summary>
public class CartDropdownBrowserTests : PlaywrightTestBase
{
    private const decimal GiaSanPham = 250_000m;

    private int _categoryId;

    protected override async Task SeedAsync()
    {
        using var scope = Factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<MiniMartDbContext>();

        var category = new Category { Name = $"CD_{Guid.NewGuid():N}"[..14] };

        // Hai sản phẩm: một để thao tác, một để chứng minh thao tác KHÔNG lan sang dòng khác.
        category.Products.Add(new Product { Name = "CDSP_MotAAA", Price = GiaSanPham, Stock = 50 });
        category.Products.Add(new Product { Name = "CDSP_HaiBBB", Price = 90_000m, Stock = 50 });

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

    private Task MoTrangChuAsync() => Page.GotoAsync($"/?categoryId={_categoryId}");

    private ILocator Badge => Page.Locator("[data-cart-count]");

    private ILocator DongGio => Page.Locator("[data-cart-line]");

    /// <summary>Thẻ sản phẩm theo TÊN, rồi lấy nút thêm vào giỏ bên trong nó.</summary>
    private ILocator NutThem(string ten) =>
        Page.Locator(".col", new PageLocatorOptions { HasTextString = ten })
            .Locator("form[data-cart-add] button");

    /// <summary>
    /// Mở dropdown bằng cách bấm thật vào nút — đây là thứ kích hoạt
    /// <c>show.bs.dropdown</c>, tức nhánh "dựng lại từ server" của JS.
    /// </summary>
    private async Task MoDropdownAsync()
    {
        await Page.ClickAsync("#cartDropdownToggle");
        await Page.WaitForLoadStateAsync(LoadState.NetworkIdle);
    }

    [Fact]
    public async Task Them_vao_gio_KHONG_chuyen_trang_va_badge_tang_ngay()
    {
        await MoTrangChuAsync();

        var urlTruoc = Page.Url;

        await NutThem("CDSP_MotAAA").ClickAsync();

        // ★ Đây là điều KHÔNG có test nào khác chứng minh được: form là form POST thật,
        // và nếu JS không chặn `submit` thì trình duyệt điều hướng sang /Cart. Không có
        // JavaScript thì hành vi đó ĐÚNG (progressive enhancement); có JS thì nó phải
        // bị chặn. Cả hai đều là hành vi mong muốn, chỉ khác ngữ cảnh.
        await Assertions.Expect(Badge).ToHaveTextAsync("1");
        Assert.Equal(urlTruoc, Page.Url);
    }

    [Fact]
    public async Task Badge_hien_ra_tu_gio_RONG_ma_khong_can_JS_tao_the_moi()
    {
        await MoTrangChuAsync();

        // Node badge phải LUÔN được server render, chỉ ẩn/hiện. Bọc nó trong
        // `@if (count > 0)` thì lần thêm hàng đầu tiên không có node nào để gán
        // textContent, và JS buộc phải tự tạo thẻ — đúng thứ quy ước cấm.
        await Assertions.Expect(Badge).ToHaveCountAsync(1);
        await Assertions.Expect(Badge).ToBeHiddenAsync();

        await NutThem("CDSP_MotAAA").ClickAsync();

        await Assertions.Expect(Badge).ToBeVisibleAsync();
        await Assertions.Expect(Badge).ToHaveTextAsync("1");
    }

    [Fact]
    public async Task Mo_dropdown_thi_thay_dong_vua_them()
    {
        await MoTrangChuAsync();
        await NutThem("CDSP_MotAAA").ClickAsync();
        await Assertions.Expect(Badge).ToHaveTextAsync("1");

        await MoDropdownAsync();

        // Nhánh này chỉ chạy được nhờ sự kiện show.bs.dropdown của Bootstrap.
        await Assertions.Expect(DongGio).ToHaveCountAsync(1);
        await Assertions.Expect(Page.Locator("[data-cart-line] >> text=CDSP_MotAAA")).ToBeVisibleAsync();
    }

    [Fact]
    public async Task Bam_cong_thi_so_luong_va_thanh_tien_doi_TAI_CHO()
    {
        await MoTrangChuAsync();
        await NutThem("CDSP_MotAAA").ClickAsync();
        await Assertions.Expect(Badge).ToHaveTextAsync("1");
        await MoDropdownAsync();

        var urlTruoc = Page.Url;

        await Page.ClickAsync("[data-cart-action='increase']");

        await Assertions.Expect(Page.Locator("[data-cart-quantity]")).ToHaveTextAsync("2");

        // ★ Tiền do SERVER định dạng, JavaScript TUYỆT ĐỐI không tự nhân hay tự format.
        // 250.000 x 2 = 500.000, in theo MoneyFormat (InvariantCulture) là "500,000".
        // Nếu ai đó cho JS tự tính thì chuỗi này sẽ lệch theo locale của máy chạy.
        await Assertions.Expect(Page.Locator("[data-cart-line-total]")).ToHaveTextAsync("500,000");
        await Assertions.Expect(Page.Locator("[data-cart-total]")).ToHaveTextAsync("500,000");

        // Và không tải lại trang.
        Assert.Equal(urlTruoc, Page.Url);
    }

    [Fact]
    public async Task Bam_tru_ve_0_thi_dong_bien_mat_khoi_DOM()
    {
        await MoTrangChuAsync();
        await NutThem("CDSP_MotAAA").ClickAsync();
        await Assertions.Expect(Badge).ToHaveTextAsync("1");
        await MoDropdownAsync();

        // Số lượng đang là 1; bấm "−" gửi Quantity = 0, và SERVER dịch 0 thành xoá dòng.
        await Page.ClickAsync("[data-cart-action='decrease']");

        await Assertions.Expect(DongGio).ToHaveCountAsync(0);
        await Assertions.Expect(Badge).ToBeHiddenAsync();
    }

    [Fact]
    public async Task Xoa_MOT_dong_thi_dong_con_lai_van_nguyen()
    {
        await MoTrangChuAsync();
        await NutThem("CDSP_MotAAA").ClickAsync();
        await Assertions.Expect(Badge).ToHaveTextAsync("1");
        await NutThem("CDSP_HaiBBB").ClickAsync();
        await Assertions.Expect(Badge).ToHaveTextAsync("2");

        await MoDropdownAsync();
        await Assertions.Expect(DongGio).ToHaveCountAsync(2);

        // Xoá đúng dòng của sản phẩm thứ nhất.
        await Page.Locator("[data-cart-line]", new PageLocatorOptions { HasTextString = "CDSP_MotAAA" })
            .Locator("[data-cart-action='remove']").ClickAsync();

        await Assertions.Expect(DongGio).ToHaveCountAsync(1);
        await Assertions.Expect(Page.Locator("[data-cart-line] >> text=CDSP_HaiBBB")).ToBeVisibleAsync();
        await Assertions.Expect(Badge).ToHaveTextAsync("1");
    }

    [Fact]
    public async Task Them_cung_san_pham_hai_lan_thi_CONG_DON_chu_khong_tao_dong_moi()
    {
        await MoTrangChuAsync();

        await NutThem("CDSP_MotAAA").ClickAsync();
        await Assertions.Expect(Badge).ToHaveTextAsync("1");

        await NutThem("CDSP_MotAAA").ClickAsync();
        await Assertions.Expect(Badge).ToHaveTextAsync("2");

        await MoDropdownAsync();

        await Assertions.Expect(DongGio).ToHaveCountAsync(1);
        await Assertions.Expect(Page.Locator("[data-cart-quantity]")).ToHaveTextAsync("2");
    }

    [Fact]
    public async Task Gio_hang_van_mua_duoc_khi_TAT_JavaScript()
    {
        // ★ Đây là lời hứa lớn nhất của thiết kế giỏ hàng, và trước bộ test này chưa
        // có gì kiểm chứng nó: CartController phục vụ cả request AJAX lẫn form POST
        // thường (Post-Redirect-Get), nên tắt JS thì mọi thứ vẫn chạy — chỉ là bằng
        // cách tải lại trang.
        var context = await Page.Context.Browser!.NewContextAsync(new BrowserNewContextOptions
        {
            BaseURL = Factory.DiaChiGoc,
            JavaScriptEnabled = false
        });

        var trang = await context.NewPageAsync();

        await trang.GotoAsync($"/?categoryId={_categoryId}");
        await trang.Locator(".col", new PageLocatorOptions { HasTextString = "CDSP_MotAAA" })
            .Locator("form[data-cart-add] button").ClickAsync();

        // Không có JS chặn submit -> form POST thật -> server redirect sang /Cart.
        Assert.Contains("/Cart", trang.Url, StringComparison.OrdinalIgnoreCase);

        // Bóc riêng vùng nội dung chính. Quét cả trang thì tên sản phẩm khớp CẢ dropdown
        // trên navbar — mà dropdown đang đóng nên phần tử đó KHÔNG hiển thị, và
        // `.First` sẽ chọn đúng cái vô hình đó. Locator khớp nhiều phần tử cũng ném lỗi
        // strict mode chứ không tự chọn giúp.
        await Assertions.Expect(
            trang.Locator(".es-main-wrapper >> text=CDSP_MotAAA").First).ToBeVisibleAsync();

        await context.CloseAsync();
    }

    /// <summary>
    /// Nút "Thêm vào giỏ" phải thuộc form <c>POST /Cart/Add</c> — không phải form nào khác.
    ///
    /// <para>
    /// ★ Test đắt giá nhất của cả phase này, vì nó bắt được một lỗi ĐANG CÓ THẬT lúc
    /// viết: trang chủ có <c>&lt;form method="get"&gt;</c> của bộ lọc bao trọn cả lưới
    /// sản phẩm, nên <c>&lt;form&gt;</c> của mỗi thẻ là form LỒNG TRONG form. HTML không
    /// cho phép điều đó, và trình duyệt xử lý bằng cách vứt bỏ thẻ <c>&lt;form&gt;</c>
    /// bên trong rồi để thẻ <c>&lt;/form&gt;</c> của nó đóng form NGOÀI.
    /// </para>
    /// <para>
    /// Hệ quả đo được: nút "Thêm vào giỏ" của thẻ ĐẦU TIÊN rơi vào form lọc
    /// (<c>get /</c>), nên bấm vào chỉ chạy lại bộ lọc và <b>không thêm gì vào giỏ</b>;
    /// còn <c>cart-dropdown.js</c> gọi <c>closest('form[data-cart-add]')</c> nhận null
    /// nên im lặng không chạy.
    /// </para>
    /// <para>
    /// ⚠ Vì sao 591 test cũ không thấy: chuỗi HTML mà SERVER gửi đi hoàn toàn đúng —
    /// <c>data-cart-add</c> có mặt đủ. Sai lầm nằm ở bộ phân tích HTML của trình duyệt.
    /// Đây chính xác là khoảng trống mà Playwright sinh ra để lấp.
    /// </para>
    /// </summary>
    [Fact]
    public async Task Nut_them_vao_gio_phai_thuoc_form_POST_Cart_Add_chu_khong_form_loc()
    {
        await MoTrangChuAsync();

        var moTa = await Page.EvaluateAsync<string>(@"() => {
            const nut = [...document.querySelectorAll('.es-btn-add-cart')];
            return JSON.stringify({
                soNut: nut.length,
                soFormCartAdd: document.querySelectorAll('form[data-cart-add]').length,
                // `nut.form` là form mà trình duyệt THẬT SỰ gán cho nút, sau khi đã
                // sửa cây DOM - khác hẳn thẻ cha trong chuỗi HTML gốc.
                form: nut.map(n => n.form
                    ? n.form.getAttribute('method') + ' ' + n.form.getAttribute('action')
                    : '(khong thuoc form nao)')
            });
        }");

        // Hai sản phẩm còn hàng -> hai nút, hai form. Trước khi sửa: soFormCartAdd = 1.
        Assert.Contains("\"soNut\":2", moTa);
        Assert.Contains("\"soFormCartAdd\":2", moTa);

        // Và KHÔNG nút nào được thuộc về form lọc.
        Assert.DoesNotContain("get /", moTa);
    }
}
