using MiniMart.Application.Services;
using MiniMart.Common.Exceptions;
using MiniMart.Domain.Entities;
using MiniMart.Domain.Interfaces;
using Moq;

namespace MiniMart.Tests;

/// <summary>
/// Quy tắc nghiệp vụ test được mà không cần database, không cần HttpContext.
/// Đó là hệ quả trực tiếp của việc đặt quy tắc ở Service: nếu nó nằm trong
/// Controller thì phải dựng HttpContext/ModelState, nếu nằm trong Repository
/// thì phải có SQL Server chạy kèm.
/// </summary>
public class CategoryServiceTests
{
    private readonly Mock<ICategoryRepository> _repository = new();
    private readonly Mock<IUnitOfWork> _unitOfWork = new();

    private CategoryService CreateSut() => new(_repository.Object, _unitOfWork.Object);

    private void GiaSuTonTaiDanhMuc(int id, string name = "Laptop") =>
        _repository
            .Setup(r => r.GetForUpdateAsync(id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Category { Id = id, Name = name });

    private void GiaSuCoSanPham(int categoryId, bool coSanPham) =>
        _repository
            .Setup(r => r.HasProductsAsync(categoryId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(coSanPham);

    // ───────────── Quy tắc trọng tâm ─────────────

    [Fact]
    public async Task DeleteAsync_danh_muc_con_san_pham_thi_nem_exception()
    {
        GiaSuTonTaiDanhMuc(1);
        GiaSuCoSanPham(1, true);
        var sut = CreateSut();

        var ex = await Assert.ThrowsAsync<CategoryHasProductsException>(
            () => sut.DeleteAsync(1));

        Assert.Equal(1, ex.CategoryId);
    }

    [Fact]
    public async Task DeleteAsync_bi_chan_thi_khong_duoc_dung_toi_du_lieu()
    {
        GiaSuTonTaiDanhMuc(1);
        GiaSuCoSanPham(1, true);
        var sut = CreateSut();

        await Assert.ThrowsAsync<CategoryHasProductsException>(() => sut.DeleteAsync(1));

        // Quan trọng hơn việc ném đúng exception: phải dừng TRƯỚC khi thay đổi
        // bất cứ thứ gì. Ném exception sau khi đã Remove là hỏng nửa vời.
        _repository.Verify(r => r.Remove(It.IsAny<Category>()), Times.Never);
        _unitOfWork.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task DeleteAsync_danh_muc_rong_thi_xoa_binh_thuong()
    {
        GiaSuTonTaiDanhMuc(1);
        GiaSuCoSanPham(1, false);
        var sut = CreateSut();

        await sut.DeleteAsync(1);

        _repository.Verify(r => r.Remove(It.IsAny<Category>()), Times.Once);
        _unitOfWork.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task DeleteAsync_khong_tim_thay_thi_nem_NotFound()
    {
        _repository
            .Setup(r => r.GetForUpdateAsync(99, It.IsAny<CancellationToken>()))
            .ReturnsAsync((Category?)null);
        var sut = CreateSut();

        await Assert.ThrowsAsync<NotFoundException>(() => sut.DeleteAsync(99));
    }

    // ───────────── Tạo & sửa ─────────────

    [Fact]
    public async Task CreateAsync_trung_ten_thi_nem_exception_va_khong_luu()
    {
        _repository
            .Setup(r => r.ExistsByNameAsync("Laptop", null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
        var sut = CreateSut();

        await Assert.ThrowsAsync<CategoryNameAlreadyExistsException>(
            () => sut.CreateAsync("Laptop"));

        _unitOfWork.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task CreateAsync_phai_cat_khoang_trang_thua()
    {
        Category? daLuu = null;
        _repository
            .Setup(r => r.AddAsync(It.IsAny<Category>(), It.IsAny<CancellationToken>()))
            .Callback<Category, CancellationToken>((c, _) => daLuu = c);
        var sut = CreateSut();

        await sut.CreateAsync("  Laptop  ");

        // Không trim thì "Laptop" và "Laptop " là hai danh mục khác nhau dưới DB.
        Assert.Equal("Laptop", daLuu?.Name);
    }

    [Fact]
    public async Task CreateAsync_gap_DuplicateKey_phai_doi_thanh_loi_nghiep_vu()
    {
        _unitOfWork
            .Setup(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()))
            .ThrowsAsync(new DuplicateKeyException(new Exception("unique index")));
        var sut = CreateSut();

        // Khe TOCTOU: kiểm tra xong mới có request khác chèn cùng tên.
        await Assert.ThrowsAsync<CategoryNameAlreadyExistsException>(
            () => sut.CreateAsync("Laptop"));
    }

    [Fact]
    public async Task UpdateAsync_giu_nguyen_ten_cu_thi_khong_bi_bao_trung()
    {
        GiaSuTonTaiDanhMuc(1, "Laptop");
        // excludeId được truyền đúng thì repository trả về false.
        _repository
            .Setup(r => r.ExistsByNameAsync("Laptop", 1, It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);
        var sut = CreateSut();

        await sut.UpdateAsync(1, "Laptop");

        _unitOfWork.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }
}
