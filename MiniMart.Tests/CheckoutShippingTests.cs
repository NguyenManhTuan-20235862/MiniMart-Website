using System.Net;
using System.Reflection;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using MiniMart.Domain.Entities;
using MiniMart.Domain.Enums;
using MiniMart.Infrastructure.Data;
using MiniMart.Web.Models;

namespace MiniMart.Tests;

/// <summary>
/// Form địa chỉ giao hàng ở <c>/Checkout</c> - qua pipeline HTTP thật.
///
/// <para>
/// Hai thứ được khoá ở đây mà không nơi nào khác khoá được:
/// (1) dữ liệu người dùng đã gõ KHÔNG bị mất khi đặt hàng thất bại, và
/// (2) <c>CheckoutViewModel.Cart</c> không nhận được gì từ form.
/// </para>
/// </summary>
public class CheckoutShippingTests : IAsyncLifetime
{
    private const string MatKhau = "MatKhau123";

    private readonly WebApplicationFactory<Program> _factory = new();
    private readonly List<string> _usernames = [];

    private int _categoryId;
    private int _productId;

    public async Task InitializeAsync()
    {
        using var scope = _factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<MiniMartDbContext>();

        var category = new Category { Name = $"SH_{Guid.NewGuid():N}"[..14] };
        var product = new Product { Name = "HangGiao", Price = 120_000m, Stock = 50, Category = category };

        context.Products.Add(product);
        await context.SaveChangesAsync();

        _categoryId = category.Id;
        _productId = product.Id;
    }

    // ───────────── Địa chỉ được chốt vào đơn ─────────────

    [Fact]
    public async Task Dia_chi_tu_form_duoc_luu_vao_don_hang()
    {
        var (client, username) = await TaoNguoiMuaAsync();

        // Giá trị RIÊNG, không dùng hằng số dùng chung: dùng hằng số chung thì test
        // này chỉ đang so một giá trị mặc định với chính nó.
        await client.PostFormAsync("/Checkout/Confirm", CheckoutTestData.Form(
            tenNguoiNhan: "Le Van Cuong",
            soDienThoai: "0335557799",
            diaChi: "88 Tran Hung Dao, Phuong Cua Nam, Ha Noi"));

        var order = await LayDonAsync(username);

        Assert.Equal("Le Van Cuong", order.RecipientName);
        Assert.Equal("0335557799", order.RecipientPhone);
        Assert.Equal("88 Tran Hung Dao, Phuong Cua Nam, Ha Noi", order.ShippingAddress);
    }

    [Fact]
    public async Task Trang_cam_on_hien_lai_thong_tin_giao_hang()
    {
        var (client, _) = await TaoNguoiMuaAsync();

        var html = await (await client.PostFormAsync("/Checkout/Confirm", CheckoutTestData.Form(
                tenNguoiNhan: "Pham Thi Dung",
                soDienThoai: "0977001122",
                diaChi: "15 Hai Ba Trung, Da Nang")))
            .Content.ReadAsStringAsync();

        Assert.Contains("Đặt hàng thành công", html);
        Assert.Contains("Pham Thi Dung", html);
        Assert.Contains("0977001122", html);
        Assert.Contains("15 Hai Ba Trung, Da Nang", html);
    }

    [Fact]
    public async Task Doi_ho_so_sau_khi_dat_khong_lam_doi_dia_chi_tren_don()
    {
        var (client, username) = await TaoNguoiMuaAsync();

        await client.PostFormAsync("/Checkout/Confirm", CheckoutTestData.Form(
            tenNguoiNhan: "Nguoi Nhan Goc",
            diaChi: "1 Dia Chi Goc, Ha Noi"));

        // Đổi username (hồ sơ tài khoản) sau khi đơn đã đặt.
        using (var scope = _factory.Services.CreateScope())
        {
            var context = scope.ServiceProvider.GetRequiredService<MiniMartDbContext>();

            await context.Users.Where(u => u.Username == username)
                .ExecuteUpdateAsync(s => s.SetProperty(u => u.Username, u => u.Username + "x"));
        }

        _usernames.Add(username + "x");

        using var kiemTra = _factory.Services.CreateScope();
        var db = kiemTra.ServiceProvider.GetRequiredService<MiniMartDbContext>();

        var order = await db.Orders.AsNoTracking()
            .SingleAsync(o => o.User.Username == username + "x");

        // Lý do ba cột này nằm trên Orders chứ không phải khoá ngoại sang hồ sơ:
        // đơn đã giao phải giữ nguyên nơi nó đã được giao tới.
        Assert.Equal("Nguoi Nhan Goc", order.RecipientName);
        Assert.Equal("1 Dia Chi Goc, Ha Noi", order.ShippingAddress);
    }

    // ───────────── Form không hợp lệ ─────────────

    [Theory]
    [InlineData("RecipientName")]
    [InlineData("RecipientPhone")]
    [InlineData("ShippingAddress")]
    public async Task Thieu_mot_truong_bat_buoc_thi_KHONG_tao_don(string truongBoTrong)
    {
        var (client, username) = await TaoNguoiMuaAsync();

        var form = CheckoutTestData.Form();
        form[truongBoTrong] = string.Empty;

        var html = await (await client.PostFormAsync("/Checkout/Confirm", form))
            .Content.ReadAsStringAsync();

        // Vẫn ở lại trang xác nhận (form còn đó), và không có đơn nào được tạo.
        Assert.Contains("action=\"/Checkout/Confirm\"", html);
        Assert.False(await CoDonAsync(username));
    }

    [Fact]
    public async Task So_dien_thoai_sai_dinh_dang_thi_bi_tu_choi()
    {
        var (client, username) = await TaoNguoiMuaAsync();

        var html = await (await client.PostFormAsync(
                "/Checkout/Confirm", CheckoutTestData.Form(soDienThoai: "goi cho toi nhe")))
            .Content.ReadAsStringAsync();

        Assert.Contains("Số điện thoại", html);
        Assert.False(await CoDonAsync(username));
    }

    [Fact]
    public async Task Form_khong_hop_le_thi_GIU_LAI_du_lieu_da_go()
    {
        var (client, _) = await TaoNguoiMuaAsync();

        var diaChiDaGo = "234 Nguyen Van Linh, Long Bien, Ha Noi";
        var tenDaGo = "Hoang Van Em";

        var html = await (await client.PostFormAsync("/Checkout/Confirm", CheckoutTestData.Form(
                tenNguoiNhan: tenDaGo,
                soDienThoai: string.Empty,   // trường hỏng
                diaChi: diaChiDaGo)))
            .Content.ReadAsStringAsync();

        // ★ Lý do tồn tại của việc render lại thay vì redirect. Sai MỘT ô mà bắt gõ
        // lại địa chỉ dài là cách nhanh nhất để mất một đơn hàng.
        Assert.Contains(diaChiDaGo, html);
        Assert.Contains(tenDaGo, html);
    }

    [Fact]
    public async Task Form_khong_hop_le_thi_van_hien_lai_gio_hang()
    {
        var (client, _) = await TaoNguoiMuaAsync();

        var html = await (await client.PostFormAsync(
                "/Checkout/Confirm", CheckoutTestData.Form(diaChi: string.Empty)))
            .Content.ReadAsStringAsync();

        // ★ Assertion này PHẢI đứng trước hai assertion dưới, và nó là kết quả của một
        // mutation test đã suýt lọt lưới.
        //
        // Quên nạp lại form.Cart thì Cart rỗng -> RenderLaiFormAsync tưởng giỏ đã hết
        // -> redirect sang /Cart. Mà trang /Cart đọc giỏ THẬT từ DB nên nó vẫn hiện
        // "HangGiao" và "240,000" đầy đủ. Hai assertion dưới vì thế VẪN XANH trong khi
        // trang xác nhận đã hỏng hoàn toàn. Chỉ việc khẳng định "vẫn đang ở đúng trang"
        // mới bắt được.
        Assert.Contains("action=\"/Checkout/Confirm\"", html);

        // Cart có [BindNever] nên sau model binding nó RỖNG. Quên nạp lại ở đường
        // render lỗi thì trang hiện ra với giỏ trống - không exception nào báo, vì
        // CartView.Empty là một giá trị hoàn toàn hợp lệ.
        Assert.Contains("HangGiao", html);
        Assert.Contains("240,000", html);   // 2 x 120.000, tổng cộng
    }

    // ───────────── Nút thanh toán VNPay ─────────────

    [Fact]
    public async Task Chon_VNPay_thi_redirect_sang_cong_thanh_toan()
    {
        var (client, username) = await TaoNguoiMuaAsync(theoRedirect: false);

        var form = CheckoutTestData.Form();
        form["PhuongThuc"] = "VnPay";

        var response = await client.PostFormAsync("/Checkout/Confirm", form);

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);

        var url = response.Headers.Location!.ToString();

        Assert.StartsWith("https://sandbox.vnpayment.vn/", url, StringComparison.Ordinal);
        Assert.Contains("vnp_SecureHash=", url, StringComparison.Ordinal);

        // Đơn PHẢI được tạo trước khi chuyển sang cổng: vnp_TxnRef là OrderId và
        // vnp_Amount là TotalAmount đã chốt - cả hai chỉ tồn tại sau khi đơn đã lưu.
        var order = await LayDonAsync(username);

        Assert.Contains($"vnp_TxnRef={order.Id}", url, StringComparison.Ordinal);

        // 2 x 120.000 = 240.000, nhân 100 theo đặc tả VNPay.
        Assert.Contains("vnp_Amount=24000000", url, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Chon_VNPay_thi_don_van_o_trang_thai_Pending()
    {
        var (client, username) = await TaoNguoiMuaAsync(theoRedirect: false);

        var form = CheckoutTestData.Form();
        form["PhuongThuc"] = "VnPay";

        await client.PostFormAsync("/Checkout/Confirm", form);

        var order = await LayDonAsync(username);

        // Chuyển hướng sang cổng KHÔNG phải là đã thanh toán. Chỉ kênh IPN mới được
        // đặt Paid, và lúc này khách còn chưa nhập thẻ.
        Assert.Equal(OrderStatus.Pending, order.Status);
    }

    [Fact]
    public async Task Khong_chon_gi_thi_dat_hang_binh_thuong_KHONG_sang_VNPay()
    {
        var (client, _) = await TaoNguoiMuaAsync(theoRedirect: false);

        // Form thiếu hẳn trường PhuongThuc - đúng như một client tự chế hoặc một lần
        // submit bằng JS quên trường.
        var response = await client.PostFormAsync("/Checkout/Confirm", CheckoutTestData.Form());

        var url = response.Headers.Location!.ToString();

        // Enum không nullable nên binder cho ra Cod = 0. Mặc định phải là lựa chọn AN
        // TOÀN NHẤT: đặt hàng bình thường, không đẩy khách sang cổng ngoài ý muốn.
        Assert.Matches(@"^/Checkout/Success/\d+$", url);
    }

    // ───────────── Chống over-posting ─────────────

    [Fact]
    public void Cart_trong_CheckoutViewModel_phai_co_BindNever()
    {
        var cart = typeof(CheckoutViewModel).GetProperty(nameof(CheckoutViewModel.Cart))!;

        // Test CẤU TRÚC, không phải test hành vi. Test hành vi ở dưới chứng minh hôm
        // nay an toàn; test này canh giữ chính LÝ DO nó an toàn, và sẽ đỏ đúng vào
        // lúc có người gỡ attribute đi - tức lúc lỗ hổng vừa trở thành khả thi.
        Assert.NotNull(cart.GetCustomAttribute<BindNeverAttribute>());
    }

    [Fact]
    public async Task Gui_kem_gia_gia_mao_qua_form_khong_co_tac_dung()
    {
        var (client, username) = await TaoNguoiMuaAsync();

        var form = CheckoutTestData.Form();

        // Cố bơm giỏ hàng giả vào model của view.
        form["Cart.Lines[0].ProductId"] = _productId.ToString();
        form["Cart.Lines[0].UnitPrice"] = "1";
        form["Cart.Lines[0].Quantity"] = "1";

        await client.PostFormAsync("/Checkout/Confirm", form);

        var order = await LayDonAsync(username);

        // Giá đến từ bảng Products đọc trong transaction, không từ form. 2 x 120.000.
        Assert.Equal(240_000m, order.TotalAmount);
    }

    // ───────────── Helper ─────────────

    private async Task<(HttpClient Client, string Username)> TaoNguoiMuaAsync(
        bool theoRedirect = true)
    {
        var client = _factory.CreateClient(
            new WebApplicationFactoryClientOptions { AllowAutoRedirect = theoRedirect });
        var username = $"sh_{Guid.NewGuid():N}"[..16];

        _usernames.Add(username);

        await client.PostFormAsync("/Account/Register", new()
        {
            ["Username"] = username,
            ["Password"] = MatKhau,
            ["ConfirmPassword"] = MatKhau
        });

        await client.PostFormAsync("/Cart/Add", new()
        {
            ["ProductId"] = _productId.ToString(),
            ["Quantity"] = "2"
        });

        return (client, username);
    }

    private async Task<Order> LayDonAsync(string username)
    {
        using var scope = _factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<MiniMartDbContext>();

        return await context.Orders
            .AsNoTracking()
            .SingleAsync(o => o.User.Username == username);
    }

    private async Task<bool> CoDonAsync(string username)
    {
        using var scope = _factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<MiniMartDbContext>();

        return await context.Orders.AnyAsync(o => o.User.Username == username);
    }

    public async Task DisposeAsync()
    {
        using var scope = _factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<MiniMartDbContext>();

        var userIds = await context.Users
            .Where(u => _usernames.Contains(u.Username))
            .Select(u => u.Id)
            .ToListAsync();

        // Thứ tự xoá đi từ con lên cha: OrderDetail -> Order -> CartItem -> Cart ->
        // User, rồi mới tới Product/Category. Ngược lại là đụng khoá ngoại Restrict.
        var orderIds = await context.Orders
            .Where(o => userIds.Contains(o.UserId))
            .Select(o => o.Id)
            .ToListAsync();

        await context.OrderDetails.Where(d => orderIds.Contains(d.OrderId)).ExecuteDeleteAsync();
        await context.Orders.Where(o => orderIds.Contains(o.Id)).ExecuteDeleteAsync();
        await context.CartItems.Where(i => userIds.Contains(i.Cart.UserId)).ExecuteDeleteAsync();
        await context.Carts.Where(c => userIds.Contains(c.UserId)).ExecuteDeleteAsync();
        await context.Users.Where(u => userIds.Contains(u.Id)).ExecuteDeleteAsync();
        await context.Products.Where(p => p.CategoryId == _categoryId).ExecuteDeleteAsync();
        await context.Categories.Where(c => c.Id == _categoryId).ExecuteDeleteAsync();

        _factory.Dispose();
    }
}
