namespace MiniMart.Domain.Enums;

/// <summary>
/// Trạng thái thanh toán của đơn hàng.
///
/// <para>
/// Cột này được hoãn từ đầu dự án với một điều kiện rõ ràng: "thêm cột trạng thái
/// trước khi biết đơn có những trạng thái nào là đoán". Điều kiện đó đã được thoả -
/// kênh IPN là nghiệp vụ đầu tiên thật sự cần ghi vào nó, và nó cần đúng hai giá trị.
/// </para>
/// <para>
/// CỐ Ý chưa có <c>Shipping</c>/<c>Delivered</c>/<c>Cancelled</c>: chưa có luồng nào
/// đặt chúng. Thêm bây giờ là lặp lại đúng cái sai đã tránh được một lần.
/// </para>
/// </summary>
public enum OrderStatus
{
    /// <summary>Đơn đã tạo, chưa ghi nhận thanh toán. Mặc định của mọi đơn.</summary>
    Pending = 0,

    /// <summary>Đã nhận IPN hợp lệ báo giao dịch thành công, và số tiền khớp.</summary>
    Paid = 1
}
