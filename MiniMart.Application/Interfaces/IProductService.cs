using MiniMart.Common;
using MiniMart.Domain.Entities;

namespace MiniMart.Application.Interfaces;

public interface IProductService
{
    /// <summary>
    /// Danh sách sản phẩm cho trang khách hàng: có lọc và phân trang.
    /// Hiện là pass-through xuống Repository, nhưng vẫn phải đi qua Service vì
    /// đây là chỗ các quy tắc nghiệp vụ sẽ được thêm vào (ẩn sản phẩm hết hàng,
    /// giá theo nhóm khách...) mà không phải sửa Controller.
    /// </summary>
    Task<PagedResult<Product>> GetProductsAsync(
        int? categoryId = null,
        decimal? minPrice = null,
        decimal? maxPrice = null,
        int page = 1,
        int pageSize = 12,
        CancellationToken cancellationToken = default);

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
