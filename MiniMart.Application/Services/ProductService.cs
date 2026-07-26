using MiniMart.Application.Interfaces;
using MiniMart.Common;
using MiniMart.Common.Exceptions;
using MiniMart.Domain.Entities;
using MiniMart.Domain.Interfaces;

namespace MiniMart.Application.Services;

public class ProductService : IProductService
{
    private readonly IProductRepository _productRepository;
    private readonly ICategoryRepository _categoryRepository;
    private readonly IUnitOfWork _unitOfWork;

    // Service được phép phối hợp NHIỀU repository - đó chính là lý do quy tắc
    // liên quan tới hai bảng phải nằm ở đây chứ không nằm trong repository nào.
    public ProductService(
        IProductRepository productRepository,
        ICategoryRepository categoryRepository,
        IUnitOfWork unitOfWork)
    {
        _productRepository = productRepository;
        _categoryRepository = categoryRepository;
        _unitOfWork = unitOfWork;
    }

    public Task<PagedResult<Product>> GetProductsAsync(
        int? categoryId = null,
        decimal? minPrice = null,
        decimal? maxPrice = null,
        int page = 1,
        int pageSize = 12,
        CancellationToken cancellationToken = default) =>
        _productRepository.GetProductsAsync(
            categoryId, minPrice, maxPrice, page, pageSize, cancellationToken);

    public Task<List<Product>> GetAllAsync(CancellationToken cancellationToken = default) =>
        _productRepository.GetAllAsync(cancellationToken);

    public Task<List<Product>> GetByCategoryAsync(
        int categoryId,
        CancellationToken cancellationToken = default) =>
        _productRepository.GetByCategoryAsync(categoryId, cancellationToken);

    public Task<Product?> GetByIdAsync(int id, CancellationToken cancellationToken = default) =>
        _productRepository.GetByIdAsync(id, cancellationToken);

    public async Task<Product> CreateAsync(
        string name,
        decimal price,
        int stock,
        int categoryId,
        string? imageUrl = null,
        CancellationToken cancellationToken = default)
    {
        await BaoDamDanhMucTonTaiAsync(categoryId, cancellationToken);

        var product = new Product
        {
            Name = name.Trim(),
            Price = price,
            Stock = stock,
            CategoryId = categoryId,
            ImageUrl = imageUrl
        };

        await _productRepository.AddAsync(product, cancellationToken);
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        return product;
    }

    public async Task UpdateAsync(
        int id,
        string name,
        decimal price,
        int stock,
        int categoryId,
        string? imageUrl = null,
        byte[]? rowVersion = null,
        CancellationToken cancellationToken = default)
    {
        // GetForUpdateAsync (có tracking) chứ không phải GetByIdAsync: entity
        // phải được Change Tracker theo dõi thì RowVersion gốc mới được giữ để
        // kẹp vào WHERE lúc UPDATE.
        var product = await _productRepository.GetForUpdateAsync(id, cancellationToken)
            ?? throw new NotFoundException(nameof(Product), id);

        // Ghim phiên bản người dùng thấy lúc mở form. Không có bước này thì
        // phiên bản được so là phiên bản VỪA ĐỌC ở dòng trên - luôn khớp, nên
        // xung đột không bao giờ bị phát hiện (lost update).
        if (rowVersion is { Length: > 0 })
        {
            _productRepository.SetExpectedRowVersion(product, rowVersion);
        }

        await BaoDamDanhMucTonTaiAsync(categoryId, cancellationToken);

        product.Name = name.Trim();
        product.Price = price;
        product.Stock = stock;
        product.CategoryId = categoryId;

        // Chỉ ghi đè khi có ảnh mới. imageUrl = null nghĩa là người dùng không
        // chọn file, phải GIỮ ảnh cũ chứ không phải xoá nó.
        if (imageUrl is not null)
        {
            product.ImageUrl = imageUrl;
        }

        // Xung đột RowVersion nổi lên từ đây dưới dạng ConcurrencyConflictException
        // (UnitOfWork đã dịch từ DbUpdateConcurrencyException). CỐ Ý không bắt ở
        // Service: quyết định "ghi đè hay bỏ" là của người dùng, không phải của
        // nghiệp vụ - Controller mới có đủ ngữ cảnh để hỏi lại họ.
        await _unitOfWork.SaveChangesAsync(cancellationToken);
    }

    public async Task DeleteAsync(int id, CancellationToken cancellationToken = default)
    {
        var product = await _productRepository.GetForUpdateAsync(id, cancellationToken)
            ?? throw new NotFoundException(nameof(Product), id);

        _productRepository.Remove(product);

        await _unitOfWork.SaveChangesAsync(cancellationToken);
    }

    /// <summary>
    /// Không dựa vào lỗi khoá ngoại của DB: thông báo "FK constraint violated"
    /// vô nghĩa với người dùng cuối.
    /// </summary>
    private async Task BaoDamDanhMucTonTaiAsync(int categoryId, CancellationToken cancellationToken)
    {
        if (await _categoryRepository.GetByIdAsync(categoryId, cancellationToken) is null)
        {
            throw new NotFoundException(nameof(Category), categoryId);
        }
    }
}
