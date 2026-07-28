using MiniMart.Domain.Enums;

namespace MiniMart.Domain.Entities;

/// <summary>
/// Một lần ghi nhận kết quả thanh toán từ cổng, do IPN tạo ra.
///
/// <para>
/// Tồn tại RIÊNG chứ không nhét thêm mấy cột vào <c>Order</c>, vì hai bản ghi trả lời
/// hai câu hỏi khác nhau: <c>Order</c> nói "khách đặt gì, giá bao nhiêu", còn
/// <c>Payment</c> nói "cổng nào, mã giao dịch nào, ngân hàng nào, lúc nào". Trộn vào
/// một bảng là để cột của cổng thanh toán rỗng trên mọi đơn trả tiền mặt.
/// </para>
/// <para>
/// Là bản ghi TÀI CHÍNH nên snapshot mọi thứ cần để đối soát với sao kê của VNPay -
/// cùng tinh thần với <c>OrderDetail</c>.
/// </para>
/// </summary>
public class Payment
{
    public int Id { get; set; }

    /// <summary>
    /// Đơn hàng được thanh toán. Có <b>UNIQUE index</b> - một đơn tối đa MỘT bản ghi
    /// thanh toán.
    ///
    /// <para>
    /// Ràng buộc đó không phải để cho gọn: nó là thứ bảo đảm tính idempotent của IPN.
    /// VNPay gửi lại thông báo khi chưa nhận được phản hồi, và hai lần gửi song song
    /// đều thấy đơn "chưa thanh toán". Lệnh kiểm ở Service có khe TOCTOU; unique index
    /// thì không.
    /// </para>
    /// </summary>
    public int OrderId { get; set; }

    public Order Order { get; set; } = null!;

    public PaymentStatus Status { get; set; }

    /// <summary>
    /// Số tiền cổng báo đã thu, ĐÃ chia lại 100. Lưu ra cột riêng dù luôn bằng
    /// <c>Order.TotalAmount</c> tại thời điểm ghi: đây là con số của BÊN KIA, và giá
    /// trị của nó nằm ở chỗ nó độc lập với số của ta khi cần đối soát.
    /// </summary>
    public decimal Amount { get; set; }

    /// <summary>Mã giao dịch phía VNPay - thứ duy nhất tra cứu được khi khiếu nại.</summary>
    public string TransactionNo { get; set; } = string.Empty;

    public string BankCode { get; set; } = string.Empty;

    /// <summary>Mã kết quả thô của VNPay. Giữ nguyên văn để đối soát và để gỡ lỗi.</summary>
    public string ResponseCode { get; set; } = string.Empty;

    /// <summary>UTC, giống mọi mốc thời gian lưu trữ khác trong dự án.</summary>
    public DateTime CreatedAt { get; set; }
}
