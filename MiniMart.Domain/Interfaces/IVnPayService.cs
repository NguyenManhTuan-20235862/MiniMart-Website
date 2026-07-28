using MiniMart.Domain.Entities;
using MiniMart.Domain.ValueObjects;

namespace MiniMart.Domain.Interfaces;

/// <summary>
/// Dựng URL chuyển hướng khách sang cổng thanh toán VNPay.
///
/// <para>
/// Đặt ở <b>Domain</b>, cài đặt ở <b>Infrastructure</b> - cùng hình dạng với
/// <c>ICartStore</c> và <c>IProductImageStorage</c>: Domain khai báo thứ nó CẦN từ
/// thế giới bên ngoài, còn việc "bên ngoài" đó là SQL Server, thư mục wwwroot hay
/// một cổng thanh toán thì Domain không biết.
/// </para>
/// <para>
/// ⚠ Nợ đặt tên đã biết: tên có chữ "VnPay" nên Domain đang biết tên một nhà cung
/// cấp cụ thể - điều lẽ ra không nên. Giữ tên này vì hôm nay chỉ có MỘT cổng và một
/// abstraction <c>IPaymentGateway</c> tổng quát hoá từ đúng một cài đặt thường đoán
/// sai chỗ cần tổng quát. Khi có cổng thứ hai (Momo, ZaloPay) thì đổi tên interface
/// thành <c>IPaymentGateway</c> và tách phần khác biệt ra - lúc đó mới đủ dữ kiện.
/// </para>
/// </summary>
public interface IVnPayService
{
    /// <summary>
    /// Trả về URL đầy đủ (đã ký) để redirect trình duyệt của khách sang VNPay.
    ///
    /// <para>
    /// Đồng bộ, không <c>async</c>: bước này KHÔNG gọi mạng. Nó chỉ ghép chuỗi và
    /// băm - toàn bộ việc trao đổi với VNPay diễn ra sau đó, giữa trình duyệt của
    /// khách và máy chủ VNPay. Đánh dấu <c>async</c> ở đây là nói dối về chi phí.
    /// </para>
    /// </summary>
    /// <param name="order">
    /// Đơn hàng ĐÃ LƯU. Cần <c>Id</c> (làm mã giao dịch) và <c>TotalAmount</c> - và
    /// phải là số tiền đã chốt trong DB, không phải tổng tính lại từ giỏ hàng.
    /// </param>
    /// <param name="clientIpAddress">
    /// IP của người đặt. VNPay bắt buộc có và dùng nó cho việc chống gian lận, nên
    /// nó phải là IP THẬT của khách, không phải IP máy chủ.
    /// </param>
    string CreatePaymentUrl(Order order, string clientIpAddress);

    /// <summary>
    /// Kiểm chữ ký của dữ liệu VNPay gửi về và đọc ra các trường cần dùng.
    ///
    /// <para>
    /// Trả về <see cref="VnPayReturn.KhongHopLe"/> khi chữ ký sai hoặc thiếu - KHÔNG
    /// ném exception. Lý do: dữ liệu này đến từ ngoài internet nên chữ ký sai là chuyện
    /// BÌNH THƯỜNG, có thể chỉ là một con bot quét URL. Ném exception biến việc thường
    /// ngày thành HTTP 500 và làm ngập log lỗi tới mức sự cố thật bị chìm.
    /// </para>
    /// </summary>
    /// <param name="thamSo">
    /// Toàn bộ tham số nhận được, giá trị đã được giải mã URL (đúng như
    /// <c>Request.Query</c> đưa ra). Bao gồm cả <c>vnp_SecureHash</c> - cài đặt tự loại
    /// nó ra trước khi băm.
    /// </param>
    VnPayReturn Verify(IReadOnlyDictionary<string, string?> thamSo);
}
