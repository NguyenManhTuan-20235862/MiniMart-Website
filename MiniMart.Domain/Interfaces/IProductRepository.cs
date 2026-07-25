using MiniMart.Common;
using MiniMart.Domain.Entities;

namespace MiniMart.Domain.Interfaces;

public interface IProductRepository
{
    /// <summary>
    /// Danh sách sản phẩm có lọc và phân trang. Mọi tham số lọc đều tuỳ chọn:
    /// null nghĩa là không áp dụng điều kiện đó.
    /// </summary>
    Task<PagedResult<Product>> GetProductsAsync(
        int? categoryId = null,
        decimal? minPrice = null,
        decimal? maxPrice = null,
        int page = 1,
        int pageSize = 12,
        CancellationToken cancellationToken = default);

    /// <summary>Danh sách để hiển thị - kèm Category, không theo dõi thay đổi.</summary>
    Task<List<Product>> GetAllAsync(CancellationToken cancellationToken = default);

    Task<List<Product>> GetByCategoryAsync(int categoryId, CancellationToken cancellationToken = default);

    /// <summary>Bản chỉ đọc dùng cho trang chi tiết.</summary>
    Task<Product?> GetByIdAsync(int id, CancellationToken cancellationToken = default);

    /// <summary>
    /// Bản CÓ theo dõi thay đổi, dùng cho luồng sửa. Phải tách khỏi GetByIdAsync
    /// vì entity AsNoTracking sửa xong gọi SaveChanges sẽ không lưu được gì.
    /// </summary>
    Task<Product?> GetForUpdateAsync(int id, CancellationToken cancellationToken = default);

    Task AddAsync(Product product, CancellationToken cancellationToken = default);

    void Remove(Product product);
}
