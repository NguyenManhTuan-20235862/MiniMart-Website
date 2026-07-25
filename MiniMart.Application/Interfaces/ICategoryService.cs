using MiniMart.Domain.Entities;

namespace MiniMart.Application.Interfaces;

public interface ICategoryService
{
    Task<List<Category>> GetAllAsync(CancellationToken cancellationToken = default);

    Task<Category?> GetByIdAsync(int id, CancellationToken cancellationToken = default);

    /// <summary>Ném <see cref="Common.Exceptions.CategoryNameAlreadyExistsException"/> nếu trùng tên.</summary>
    Task<Category> CreateAsync(string name, CancellationToken cancellationToken = default);

    Task UpdateAsync(int id, string name, CancellationToken cancellationToken = default);

    /// <summary>
    /// Ném <see cref="Common.Exceptions.CategoryHasProductsException"/> nếu danh mục
    /// còn sản phẩm.
    /// </summary>
    Task DeleteAsync(int id, CancellationToken cancellationToken = default);
}
