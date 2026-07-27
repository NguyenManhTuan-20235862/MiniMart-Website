namespace MiniMart.Application.Models;

/// <summary>
/// Phản hồi cho VNPay ở kênh IPN. Hợp đồng do VNPay định nghĩa, không do ta chọn.
///
/// <para>
/// ⚠ Hiểu đúng ý nghĩa của <see cref="RspCode"/>, đây là chỗ rất dễ nhầm: nó trả lời
/// <b>"tôi đã nhận và xử lý xong thông báo của bạn chưa"</b>, KHÔNG phải "giao dịch có
/// thành công không". Một giao dịch THẤT BẠI mà ta ghi nhận được vẫn phải trả
/// <c>00</c>. Trả mã lỗi cho một giao dịch thất bại sẽ khiến VNPay tưởng ta chưa nhận
/// được và gửi lại mãi.
/// </para>
/// <para>
/// <c>Message</c> giữ nguyên tiếng Anh theo đặc tả - đây là chuỗi cho máy đọc, không
/// phải câu hiển thị cho người dùng.
/// </para>
/// </summary>
public sealed record IpnResult(string RspCode, string Message)
{
    /// <summary>Đã ghi nhận xong. Dùng cho CẢ giao dịch thành công lẫn thất bại.</summary>
    public static IpnResult ThanhCong { get; } = new("00", "Confirm Success");

    /// <summary><c>vnp_TxnRef</c> không trỏ tới đơn nào.</summary>
    public static IpnResult KhongTimThayDon { get; } = new("01", "Order not found");

    /// <summary>Đơn đã được ghi nhận trước đó. VNPay hiểu là "thôi đừng gửi lại nữa".</summary>
    public static IpnResult DonDaXacNhan { get; } = new("02", "Order already confirmed");

    /// <summary>Số tiền cổng báo KHÔNG khớp số tiền của đơn.</summary>
    public static IpnResult SaiSoTien { get; } = new("04", "Invalid amount");

    /// <summary>Chữ ký sai - dữ liệu không do VNPay tạo ra, hoặc đã bị sửa.</summary>
    public static IpnResult SaiChuKy { get; } = new("97", "Invalid signature");

    /// <summary>Lỗi ngoài dự kiến phía ta. VNPay sẽ thử lại.</summary>
    public static IpnResult LoiKhac { get; } = new("99", "Unknown error");
}
