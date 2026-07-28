using MiniMart.Domain.Enums;

namespace MiniMart.Domain.Entities;

/// <summary>
/// Một đơn hàng đã đặt. Khác giỏ hàng ở điểm cốt lõi: đơn hàng là bản ghi
/// LỊCH SỬ, nên mọi con số trong nó phải được chốt tại thời điểm đặt và không
/// bao giờ đổi theo dữ liệu sản phẩm về sau.
/// </summary>
public class Order
{
    public int Id { get; set; }

    public int UserId { get; set; }

    public User User { get; set; } = null!;

    /// <summary>UTC. Giờ địa phương phụ thuộc máy chạy nên không dùng cho dữ liệu lưu trữ.</summary>
    public DateTime CreatedAt { get; set; }

    /// <summary>
    /// Tổng tiền đã chốt. Lưu ra cột riêng dù có thể tính lại từ
    /// <see cref="Items"/>: danh sách đơn hàng cần tổng tiền mà không phải join
    /// và cộng dồn, và đây là con số ràng buộc với khách - nó phải là dữ liệu,
    /// không phải kết quả một phép tính có thể đổi khi code đổi.
    /// </summary>
    public decimal TotalAmount { get; set; }

    /// <summary>
    /// Tên người nhận hàng - CHỐT tại thời điểm đặt.
    ///
    /// <para>
    /// Cố ý KHÔNG đọc từ <see cref="User"/>: tên tài khoản là tên người MUA, còn đây
    /// là tên người NHẬN, và hai cái thường khác nhau (mua tặng, giao tới cơ quan).
    /// Kể cả khi trùng, người dùng đổi tên tài khoản sau đó cũng không được phép làm
    /// đổi tên trên một đơn đã giao.
    /// </para>
    /// </summary>
    public string RecipientName { get; set; } = string.Empty;

    /// <summary>Số điện thoại người nhận, để bên giao hàng liên hệ.</summary>
    public string RecipientPhone { get; set; } = string.Empty;

    /// <summary>
    /// Địa chỉ giao hàng - CHỐT tại thời điểm đặt, cùng lý do với
    /// <see cref="OrderDetail.ProductName"/> và <see cref="OrderDetail.UnitPrice"/>.
    ///
    /// <para>
    /// Đây là lý do ba trường này là CỘT trên Orders chứ không phải khoá ngoại sang
    /// một bảng <c>Addresses</c>: khoá ngoại nghĩa là khách sửa sổ địa chỉ của họ thì
    /// mọi đơn cũ đổi theo, tức đơn đã giao xong lại ghi một địa chỉ chưa từng được
    /// giao tới. Sổ địa chỉ (chọn nhanh khi đặt) là tính năng khác, và kể cả khi có
    /// nó thì đơn hàng vẫn phải snapshot đúng như thế này.
    /// </para>
    /// </summary>
    public string ShippingAddress { get; set; } = string.Empty;

    /// <summary>
    /// Trạng thái thanh toán. Chỉ có MỘT nơi được đổi nó sang <c>Paid</c>: kênh IPN
    /// (<c>PaymentService.XuLyIpnAsync</c>). Xem <c>.claude/rules/payments.md</c>.
    /// </summary>
    public OrderStatus Status { get; set; } = OrderStatus.Pending;

    /// <summary>Bản ghi thanh toán, <c>null</c> khi chưa có IPN nào tới.</summary>
    public Payment? Payment { get; set; }

    public ICollection<OrderDetail> Items { get; set; } = [];

    // KHÔNG có RowVersion, và điều đó VẪN đúng sau khi có Status.
    //
    // Nghe qua thì IPN đổi Status là "hai người cùng sửa một đơn" nên cần khoá. Nhưng
    // thứ chống ghi đè ở đây là UNIQUE(Payments.OrderId): hai IPN song song thì đúng
    // một bản INSERT thành công, bản kia nhận DuplicateKeyException. RowVersion sẽ là
    // lớp khoá thứ hai cho cùng một chuyện, và hai mô hình concurrency trên một bảng
    // khó suy luận hơn nhiều so với một - đúng lý do đã chọn Optimistic cho Products.
}
