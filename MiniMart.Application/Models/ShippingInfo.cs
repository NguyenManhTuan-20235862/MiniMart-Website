namespace MiniMart.Application.Models;

/// <summary>
/// Thông tin giao hàng do người mua nhập, đầu vào của <c>IOrderService.CheckoutAsync</c>.
///
/// <para>
/// Là một type riêng chứ không phải ba tham số <c>string</c> rời. Ba chuỗi liền nhau
/// trong danh sách tham số là chỗ hoán vị nhầm mà trình biên dịch không thể phát hiện:
/// truyền nhầm số điện thoại vào ô tên thì code vẫn build, vẫn chạy, và đơn hàng chỉ
/// đơn giản là sai. Gói lại thành một type cũng theo đúng tiền lệ <c>ProductFilter</c>:
/// thêm trường về sau thì mọi nơi nhận được ngay thay vì phải sửa từng chữ ký.
/// </para>
/// <para>
/// Đặt ở <b>Application</b> chứ không phải Domain: đây là hình dạng ĐẦU VÀO của một
/// use case, không phải một khái niệm nghiệp vụ tự thân. <c>Order</c> ở Domain vẫn giữ
/// ba cột phẳng - nó là bản ghi lịch sử, không phải là đầu vào của ai cả.
/// </para>
/// </summary>
public sealed record ShippingInfo(string RecipientName, string RecipientPhone, string Address)
{
    /// <summary>
    /// Bản đã cắt khoảng trắng thừa ở hai đầu.
    ///
    /// <para>
    /// <c>[Required]</c> đã loại chuỗi toàn khoảng trắng giúp (nó trim trước khi kiểm),
    /// nên đây KHÔNG phải lớp chặn thứ hai cho trường hợp đó. Việc nó làm là chuẩn hoá:
    /// <c>"  12 Nguyễn Trãi  "</c> hợp lệ với mọi annotation và sẽ được lưu NGUYÊN
    /// khoảng trắng nếu không cắt - dữ liệu bẩn im lặng, và hai địa chỉ giống hệt nhau
    /// lại không so sánh bằng nhau.
    /// </para>
    /// <para>
    /// <see cref="ThieuThongTin"/> thì mới là lớp chặn thứ hai, và nó dành cho người
    /// gọi KHÔNG đi qua ASP.NET Core (job nền, test, một API sau này) - nơi không có
    /// <c>ModelState</c> nào chạy trước.
    /// </para>
    /// </summary>
    public ShippingInfo ChuanHoa() =>
        new(RecipientName.Trim(), RecipientPhone.Trim(), Address.Trim());

    public bool ThieuThongTin =>
        string.IsNullOrWhiteSpace(RecipientName)
        || string.IsNullOrWhiteSpace(RecipientPhone)
        || string.IsNullOrWhiteSpace(Address);
}
