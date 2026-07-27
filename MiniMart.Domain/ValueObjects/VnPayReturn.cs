namespace MiniMart.Domain.ValueObjects;

/// <summary>
/// Kết quả đọc và KIỂM CHỮ KÝ của dữ liệu VNPay gửi về.
///
/// <para>
/// Là một value object thuần: không hành vi nào chạm DB hay mạng. Nó chỉ trả lời hai
/// câu - "dữ liệu này có thật do VNPay tạo ra không" và "nó nói gì".
/// </para>
/// <para>
/// ⚠ Chú ý ranh giới: <see cref="ThanhToanThanhCong"/> nghĩa là <b>VNPay báo</b> giao
/// dịch thành công, KHÔNG phải "đơn hàng đã được xác nhận thanh toán". Việc ghi nhận
/// đó thuộc về kênh IPN (máy chủ gọi máy chủ), không phải kênh này - xem
/// <c>.claude/rules/payments.md</c>.
/// </para>
/// </summary>
public sealed record VnPayReturn(
    bool ChuKyHopLe,
    int? OrderId,
    string? ResponseCode,
    string? TransactionStatus,
    string? TransactionNo,
    string? BankCode,
    decimal? Amount)
{
    /// <summary>Mã VNPay dùng cho "thành công" ở cả hai trường mã.</summary>
    public const string MaThanhCong = "00";

    /// <summary>Mã VNPay dùng khi khách tự bấm huỷ ở trang thanh toán.</summary>
    public const string MaKhachHuy = "24";

    /// <summary>
    /// Chữ ký hợp lệ VÀ cả hai mã đều báo thành công.
    ///
    /// <para>
    /// Phải kiểm CẢ HAI: <c>vnp_ResponseCode</c> là kết quả của lệnh gửi tới cổng, còn
    /// <c>vnp_TransactionStatus</c> là kết quả của chính giao dịch. Chỉ kiểm một cái là
    /// có kịch bản cổng nhận lệnh thành công nhưng giao dịch không thành.
    /// </para>
    /// <para>
    /// <c>ChuKyHopLe</c> đứng ĐẦU trong biểu thức là chủ ý: mã trả về không có nghĩa gì
    /// nếu chưa biết dữ liệu có thật hay không.
    /// </para>
    /// </summary>
    public bool ThanhToanThanhCong =>
        ChuKyHopLe
        && ResponseCode == MaThanhCong
        && TransactionStatus == MaThanhCong;

    /// <summary>Khách tự huỷ - không phải lỗi, nên giao diện không được báo như lỗi.</summary>
    public bool KhachTuHuy => ChuKyHopLe && ResponseCode == MaKhachHuy;

    /// <summary>Dữ liệu không do VNPay tạo ra, hoặc đã bị sửa trên đường về.</summary>
    public static VnPayReturn KhongHopLe { get; } =
        new(false, null, null, null, null, null, null);
}
