using MiniMart.Domain.Entities;

namespace MiniMart.Domain.Interfaces;

public interface IOrderRepository
{
    /// <summary>
    /// Thêm đơn hàng kèm toàn bộ <c>Items</c> vào Change Tracker.
    /// KHÔNG lưu - <c>IUnitOfWork.SaveChangesAsync</c> mới lưu.
    /// </summary>
    Task AddAsync(Order order, CancellationToken cancellationToken = default);

    /// <summary>
    /// Một đơn theo Id, CÓ tracking - dùng cho đường GHI (kênh IPN đổi trạng thái).
    ///
    /// <para>
    /// Cố ý KHÔNG có tham số <c>userId</c>, khác <see cref="GetByIdForUserAsync"/>.
    /// Người gọi là IPN - request từ máy chủ VNPay, không có người dùng đăng nhập nào
    /// để lọc theo. Thứ xác thực request đó là chữ ký HMAC, và việc kiểm nó phải xảy
    /// ra TRƯỚC khi gọi vào đây.
    /// </para>
    /// <para>
    /// ⚠ Vì vậy method này TUYỆT ĐỐI không được dùng cho bất kỳ endpoint nào nhận
    /// <c>orderId</c> từ người dùng - đó sẽ là IDOR ngay lập tức. Đường đọc đơn của
    /// người dùng là <see cref="GetByIdForUserAsync"/>.
    /// </para>
    /// </summary>
    Task<Order?> GetForUpdateAsync(int orderId, CancellationToken cancellationToken = default);

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
