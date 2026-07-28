using MiniMart.Application.Models;

namespace MiniMart.Application.Interfaces;

public interface IPaymentService
{
    /// <summary>
    /// Xử lý một thông báo IPN từ VNPay: kiểm chữ ký, đối chiếu đơn và số tiền, rồi
    /// ghi nhận kết quả thanh toán.
    ///
    /// <para>
    /// Đây là nơi DUY NHẤT trong toàn hệ thống được phép đặt <c>Order.Status = Paid</c>.
    /// </para>
    /// <para>
    /// Nhận query string THÔ chứ không nhận kết quả đã xác thực: việc kiểm chữ ký là
    /// bước ĐẦU TIÊN của nghiệp vụ này, không phải một tiền xử lý của tầng Web. Để
    /// Controller kiểm rồi truyền dữ liệu đã tin vào đây là mở đường cho một lối gọi
    /// thứ hai quên kiểm.
    /// </para>
    /// <para>
    /// KHÔNG bao giờ ném ra ngoài - mọi kết cục đều là một <see cref="IpnResult"/>.
    /// Exception lọt lên Controller sẽ thành HTTP 500, mà VNPay đọc 500 là "chưa nhận
    /// được" và gửi lại; nếu nguyên nhân là lỗi dữ liệu cố định thì nó gửi lại vô hạn.
    /// </para>
    /// </summary>
    Task<IpnResult> XuLyIpnAsync(
        IReadOnlyDictionary<string, string?> thamSo,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Dựng URL thanh toán VNPay cho một đơn CỦA CHÍNH người này.
    ///
    /// <para>
    /// Nhận <paramref name="userId"/> và lọc theo nó NGAY TRONG truy vấn, dù URL trả về
    /// không phải bí mật lớn. Lý do: nó mang theo mã đơn và SỐ TIỀN, và quan trọng hơn
    /// là không có lý do gì để người này dựng được lệnh thanh toán cho đơn người khác.
    /// Đây là cùng khuôn với <c>GetMyOrderAsync</c>.
    /// </para>
    /// <para>
    /// Đặt ở Application chứ không để Controller tự gọi <c>IVnPayService</c>: Controller
    /// sẽ phải tự nạp đơn, tự kiểm chủ sở hữu, tự kiểm trạng thái - tức chứa nghiệp vụ.
    /// </para>
    /// </summary>
    /// <param name="clientIpAddress">
    /// IP THẬT của khách (VNPay dùng cho chống gian lận), lấy từ
    /// <c>HttpContext.Connection.RemoteIpAddress</c> ở tầng Web.
    /// </param>
    /// <exception cref="Common.Exceptions.NotFoundException">
    /// Đơn không tồn tại <b>hoặc</b> thuộc người khác - cố ý không phân biệt.
    /// </exception>
    /// <exception cref="InvalidOperationException">
    /// Đơn đã thanh toán. Là lỗi LẬP TRÌNH: không giao diện nào dẫn tới đây, vì đơn vừa
    /// tạo luôn ở <c>Pending</c>.
    /// </exception>
    Task<string> TaoUrlThanhToanAsync(
        int orderId,
        int userId,
        string clientIpAddress,
        CancellationToken cancellationToken = default);
}
