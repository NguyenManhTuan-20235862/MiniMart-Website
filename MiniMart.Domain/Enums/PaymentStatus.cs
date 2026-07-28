namespace MiniMart.Domain.Enums;

/// <summary>
/// Kết quả một lần thanh toán qua cổng.
///
/// <para>
/// Ghi CẢ lần thất bại, không chỉ lần thành công. Lý do: khi khách gọi lên nói "tôi
/// trả rồi mà đơn vẫn chưa thanh toán", câu trả lời nằm ở chính bản ghi thất bại kèm
/// <c>ResponseCode</c>. Không ghi thì không có gì để tra ngoài log, mà log thì bị xoay
/// vòng.
/// </para>
/// </summary>
public enum PaymentStatus
{
    /// <summary>Cổng báo giao dịch thành công VÀ số tiền khớp với đơn.</summary>
    Succeeded = 0,

    /// <summary>Cổng báo giao dịch không thành công (khách huỷ, thiếu tiền, sai OTP...).</summary>
    Failed = 1
}
