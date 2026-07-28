// NullLogger thay vì Mock<ILogger>: unit test ở đây kiểm NGHIỆP VỤ, không kiểm log.
// Mock ILogger sẽ dụ người viết assert vào câu chữ của một dòng log - thứ đổi thường
// xuyên và đổi mà không hỏng gì.
using Microsoft.Extensions.Logging.Abstractions;
using MiniMart.Application.Models;
using MiniMart.Application.Services;
using MiniMart.Common.Exceptions;
using MiniMart.Domain.Entities;
using MiniMart.Domain.Enums;
using MiniMart.Domain.Interfaces;
using MiniMart.Domain.ValueObjects;
using Moq;

namespace MiniMart.Tests;

/// <summary>
/// Nghiệp vụ đặt hàng - unit test với Moq, không cần DB.
///
/// <para>
/// Ở đây kiểm được: snapshot giá/tên, trừ đúng số lượng, tổng tiền, thứ tự
/// SaveChanges → Commit, và nhánh xung đột có rollback rồi dịch exception hay không.
/// </para>
/// <para>
/// KHÔNG kiểm được ở đây: tranh chấp thật. Mock <c>IUnitOfWork</c> thì không có hai
/// transaction nào chạy song song, và ném <c>ConcurrencyConflictException</c> từ mock
/// chỉ chứng minh code xử lý ĐÚNG khi xung đột xảy ra, không chứng minh xung đột thật
/// sự được phát hiện. Việc đó là của <see cref="CheckoutConcurrencyTests"/> trên SQL
/// Server thật.
/// </para>
/// </summary>
public class OrderServiceTests
{
    private const int UserId = 7;

    private readonly Mock<ICartStore> _cartStore = new();
    private readonly Mock<IProductRepository> _productRepository = new();
    private readonly Mock<IOrderRepository> _orderRepository = new();
    private readonly Mock<IUnitOfWork> _unitOfWork = new();
    private readonly Mock<ITransaction> _transaction = new();

    /// <summary>Ghi lại thứ tự các bước để khẳng định Commit đứng SAU SaveChanges.</summary>
    private readonly List<string> _thuTu = [];

    private Order? _donDaLuu;

    public OrderServiceTests()
    {
        _unitOfWork.Setup(u => u.BeginTransactionAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(_transaction.Object)
            .Callback(() => _thuTu.Add("begin"));

        _unitOfWork.Setup(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(1)
            .Callback(() => _thuTu.Add("save"));

        _transaction.Setup(t => t.CommitAsync(It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask)
            .Callback(() => _thuTu.Add("commit"));

        _transaction.Setup(t => t.RollbackAsync(It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask)
            .Callback(() => _thuTu.Add("rollback"));

        _transaction.Setup(t => t.DisposeAsync()).Returns(ValueTask.CompletedTask);

        _orderRepository.Setup(r => r.AddAsync(It.IsAny<Order>(), It.IsAny<CancellationToken>()))
            .Callback<Order, CancellationToken>((o, _) => { _donDaLuu = o; _thuTu.Add("add-order"); })
            .Returns(Task.CompletedTask);

        _cartStore.Setup(s => s.ClearAsync(It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask)
            .Callback(() => _thuTu.Add("clear-cart"));
    }

    // ───────────── Đường thành công ─────────────

    [Fact]
    public async Task Snapshot_gia_va_ten_tai_thoi_diem_dat()
    {
        var product = TaoSanPham(1, "Laptop A", gia: 20_000_000m, ton: 5);
        var service = TaoService([new CartLine(1, 2)], product);

        await service.CheckoutAsync(UserId, CheckoutTestData.GiaoHang);

        var dong = Assert.Single(_donDaLuu!.Items);

        // Lý do tồn tại của cả OrderDetail: hoá đơn phải đọc lại được đúng con số
        // hôm nay, kể cả khi shop đổi giá hoặc đổi tên sản phẩm về sau.
        Assert.Equal(20_000_000m, dong.UnitPrice);
        Assert.Equal("Laptop A", dong.ProductName);

        // Đổi giá SAU khi đặt không được ảnh hưởng tới dòng đã snapshot.
        product.Price = 15_000_000m;
        product.Name = "Laptop A (hàng cũ)";

        Assert.Equal(20_000_000m, dong.UnitPrice);
        Assert.Equal("Laptop A", dong.ProductName);
    }

    [Fact]
    public async Task Tru_ton_kho_dung_so_luong_da_dat()
    {
        var product = TaoSanPham(1, "A", 100m, ton: 10);
        var service = TaoService([new CartLine(1, 3)], product);

        await service.CheckoutAsync(UserId, CheckoutTestData.GiaoHang);

        Assert.Equal(7, product.Stock);
    }

    [Fact]
    public async Task Tong_tien_don_bang_tong_cac_dong_da_snapshot()
    {
        var a = TaoSanPham(1, "A", 100_000m, ton: 10);
        var b = TaoSanPham(2, "B", 250_000m, ton: 10);
        var service = TaoService([new CartLine(1, 2), new CartLine(2, 1)], a, b);

        var ketQua = await service.CheckoutAsync(UserId, CheckoutTestData.GiaoHang);

        // 2 x 100.000 + 1 x 250.000
        Assert.Equal(450_000m, _donDaLuu!.TotalAmount);
        Assert.Equal(450_000m, ketQua.TotalAmount);
        Assert.Equal(2, ketQua.ItemCount);
    }

    [Fact]
    public async Task Don_duoc_gan_dung_UserId_truyen_vao()
    {
        var service = TaoService([new CartLine(1, 1)], TaoSanPham(1, "A", 100m, 5));

        await service.CheckoutAsync(UserId, CheckoutTestData.GiaoHang);

        // UserId phải đến từ ICurrentUser ở tầng Web, không từ form. Test này khoá
        // việc nó được gán đúng chỗ; test IDOR ở CheckoutConcurrencyTests khoá việc
        // Controller không nhận nó từ request.
        Assert.Equal(UserId, _donDaLuu!.UserId);
    }

    [Fact]
    public async Task Thu_tu_bat_buoc_la_luu_roi_moi_commit()
    {
        var service = TaoService([new CartLine(1, 1)], TaoSanPham(1, "A", 100m, 5));

        await service.CheckoutAsync(UserId, CheckoutTestData.GiaoHang);

        // Commit trước SaveChanges thì commit một transaction chưa có gì trong đó,
        // và thay đổi sau đó nằm ngoài mọi transaction.
        Assert.Equal(
            new[] { "begin", "add-order", "clear-cart", "save", "commit" },
            _thuTu.ToArray());
    }

    [Fact]
    public async Task Xoa_gio_hang_trong_CUNG_transaction()
    {
        var service = TaoService([new CartLine(1, 1)], TaoSanPham(1, "A", 100m, 5));

        await service.CheckoutAsync(UserId, CheckoutTestData.GiaoHang);

        // clear-cart phải nằm giữa begin và commit. Xoá giỏ ngoài transaction thì
        // lưu đơn thất bại sẽ để người dùng không có đơn mà cũng không còn giỏ.
        Assert.InRange(_thuTu.IndexOf("clear-cart"), _thuTu.IndexOf("begin") + 1, _thuTu.IndexOf("commit") - 1);
    }

    // ───────────── Đổi trạng thái thanh toán ─────────────

    [Fact]
    public async Task Doi_trang_thai_tu_Pending_sang_Paid_thi_duoc()
    {
        var order = new Order { Id = 5, Status = OrderStatus.Pending };
        var service = TaoServiceRong(order);

        await service.UpdatePaymentStatusAsync(5, OrderStatus.Paid);

        Assert.Equal(OrderStatus.Paid, order.Status);
    }

    [Fact]
    public async Task Doi_trang_thai_KHONG_tu_luu()
    {
        var order = new Order { Id = 5, Status = OrderStatus.Pending };
        var service = TaoServiceRong(order);

        await service.UpdatePaymentStatusAsync(5, OrderStatus.Paid);

        // ★ Chủ ý, không phải quên. Nếu method này tự lưu thì luồng IPN có HAI
        // SaveChanges, và giữa chúng tồn tại một khoảnh khắc đơn đã Paid mà chưa có
        // bản ghi Payment nào - tiền đã thu mà không tra được từ đâu.
        //
        // Test này tồn tại để một người đọc code thấy "service mà không SaveChanges"
        // rồi "sửa cho nhất quán" sẽ làm nó đỏ ngay.
        _unitOfWork.Verify(
            u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Doi_trang_thai_cho_don_khong_ton_tai_thi_nem_NotFound()
    {
        var service = TaoServiceRong(order: null);

        var ex = await Assert.ThrowsAsync<NotFoundException>(
            () => service.UpdatePaymentStatusAsync(404, OrderStatus.Paid));

        Assert.Equal(nameof(Order), ex.EntityName);
    }

    [Theory]
    [InlineData(OrderStatus.Paid, OrderStatus.Paid)]        // gọi trùng
    [InlineData(OrderStatus.Paid, OrderStatus.Pending)]     // lùi trạng thái
    [InlineData(OrderStatus.Pending, OrderStatus.Pending)]  // không đổi gì
    public async Task Chuyen_trang_thai_khong_hop_le_thi_nem(OrderStatus tu, OrderStatus sang)
    {
        var order = new Order { Id = 5, Status = tu };
        var service = TaoServiceRong(order);

        // Paid -> Pending là mất dấu một khoản tiền đã thu, và không có gì trong DB
        // tố giác chuyện đó. Nổ to là cách duy nhất để nó không xảy ra trong im lặng.
        await Assert.ThrowsAsync<InvalidOrderStatusTransitionException>(
            () => service.UpdatePaymentStatusAsync(5, sang));

        Assert.Equal(tu, order.Status);
    }

    /// <summary>Service chỉ cần IOrderRepository cho luồng đổi trạng thái.</summary>
    private OrderService TaoServiceRong(Order? order)
    {
        _orderRepository
            .Setup(r => r.GetForUpdateAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(order);

        return new OrderService(
            _cartStore.Object,
            _productRepository.Object,
            _orderRepository.Object,
            _unitOfWork.Object,
            NullLogger<OrderService>.Instance);
    }

    // ───────────── Thông tin giao hàng ─────────────

    [Fact]
    public async Task Thong_tin_giao_hang_duoc_snapshot_vao_don()
    {
        var service = TaoService([new CartLine(1, 1)], TaoSanPham(1, "A", 100m, 5));

        await service.CheckoutAsync(
            UserId, new ShippingInfo("Tran Thi B", "0987654321", "5 Le Loi, Hue"));

        // Chép GIÁ TRỊ vào đơn, cùng tinh thần với ProductName/UnitPrice. Đọc từ
        // bảng khác lúc hiển thị là để lịch sử đơn hàng tự viết lại theo hồ sơ
        // hiện tại của người dùng.
        Assert.Equal("Tran Thi B", _donDaLuu!.RecipientName);
        Assert.Equal("0987654321", _donDaLuu.RecipientPhone);
        Assert.Equal("5 Le Loi, Hue", _donDaLuu.ShippingAddress);
    }

    [Fact]
    public async Task Khoang_trang_thua_bi_cat_truoc_khi_luu()
    {
        var service = TaoService([new CartLine(1, 1)], TaoSanPham(1, "A", 100m, 5));

        await service.CheckoutAsync(
            UserId, new ShippingInfo("  Tran Thi B  ", " 0987654321 ", "  5 Le Loi, Hue  "));

        // [Required] chấp nhận chuỗi có khoảng trắng thừa, nên không chuẩn hoá thì
        // dữ liệu bẩn đi thẳng xuống DB mà không có lỗi nào.
        Assert.Equal("Tran Thi B", _donDaLuu!.RecipientName);
        Assert.Equal("0987654321", _donDaLuu.RecipientPhone);
        Assert.Equal("5 Le Loi, Hue", _donDaLuu.ShippingAddress);
    }

    [Theory]
    [InlineData("", "0987654321", "5 Le Loi, Hue")]
    [InlineData("Tran Thi B", "", "5 Le Loi, Hue")]
    [InlineData("Tran Thi B", "0987654321", "")]
    [InlineData("   ", "0987654321", "5 Le Loi, Hue")]
    [InlineData("Tran Thi B", "0987654321", "     ")]
    public async Task Thieu_thong_tin_giao_hang_thi_nem_ArgumentException(
        string ten, string dienThoai, string diaChi)
    {
        var service = TaoService([new CartLine(1, 1)], TaoSanPham(1, "A", 100m, 5));

        // ArgumentException chứ không phải exception nghiệp vụ: tầng Web đã chặn bằng
        // [Required], nên tới được đây là lỗi LẬP TRÌNH. Ném exception nghiệp vụ ở đây
        // sẽ khiến Controller hiện nó ra như một thông báo bình thường cho người dùng
        // và giấu mất một cái bug thật.
        await Assert.ThrowsAsync<ArgumentException>(
            () => service.CheckoutAsync(UserId, new ShippingInfo(ten, dienThoai, diaChi)));
    }

    [Fact]
    public async Task Kiem_thong_tin_giao_hang_chay_TRUOC_khi_mo_transaction()
    {
        var service = TaoService([new CartLine(1, 1)], TaoSanPham(1, "A", 100m, 5));

        await Assert.ThrowsAsync<ArgumentException>(
            () => service.CheckoutAsync(UserId, new ShippingInfo("A", "0912345678", "")));

        // Danh sách bước phải RỖNG. Đây là kiểm tra thuần bộ nhớ nên đặt nó sau
        // BeginTransaction là mở một transaction chỉ để lập tức đóng lại - vẫn chiếm
        // một connection của pool và vẫn ghi một cặp BEGIN/ROLLBACK vào log SQL Server.
        Assert.Empty(_thuTu);

        // Và cũng chưa được chạm tới giỏ hàng: một round-trip DB cho một request chắc
        // chắn thất bại.
        _cartStore.Verify(s => s.GetLinesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Xu_ly_theo_thu_tu_ProductId_tang_dan()
    {
        var a = TaoSanPham(9, "Sau", 100m, 10);
        var b = TaoSanPham(2, "Truoc", 100m, 10);

        // Giỏ trả về theo thứ tự ngược để test không xanh nhờ tình cờ.
        var service = TaoService([new CartLine(9, 1), new CartLine(2, 1)], a, b);

        await service.CheckoutAsync(UserId, CheckoutTestData.GiaoHang);

        // Mọi transaction chạm nhiều dòng phải chạm chúng theo CÙNG một thứ tự, nếu
        // không hai đơn có chung hai sản phẩm sẽ deadlock. Đây là loại lỗi chỉ xuất
        // hiện dưới tải nên phải khoá bằng test chứ không bằng mắt.
        Assert.Equal(new[] { 2, 9 }, _donDaLuu!.Items.Select(i => i.ProductId).ToArray());
    }

    // ───────────── Đường lỗi ─────────────

    [Fact]
    public async Task Gio_rong_thi_nem_EmptyCartException_va_khong_mo_transaction()
    {
        var service = TaoService([]);

        await Assert.ThrowsAsync<EmptyCartException>(() => service.CheckoutAsync(UserId, CheckoutTestData.GiaoHang));

        // Mở transaction rồi mới phát hiện giỏ rỗng là mở một transaction để không
        // làm gì - tốn một kết nối và một lần round-trip.
        Assert.Empty(_thuTu);
    }

    [Fact]
    public async Task Khong_du_ton_kho_thi_nem_InsufficientStock_va_KHONG_commit()
    {
        var product = TaoSanPham(1, "Chuot", 100m, ton: 2);
        var service = TaoService([new CartLine(1, 5)], product);

        var ex = await Assert.ThrowsAsync<InsufficientStockException>(
            () => service.CheckoutAsync(UserId, CheckoutTestData.GiaoHang));

        // Thông báo phải nói RÕ còn bao nhiêu, để người dùng biết giảm xuống mấy.
        Assert.Contains("Chuot", ex.Message);
        Assert.Contains("chỉ còn 2", ex.Message);
        Assert.Equal(2, ex.Available);
        Assert.Equal(5, ex.Requested);

        Assert.DoesNotContain("save", _thuTu);
        Assert.DoesNotContain("commit", _thuTu);
    }

    [Fact]
    public async Task Het_sach_hang_thi_thong_bao_la_vua_het_hang()
    {
        var service = TaoService([new CartLine(1, 1)], TaoSanPham(1, "Ban phim", 100m, ton: 0));

        var ex = await Assert.ThrowsAsync<InsufficientStockException>(
            () => service.CheckoutAsync(UserId, CheckoutTestData.GiaoHang));

        Assert.Contains("vừa hết hàng, vui lòng cập nhật giỏ hàng", ex.Message);
    }

    [Fact]
    public async Task Mot_dong_thieu_hang_thi_KHONG_dong_nao_duoc_dat()
    {
        var du = TaoSanPham(1, "Du", 100m, ton: 10);
        var thieu = TaoSanPham(2, "Thieu", 100m, ton: 1);
        var service = TaoService([new CartLine(1, 1), new CartLine(2, 5)], du, thieu);

        await Assert.ThrowsAsync<InsufficientStockException>(() => service.CheckoutAsync(UserId, CheckoutTestData.GiaoHang));

        // Đây là điểm của transaction: đơn hàng là tất-cả-hoặc-không-gì. Trừ kho
        // sản phẩm "Du" rồi bỏ dở là bán một phần đơn mà khách không hề đồng ý.
        //
        // Ở nhánh này phép trừ trong bộ nhớ CÓ thể đã chạy cho sản phẩm đầu (thứ tự
        // ProductId), nhưng vì SaveChanges không bao giờ được gọi nên không có gì
        // xuống DB.
        Assert.DoesNotContain("save", _thuTu);
        Assert.DoesNotContain("commit", _thuTu);
    }

    [Fact]
    public async Task San_pham_da_bi_xoa_khoi_shop_thi_nem_NotFound()
    {
        // Repository trả về danh sách RỖNG: sản phẩm trong giỏ không còn tồn tại.
        var service = TaoService([new CartLine(1, 1)]);

        var ex = await Assert.ThrowsAsync<NotFoundException>(() => service.CheckoutAsync(UserId, CheckoutTestData.GiaoHang));

        // KHÔNG bỏ qua im lặng như lúc gộp giỏ: người dùng vừa bấm xác nhận trên một
        // tổng tiền cụ thể, lặng lẽ đặt ít hàng hơn và thu số tiền khác là không được.
        Assert.Equal(nameof(Product), ex.EntityName);
        Assert.DoesNotContain("commit", _thuTu);
    }

    // ───────────── Nhánh xung đột (phần skill yêu cầu bắt RIÊNG) ─────────────

    [Fact]
    public async Task Xung_dot_RowVersion_thi_ROLLBACK_va_dich_thanh_InsufficientStock()
    {
        var product = TaoSanPham(1, "Man hinh", 100m, ton: 5);
        var service = TaoService([new CartLine(1, 1)], product);

        // ConcurrencyConflictException CHÍNH LÀ DbUpdateConcurrencyException sau khi
        // UnitOfWork dịch tầng - Application không được using EF Core nên không bắt
        // được kiểu gốc.
        _unitOfWork.Setup(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()))
            .Callback(() => _thuTu.Add("save"))
            .ThrowsAsync(new ConcurrencyConflictException(nameof(Product), 1, null));

        var ex = await Assert.ThrowsAsync<InsufficientStockException>(
            () => service.CheckoutAsync(UserId, CheckoutTestData.GiaoHang));

        // Người dùng phải đọc được TÊN sản phẩm, không phải "Product id 1".
        Assert.Contains("Man hinh", ex.Message);
        Assert.Contains("vừa hết hàng", ex.Message);

        // Rollback tường minh, và TUYỆT ĐỐI không commit.
        Assert.Contains("rollback", _thuTu);
        Assert.DoesNotContain("commit", _thuTu);
    }

    [Fact]
    public async Task Xung_dot_khong_ro_san_pham_nao_thi_van_bao_loi_tu_te()
    {
        var service = TaoService([new CartLine(1, 1)], TaoSanPham(1, "A", 100m, 5));

        // Id không khớp sản phẩm nào trong giỏ (hoặc null).
        _unitOfWork.Setup(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()))
            .ThrowsAsync(new ConcurrencyConflictException("Product", null, null));

        var ex = await Assert.ThrowsAsync<InsufficientStockException>(
            () => service.CheckoutAsync(UserId, CheckoutTestData.GiaoHang));

        // Không được ném exception MỚI trong lúc xử lý exception - đó là cách nhanh
        // nhất để mất luôn nguyên nhân gốc.
        Assert.Contains("giỏ hàng", ex.Message);
    }

    [Fact]
    public async Task Xung_dot_giu_lai_exception_goc_lam_InnerException()
    {
        var service = TaoService([new CartLine(1, 1)], TaoSanPham(1, "A", 100m, 5));
        var goc = new ConcurrencyConflictException(nameof(Product), 1, null);

        _unitOfWork.Setup(u => u.SaveChangesAsync(It.IsAny<CancellationToken>())).ThrowsAsync(goc);

        var ex = await Assert.ThrowsAsync<InsufficientStockException>(
            () => service.CheckoutAsync(UserId, CheckoutTestData.GiaoHang));

        // Người dùng đọc thông báo nghiệp vụ, còn log giữ được nguyên nhân kỹ thuật.
        Assert.Same(goc, ex.InnerException);
    }

    // ───────────── Helper ─────────────

    private static Product TaoSanPham(int id, string ten, decimal gia, int ton) =>
        new() { Id = id, Name = ten, Price = gia, Stock = ton, CategoryId = 1 };

    private OrderService TaoService(IReadOnlyList<CartLine> gio, params Product[] sanPham)
    {
        _cartStore.Setup(s => s.GetLinesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(gio);

        _productRepository.Setup(r => r.GetManyForUpdateAsync(
                It.IsAny<IEnumerable<int>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(sanPham.ToList());

        return new OrderService(
            _cartStore.Object,
            _productRepository.Object,
            _orderRepository.Object,
            _unitOfWork.Object,
            NullLogger<OrderService>.Instance);
    }
}
