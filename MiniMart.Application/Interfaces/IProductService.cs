using MiniMart.Domain.Entities;

namespace MiniMart.Application.Interfaces;

public interface IProductService
{
    Task<List<Product>> GetAllAsync(CancellationToken cancellationToken = default);

    Task<List<Product>> GetByCategoryAsync(int categoryId, CancellationToken cancellationToken = default);

    Task<Product?> GetByIdAsync(int id, CancellationToken cancellationToken = default);

    Task<Product> CreateAsync(
        string name,
        decimal price,
        int stock,
        int categoryId,
        string? imageUrl = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Ném DbUpdateConcurrencyException nếu bản ghi đã bị người khác sửa
    /// (RowVersion lệch). Việc xử lý xung đột thuộc phase Concurrency.
    /// </summary>
    /// <param name="imageUrl">
    /// null nghĩa là GIỮ NGUYÊN ảnh cũ, không phải xoá ảnh. Người dùng không
    /// chọn file mới thì ảnh hiện tại phải được giữ lại.
    /// </param>
    Task UpdateAsync(
        int id,
        string name,
        decimal price,
        int stock,
        int categoryId,
        string? imageUrl = null,
        CancellationToken cancellationToken = default);

    Task DeleteAsync(int id, CancellationToken cancellationToken = default);
}
