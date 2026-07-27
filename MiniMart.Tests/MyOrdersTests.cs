using System.Net;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using MiniMart.Application.Interfaces;
using MiniMart.Domain.Entities;
using MiniMart.Domain.Enums;
using MiniMart.Infrastructure.Data;
using MiniMart.Web.Extensions;

namespace MiniMart.Tests;

/// <summary>
/// Trang "Đơn hàng của tôi" — danh sách và chi tiết.
///
/// <para>
/// Trọng tâm là <b>IDOR</b>: đây là màn hình đầu tiên của dự án nhận một <c>id</c> từ
/// URL và trả về dữ liệu riêng tư. Giỏ hàng tránh được bài toán này bằng cấu trúc
/// (endpoint chỉ nhận <c>productId</c>, chủ sở hữu đến từ cookie), nhưng đơn hàng thì
/// bắt buộc phải có <c>/Order/Details/42</c> - nên hàng rào phải nằm trong TRUY VẤN.
/// </para>
/// </summary>
public class MyOrdersTests : IAsyncLifetime
{
    private readonly WebApplicationFactory<Program> _factory = new();
    private readonly List<string> _usernames = [];

    private int _categoryId;
    private int _productId;
    private int _userA;
    private int _userB;
    private int _donCuaA;
    private int _donCuaB;
    private HttpClient _clientA = null!;

    public async Task InitializeAsync()
    {
        using (var scope = _factory.Services.CreateScope())
        {
            var context = scope.ServiceProvider.GetRequiredService<MiniMartDbContext>();

            var category = new Category { Name = $"MO_{Guid.NewGuid():N}"[..14] };

            context.Products.Add(new Product
            {
                Name = $"SanPhamDonHang_{Guid.NewGuid():N}"[..24],
                Price = 250_000m,
                Stock = 100,
                Category = category
            });

            await context.SaveChangesAsync();

            _categoryId = category.Id;
            _productId = context.Products.Single(p => p.CategoryId == _categoryId).Id;
        }

        (_clientA, _userA) = await DangKyAsync("moa");
        var (clientB, userB) = await DangKyAsync("mob");
        clientB.Dispose();
        _userB = userB;

        using (var scope = _factory.Services.CreateScope())
        {
            var context = scope.ServiceProvider.GetRequiredService<MiniMartDbContext>();

            _donCuaA = await TaoDonAsync(context, _userA, soLuong: 3, OrderStatus.Paid);
            _donCuaB = await TaoDonAsync(context, _userB, soLuong: 1, OrderStatus.Pending);
        }
    }

    // ───────────── IDOR ─────────────

    [Fact]
    public async Task Khong_doc_duoc_don_cua_NGUOI_KHAC()
    {
        using var scope = _factory.Services.CreateScope();
        var orderService = scope.ServiceProvider.GetRequiredService<IOrderService>();

        // ★ Lệnh kiểm quan trọng nhất của cả màn hình. A đăng nhập, gõ id đơn của B.
        var ketQua = await orderService.GetMyOrderAsync(_donCuaB, _userA);

        Assert.Null(ketQua);
    }

    [Fact]
    public async Task Don_khong_ton_tai_va_don_nguoi_khac_TRA_VE_GIONG_HET_NHAU()
    {
        using var scope = _factory.Services.CreateScope();
        var orderService = scope.ServiceProvider.GetRequiredService<IOrderService>();

        var donNguoiKhac = await orderService.GetMyOrderAsync(_donCuaB, _userA);
        var donKhongCo = await orderService.GetMyOrderAsync(999_000_000, _userA);

        // Phân biệt được hai trường hợp này là xác nhận "đơn số 42 CÓ tồn tại" - mà Id
        // đơn hàng tuần tự nên đoán được. Cả hai cùng trả null là cách duy nhất để
        // không nói gì cả, và nó đến từ hình dạng API chứ không từ một câu if.
        Assert.Null(donNguoiKhac);
        Assert.Null(donKhongCo);
    }

    [Fact]
    public async Task Danh_sach_chi_chua_don_CUA_CHINH_MINH()
    {
        using var scope = _factory.Services.CreateScope();
        var orderService = scope.ServiceProvider.GetRequiredService<IOrderService>();

        var trang = await orderService.GetMyOrdersAsync(_userA);

        Assert.Contains(trang.Items, d => d.Id == _donCuaA);
        Assert.DoesNotContain(trang.Items, d => d.Id == _donCuaB);
    }

    [Fact]
    public async Task HTTP_don_cua_nguoi_khac_tra_404_chu_khong_403()
    {
        var response = await _clientA.GetAsync($"/Order/Details/{_donCuaB}");

        // 403 sẽ nói "có đơn này nhưng không phải của bạn". 404 không nói gì.
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Chua_dang_nhap_thi_bi_day_ve_trang_dang_nhap()
    {
        using var client = _factory.CreateClient(
            new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });

        foreach (var duongDan in new[] { "/Order", $"/Order/Details/{_donCuaA}" })
        {
            var response = await client.GetAsync(duongDan);

            Assert.Equal(HttpStatusCode.Found, response.StatusCode);
            Assert.Contains("/Account/Login", response.Headers.Location!.ToString(),
                StringComparison.OrdinalIgnoreCase);
        }
    }

    [Fact]
    public void OrderController_KHONG_duoc_nhan_userId_tu_ben_ngoai()
    {
        var thamSo = typeof(MiniMart.Web.Controllers.OrderController)
            .GetMethods()
            .Where(m => m.IsPublic && m.DeclaringType == typeof(MiniMart.Web.Controllers.OrderController))
            .SelectMany(m => m.GetParameters())
            .Select(p => p.Name!)
            .ToArray();

        // ★ Test CẤU TRÚC, canh giữ chính LÝ DO các test hành vi ở trên đúng.
        //
        // Test hành vi chứng minh hôm nay không đọc được đơn người khác. Test này tố
        // giác đúng lúc lỗ hổng vừa TRỞ NÊN KHẢ THI - lúc có người thêm `int userId`
        // vào một action "cho tiện gọi từ trang admin". Cùng khuôn với test cấu trúc
        // chống IDOR của giỏ hàng.
        Assert.DoesNotContain("userId", thamSo, StringComparer.OrdinalIgnoreCase);
    }

    // ───────────── Nội dung hiển thị ─────────────

    [Fact]
    public async Task Danh_sach_hien_trang_thai_bang_TIENG_VIET_khong_phai_ten_enum()
    {
        var html = await _clientA.GetStringAsync("/Order");

        Assert.Contains("Đã thanh toán", html, StringComparison.Ordinal);

        // Tên enum thô vô nghĩa với khách - cùng lý do trang Return của VNPay không
        // hiện mã phản hồi thô.
        Assert.DoesNotContain(">Paid<", html, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Chi_tiet_hien_du_gia_snapshot_va_dia_chi_giao_hang()
    {
        var html = await _clientA.GetStringAsync($"/Order/Details/{_donCuaA}");

        // Giá và tên đọc từ OrderDetail đã snapshot, không join sang Products.
        Assert.Contains("250,000", html, StringComparison.Ordinal);
        Assert.Contains("Nguoi Nhan A", html, StringComparison.Ordinal);
        Assert.Contains("So 1 Duong ABC", html, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Doi_ten_san_pham_KHONG_lam_doi_don_cu()
    {
        using (var scope = _factory.Services.CreateScope())
        {
            var context = scope.ServiceProvider.GetRequiredService<MiniMartDbContext>();
            var product = await context.Products.SingleAsync(p => p.Id == _productId);
            product.Name = "TEN_MOI_HOAN_TOAN";
            await context.SaveChangesAsync();
        }

        var html = await _clientA.GetStringAsync($"/Order/Details/{_donCuaA}");

        // ★ Đây là toàn bộ lý do OrderDetail snapshot ProductName. Join sang Products
        // để lấy tên hiện tại thì lịch sử đơn hàng tự viết lại chính nó, và khách mở
        // đơn cũ thấy một sản phẩm họ chưa từng mua.
        Assert.DoesNotContain("TEN_MOI_HOAN_TOAN", html, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Khong_co_don_nao_thi_hien_loi_di_tiep_chu_khong_chi_mot_cau()
    {
        var (clientMoi, _) = await DangKyAsync("moc");
        using var client = clientMoi;

        var html = await client.GetStringAsync("/Order");

        Assert.Contains("chưa có đơn hàng nào", html, StringComparison.OrdinalIgnoreCase);

        // Màn hình rỗng phải có đường đi tiếp: người vừa đăng ký nhìn thấy nó đầu tiên.
        Assert.Contains("Bắt đầu mua sắm", html, StringComparison.Ordinal);
    }

    [Theory]
    [MemberData(nameof(MoiTrangThai))]
    public void Moi_OrderStatus_deu_phai_co_nhan_tieng_Viet(OrderStatus trangThai)
    {
        var nhan = trangThai.ToText();

        // ★ Đây mới là hàng rào, không phải nhánh `_ =>` trong switch. Nhánh đó chỉ giữ
        // cho trang khỏi nổ; nó KHÔNG ngăn được việc thêm `Cancelled` rồi hiện ra chữ
        // "Cancelled" giữa một trang tiếng Việt. Test này đỏ ngay lúc đó.
        Assert.NotEqual(trangThai.ToString(), nhan);
        Assert.False(string.IsNullOrWhiteSpace(trangThai.ToBadgeClass()));
    }

    public static TheoryData<OrderStatus> MoiTrangThai()
    {
        var data = new TheoryData<OrderStatus>();

        // Duyệt qua enum bằng reflection chứ không liệt kê tay: liệt kê tay thì thêm
        // một trạng thái mới mà quên thêm vào danh sách là test vẫn xanh.
        foreach (var trangThai in Enum.GetValues<OrderStatus>())
        {
            data.Add(trangThai);
        }

        return data;
    }

    // ───────────── Phân trang ─────────────

    [Fact]
    public async Task Page_bay_bi_kep_ve_trang_1()
    {
        using var scope = _factory.Services.CreateScope();
        var orderService = scope.ServiceProvider.GetRequiredService<IOrderService>();

        var trang = await orderService.GetMyOrdersAsync(_userA, page: -5);

        Assert.Equal(1, trang.Page);
    }

    [Fact]
    public async Task PageSize_bay_bi_kep_toi_da_100()
    {
        using var scope = _factory.Services.CreateScope();
        var orderService = scope.ServiceProvider.GetRequiredService<IOrderService>();

        // ?pageSize=999999 không được phép kéo cả bảng về.
        var trang = await orderService.GetMyOrdersAsync(_userA, pageSize: 999_999);

        Assert.Equal(100, trang.PageSize);
    }

    [Fact]
    public async Task Don_moi_nhat_dung_dau()
    {
        int donMoi;

        using (var scope = _factory.Services.CreateScope())
        {
            var context = scope.ServiceProvider.GetRequiredService<MiniMartDbContext>();
            donMoi = await TaoDonAsync(context, _userA, soLuong: 2, OrderStatus.Pending);
        }

        using var scopeDoc = _factory.Services.CreateScope();
        var orderService = scopeDoc.ServiceProvider.GetRequiredService<IOrderService>();

        var trang = await orderService.GetMyOrdersAsync(_userA);

        Assert.Equal(donMoi, trang.Items[0].Id);
    }

    [Fact]
    public async Task Tong_so_luong_dem_dung_khong_can_tai_dong_don()
    {
        using var scope = _factory.Services.CreateScope();
        var orderService = scope.ServiceProvider.GetRequiredService<IOrderService>();

        var trang = await orderService.GetMyOrdersAsync(_userA);
        var don = trang.Items.Single(d => d.Id == _donCuaA);

        // Con số này do SQL Server tính bằng subquery SUM. Nếu ai đó đổi sang
        // Include(o => o.Items) rồi Sum trong C# thì kết quả VẪN đúng - đó chính là lý
        // do phải có ProductBulkUpdateSqlTests-style đo số lệnh, xem MyOrdersSqlTests.
        Assert.Equal(3, don.TongSoLuong);
    }

    // ───────────── Helper ─────────────

    /// <summary>
    /// Đăng ký người dùng qua HTTP và giữ lại client đã có cookie.
    ///
    /// <para>
    /// Cố ý KHÔNG chèn thẳng <c>User</c> vào DB: <c>ICurrentUser</c> đọc Id từ cookie
    /// đã ký, nên một người dùng không có cookie thì không kiểm được đường HTTP - mà
    /// đường HTTP mới là chỗ IDOR xảy ra.
    /// </para>
    /// <para>
    /// Dùng <c>/Account/Register</c> chứ không <c>/Account/Login</c>: đăng ký vừa tạo
    /// tài khoản vừa đăng nhập luôn trong một request, và nó KHÔNG nằm trong hạn mức
    /// rate limit dùng chung mà cả bộ test đang tranh nhau.
    /// </para>
    /// </summary>
    private async Task<(HttpClient Client, int UserId)> DangKyAsync(string tienTo)
    {
        var client = _factory.CreateClient(
            new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });

        var username = $"{tienTo}_{Guid.NewGuid():N}"[..16];
        const string password = "MatKhau123";
        _usernames.Add(username);

        var response = await client.PostFormAsync("/Account/Register", new()
        {
            ["Username"] = username,
            ["Password"] = password,
            ["ConfirmPassword"] = password
        });

        // Tự tố giác ngay: đăng ký hỏng cũng trả 200 (render lại form), và test sẽ đỏ
        // ở một assertion nói về IDOR mà không manh mối nào chỉ về đăng ký.
        Assert.True(response.StatusCode is HttpStatusCode.Found,
            $"Đăng ký thất bại với {(int)response.StatusCode}.");

        using var scope = _factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<MiniMartDbContext>();
        var user = await context.Users.SingleAsync(u => u.Username == username);

        return (client, user.Id);
    }

    private async Task<int> TaoDonAsync(
        MiniMartDbContext context, int userId, int soLuong, OrderStatus trangThai)
    {
        var order = new Order
        {
            UserId = userId,
            CreatedAt = DateTime.UtcNow,
            Status = trangThai,
            RecipientName = "Nguoi Nhan A",
            RecipientPhone = "0900000000",
            ShippingAddress = "So 1 Duong ABC",
            TotalAmount = 250_000m * soLuong
        };

        order.Items.Add(new OrderDetail
        {
            ProductId = _productId,
            ProductName = "Ten luc mua",
            UnitPrice = 250_000m,
            Quantity = soLuong
        });

        context.Orders.Add(order);
        await context.SaveChangesAsync();

        return order.Id;
    }

    public async Task DisposeAsync()
    {
        using var scope = _factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<MiniMartDbContext>();

        var userIds = await context.Users
            .Where(u => _usernames.Contains(u.Username))
            .Select(u => u.Id)
            .ToListAsync();

        // Xoá theo thứ tự khoá ngoại: OrderDetail -> Order -> Product -> Category -> User.
        var orderIds = await context.Orders
            .Where(o => userIds.Contains(o.UserId))
            .Select(o => o.Id)
            .ToListAsync();

        await context.OrderDetails.Where(d => orderIds.Contains(d.OrderId)).ExecuteDeleteAsync();
        await context.Orders.Where(o => orderIds.Contains(o.Id)).ExecuteDeleteAsync();
        await context.Products.Where(p => p.CategoryId == _categoryId).ExecuteDeleteAsync();
        await context.Categories.Where(c => c.Id == _categoryId).ExecuteDeleteAsync();
        await context.Users.Where(u => userIds.Contains(u.Id)).ExecuteDeleteAsync();

        _clientA.Dispose();
        _factory.Dispose();
    }
}
