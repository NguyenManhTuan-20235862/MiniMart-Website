using MiniMart.Domain.Enums;

namespace MiniMart.Web.Extensions;

/// <summary>
/// Dịch <see cref="OrderStatus"/> sang chữ cho người đọc.
///
/// <para>
/// Nằm ở tầng Web chứ không Domain: <c>OrderStatus</c> là một khái niệm nghiệp vụ, còn
/// "Chờ thanh toán" là một câu tiếng Việt hiển thị trên màn hình. Nhét chuỗi hiển thị
/// vào Domain là buộc nó biết ứng dụng dùng ngôn ngữ nào - và ngày thêm tiếng Anh thì
/// phải sửa Domain.
/// </para>
/// <para>
/// KHÔNG hiện tên enum thô (<c>Pending</c>) ra màn hình. Với khách nó vô nghĩa; cùng lý
/// do trang Return của VNPay không hiện mã phản hồi thô.
/// </para>
/// </summary>
public static class OrderStatusText
{
    public static string ToText(this OrderStatus status) => status switch
    {
        OrderStatus.Pending => "Chờ thanh toán",
        OrderStatus.Paid => "Đã thanh toán",

        // Nhánh này TỒN TẠI để trang không nổ khi thêm trạng thái mới, nhưng nó KHÔNG
        // phải hàng rào - hàng rào là test `Moi_OrderStatus_deu_phai_co_nhan_tieng_Viet`
        // duyệt qua mọi giá trị của enum. Thiếu test đó thì thêm `Cancelled` sẽ hiện ra
        // chữ "Cancelled" giữa trang tiếng Việt mà không có gì báo.
        _ => status.ToString()
    };

    /// <summary>
    /// Lớp CSS của badge. Hai trạng thái phải trông KHÁC nhau - dùng chung một màu là
    /// nói với khách rằng "chờ thanh toán" và "đã thanh toán" giống nhau.
    /// </summary>
    public static string ToBadgeClass(this OrderStatus status) => status switch
    {
        OrderStatus.Paid => "bg-success",
        OrderStatus.Pending => "bg-warning text-dark",
        _ => "bg-secondary"
    };
}
