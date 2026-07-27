using MiniMart.Application.Models;
using MiniMart.Common;
using MiniMart.Domain.Enums;
using MiniMart.Domain.ValueObjects;

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
    /// <param name="shipping">
    /// Người nhận và nơi giao. KHÁC <paramref name="userId"/> ở một điểm cốt lõi: đây
    /// là dữ liệu người dùng TỰ KHAI về chính đơn của họ, nên nó BẮT BUỘC đến từ form.
    /// Nhận nó từ request không mở lỗ IDOR nào - nó không quyết định đơn thuộc về ai,
    /// chỉ nói hàng giao tới đâu.
    /// </param>
    /// <exception cref="ArgumentException">
    /// <paramref name="shipping"/> thiếu trường. Là <c>ArgumentException</c> chứ không
    /// phải exception nghiệp vụ vì tầng Web đã chặn bằng <c>[Required]</c> - tới được
    /// đây nghĩa là lỗi LẬP TRÌNH, và phải nổ to thay vì lưu một đơn không giao được.
    /// </exception>
    /// <exception cref="Common.Exceptions.EmptyCartException">Giỏ rỗng.</exception>
    /// <exception cref="Common.Exceptions.NotFoundException">
    /// Sản phẩm trong giỏ đã bị xoá khỏi shop.
    /// </exception>
    /// <exception cref="Common.Exceptions.InsufficientStockException">
    /// Không đủ tồn kho - do đọc lên đã thiếu, HOẶC do người khác mua trước làm
    /// <c>RowVersion</c> lệch lúc ghi. Hai nguyên nhân, một exception, vì với người
    /// dùng chúng là cùng một chuyện.
    /// </exception>
    Task<CheckoutResult> CheckoutAsync(
        int userId,
        ShippingInfo shipping,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Đổi trạng thái thanh toán của một đơn, kèm kiểm tra chuyển trạng thái hợp lệ.
    ///
    /// <para>
    /// ★ <b>KHÔNG gọi <c>SaveChangesAsync</c></b>, khác mọi method public khác của
    /// tầng Application. Đây là chủ ý, không phải quên - đừng "sửa" bằng cách thêm vào.
    /// </para>
    /// <para>
    /// Lý do: người gọi duy nhất là luồng IPN, và ở đó việc đổi trạng thái đơn phải
    /// nguyên tử với việc ghi bản ghi <c>Payment</c>. Nếu method này tự lưu thì có HAI
    /// <c>SaveChanges</c>, và giữa chúng tồn tại một khoảnh khắc đơn đã <c>Paid</c> mà
    /// chưa có bản ghi thanh toán nào - tiền đã thu mà không tra được từ đâu. Để người
    /// gọi quyết định ranh giới transaction là cách duy nhất giữ hai thay đổi đó trong
    /// cùng một lần ghi.
    /// </para>
    /// <para>
    /// Đây cũng là nơi DUY NHẤT trong hệ thống được phép ghi <c>Order.Status</c>. Gom
    /// về một chỗ để luật chuyển trạng thái có đúng một nơi để sống.
    /// </para>
    /// </summary>
    /// <exception cref="Common.Exceptions.NotFoundException">Không có đơn với id này.</exception>
    /// <exception cref="Common.Exceptions.InvalidOrderStatusTransitionException">
    /// Chuyển trạng thái không hợp lệ (VD <c>Paid</c> -> <c>Pending</c>).
    /// </exception>
    Task UpdatePaymentStatusAsync(
        int orderId,
        OrderStatus status,
        CancellationToken cancellationToken = default);

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

    /// <summary>
    /// Danh sách đơn CỦA CHÍNH người này, mới nhất trước, có phân trang.
    ///
    /// <para>
    /// Hiện là pass-through xuống Repository, và vẫn phải đi qua Service vì lý do
    /// giống <c>IProductService.GetProductsAsync</c>: đây là chỗ các quy tắc nghiệp vụ
    /// sẽ được thêm vào (ẩn đơn đã huỷ, gộp đơn định kỳ) mà không phải sửa Controller.
    /// </para>
    /// </summary>
    /// <param name="userId">
    /// BẮT BUỘC từ <c>ICurrentUser.Id</c>, không bao giờ từ route hay query string.
    /// </param>
    Task<PagedResult<OrderSummary>> GetMyOrdersAsync(
        int userId,
        int page = 1,
        int pageSize = 10,
        CancellationToken cancellationToken = default);
}
