using MiniMart.Domain.Entities;

namespace MiniMart.Domain.Interfaces;

public interface IProductRepository
{
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
