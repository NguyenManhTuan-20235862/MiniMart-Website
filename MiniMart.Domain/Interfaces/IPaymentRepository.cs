using MiniMart.Domain.Entities;

namespace MiniMart.Domain.Interfaces;

public interface IPaymentRepository
{
    /// <summary>
    /// Thêm bản ghi thanh toán vào Change Tracker. KHÔNG lưu -
    /// <c>IUnitOfWork.SaveChangesAsync</c> mới lưu.
    /// </summary>
    Task AddAsync(Payment payment, CancellationToken cancellationToken = default);

    /// <summary>Bản ghi thanh toán của một đơn, chỉ đọc. <c>null</c> nếu chưa có.</summary>
    Task<Payment?> GetByOrderIdAsync(int orderId, CancellationToken cancellationToken = default);
}
