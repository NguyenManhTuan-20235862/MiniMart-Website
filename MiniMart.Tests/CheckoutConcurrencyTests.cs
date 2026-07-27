using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using MiniMart.Application.Interfaces;
using MiniMart.Common.Exceptions;
using MiniMart.Domain.Entities;
using MiniMart.Infrastructure.Data;

namespace MiniMart.Tests;

/// <summary>
/// Chống oversell khi nhiều người mua cùng lúc - trên SQL Server THẬT.
///
/// <para>
/// Bắt buộc là integration test. Hai cách "giả lập" đều cho test luôn xanh kể cả khi
/// code có bug, tức tệ hơn là không viết test:
/// </para>
/// <para>
/// - <b>Moq</b>: mock <c>IProductRepository</c> thì không có tranh chấp nào xảy ra,
///   chỉ đang test chính cái mock.
/// </para>
/// <para>
/// - <b>EF Core InMemory</b>: KHÔNG thực thi concurrency token, nên
///   <c>DbUpdateConcurrencyException</c> không bao giờ được ném và mọi đơn đều
///   "thành công" - đúng cái bug đang cần phát hiện.
/// </para>
/// <para>
/// Mỗi "người mua" chạy trong MỘT DI scope riêng. Dùng chung một scope là dùng chung
/// một <c>DbContext</c> với một Change Tracker: hai bên nhìn cùng một entity trong bộ
/// nhớ nên không xung đột nào xảy ra và test xanh vô nghĩa.
/// </para>
/// </summary>
public class CheckoutConcurrencyTests : IAsyncLifetime
{
    private readonly WebApplicationFactory<Program> _factory = new();
    private readonly List<int> _userIds = [];

    private int _categoryId;
    private int _productId;

    public async Task InitializeAsync()
    {
        using var scope = _factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<MiniMartDbContext>();

        var category = new Category { Name = $"CC_{Guid.NewGuid():N}"[..14] };
        var product = new Product { Name = "HangHot", Price = 1_000_000m, Stock = 5, Category = category };

        context.Products.Add(product);
        await context.SaveChangesAsync();

        _categoryId = category.Id;
        _productId = product.Id;
    }

    // ───────────── Test trung tâm ─────────────

    [Fact]
    public async Task Muoi_nguoi_mua_song_song_khi_chi_con_5_hang_thi_KHONG_BAO_GIO_oversell()
    {
        const int SoNguoi = 10;
        const int TonKhoBanDau = 5;

        var userIds = await TaoNguoiMuaAsync(SoNguoi, soLuongMoiNguoi: 1);

        // Bắn tất cả cùng lúc. Task.WhenAll là thứ tạo ra tranh chấp thật - chạy
        // tuần tự thì không có race nào và test mất hết ý nghĩa.
        var ketQua = await Task.WhenAll(userIds.Select(DatHangAsync));

        var thanhCong = ketQua.Count(k => k.ThanhCong);
        var tonKhoConLai = await LayTonKhoAsync();
        var tongDaBan = await TongSoLuongDaBanAsync();

        // ★ Bất biến quan trọng nhất của cả dự án: KHÔNG BAO GIỜ bán quá số hàng có.
        Assert.True(
            tongDaBan <= TonKhoBanDau,
            $"OVERSELL: đã bán {tongDaBan} trong khi chỉ có {TonKhoBanDau}.");

        // Tồn kho không được âm. CHECK constraint dưới DB là lớp bảo vệ thứ hai,
        // độc lập với logic ở Service.
        Assert.True(tonKhoConLai >= 0, $"Tồn kho âm: {tonKhoConLai}");

        // Bảo toàn: mỗi món bán ra phải trừ đúng một món khỏi kho, không thất thoát
        // và không sinh thêm.
        Assert.Equal(TonKhoBanDau, tonKhoConLai + tongDaBan);

        // Số đơn thành công phải khớp số món đã bán (mỗi người mua 1).
        Assert.Equal(thanhCong, tongDaBan);

        // Không có ai thất bại vì lý do ngoài dự kiến (500, timeout...). Mọi thất bại
        // phải là InsufficientStockException - tức người dùng nhận được lời giải thích.
        foreach (var loi in ketQua.Where(k => !k.ThanhCong))
        {
            Assert.IsType<InsufficientStockException>(loi.Loi);
            Assert.Contains("cập nhật giỏ hàng", loi.Loi!.Message);
        }

        // Ít nhất một người phải mua được - nếu không thì đây là livelock, không phải
        // bảo vệ.
        Assert.True(thanhCong >= 1, "Không ai đặt được hàng.");
    }

    [Fact]
    public async Task Transaction_khong_de_lai_don_hang_MO_CO_khi_bi_rollback()
    {
        var userIds = await TaoNguoiMuaAsync(8, soLuongMoiNguoi: 1);

        var ketQua = await Task.WhenAll(userIds.Select(DatHangAsync));

        var thanhCong = ketQua.Where(k => k.ThanhCong).ToList();

        using var scope = _factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<MiniMartDbContext>();

        var donTrongDb = await context.Orders
            .AsNoTracking()
            .Include(o => o.Items)
            .Where(o => _userIds.Contains(o.UserId))
            .ToListAsync();

        // Số đơn dưới DB phải bằng ĐÚNG số lần CheckoutAsync trả về thành công.
        // Nhiều hơn nghĩa là có transaction bị rollback mà đơn vẫn nằm lại - tức
        // transaction không thật sự bao cả INSERT Order lẫn UPDATE Stock.
        Assert.Equal(thanhCong.Count, donTrongDb.Count);

        // Mỗi đơn phải có đủ dòng và tổng tiền khớp - không có đơn "một nửa".
        foreach (var don in donTrongDb)
        {
            Assert.NotEmpty(don.Items);
            Assert.Equal(don.Items.Sum(i => i.UnitPrice * i.Quantity), don.TotalAmount);
        }

        // Id của đơn trả về phải tồn tại thật dưới DB (Commit đã chạy).
        foreach (var k in thanhCong)
        {
            Assert.Contains(donTrongDb, d => d.Id == k.OrderId);
        }
    }

    [Fact]
    public async Task Gio_bi_xoa_chi_khi_dat_hang_THANH_CONG()
    {
        var userIds = await TaoNguoiMuaAsync(8, soLuongMoiNguoi: 1);

        var ketQua = await Task.WhenAll(userIds.Select(DatHangAsync));

        using var scope = _factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<MiniMartDbContext>();

        foreach (var k in ketQua)
        {
            var conHangTrongGio = await context.CartItems
                .AsNoTracking()
                .AnyAsync(i => i.Cart.UserId == k.UserId);

            if (k.ThanhCong)
            {
                Assert.False(conHangTrongGio, "Đặt hàng thành công mà giỏ vẫn còn hàng.");
            }
            else
            {
                // Đây là lý do ClearAsync phải nằm TRONG transaction: thất bại thì giỏ
                // phải còn nguyên để người dùng thử lại, không phải mất cả hai.
                Assert.True(conHangTrongGio, "Đặt hàng thất bại mà giỏ đã bị xoá.");
            }
        }
    }

    [Fact]
    public async Task Mua_nhieu_hon_mot_moi_nguoi_van_khong_oversell()
    {
        // 4 người x 2 món = 8 món mong muốn, kho chỉ có 5.
        var userIds = await TaoNguoiMuaAsync(4, soLuongMoiNguoi: 2);

        await Task.WhenAll(userIds.Select(DatHangAsync));

        var tonKho = await LayTonKhoAsync();
        var daBan = await TongSoLuongDaBanAsync();

        Assert.True(daBan <= 5, $"OVERSELL: bán {daBan}/5.");
        Assert.True(tonKho >= 0);
        Assert.Equal(5, tonKho + daBan);

        // Mỗi đơn thành công phải bán đúng 2 món - không có đơn bị cắt còn 1.
        using var scope = _factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<MiniMartDbContext>();

        var soLuongTungDon = await context.OrderDetails
            .AsNoTracking()
            .Where(d => _userIds.Contains(d.Order.UserId))
            .Select(d => d.Quantity)
            .ToListAsync();

        Assert.All(soLuongTungDon, q => Assert.Equal(2, q));
    }

    [Fact]
    public async Task Hai_request_dong_thoi_tren_san_pham_CHI_CON_1_thi_dung_MOT_don_thanh_cong()
    {
        // Trường hợp tối giản của oversell: 2 người, 1 món. Kết quả xác định TUYỆT ĐỐI
        // dù hai luồng đan xen kiểu nào, vì chỉ có đúng hai kịch bản:
        //
        //   (a) Cả hai cùng đọc Stock = 1 -> cùng qua lệnh kiểm -> cùng trừ về 0 trong
        //       Change Tracker RIÊNG của mình -> cùng SaveChanges. Một người thắng,
        //       người kia có RowVersion đã cũ nên UPDATE khớp 0 dòng -> xung đột.
        //
        //   (b) Người thứ nhất xong hẳn trước khi người thứ hai kịp đọc -> người thứ
        //       hai đọc Stock = 0 và dừng ngay ở lệnh kiểm của Service.
        //
        // Hai đường khác nhau nhưng CÙNG một kết cục quan sát được: 1 thành công,
        // 1 nhận InsufficientStockException. Nhờ vậy test không bị nhấp nháy (flaky).
        await DoiTonKhoAsync(1);

        var userIds = await TaoNguoiMuaAsync(2, soLuongMoiNguoi: 1);

        var ketQua = await Task.WhenAll(userIds.Select(DatHangAsync));

        var thanhCong = ketQua.Where(k => k.ThanhCong).ToList();
        var thatBai = ketQua.Where(k => !k.ThanhCong).ToList();

        Assert.Single(thanhCong);
        Assert.Single(thatBai);

        // ★ Assertion QUAN TRỌNG NHẤT của test này, và không phải vì lý do hiển nhiên.
        //
        // Nó khẳng định người thua nhận LỖI NGHIỆP VỤ đọc được, không phải exception
        // thô của tầng dữ liệu (DbUpdateConcurrencyException đã được UnitOfWork dịch
        // thành ConcurrencyConflictException, rồi OrderService dịch tiếp thành đây).
        //
        // Nhưng nó còn canh giữ CHÍNH PHƯƠNG PHÁP của test. Đã đo bằng mutation kép:
        // tắt concurrency token VÀ cho test dùng chung một DI scope thì hai assertion
        // Assert.Single ở trên VẪN PASS - vì DbContext không thread-safe nên một luồng
        // ném InvalidOperationException ("A second operation was started"), cho ra
        // đúng hình dạng "1 thành công, 1 thất bại" của kết quả đúng.
        //
        // Không có dòng này thì test xanh trong khi cả code lẫn test đều hỏng.
        Assert.IsType<InsufficientStockException>(thatBai[0].Loi);
        Assert.Contains("cập nhật giỏ hàng", thatBai[0].Loi!.Message);

        // Tồn kho cuối cùng: 0, KHÔNG âm.
        Assert.Equal(0, await LayTonKhoAsync());

        // Và đúng 1 món được bán ra - đây là câu khẳng định "không oversell".
        Assert.Equal(1, await TongSoLuongDaBanAsync());

        using var scope = _factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<MiniMartDbContext>();

        // Đúng MỘT đơn dưới DB. Nhiều hơn nghĩa là transaction của người thua vẫn để
        // lại đơn dù tồn kho không trừ được.
        Assert.Equal(1, await context.Orders.CountAsync(o => _userIds.Contains(o.UserId)));
    }

    [Fact]
    public async Task Chay_tuan_tu_thi_ban_duoc_dung_het_hang_trong_kho()
    {
        var userIds = await TaoNguoiMuaAsync(8, soLuongMoiNguoi: 1);

        // Tuần tự = không tranh chấp. Test này là đối chứng cho test song song: nó
        // chứng minh 5 người ĐẦU mua được, tức phần thất bại ở test kia đến từ tranh
        // chấp chứ không phải từ một lỗi logic luôn từ chối.
        var ketQua = new List<KetQuaDatHang>();

        foreach (var userId in userIds)
        {
            ketQua.Add(await DatHangAsync(userId));
        }

        Assert.Equal(5, ketQua.Count(k => k.ThanhCong));
        Assert.Equal(0, await LayTonKhoAsync());
        Assert.Equal(5, await TongSoLuongDaBanAsync());

        // Ba người sau phải nhận InsufficientStockException với thông báo đọc được -
        // KHÔNG phải một exception thô của tầng dữ liệu.
        //
        // Assertion này được thêm vào sau khi mutation test lộ ra một lỗ: bỏ hẳn phép
        // kiểm `Stock < Quantity` ở Service thì tồn kho vẫn không âm (CHECK constraint
        // dưới DB chặn) nên các assertion về số lượng vẫn xanh - chỉ có KIỂU exception
        // là đổi. Không khẳng định kiểu thì mutation đó lọt qua toàn bộ test tích hợp.
        foreach (var thatBai in ketQua.Where(k => !k.ThanhCong))
        {
            Assert.IsType<InsufficientStockException>(thatBai.Loi);
            Assert.Contains("vừa hết hàng", thatBai.Loi!.Message);
        }
    }

    // ───────────── Helper ─────────────

    private sealed record KetQuaDatHang(int UserId, bool ThanhCong, int OrderId, Exception? Loi);

    /// <summary>
    /// Đặt hàng trong MỘT DI scope riêng - mô phỏng một request HTTP độc lập.
    /// Đây là điểm khiến test này có ý nghĩa: mỗi scope có DbContext riêng, nên
    /// RowVersion thật sự phải làm việc.
    /// </summary>
    private async Task<KetQuaDatHang> DatHangAsync(int userId)
    {
        using var scope = _factory.Services.CreateScope();

        // Gán danh tính cho scope này để factory chọn DatabaseCartStore và
        // ICurrentUser trả đúng userId.
        GanNguoiDung(scope, userId);

        var orderService = scope.ServiceProvider.GetRequiredService<IOrderService>();

        try
        {
            var ketQua = await orderService.CheckoutAsync(userId);

            return new KetQuaDatHang(userId, true, ketQua.OrderId, null);
        }
        catch (Exception ex)
        {
            return new KetQuaDatHang(userId, false, 0, ex);
        }
    }

    private static void GanNguoiDung(IServiceScope scope, int userId)
    {
        var accessor = scope.ServiceProvider
            .GetRequiredService<Microsoft.AspNetCore.Http.IHttpContextAccessor>();

        accessor.HttpContext = new Microsoft.AspNetCore.Http.DefaultHttpContext
        {
            User = new System.Security.Claims.ClaimsPrincipal(
                new System.Security.Claims.ClaimsIdentity(
                    [new System.Security.Claims.Claim(
                        System.Security.Claims.ClaimTypes.NameIdentifier,
                        userId.ToString())],
                    "Cookies")),
            RequestServices = scope.ServiceProvider
        };
    }

    /// <summary>Tạo N người mua, mỗi người có giỏ DB chứa sẵn sản phẩm đang test.</summary>
    private async Task<List<int>> TaoNguoiMuaAsync(int soNguoi, int soLuongMoiNguoi)
    {
        using var scope = _factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<MiniMartDbContext>();

        var ids = new List<int>();

        for (var i = 0; i < soNguoi; i++)
        {
            var user = new User
            {
                Username = $"cc_{Guid.NewGuid():N}"[..16],
                PasswordHash = "khong-dung-de-dang-nhap",
                Role = UserRole.Customer
            };

            context.Users.Add(user);
            await context.SaveChangesAsync();

            context.Carts.Add(new Cart
            {
                UserId = user.Id,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow,
                Items = [new CartItem { ProductId = _productId, Quantity = soLuongMoiNguoi }]
            });

            await context.SaveChangesAsync();

            ids.Add(user.Id);
            _userIds.Add(user.Id);
        }

        return ids;
    }

    private async Task DoiTonKhoAsync(int stock)
    {
        using var scope = _factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<MiniMartDbContext>();

        await context.Products
            .Where(p => p.Id == _productId)
            .ExecuteUpdateAsync(s => s.SetProperty(p => p.Stock, stock));
    }

    private async Task<int> LayTonKhoAsync()
    {
        using var scope = _factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<MiniMartDbContext>();

        return await context.Products
            .AsNoTracking()
            .Where(p => p.Id == _productId)
            .Select(p => p.Stock)
            .SingleAsync();
    }

    private async Task<int> TongSoLuongDaBanAsync()
    {
        using var scope = _factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<MiniMartDbContext>();

        return await context.OrderDetails
            .AsNoTracking()
            .Where(d => d.ProductId == _productId)
            .SumAsync(d => d.Quantity);
    }

    public async Task DisposeAsync()
    {
        using var scope = _factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<MiniMartDbContext>();

        // Thứ tự xoá theo chiều khoá ngoại: OrderDetails -> Orders -> CartItems ->
        // Carts -> Products -> Categories -> Users. OrderDetails.ProductId là
        // Restrict nên không xoá được Product khi còn dòng đơn hàng trỏ tới.
        await context.OrderDetails.Where(d => _userIds.Contains(d.Order.UserId)).ExecuteDeleteAsync();
        await context.Orders.Where(o => _userIds.Contains(o.UserId)).ExecuteDeleteAsync();
        await context.CartItems.Where(i => _userIds.Contains(i.Cart.UserId)).ExecuteDeleteAsync();
        await context.Carts.Where(c => _userIds.Contains(c.UserId)).ExecuteDeleteAsync();
        await context.Products.Where(p => p.CategoryId == _categoryId).ExecuteDeleteAsync();
        await context.Categories.Where(c => c.Id == _categoryId).ExecuteDeleteAsync();
        await context.Users.Where(u => _userIds.Contains(u.Id)).ExecuteDeleteAsync();

        _factory.Dispose();
    }
}
