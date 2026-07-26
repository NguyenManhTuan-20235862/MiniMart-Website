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

    public ICollection<OrderDetail> Items { get; set; } = [];

    // KHÔNG có RowVersion: đơn hàng đã đặt thì không ai sửa đồng thời. Khi nào có
    // luồng đổi trạng thái đơn (Đang xử lý -> Đã giao) thì mới cân nhắc thêm.
    //
    // KHÔNG có Status: chưa có nghiệp vụ nào dùng tới, và thêm một cột trạng thái
    // trước khi biết đơn có những trạng thái nào là đoán. Cùng lý do với việc hoãn
    // API transaction cho tới đúng lúc này.
}
