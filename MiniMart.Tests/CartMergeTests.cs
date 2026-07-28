using System.Net;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using MiniMart.Domain.Entities;
using MiniMart.Infrastructure.Data;

namespace MiniMart.Tests;

/// <summary>
/// Gộp giỏ hàng khi đăng nhập / đăng ký.
///
/// <para>
/// Bắt buộc là integration test, không phải unit test: phần dễ sai nhất không nằm
/// trong <c>CartService.MergeAsync</c> (đã có unit test riêng) mà nằm ở chỗ ghép
/// nối - <c>SignInAsync</c> KHÔNG cập nhật <c>HttpContext.User</c>, nên nếu
/// Controller quên gán lại thì <c>ICurrentUser.Id</c> vẫn null và
/// <c>DatabaseCartStore</c> ném ngay. Mock <c>ICartStore</c> không tái hiện được
/// điều đó vì mock không đọc HttpContext.
/// </para>
/// </summary>
public class CartMergeTests : IAsyncLifetime
{
    private const string MatKhau = "MatKhau123";

    private readonly WebApplicationFactory<Program> _factory = new();
    private readonly List<string> _usernames = [];

    private int _categoryId;
    private int _productConHang;
    private int _productItHang;

    public async Task InitializeAsync()
    {
        using var scope = _factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<MiniMartDbContext>();

        var category = new Category { Name = $"GG_{Guid.NewGuid():N}"[..14] };
        var conHang = new Product { Name = "ConHang", Price = 100_000m, Stock = 50, Category = category };
        var itHang = new Product { Name = "ItHang", Price = 200_000m, Stock = 2, Category = category };

        // Không tạo sẵn sản phẩm hết hàng: test cần nó phải mô phỏng đúng kịch bản
        // thật - hàng CÒN lúc bỏ vào giỏ vãng lai rồi HẾT trước khi đăng nhập, nên
        // nó dùng DoiTonKhoAsync ngay trong thân test.
        context.Products.AddRange(conHang, itHang);
        await context.SaveChangesAsync();

        _categoryId = category.Id;
        _productConHang = conHang.Id;
        _productItHang = itHang.Id;
    }

    // ───────────── Đường chính ─────────────

    [Fact]
    public async Task Dang_nhap_thi_gio_khach_vang_lai_duoc_chuyen_vao_DB()
    {
        using var client = CreateClient();

        // Tạo tài khoản TRƯỚC rồi đăng xuất: nếu đăng ký sau thì chính Register đã
        // gộp mất rồi, và lần đăng nhập tiếp theo chỉ gộp một giỏ rỗng - test vẫn
        // xanh nhưng không kiểm chứng được đường Login.
        var username = await DangKyRoiDangXuatAsync(client);

        await ThemVaoGioAsync(client, _productConHang, 2);
        await DangNhapAsync(client, username);

        var dong = await DocGioDuoiDbAsync(username);

        Assert.Equal(new[] { (_productConHang, 2) }, dong);
    }

    [Fact]
    public async Task Dang_KY_cung_gop_gio_hang()
    {
        using var client = CreateClient();

        await ThemVaoGioAsync(client, _productConHang, 3);

        // Register cũng tự đăng nhập, nên nó cũng phải gộp. Gắn gộp vào từng action
        // thay vì vào SignInUserAsync là để quên đúng ở đây: người vừa đăng ký mất
        // sạch giỏ, mà không có exception nào báo.
        var username = await DangKyAsync(client);

        Assert.Equal(new[] { (_productConHang, 3) }, await DocGioDuoiDbAsync(username));
    }

    [Fact]
    public async Task Dang_nhap_thanh_cong_khi_dang_co_gio_van_tra_ve_Redirect_chu_khong_500()
    {
        using var client = CreateClient(theoRedirect: false);

        var username = await DangKyRoiDangXuatAsync(client);

        await ThemVaoGioAsync(client, _productConHang, 1);

        var response = await DangNhapAsync(client, username);

        // Đây là test canh giữ dòng `HttpContext.User = principal`. Bỏ dòng đó thì
        // ICurrentUser.Id là null -> DatabaseCartStore ném
        // InvalidOperationException -> đăng nhập ĐÚNG mật khẩu mà nhận trang lỗi.
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
    }

    [Fact]
    public async Task Gop_xong_thi_gio_Session_bi_XOA()
    {
        using var client = CreateClient();

        await ThemVaoGioAsync(client, _productConHang, 2);

        var username = await DangKyAsync(client);
        await DangXuatAsync(client);

        // Đăng xuất KHÔNG xoá cookie Session, nên nếu MergeAsync không gọi
        // ClearAsync thì giỏ vãng lai vẫn còn nguyên và lần đăng nhập sau sẽ cộng
        // dồn thêm một lần nữa - số lượng tự nhân đôi qua mỗi lần đăng nhập.
        Assert.Contains("Giỏ hàng đang trống", await client.GetStringAsync("/Cart"));

        // Và hàng đã thật sự nằm dưới DB, không phải bị mất.
        Assert.Equal(new[] { (_productConHang, 2) }, await DocGioDuoiDbAsync(username));
    }

    // ───────────── Gộp vào giỏ ĐÃ CÓ sẵn ─────────────

    [Fact]
    public async Task Gop_thi_CONG_DON_voi_gio_da_co_duoi_DB()
    {
        using var client = CreateClient();

        var username = await DangKyAsync(client);
        await ThemVaoGioAsync(client, _productConHang, 2);   // vào DB
        await DangXuatAsync(client);

        await ThemVaoGioAsync(client, _productConHang, 3);   // vào Session
        await DangNhapAsync(client, username);

        // TỔNG, không phải MAX và cũng không phải ghi đè: người dùng đã chủ động
        // thêm ở cả hai nơi nên cả hai ý định đều thật.
        Assert.Equal(new[] { (_productConHang, 5) }, await DocGioDuoiDbAsync(username));
    }

    [Fact]
    public async Task Gop_khong_tao_dong_trung_cho_cung_san_pham()
    {
        using var client = CreateClient();

        var username = await DangKyAsync(client);
        await ThemVaoGioAsync(client, _productConHang, 1);
        await DangXuatAsync(client);

        await ThemVaoGioAsync(client, _productConHang, 1);
        await DangNhapAsync(client, username);

        // UNIQUE(CartId, ProductId) dưới DB sẽ chặn nếu gộp cố thêm dòng thứ hai -
        // và người dùng nhận "Vui lòng thử lại" thay vì giỏ hàng.
        Assert.Single(await DocGioDuoiDbAsync(username));
    }

    [Fact]
    public async Task Gop_bi_KEP_theo_ton_kho()
    {
        using var client = CreateClient();

        var username = await DangKyAsync(client);
        await ThemVaoGioAsync(client, _productItHang, 2);    // ItHang chỉ còn 2
        await DangXuatAsync(client);

        await ThemVaoGioAsync(client, _productItHang, 2);
        await DangNhapAsync(client, username);

        // 2 + 2 = 4 nhưng kho chỉ có 2. Không kẹp là bán quá số hàng thực có.
        Assert.Equal(new[] { (_productItHang, 2) }, await DocGioDuoiDbAsync(username));
    }

    // ───────────── Trường hợp biên ─────────────

    [Fact]
    public async Task San_pham_het_hang_bi_BO_QUA_khi_gop()
    {
        using var client = CreateClient();

        await ThemVaoGioAsync(client, _productConHang, 1);

        // Hết hàng SAU khi đã vào giỏ vãng lai - đúng kịch bản thật: giỏ Session
        // sống 2 ngày, đủ lâu để shop bán hết món đó.
        await DoiTonKhoAsync(_productConHang, 0);

        var username = await DangKyAsync(client);

        Assert.Empty(await DocGioDuoiDbAsync(username));
    }

    [Fact]
    public async Task San_pham_da_bi_XOA_khong_lam_vo_viec_dang_nhap()
    {
        using var client = CreateClient();

        await ThemVaoGioAsync(client, _productConHang, 1);
        await ThemVaoGioAsync(client, _productItHang, 1);
        await XoaSanPhamAsync(_productConHang);

        var username = await DangKyAsync(client);

        // Bỏ qua dòng chết, giữ dòng còn sống. Ném ở đây là chặn hẳn việc đăng nhập
        // chỉ vì một sản phẩm bị xoá - hỏng nặng hơn nhiều so với mất một dòng giỏ.
        Assert.Equal(new[] { (_productItHang, 1) }, await DocGioDuoiDbAsync(username));
    }

    [Fact]
    public async Task Gio_vang_lai_RONG_thi_khong_tao_ban_ghi_gio_nao_duoi_DB()
    {
        using var client = CreateClient();

        var username = await DangKyAsync(client);

        using var scope = _factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<MiniMartDbContext>();

        // MergeAsync return sớm khi giỏ nguồn rỗng. Thiếu nhánh đó thì MỌI lần đăng
        // nhập đều tạo một dòng Carts trống - bảng phình lên vì những phiên chỉ ghé
        // qua xem rồi đi.
        Assert.False(await context.Carts.AnyAsync(c => c.User.Username == username));
    }

    [Fact]
    public async Task Dang_nhap_SAI_mat_khau_thi_KHONG_gop_va_gio_van_con_trong_Session()
    {
        using var client = CreateClient();

        var username = await DangKyRoiDangXuatAsync(client);

        await ThemVaoGioAsync(client, _productConHang, 2);

        await client.PostFormAsync("/Account/Login", new()
        {
            ["Username"] = username,
            ["Password"] = "SaiHoanToan999"
        });

        // Gộp nằm trong SignInUserAsync, mà nhánh sai mật khẩu không gọi tới đó.
        Assert.Empty(await DocGioDuoiDbAsync(username));

        // Và quan trọng hơn: giỏ của họ không bị xoá vì một lần gõ sai mật khẩu.
        Assert.Contains("ConHang", await client.GetStringAsync("/Cart"));
    }

    [Fact]
    public async Task Gio_cua_NGUOI_KHAC_khong_bi_gop_lay()
    {
        using var clientA = CreateClient();
        var userA = await DangKyAsync(clientA);
        await ThemVaoGioAsync(clientA, _productConHang, 4);

        // Client B là trình duyệt khác: cookie Session riêng, giỏ vãng lai riêng.
        using var clientB = CreateClient();
        await ThemVaoGioAsync(clientB, _productItHang, 1);
        var userB = await DangKyAsync(clientB);

        // Gộp phải bám theo ICurrentUser của CHÍNH request đó. Nếu ai đó "tối ưu"
        // DatabaseCartStore thành Singleton hoặc cache userId ở đâu, giỏ hai người
        // sẽ trộn vào nhau.
        Assert.Equal(new[] { (_productConHang, 4) }, await DocGioDuoiDbAsync(userA));
        Assert.Equal(new[] { (_productItHang, 1) }, await DocGioDuoiDbAsync(userB));
    }

    // ───────────── Helper ─────────────

    private HttpClient CreateClient(bool theoRedirect = true) =>
        _factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = theoRedirect });

    /// <summary>Đọc giỏ dưới DB theo username, sắp xếp ổn định để so sánh được.</summary>
    private async Task<(int ProductId, int Quantity)[]> DocGioDuoiDbAsync(string username)
    {
        using var scope = _factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<MiniMartDbContext>();

        var dong = await context.CartItems
            .AsNoTracking()
            .Where(i => i.Cart.User.Username == username)
            .OrderBy(i => i.ProductId)
            .Select(i => new { i.ProductId, i.Quantity })
            .ToListAsync();

        return dong.Select(d => (d.ProductId, d.Quantity)).ToArray();
    }

    private async Task DoiTonKhoAsync(int productId, int stock)
    {
        using var scope = _factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<MiniMartDbContext>();

        await context.Products
            .Where(p => p.Id == productId)
            .ExecuteUpdateAsync(s => s.SetProperty(p => p.Stock, stock));
    }

    private async Task XoaSanPhamAsync(int productId)
    {
        using var scope = _factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<MiniMartDbContext>();

        await context.Products.Where(p => p.Id == productId).ExecuteDeleteAsync();
    }

    private Task<HttpResponseMessage> ThemVaoGioAsync(HttpClient client, int productId, int quantity) =>
        client.PostFormAsync("/Cart/Add", new()
        {
            ["ProductId"] = productId.ToString(),
            ["Quantity"] = quantity.ToString()
        });

    private async Task<string> DangKyAsync(HttpClient client)
    {
        var username = TestAuthExtensions.SinhUsername("gg");

        _usernames.Add(username);
        await client.DangKyAsync(username, MatKhau);

        return username;
    }

    private async Task<string> DangKyRoiDangXuatAsync(HttpClient client)
    {
        var username = await DangKyAsync(client);

        await DangXuatAsync(client);

        return username;
    }

    private Task<HttpResponseMessage> DangNhapAsync(HttpClient client, string username) =>
        client.PostFormAsync("/Account/Login", new()
        {
            ["Username"] = username,
            ["Password"] = MatKhau
        });

    private static Task<HttpResponseMessage> DangXuatAsync(HttpClient client) =>
        client.PostFormAsync("/Account/Logout", new Dictionary<string, string>());

    public async Task DisposeAsync()
    {
        using var scope = _factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<MiniMartDbContext>();

        // Xoá CartItems theo USERNAME chứ không theo category: có test cố ý xoá
        // sản phẩm giữa đường, lúc đó không còn đường lần từ category tới item.
        await context.CartItems.Where(i => _usernames.Contains(i.Cart.User.Username)).ExecuteDeleteAsync();
        await context.Carts.Where(c => _usernames.Contains(c.User.Username)).ExecuteDeleteAsync();
        await context.Products.Where(p => p.CategoryId == _categoryId).ExecuteDeleteAsync();
        await context.Categories.Where(c => c.Id == _categoryId).ExecuteDeleteAsync();
        await context.Users.Where(u => _usernames.Contains(u.Username)).ExecuteDeleteAsync();

        _factory.Dispose();
    }
}
