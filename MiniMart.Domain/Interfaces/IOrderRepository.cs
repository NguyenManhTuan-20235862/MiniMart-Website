using MiniMart.Common;
using MiniMart.Domain.Entities;
using MiniMart.Domain.ValueObjects;

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

    /// <summary>
    /// Danh sách đơn của MỘT người, mới nhất trước, có phân trang.
    ///
    /// <para>
    /// Trả <see cref="OrderSummary"/> chứ không <see cref="Order"/>: trang danh sách
    /// chỉ cần tổng tiền, trạng thái và số món. Trả entity thì hoặc thiếu
    /// <c>Include(o =&gt; o.Items)</c> (số món luôn bằng 0, im lặng), hoặc có
    /// <c>Include</c> và kéo về hàng trăm dòng <c>OrderDetail</c> để đếm rồi vứt.
    /// </para>
    /// <para>
    /// <c>userId</c> là tham số BẮT BUỘC, không nullable và không có giá trị mặc định:
    /// không tồn tại cách gọi "lấy tất cả đơn" qua đường này. Đó là chống IDOR bằng
    /// CẤU TRÚC - cùng cách giỏ hàng chỉ nhận <c>productId</c>.
    /// </para>
    /// </summary>
    Task<PagedResult<OrderSummary>> GetPagedForUserAsync(
        int userId,
        int page = 1,
        int pageSize = 10,
        CancellationToken cancellationToken = default);

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
