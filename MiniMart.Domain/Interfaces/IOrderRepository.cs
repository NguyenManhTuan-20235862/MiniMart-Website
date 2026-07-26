using MiniMart.Domain.Entities;

namespace MiniMart.Domain.Interfaces;

public interface IOrderRepository
{
    /// <summary>
    /// Thêm đơn hàng kèm toàn bộ <c>Items</c> vào Change Tracker.
    /// KHÔNG lưu - <c>IUnitOfWork.SaveChangesAsync</c> mới lưu.
    /// </summary>
    Task AddAsync(Order order, CancellationToken cancellationToken = default);

    /// <summary>Một đơn kèm các dòng, chỉ đọc. Trả null nếu đơn không thuộc người này.</summary>
    Task<Order?> GetByIdForUserAsync(
        int orderId,
        int userId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Sản phẩm này đã từng nằm trong đơn hàng nào chưa - dùng để chặn xoá TRƯỚC khi
    /// khoá ngoại Restrict chặn, để có thông báo tử tế thay vì lỗi 547 của SQL Server.
    /// </summary>
    Task<bool> HasOrdersForProductAsync(
        int productId,
        CancellationToken cancellationToken = default);
}
