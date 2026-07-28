using MiniMart.Application.Models;
using MiniMart.Application.Services;
using MiniMart.Domain.Entities;
using MiniMart.Domain.Interfaces;
using Moq;

namespace MiniMart.Tests;

/// <summary>
/// Nghiệp vụ của <c>BulkUpdatePriceStockAsync</c> - unit test, không chạm DB.
///
/// <para>
/// Cái kiểm được ở đây là những quyết định của Service: chặn Id trùng, bỏ qua CHỌN LỌC
/// dòng có <c>RowVersion</c> lệch, báo lại đủ thông tin để Admin xử lý, và một truy vấn
/// duy nhất cho cả danh sách. Hành vi của chính database engine - batch, transaction
/// ngầm, dòng không đổi thì không sinh UPDATE - nằm ở
/// <see cref="ProductBulkUpdateTests"/> trên SQL Server thật, vì mock không chứng minh
/// được thứ nào trong số đó.
/// </para>
/// </summary>
public class ProductBulkUpdateServiceTests
{
    private readonly Mock<IProductRepository> _productRepository = new();
    private readonly Mock<ICategoryRepository> _categoryRepository = new();
    private readonly Mock<IOrderRepository> _orderRepository = new();
    private readonly Mock<IUnitOfWork> _unitOfWork = new();

    private ProductService CreateSut() =>
        new(_productRepository.Object,
            _categoryRepository.Object,
            _orderRepository.Object,
            _unitOfWork.Object);

    /// <summary>Phiên bản "hiện tại trong DB" của một sản phẩm, suy ra từ Id.</summary>
    private static byte[] PhienBanHienTai(int id) => [(byte)id, 0, 0];

    private static byte[] PhienBanCu(int id) => [(byte)id, 0, 99];

    private List<Product> GiaSuCoSanPham(params int[] ids)
    {
        var products = ids
            .Select(id => new Product
            {
                Id = id,
                Name = $"SP {id}",
                Price = 1_000m,
                Stock = 5,
                CategoryId = 1,
                RowVersion = PhienBanHienTai(id)
            })
            .ToList();

        _productRepository
            .Setup(r => r.GetManyForUpdateAsync(
                It.IsAny<IEnumerable<int>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(products);

        return products;
    }

    [Fact]
    public async Task Danh_sach_rong_thi_KHONG_cham_DB_va_khong_co_xung_dot()
    {
        var sut = CreateSut();

        var ketQua = await sut.BulkUpdatePriceStockAsync([]);

        Assert.Equal(0, ketQua.SoDongDaLuu);
        Assert.False(ketQua.CoXungDot);

        // Bảng rỗng là chuyện bình thường, không phải lỗi - nhưng cũng không đáng một
        // round-trip nào.
        _productRepository.Verify(
            r => r.GetManyForUpdateAsync(It.IsAny<IEnumerable<int>>(), It.IsAny<CancellationToken>()),
            Times.Never);
        _unitOfWork.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Hai_dong_cung_Id_thi_nem_ArgumentException_va_KHONG_luu_gi()
    {
        var sut = CreateSut();

        // ★ Nếu để lọt: identity map của Change Tracker cho ra MỘT object cho một khoá
        // chính, nên hai dòng cùng ghi vào đó và dòng sau đè dòng trước. Không exception,
        // không cảnh báo - người dùng thấy "đã cập nhật 2 sản phẩm" và một giá trị biến mất.
        //
        // Đây là loại DUY NHẤT còn ném exception ở method này: nó không phải kết cục
        // nghiệp vụ mà là một request không hợp lệ (form do server render không tạo ra
        // được), nên nó không thuộc về BulkUpdateResult.
        var ex = await Assert.ThrowsAsync<ArgumentException>(() =>
            sut.BulkUpdatePriceStockAsync(
            [
                new ProductBulkUpdateItem(7, 1_000m, 1, null),
                new ProductBulkUpdateItem(7, 2_000m, 2, null)
            ]));

        Assert.Contains("7", ex.Message, StringComparison.Ordinal);

        _productRepository.Verify(
            r => r.GetManyForUpdateAsync(It.IsAny<IEnumerable<int>>(), It.IsAny<CancellationToken>()),
            Times.Never);
        _unitOfWork.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Ca_danh_sach_doc_bang_DUNG_MOT_truy_van()
    {
        GiaSuCoSanPham(1, 2, 3);
        var sut = CreateSut();

        await sut.BulkUpdatePriceStockAsync(
        [
            new ProductBulkUpdateItem(1, 10m, 1, null),
            new ProductBulkUpdateItem(2, 20m, 2, null),
            new ProductBulkUpdateItem(3, 30m, 3, null)
        ]);

        // GetManyForUpdateAsync đúng MỘT lần, không phải GetForUpdateAsync ba lần.
        // Đây là khác biệt N+1 - với bảng 20 dòng là 20 round-trip thay vì 1.
        _productRepository.Verify(
            r => r.GetManyForUpdateAsync(It.IsAny<IEnumerable<int>>(), It.IsAny<CancellationToken>()),
            Times.Once);
        _productRepository.Verify(
            r => r.GetForUpdateAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()),
            Times.Never);

        // Và đúng MỘT SaveChanges. Việc cho phép thành công một phần KHÔNG được thực
        // hiện bằng cách lưu từng dòng: đó là N round-trip và N transaction, trong khi
        // lệnh kiểm trong bộ nhớ đã đủ để chọn ra dòng nào được ghi.
        _unitOfWork.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Gan_dung_gia_va_ton_kho_cho_tung_dong()
    {
        var products = GiaSuCoSanPham(1, 2);
        var sut = CreateSut();

        await sut.BulkUpdatePriceStockAsync(
        [
            new ProductBulkUpdateItem(2, 222m, 22, null),   // cố ý ĐẢO thứ tự so với
            new ProductBulkUpdateItem(1, 111m, 11, null)    // thứ tự repository trả về
        ]);

        // Ghép theo Id chứ không theo vị trí trong danh sách. Ghép theo vị trí thì giá
        // của sản phẩm này rơi vào sản phẩm khác - và cả hai vẫn là số hợp lệ nên
        // không có gì tố giác.
        Assert.Equal(111m, products.Single(p => p.Id == 1).Price);
        Assert.Equal(11, products.Single(p => p.Id == 1).Stock);
        Assert.Equal(222m, products.Single(p => p.Id == 2).Price);
        Assert.Equal(22, products.Single(p => p.Id == 2).Stock);
    }

    // ───────────── Bỏ qua CHỌN LỌC dòng xung đột ─────────────

    [Fact]
    public async Task Dong_lech_phien_ban_thi_bi_BO_QUA_dong_con_lai_van_duoc_ghi()
    {
        var products = GiaSuCoSanPham(1, 2);
        var sut = CreateSut();

        var ketQua = await sut.BulkUpdatePriceStockAsync(
        [
            new ProductBulkUpdateItem(1, 111m, 11, PhienBanCu(1)),        // lệch
            new ProductBulkUpdateItem(2, 222m, 22, PhienBanHienTai(2))    // khớp
        ]);

        // ★ Yêu cầu cốt lõi: một dòng hỏng KHÔNG chặn dòng còn lại.
        Assert.Single(ketQua.XungDot);
        Assert.Equal(1, ketQua.XungDot[0].ProductId);

        // ★★ Và đây là phần dễ làm hỏng nhất: entity của dòng xung đột phải KHÔNG bị
        // chạm. Gán Price/Stock rồi mới bỏ qua thì EF vẫn sinh câu UPDATE cho nó, câu
        // đó khớp 0 dòng, và CẢ BATCH bị revert - tức quay về đúng all-or-nothing mà
        // yêu cầu này muốn bỏ. Không có gì trong thông báo tố giác được điều đó.
        Assert.Equal(1_000m, products.Single(p => p.Id == 1).Price);
        Assert.Equal(5, products.Single(p => p.Id == 1).Stock);

        // Dòng khớp phiên bản thì được ghi bình thường.
        Assert.Equal(222m, products.Single(p => p.Id == 2).Price);

        // Dòng xung đột cũng KHÔNG được ghim phiên bản: ghim là đánh dấu nó tham gia
        // vào lần lưu này.
        _productRepository.Verify(
            r => r.SetExpectedRowVersion(It.Is<Product>(p => p.Id == 1), It.IsAny<byte[]>()),
            Times.Never);
    }

    [Fact]
    public async Task Dong_xung_dot_bao_lai_kem_TEN_va_gia_tri_HIEN_TAI()
    {
        GiaSuCoSanPham(1);
        var sut = CreateSut();

        var ketQua = await sut.BulkUpdatePriceStockAsync(
            [new ProductBulkUpdateItem(1, 111m, 11, PhienBanCu(1))]);

        var xungDot = Assert.Single(ketQua.XungDot);

        // Giá trị HIỆN TẠI trong DB, không phải giá trị người dùng vừa gõ (111/11) -
        // thứ họ vẫn đang nhìn thấy trên màn hình. Câu hỏi của họ lúc này là "người kia
        // đã đổi thành gì".
        Assert.Equal("SP 1", xungDot.ProductName);
        Assert.Equal(1_000m, xungDot.PriceHienTai);
        Assert.Equal(5, xungDot.StockHienTai);

        // Phiên bản mới đi kèm để tầng trên nạp lại vào form - thiếu nó thì người dùng
        // bấm Lưu bao nhiêu lần cũng nhận đúng một lỗi.
        Assert.Equal(PhienBanHienTai(1), xungDot.RowVersionHienTai);
        Assert.False(xungDot.DaBiXoa);
    }

    [Fact]
    public async Task San_pham_da_bi_xoa_cung_la_mot_xung_dot_KHONG_nem_exception()
    {
        GiaSuCoSanPham(1);   // repository chỉ trả về sản phẩm 1
        var sut = CreateSut();

        var ketQua = await sut.BulkUpdatePriceStockAsync(
        [
            new ProductBulkUpdateItem(1, 111m, 11, PhienBanHienTai(1)),
            new ProductBulkUpdateItem(999, 222m, 22, PhienBanHienTai(99))
        ]);

        // Cùng LOẠI với RowVersion lệch - "người khác đã thay đổi thứ bạn đang nhìn" -
        // nên đi chung một đường báo cáo. Ném exception ở đây là để một sản phẩm bị
        // người khác xoá chặn cả 19 dòng còn lại, đúng thứ yêu cầu này muốn bỏ.
        var xungDot = Assert.Single(ketQua.XungDot);
        Assert.Equal(999, xungDot.ProductId);
        Assert.True(xungDot.DaBiXoa);

        _unitOfWork.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    // ───────────── Lớp bảo đảm thứ hai (cửa sổ hẹp) ─────────────

    [Fact]
    public async Task Dong_duoc_ghi_VAN_phai_ghim_RowVersion_rieng_cho_tung_dong()
    {
        var products = GiaSuCoSanPham(1, 2);
        var daGhim = new Dictionary<int, byte[]>();

        _productRepository
            .Setup(r => r.SetExpectedRowVersion(It.IsAny<Product>(), It.IsAny<byte[]>()))
            .Callback<Product, byte[]>((p, rv) => daGhim[p.Id] = rv);

        var sut = CreateSut();

        await sut.BulkUpdatePriceStockAsync(
        [
            new ProductBulkUpdateItem(1, 111m, 11, PhienBanHienTai(1)),
            new ProductBulkUpdateItem(2, 222m, 22, PhienBanHienTai(2))
        ]);

        // ★ Lệnh so sánh trong bộ nhớ ở trên là TOCTOU: giữa lúc so và lúc ghi vẫn còn
        // một khe vài mili giây. Ghim phiên bản mong đợi là thứ kẹp nó vào WHERE dưới
        // DB - bảo đảm THẬT. Bỏ lớp này thì test hành vi vẫn xanh (khe quá hẹp để test
        // nào chạm tới), nên nó chỉ được canh ở đây.
        Assert.Equal(PhienBanHienTai(1), daGhim[1]);
        Assert.Equal(PhienBanHienTai(2), daGhim[2]);
        Assert.Equal(2, products.Count);
    }

    [Fact]
    public async Task RowVersion_null_thi_BO_QUA_kiem_tra_xung_dot()
    {
        GiaSuCoSanPham(1);
        var sut = CreateSut();

        var ketQua = await sut.BulkUpdatePriceStockAsync(
            [new ProductBulkUpdateItem(1, 111m, 11, null)]);

        // Cùng quy ước với UpdateAsync: null = luồng nội bộ không có form (job, seed).
        // Là chủ ý, không phải quên. Không ghim, và cũng không bị coi là xung đột.
        Assert.False(ketQua.CoXungDot);
        _productRepository.Verify(
            r => r.SetExpectedRowVersion(It.IsAny<Product>(), It.IsAny<byte[]>()),
            Times.Never);
    }

    [Fact]
    public async Task So_dong_da_luu_lay_tu_SaveChanges_khong_phai_dem_item()
    {
        GiaSuCoSanPham(1, 2, 3);
        _unitOfWork
            .Setup(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(2);
        var sut = CreateSut();

        var ketQua = await sut.BulkUpdatePriceStockAsync(
        [
            new ProductBulkUpdateItem(1, 10m, 1, null),
            new ProductBulkUpdateItem(2, 20m, 2, null),
            new ProductBulkUpdateItem(3, 30m, 3, null)
        ]);

        // Gửi lên 3 dòng mà chỉ đổi 2 thì thông báo phải nói 2. Đếm theo items.Count là
        // nói với người dùng rằng một việc đã xảy ra trong khi nó không xảy ra.
        Assert.Equal(2, ketQua.SoDongDaLuu);
    }
}
