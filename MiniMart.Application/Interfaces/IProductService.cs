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
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Ném DbUpdateConcurrencyException nếu bản ghi đã bị người khác sửa
    /// (RowVersion lệch). Việc xử lý xung đột thuộc phase Concurrency.
    /// </summary>
    Task UpdateAsync(
        int id,
        string name,
        decimal price,
        int stock,
        int categoryId,
        CancellationToken cancellationToken = default);

    Task DeleteAsync(int id, CancellationToken cancellationToken = default);
}
