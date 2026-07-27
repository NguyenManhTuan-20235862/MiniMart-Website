namespace MiniMart.Common.Exceptions;

/// <summary>
/// Cố đổi trạng thái đơn sang một giá trị không hợp lệ từ trạng thái hiện tại.
///
/// <para>
/// Đây là lỗi LẬP TRÌNH, không phải lỗi người dùng: mọi đường đi hợp lệ đều đã kiểm
/// trạng thái trước khi gọi. Ném ra để nó nổ to thay vì âm thầm ghi đè - một đơn từ
/// <c>Paid</c> quay về <c>Pending</c> là mất dấu một khoản tiền đã thu.
/// </para>
/// </summary>
public class InvalidOrderStatusTransitionException : Exception
{
    public InvalidOrderStatusTransitionException(int orderId, string tuTrangThai, string sangTrangThai)
        : base($"Đơn hàng {orderId} không thể chuyển từ '{tuTrangThai}' sang '{sangTrangThai}'.")
    {
        OrderId = orderId;
        TuTrangThai = tuTrangThai;
        SangTrangThai = sangTrangThai;
    }

    public int OrderId { get; }

    public string TuTrangThai { get; }

    public string SangTrangThai { get; }
}
