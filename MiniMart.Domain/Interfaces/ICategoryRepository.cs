using MiniMart.Domain.Entities;

namespace MiniMart.Domain.Interfaces;

public interface ICategoryRepository
{
    Task<List<Category>> GetAllAsync(CancellationToken cancellationToken = default);

    Task<Category?> GetByIdAsync(int id, CancellationToken cancellationToken = default);

    Task<Category?> GetForUpdateAsync(int id, CancellationToken cancellationToken = default);

    Task<bool> ExistsByNameAsync(string name, int? excludeId = null, CancellationToken cancellationToken = default);

    /// <summary>
    /// Cần cho luồng xoá: FK đặt DeleteBehavior.Restrict nên xoá danh mục còn
    /// sản phẩm sẽ văng DbUpdateException. Kiểm tra trước để báo lỗi tử tế.
    /// </summary>
    Task<bool> HasProductsAsync(int categoryId, CancellationToken cancellationToken = default);

    Task AddAsync(Category category, CancellationToken cancellationToken = default);

    void Remove(Category category);
}
