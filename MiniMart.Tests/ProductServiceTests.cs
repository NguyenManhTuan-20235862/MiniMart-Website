using MiniMart.Application.Services;
using MiniMart.Common.Exceptions;
using MiniMart.Domain.Entities;
using MiniMart.Domain.Interfaces;
using Moq;

namespace MiniMart.Tests;

public class ProductServiceTests
{
    private readonly Mock<IProductRepository> _productRepository = new();
    private readonly Mock<ICategoryRepository> _categoryRepository = new();
    private readonly Mock<IUnitOfWork> _unitOfWork = new();

    private ProductService CreateSut() =>
        new(_productRepository.Object, _categoryRepository.Object, _unitOfWork.Object);

    private void GiaSuTonTaiDanhMuc(int id) =>
        _categoryRepository
            .Setup(r => r.GetByIdAsync(id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Category { Id = id, Name = "Laptop" });

    private void GiaSuTonTaiSanPham(int id) =>
        _productRepository
            .Setup(r => r.GetForUpdateAsync(id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Product { Id = id, Name = "Cu", Price = 1m, Stock = 1, CategoryId = 1 });

    [Fact]
    public async Task CreateAsync_danh_muc_khong_ton_tai_thi_nem_NotFound()
    {
        _categoryRepository
            .Setup(r => r.GetByIdAsync(99, It.IsAny<CancellationToken>()))
            .ReturnsAsync((Category?)null);
        var sut = CreateSut();

        // Kiểm tra chủ động thay vì để lỗi khoá ngoại của DB văng ra - thông báo
        // "FK constraint violated" vô nghĩa với người dùng cuối.
        await Assert.ThrowsAsync<NotFoundException>(
            () => sut.CreateAsync("San pham", 100m, 5, categoryId: 99));

        _unitOfWork.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task CreateAsync_hop_le_thi_luu_dung_du_lieu()
    {
        GiaSuTonTaiDanhMuc(1);
        Product? daLuu = null;
        _productRepository
            .Setup(r => r.AddAsync(It.IsAny<Product>(), It.IsAny<CancellationToken>()))
            .Callback<Product, CancellationToken>((p, _) => daLuu = p);
        var sut = CreateSut();

        await sut.CreateAsync("  Laptop X1  ", 25_000_000m, 3, categoryId: 1);

        Assert.Equal("Laptop X1", daLuu?.Name);
        Assert.Equal(25_000_000m, daLuu?.Price);
        Assert.Equal(3, daLuu?.Stock);
        Assert.Equal(1, daLuu?.CategoryId);
    }

    [Fact]
    public async Task UpdateAsync_phai_dung_ban_CO_tracking()
    {
        GiaSuTonTaiSanPham(1);
        GiaSuTonTaiDanhMuc(1);
        var sut = CreateSut();

        await sut.UpdateAsync(1, "Moi", 200m, 7, categoryId: 1);

        // Dùng nhầm GetByIdAsync (AsNoTracking) thì SaveChanges không lưu gì,
        // và RowVersion gốc cũng mất -> mất luôn khả năng phát hiện xung đột.
        _productRepository.Verify(
            r => r.GetForUpdateAsync(1, It.IsAny<CancellationToken>()), Times.Once);
        _productRepository.Verify(
            r => r.GetByIdAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task UpdateAsync_doi_sang_danh_muc_khong_ton_tai_thi_nem_NotFound()
    {
        GiaSuTonTaiSanPham(1);
        _categoryRepository
            .Setup(r => r.GetByIdAsync(99, It.IsAny<CancellationToken>()))
            .ReturnsAsync((Category?)null);
        var sut = CreateSut();

        await Assert.ThrowsAsync<NotFoundException>(
            () => sut.UpdateAsync(1, "Moi", 200m, 7, categoryId: 99));

        _unitOfWork.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task DeleteAsync_khong_tim_thay_thi_nem_NotFound()
    {
        _productRepository
            .Setup(r => r.GetForUpdateAsync(99, It.IsAny<CancellationToken>()))
            .ReturnsAsync((Product?)null);
        var sut = CreateSut();

        await Assert.ThrowsAsync<NotFoundException>(() => sut.DeleteAsync(99));
    }
}
