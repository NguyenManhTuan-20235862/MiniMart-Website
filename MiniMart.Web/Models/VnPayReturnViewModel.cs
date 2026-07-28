using MiniMart.Domain.ValueObjects;

namespace MiniMart.Web.Models;

/// <summary>
/// Những gì trang kết quả thanh toán được phép hiển thị.
///
/// <para>
/// Cố ý NGHÈO NÀN: một trạng thái, một câu, và mã đơn. Không số tiền, không mã giao
/// dịch ngân hàng, không tên khách. Trang này <c>[AllowAnonymous]</c> nên mọi trường
/// thêm vào đây là một trường ai cầm được URL cũng đọc được.
/// </para>
/// </summary>
/// <param name="TrangThai">Quyết định màu và biểu tượng, không phải chuỗi để so sánh.</param>
/// <param name="ThongBao">Câu hiển thị cho khách. Đã là tiếng Việt sẵn sàng in ra.</param>
/// <param name="OrderId">
/// Chỉ có khi chữ ký hợp lệ. <c>null</c> nghĩa là không được phép link sang đơn hàng.
/// </param>
public sealed record VnPayReturnViewModel(
    KetQuaThanhToan TrangThai,
    string ThongBao,
    int? OrderId)
{
    public static VnPayReturnViewModel KhongXacThucDuoc { get; } = new(
        KetQuaThanhToan.KhongXacThucDuoc,

        // Câu chữ dành cho KHÁCH, không phải cho lập trình viên. Chi tiết kỹ thuật
        // ("sai chữ ký") thuộc về log phía server.
        "Không xác nhận được kết quả thanh toán từ đường dẫn này. "
        + "Nếu bạn vừa thanh toán, vui lòng mở lại đơn hàng của bạn để kiểm tra.",

        OrderId: null);

    public static VnPayReturnViewModel Tu(VnPayReturn ketQua)
    {
        if (ketQua.ThanhToanThanhCong)
        {
            return new VnPayReturnViewModel(
                KetQuaThanhToan.ThanhCong,

                // ⚠ Chữ "đang được xác nhận", KHÔNG phải "đã thanh toán thành công".
                // Câu này nói ĐÚNG những gì hệ thống biết tại thời điểm này: VNPay báo
                // giao dịch thành công, còn việc ghi nhận vào đơn hàng đi qua kênh IPN
                // và chưa chắc đã xong. Hứa quá lời ở đây là tạo ra tranh cãi về sau
                // với chính khách hàng đang đọc câu này.
                "Ngân hàng báo giao dịch thành công. Đơn hàng của bạn đang được xác nhận.",

                ketQua.OrderId);
        }

        if (ketQua.KhachTuHuy)
        {
            return new VnPayReturnViewModel(
                KetQuaThanhToan.DaHuy,

                // Khách tự huỷ KHÔNG phải lỗi. Hiện màu đỏ kèm chữ "thất bại" cho một
                // hành động họ chủ động làm là khiến họ tưởng có sự cố.
                "Bạn đã huỷ giao dịch. Đơn hàng vẫn còn, bạn có thể thanh toán lại.",

                ketQua.OrderId);
        }

        return new VnPayReturnViewModel(
            KetQuaThanhToan.ThatBai,
            MoTaThatBai(ketQua.ResponseCode),
            ketQua.OrderId);
    }

    /// <summary>
    /// Diễn giải <c>vnp_ResponseCode</c> thành câu khách hiểu được.
    ///
    /// <para>
    /// Chỉ liệt kê những mã mà khách LÀM ĐƯỢC GÌ ĐÓ khi biết: hết tiền thì nạp thêm,
    /// quá hạn thì đặt lại, sai OTP nhiều lần thì gọi ngân hàng. Những mã còn lại gộp
    /// vào câu chung - liệt kê đủ 30 mã của đặc tả chỉ tạo ra 30 câu mà 29 câu không ai
    /// đọc, và mỗi câu là một chỗ để sai.
    /// </para>
    /// <para>
    /// Mã thô KHÔNG hiện ra màn hình: với khách nó vô nghĩa, còn để tra cứu thì nó đã
    /// nằm trong <c>Payments.ResponseCode</c> dưới DB.
    /// </para>
    /// </summary>
    private static string MoTaThatBai(string? responseCode) => responseCode switch
    {
        "09" => "Thẻ/tài khoản chưa đăng ký dịch vụ thanh toán trực tuyến (Internet Banking).",
        "10" => "Xác thực thông tin thẻ không đúng quá 3 lần.",
        "11" => "Đã hết hạn chờ thanh toán. Bạn có thể thử lại từ đơn hàng của mình.",
        "12" => "Thẻ/tài khoản đang bị khoá.",
        "51" => "Tài khoản không đủ số dư để thực hiện giao dịch.",
        "65" => "Tài khoản đã vượt hạn mức giao dịch trong ngày.",
        "75" => "Ngân hàng đang bảo trì. Vui lòng thử lại sau.",
        "79" => "Nhập sai mật khẩu thanh toán quá số lần quy định.",
        _ => "Giao dịch không thành công. Tiền chưa bị trừ, hoặc sẽ được ngân hàng hoàn lại."
    };
}

/// <summary>
/// Bốn kết cục có thể hiện lên màn hình.
///
/// <para>
/// Enum chứ không phải <c>bool ThanhCong</c>: "khách tự huỷ" và "giao dịch lỗi" đều
/// không-thành-công nhưng phải hiện khác nhau, còn "không xác thực được" thì khác cả
/// hai. Ba trạng thái nhét vào một bool là chỗ sinh ra những câu thông báo sai.
/// </para>
/// </summary>
public enum KetQuaThanhToan
{
    ThanhCong,
    DaHuy,
    ThatBai,
    KhongXacThucDuoc
}
