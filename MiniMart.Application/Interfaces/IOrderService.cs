using MiniMart.Application.Models;

namespace MiniMart.Application.Interfaces;

public interface IOrderService
{
    /// <summary>
    /// Chốt giỏ hàng thành đơn hàng: trừ tồn kho từng sản phẩm, tạo <c>Order</c> +
    /// <c>OrderDetail</c> với giá đã snapshot, rồi xoá giỏ. Tất cả trong MỘT
    /// transaction - hoặc thành công trọn vẹn, hoặc không có gì xảy ra.
    /// </summary>
    /// <param name="userId">
    /// Chủ đơn hàng. BẮT BUỘC lấy từ <c>ICurrentUser.Id</c> ở tầng Web, TUYỆT ĐỐI
    /// không nhận từ form hay query string: nhận từ request là mở đúng một lỗ IDOR -
    /// đặt đơn và trừ tồn kho dưới tên người khác.
    /// </param>
    /// <exception cref="Common.Exceptions.EmptyCartException">Giỏ rỗng.</exception>
    /// <exception cref="Common.Exceptions.NotFoundException">
    /// Sản phẩm trong giỏ đã bị xoá khỏi shop.
    /// </exception>
    /// <exception cref="Common.Exceptions.InsufficientStockException">
    /// Không đủ tồn kho - do đọc lên đã thiếu, HOẶC do người khác mua trước làm
    /// <c>RowVersion</c> lệch lúc ghi. Hai nguyên nhân, một exception, vì với người
    /// dùng chúng là cùng một chuyện.
    /// </exception>
    Task<CheckoutResult> CheckoutAsync(int userId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Một đơn hàng CỦA CHÍNH người này. Trả <c>null</c> nếu đơn không tồn tại
    /// <b>hoặc</b> thuộc người khác - hai trường hợp cố ý không phân biệt được từ
    /// bên ngoài, vì phân biệt được là để lộ "đơn số 42 có tồn tại".
    /// </summary>
    /// <param name="userId">
    /// Như <see cref="CheckoutAsync"/>: BẮT BUỘC từ <c>ICurrentUser.Id</c>. Đây là
    /// tham số duy nhất chặn việc đọc đơn hàng người khác, nên nhận nó từ request
    /// là mở thẳng một lỗ IDOR.
    /// </param>
    Task<OrderView?> GetMyOrderAsync(
        int orderId,
        int userId,
        CancellationToken cancellationToken = default);
}
