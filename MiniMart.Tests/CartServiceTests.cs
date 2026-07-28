using MiniMart.Application.Services;
using MiniMart.Common.Exceptions;
using MiniMart.Domain.Entities;
using MiniMart.Domain.Interfaces;
using MiniMart.Domain.ValueObjects;
using Moq;

namespace MiniMart.Tests;

/// <summary>
/// Nghiệp vụ giỏ hàng - unit test, KHÔNG cần DB. Đây là lợi ích cụ thể của DIP:
/// CartService chỉ phụ thuộc ICartStore và IProductRepository nên thay cả hai
/// bằng bản giả là test được toàn bộ quy tắc.
///
/// <para>
/// Kho giỏ hàng dùng bản giả CÓ TRẠNG THÁI thay vì Moq: mỗi thao tác đọc lại giỏ
/// sau khi ghi, mà mock trả giá trị cố định sẽ luôn trả về dữ liệu cũ và test
/// "cộng dồn" trở thành vô nghĩa.
/// </para>
/// </summary>
public class CartServiceTests
{
    private const int MaSanPhamA = 101;
    private const int MaSanPhamB = 102;

    private readonly KhoGia _kho = new();
    private readonly Mock<IProductRepository> _productRepository = new();
    private readonly Mock<IUnitOfWork> _unitOfWork = new();
    private readonly List<string> _thuTuThaoTac = [];

    private readonly Dictionary<int, Product> _sanPham = [];

    public CartServiceTests()
    {
        ThemSanPham(MaSanPhamA, "Điện thoại A", 1_000_000m, stock: 10);
        ThemSanPham(MaSanPhamB, "Laptop B", 20_000_000m, stock: 3);

        _productRepository
            .Setup(r => r.GetByIdAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((int id, CancellationToken _) =>
                _sanPham.TryGetValue(id, out var p) ? p : null);

        _productRepository
            .Setup(r => r.GetByIdsAsync(It.IsAny<IEnumerable<int>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((IEnumerable<int> ids, CancellationToken _) =>
                ids.Where(_sanPham.ContainsKey).Select(id => _sanPham[id]).ToList());

        _unitOfWork
            .Setup(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(1)
            .Callback(() => _thuTuThaoTac.Add("luu"));

        _kho.KhiClear = () => _thuTuThaoTac.Add("xoa-gio-nguon");
    }

    private void ThemSanPham(int id, string ten, decimal gia, int stock) =>
        _sanPham[id] = new Product { Id = id, Name = ten, Price = gia, Stock = stock, CategoryId = 1 };

    private CartService TaoService(ICartStore? kho = null) =>
        new(kho ?? _kho, _productRepository.Object, _unitOfWork.Object);

    // ───────────── AddAsync: cộng dồn ─────────────

    [Fact]
    public async Task Add_hai_lan_thi_CONG_DON_chu_khong_ghi_de()
    {
        var service = TaoService();

        await service.AddAsync(MaSanPhamA, 2);
        var ketQua = await service.AddAsync(MaSanPhamA, 3);

        // Bấm "thêm vào giỏ" hai lần với số lượng 1 phải ra 2. Ghi đè là ngữ
        // nghĩa của UpdateQuantityAsync, không phải của AddAsync.
        var dong = Assert.Single(ketQua.Cart.Lines);
        Assert.Equal(5, dong.Quantity);
        Assert.Null(ketQua.Notice);
    }

    [Fact]
    public async Task Add_vuot_ton_kho_thi_KEP_lai_va_bao_cho_nguoi_dung()
    {
        var service = TaoService();

        // Laptop B chỉ còn 3.
        var ketQua = await service.AddAsync(MaSanPhamB, 10);

        var dong = Assert.Single(ketQua.Cart.Lines);
        Assert.Equal(3, dong.Quantity);

        // Kẹp âm thầm là người dùng thấy số khác số mình vừa nhập mà không hiểu
        // vì sao. Phải nói rõ còn bao nhiêu.
        Assert.NotNull(ketQua.Notice);
        Assert.Contains("chỉ còn 3", ketQua.Notice);
    }

    [Fact]
    public async Task Add_khi_ton_kho_bang_0_thi_nem_OutOfStock()
    {
        ThemSanPham(999, "Hàng hết", 1_000m, stock: 0);
        var service = TaoService();

        // Kẹp về 0 sẽ là "bấm nút mà không có gì xảy ra" - phản hồi tệ nhất.
        var loi = await Assert.ThrowsAsync<OutOfStockException>(() => service.AddAsync(999, 1));

        Assert.Equal("Hàng hết", loi.ProductName);
        Assert.True((await service.GetCartAsync()).IsEmpty);
    }

    [Fact]
    public async Task Add_san_pham_khong_ton_tai_thi_nem_NotFound()
    {
        var service = TaoService();

        var loi = await Assert.ThrowsAsync<NotFoundException>(() => service.AddAsync(555, 1));

        Assert.Equal(nameof(Product), loi.EntityName);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-3)]
    public async Task Add_so_luong_khong_duong_thi_nem(int soLuong)
    {
        var service = TaoService();

        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(
            () => service.AddAsync(MaSanPhamA, soLuong));
    }

    // ───────────── UpdateQuantityAsync: đặt tuyệt đối ─────────────

    [Fact]
    public async Task UpdateQuantity_dat_TUYET_DOI_chu_khong_cong_don()
    {
        var service = TaoService();
        await service.AddAsync(MaSanPhamA, 4);

        var ketQua = await service.UpdateQuantityAsync(MaSanPhamA, 2);

        // Ô nhập số lượng trên trang giỏ hàng: nhập 2 nghĩa là "tôi muốn 2",
        // không phải "thêm 2 nữa".
        Assert.Equal(2, Assert.Single(ketQua.Cart.Lines).Quantity);
    }

    [Fact]
    public async Task UpdateQuantity_bang_0_thi_XOA_dong()
    {
        var service = TaoService();
        await service.AddAsync(MaSanPhamA, 4);
        await service.AddAsync(MaSanPhamB, 1);

        var ketQua = await service.UpdateQuantityAsync(MaSanPhamA, 0);

        // ICartStore cố ý KHÔNG nhận số 0 (DB có CHECK Quantity > 0), nên việc
        // dịch 0 thành "xoá" phải nằm ở Service.
        Assert.Equal(MaSanPhamB, Assert.Single(ketQua.Cart.Lines).ProductId);
    }

    [Fact]
    public async Task UpdateQuantity_vuot_ton_kho_thi_kep_lai()
    {
        var service = TaoService();

        var ketQua = await service.UpdateQuantityAsync(MaSanPhamB, 99);

        Assert.Equal(3, Assert.Single(ketQua.Cart.Lines).Quantity);
        Assert.NotNull(ketQua.Notice);
    }

    [Fact]
    public async Task UpdateQuantity_am_thi_nem()
    {
        var service = TaoService();

        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(
            () => service.UpdateQuantityAsync(MaSanPhamA, -1));
    }

    // ───────────── RemoveAsync ─────────────

    [Fact]
    public async Task Remove_xoa_duoc_dong_cua_san_pham_DA_BI_XOA_khoi_shop()
    {
        var service = TaoService();
        await service.AddAsync(MaSanPhamA, 1);

        // Admin xoá sản phẩm trong lúc giỏ Session của khách vẫn còn sống.
        _sanPham.Remove(MaSanPhamA);

        await service.RemoveAsync(MaSanPhamA);

        // RemoveAsync cố ý KHÔNG kiểm tra sản phẩm còn tồn tại: kiểm tra thì giỏ
        // Session mắc kẹt vĩnh viễn với một dòng không xoá nổi.
        Assert.Empty(_kho.Dong);
    }

    [Fact]
    public async Task Remove_san_pham_khong_co_trong_gio_thi_khong_nem()
    {
        var service = TaoService();
        await service.AddAsync(MaSanPhamA, 1);

        await service.RemoveAsync(MaSanPhamB);

        Assert.Single(_kho.Dong);
    }

    // ───────────── GetCartAsync ─────────────

    [Fact]
    public async Task GetCart_LOC_BO_dong_tro_toi_san_pham_da_bi_xoa()
    {
        var service = TaoService();
        await service.AddAsync(MaSanPhamA, 1);
        await service.AddAsync(MaSanPhamB, 2);

        _sanPham.Remove(MaSanPhamA);

        var gio = await service.GetCartAsync();

        Assert.Equal(MaSanPhamB, Assert.Single(gio.Lines).ProductId);

        // KHÔNG tự xoá khỏi kho: đây là đường đọc, mà GET phải là thao tác an toàn.
        Assert.Equal(2, _kho.Dong.Count);
    }

    [Fact]
    public async Task GetCart_dung_MOT_truy_van_cho_ca_gio()
    {
        var service = TaoService();
        await service.AddAsync(MaSanPhamA, 1);
        await service.AddAsync(MaSanPhamB, 1);

        _productRepository.Invocations.Clear();

        await service.GetCartAsync();

        // Gọi GetByIdAsync cho từng dòng là bài toán N+1: giỏ 12 món thành 12
        // round-trip xuống SQL Server.
        _productRepository.Verify(
            r => r.GetByIdsAsync(It.IsAny<IEnumerable<int>>(), It.IsAny<CancellationToken>()),
            Times.Once);
        _productRepository.Verify(
            r => r.GetByIdAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task GetCart_lay_gia_HIEN_TAI_chu_khong_phai_gia_luc_them_vao_gio()
    {
        var service = TaoService();
        await service.AddAsync(MaSanPhamA, 2);

        // Shop điều chỉnh bảng giá sau khi khách đã thêm vào giỏ.
        _sanPham[MaSanPhamA].Price = 1_500_000m;

        var gio = await service.GetCartAsync();

        // Người dùng thanh toán theo giá lúc ĐẶT HÀNG. Chốt giá là việc của
        // OrderItem ở phase sau, không phải của giỏ hàng.
        Assert.Equal(1_500_000m, Assert.Single(gio.Lines).UnitPrice);
        Assert.Equal(3_000_000m, gio.TotalAmount);
    }

    [Fact]
    public async Task GetCart_tinh_dung_tong_so_luong_va_tong_tien()
    {
        var service = TaoService();
        await service.AddAsync(MaSanPhamA, 2);   // 2 x 1.000.000
        await service.AddAsync(MaSanPhamB, 3);   // 3 x 20.000.000

        var gio = await service.GetCartAsync();

        // TotalQuantity là tổng SỐ MÓN, không phải số dòng - badge trên navbar
        // hiện "5" chứ không phải "2".
        Assert.Equal(5, gio.TotalQuantity);
        Assert.Equal(62_000_000m, gio.TotalAmount);
    }

    [Fact]
    public async Task GetCart_bao_dong_vuot_ton_kho_khi_hang_bi_mua_bot()
    {
        var service = TaoService();
        await service.AddAsync(MaSanPhamB, 3);

        // Người khác mua bớt: giỏ hàng KHÔNG giữ hàng.
        _sanPham[MaSanPhamB].Stock = 1;

        var gio = await service.GetCartAsync();

        Assert.True(Assert.Single(gio.Lines).ExceedsStock);
        Assert.True(gio.HasStockProblem);
    }

    [Fact]
    public async Task GetCart_gio_rong_thi_khong_truy_van_gi()
    {
        var service = TaoService();

        var gio = await service.GetCartAsync();

        Assert.True(gio.IsEmpty);
        _productRepository.Verify(
            r => r.GetByIdsAsync(It.IsAny<IEnumerable<int>>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    // ───────────── MergeAsync ─────────────

    [Fact]
    public async Task Merge_TONG_so_luong_cua_hai_gio()
    {
        var nguon = new KhoGia();
        await nguon.SetQuantityAsync(MaSanPhamA, 2);

        var dich = new KhoGia();
        await dich.SetQuantityAsync(MaSanPhamA, 3);

        await TaoService().MergeAsync(nguon, dich);

        // Lấy TỔNG chứ không phải max: người dùng đã chủ động thêm ở cả hai nơi
        // nên cả hai ý định đều thật. Max sẽ âm thầm bỏ một trong hai.
        Assert.Equal(5, Assert.Single(dich.Dong).Quantity);
    }

    [Fact]
    public async Task Merge_kep_theo_ton_kho()
    {
        var nguon = new KhoGia();
        await nguon.SetQuantityAsync(MaSanPhamB, 2);

        var dich = new KhoGia();
        await dich.SetQuantityAsync(MaSanPhamB, 2);

        await TaoService().MergeAsync(nguon, dich);

        // Laptop B chỉ còn 3, tổng 4 phải bị kẹp.
        Assert.Equal(3, Assert.Single(dich.Dong).Quantity);
    }

    [Fact]
    public async Task Merge_xoa_gio_nguon_SAU_khi_da_luu_gio_dich()
    {
        var nguon = new KhoGia { KhiClear = () => _thuTuThaoTac.Add("xoa-gio-nguon") };
        await nguon.SetQuantityAsync(MaSanPhamA, 1);

        await TaoService().MergeAsync(nguon, new KhoGia());

        // Thứ tự này quan trọng: lưu thất bại thì giỏ nguồn vẫn còn và lần đăng
        // nhập sau gộp lại được. Làm ngược lại là người dùng mất sạch giỏ hàng.
        Assert.Equal(["luu", "xoa-gio-nguon"], _thuTuThaoTac);
        Assert.Empty(nguon.Dong);
    }

    [Fact]
    public async Task Merge_bo_qua_san_pham_da_bi_xoa_hoac_het_hang()
    {
        ThemSanPham(777, "Hết hàng", 1_000m, stock: 0);

        var nguon = new KhoGia();
        await nguon.SetQuantityAsync(MaSanPhamA, 1);
        await nguon.SetQuantityAsync(777, 1);
        await nguon.SetQuantityAsync(888, 1);   // id không tồn tại

        var dich = new KhoGia();

        await TaoService().MergeAsync(nguon, dich);

        Assert.Equal(MaSanPhamA, Assert.Single(dich.Dong).ProductId);
    }

    [Fact]
    public async Task Merge_gio_nguon_rong_thi_khong_lam_gi()
    {
        var dich = new KhoGia();

        await TaoService().MergeAsync(new KhoGia(), dich);

        Assert.Empty(dich.Dong);

        // Không có gì để gộp thì không được chạm DB - đăng nhập của người chưa
        // từng thêm gì vào giỏ không nên tốn một lần SaveChanges.
        _unitOfWork.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    // ───────────── Bản giả có trạng thái ─────────────

    private sealed class KhoGia : ICartStore
    {
        private readonly List<CartLine> _dong = [];

        public IReadOnlyList<CartLine> Dong => _dong;

        public Action? KhiClear { get; set; }

        public Task<IReadOnlyList<CartLine>> GetLinesAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<CartLine>>(_dong.ToList());

        public Task SetQuantityAsync(int productId, int quantity, CancellationToken cancellationToken = default)
        {
            if (quantity < 1)
            {
                throw new ArgumentOutOfRangeException(nameof(quantity), quantity, "Phải >= 1.");
            }

            _dong.RemoveAll(l => l.ProductId == productId);
            _dong.Add(new CartLine(productId, quantity));

            return Task.CompletedTask;
        }

        public Task RemoveAsync(int productId, CancellationToken cancellationToken = default)
        {
            _dong.RemoveAll(l => l.ProductId == productId);
            return Task.CompletedTask;
        }

        public Task ClearAsync(CancellationToken cancellationToken = default)
        {
            KhiClear?.Invoke();
            _dong.Clear();
            return Task.CompletedTask;
        }
    }
}
